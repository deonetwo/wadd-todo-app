using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;

namespace Wadd.Services;

public class GoogleDriveSyncService : ISyncService
{
    private readonly ITodoService _todoService;
    private readonly ISyncLogRepository _syncLogRepository;
    private readonly IConflictRepository _conflictRepository;
    private readonly IDeviceService _deviceService;
    private readonly ConflictResolutionEngine _conflictEngine;
    private readonly HttpClient _httpClient;
    private readonly INativeGoogleAuthService? _nativeAuthService;
    private readonly IGoalService _goalService;
    private UserAuthRecord? _authRecord;
    private readonly string _authFilePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public bool IsSignedIn => _authRecord?.IsSignedIn ?? false;
    public string? UserEmail => _authRecord?.UserEmail;
    public string? UserName => _authRecord?.UserName;
    public bool MigratedToManifests => _authRecord?.MigratedToManifests ?? false;
    public event EventHandler? AuthStateChanged;

    internal void SetAuthRecordForTesting(UserAuthRecord record) => _authRecord = record;

    private void NotifyAuthStateChanged() => AuthStateChanged?.Invoke(this, EventArgs.Empty);

    private static string? GetAssemblyMetadata(string key)
    {
        var attribute = typeof(GoogleDriveSyncService).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
        return attribute?.Value;
    }

    public string GoogleClientId
    {
        get
        {
            var envVal = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
            if (!string.IsNullOrWhiteSpace(envVal)) return envVal.Trim();

            if (!string.IsNullOrWhiteSpace(_authRecord?.GoogleClientId))
                return _authRecord.GoogleClientId;

            var compiledVal = GetAssemblyMetadata("GoogleClientId");
            if (!string.IsNullOrWhiteSpace(compiledVal)) return compiledVal.Trim();

            return string.Empty;
        }
        set
        {
            _authRecord ??= new UserAuthRecord();
            _authRecord.GoogleClientId = value;
            SaveAuthRecord();
        }
    }

    public string GoogleClientSecret
    {
        get
        {
            var envVal = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
            if (!string.IsNullOrWhiteSpace(envVal)) return envVal.Trim();

            if (!string.IsNullOrWhiteSpace(_authRecord?.GoogleClientSecret))
                return _authRecord.GoogleClientSecret;

            var compiledVal = GetAssemblyMetadata("GoogleClientSecret");
            if (!string.IsNullOrWhiteSpace(compiledVal)) return compiledVal.Trim();

            return string.Empty;
        }
        set
        {
            _authRecord ??= new UserAuthRecord();
            _authRecord.GoogleClientSecret = value;
            SaveAuthRecord();
        }
    }

    public string FirebaseApiKey
    {
        get
        {
            var envVal = Environment.GetEnvironmentVariable("FIREBASE_API_KEY");
            if (!string.IsNullOrWhiteSpace(envVal)) return envVal.Trim();

            if (!string.IsNullOrWhiteSpace(_authRecord?.FirebaseApiKey))
                return _authRecord.FirebaseApiKey;

            var compiledVal = GetAssemblyMetadata("FirebaseApiKey");
            if (!string.IsNullOrWhiteSpace(compiledVal)) return compiledVal.Trim();

            return string.Empty;
        }
        set
        {
            _authRecord ??= new UserAuthRecord();
            _authRecord.FirebaseApiKey = value;
            SaveAuthRecord();
        }
    }

    public string FirebaseProjectId
    {
        get
        {
            var envVal = Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID");
            if (!string.IsNullOrWhiteSpace(envVal)) return envVal.Trim();

            if (!string.IsNullOrWhiteSpace(_authRecord?.FirebaseProjectId))
                return _authRecord.FirebaseProjectId;

            var compiledVal = GetAssemblyMetadata("FirebaseProjectId");
            if (!string.IsNullOrWhiteSpace(compiledVal)) return compiledVal.Trim();

            return string.Empty;
        }
        set
        {
            _authRecord ??= new UserAuthRecord();
            _authRecord.FirebaseProjectId = value;
            SaveAuthRecord();
        }
    }

    public string OAuthProxyUrl
    {
        get
        {
            var envVal = Environment.GetEnvironmentVariable("OAUTH_PROXY_URL");
            if (!string.IsNullOrWhiteSpace(envVal)) return envVal.Trim().TrimEnd('/');

            if (!string.IsNullOrWhiteSpace(_authRecord?.OAuthProxyUrl))
                return _authRecord.OAuthProxyUrl.TrimEnd('/');

            var compiledVal = GetAssemblyMetadata("OAuthProxyUrl");
            if (!string.IsNullOrWhiteSpace(compiledVal)) return compiledVal.Trim().TrimEnd('/');

            return string.Empty;
        }
        set
        {
            _authRecord ??= new UserAuthRecord();
            _authRecord.OAuthProxyUrl = value;
            SaveAuthRecord();
        }
    }

    public string OAuthProxySecret
    {
        get
        {
            var envVal = Environment.GetEnvironmentVariable("OAUTH_PROXY_SECRET");
            if (!string.IsNullOrWhiteSpace(envVal)) return envVal.Trim();

            if (!string.IsNullOrWhiteSpace(_authRecord?.OAuthProxySecret))
                return _authRecord.OAuthProxySecret;

            var compiledVal = GetAssemblyMetadata("OAuthProxySecret");
            if (!string.IsNullOrWhiteSpace(compiledVal)) return compiledVal.Trim();

            return string.Empty;
        }
        set
        {
            _authRecord ??= new UserAuthRecord();
            _authRecord.OAuthProxySecret = value;
            SaveAuthRecord();
        }
    }

    public string WebAppUrl { get; set; } = string.Empty;

    public bool HasChangesApplied { get; private set; }

    public int UnresolvedConflictCount { get; private set; }
    public event EventHandler? ConflictCountChanged;

    public GoogleDriveSyncService(
        ITodoService todoService,
        HttpClient? httpClient = null,
        string? webAppUrl = null,
        ISyncLogRepository? syncLogRepository = null,
        IConflictRepository? conflictRepository = null,
        IDeviceService? deviceService = null,
        ConflictResolutionEngine? conflictEngine = null,
        INativeGoogleAuthService? nativeAuthService = null,
        IGoalService? goalService = null)
    {
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        _httpClient = httpClient ?? CreateOptimizedHttpClient();
        _nativeAuthService = nativeAuthService;

        if (_todoService is SQLiteTodoService sqliteService)
        {
            _syncLogRepository = syncLogRepository ?? sqliteService.SyncLogRepository;
            _deviceService = deviceService ?? sqliteService.DeviceService;
            _conflictRepository = conflictRepository ?? new SQLiteConflictRepository(sqliteService.DatabaseConnection);
            _goalService = goalService ?? new SQLiteGoalService(sqliteService.DatabaseConnection);
        }
        else
        {
            _deviceService = deviceService ?? new DeviceService();
            _syncLogRepository = syncLogRepository ?? new SQLiteSyncLogRepository(Wadd.Core.Helpers.AppDataHelper.GetWaddFilePath("wadd.db"));
            _conflictRepository = conflictRepository ?? new SQLiteConflictRepository(Wadd.Core.Helpers.AppDataHelper.GetWaddFilePath("wadd.db"));
            _goalService = goalService ?? new SQLiteGoalService(Wadd.Core.Helpers.AppDataHelper.GetWaddFilePath("wadd.db"));
        }

        _conflictEngine = conflictEngine ?? new ConflictResolutionEngine();

        LoadEnvFile();
        _authFilePath = Wadd.Core.Helpers.AppDataHelper.GetWaddFilePath("google_user_auth.json");
        LoadAuthRecord();
        _ = RefreshConflictCountAsync();
    }

    public async Task RefreshConflictCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var conflicts = await _conflictRepository.GetUnresolvedConflictsAsync(cancellationToken);
            var count = conflicts.Count();
            if (UnresolvedConflictCount != count)
            {
                UnresolvedConflictCount = count;
                ConflictCountChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch { }
    }

    private static void LoadEnvFile()
    {
        try
        {
            var pathsToTry = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), ".env"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wadd", ".env")
            };

            foreach (var path in pathsToTry)
            {
                if (File.Exists(path))
                {
                    var lines = File.ReadAllLines(path);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim().Trim('\uFEFF', '\u200B');
                        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                        var parts = trimmed.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            var key = parts[0].Trim().Trim('\uFEFF', '\u200B');
                            var val = parts[1].Trim().Trim('"', '\'').Trim('\uFEFF', '\u200B');
                            if (!string.IsNullOrWhiteSpace(key))
                            {
                                Environment.SetEnvironmentVariable(key, val);
                            }
                        }
                    }
                    break;
                }
            }
        }
        catch { }
    }

    private void LoadAuthRecord()
    {
        try
        {
            if (File.Exists(_authFilePath))
            {
                var json = File.ReadAllText(_authFilePath);
                _authRecord = JsonSerializer.Deserialize<UserAuthRecord>(json, JsonOptions);
                if (_authRecord?.LastSyncTimestampUtc.HasValue == true)
                {
                    _lastSyncTimestampUtc = _authRecord.LastSyncTimestampUtc.Value;
                }
            }
        }
        catch { }
    }

    private void SaveAuthRecord()
    {
        try
        {
            if (_authRecord != null)
            {
                var json = JsonSerializer.Serialize(_authRecord, JsonOptions);
                File.WriteAllText(_authFilePath, json);
            }
        }
        catch { }
    }

    public async Task<bool> SignInAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(GoogleClientId))
        {
            throw new InvalidOperationException("Google Client ID is missing. Please set GOOGLE_CLIENT_ID environment variable or paste it in Settings.");
        }

        if (_nativeAuthService != null && _nativeAuthService.IsSupported)
        {
            var nativeResult = await _nativeAuthService.SignInAsync(GoogleClientId, cancellationToken);
            if (nativeResult.IsSuccess && !string.IsNullOrWhiteSpace(nativeResult.IdToken))
            {
                var fbSession = await ExchangeGoogleIdTokenWithFirebaseAsync(nativeResult.IdToken, nativeResult.AccessToken ?? string.Empty, "http://localhost:5001/", cancellationToken);

                _authRecord = new UserAuthRecord
                {
                    IsSignedIn = true,
                    UserEmail = !string.IsNullOrWhiteSpace(fbSession.Email) ? fbSession.Email : (nativeResult.Email ?? string.Empty),
                    UserName = !string.IsNullOrWhiteSpace(fbSession.DisplayName) ? fbSession.DisplayName : (nativeResult.DisplayName ?? string.Empty),
                    GoogleClientId = GoogleClientId,
                    GoogleClientSecret = GoogleClientSecret,
                    FirebaseApiKey = FirebaseApiKey,
                    FirebaseProjectId = FirebaseProjectId,
                    AccessToken = !string.IsNullOrWhiteSpace(nativeResult.AccessToken) ? nativeResult.AccessToken : fbSession.OAuthAccessToken,
                    RefreshToken = string.Empty,
                    FirebaseIdToken = fbSession.FirebaseIdToken,
                    FirebaseRefreshToken = fbSession.FirebaseRefreshToken,
                    FirebaseLocalId = fbSession.LocalId,
                    AuthenticatedAt = DateTime.UtcNow,
                    TokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(50)
                };

                SaveAuthRecord();
                NotifyAuthStateChanged();
                return true;
            }

            if (!string.IsNullOrWhiteSpace(nativeResult.ErrorMessage))
            {
                Wadd.Core.Logging.AppLogger.LogError("GoogleDriveSyncService", $"Native Google Sign-In failed: {nativeResult.ErrorMessage}");
                throw new InvalidOperationException($"Native Google Sign-In failed: {nativeResult.ErrorMessage}");
            }
        }

        var localRedirectUri = "http://localhost:5001/";
        var redirectUri = localRedirectUri;
        var state = Guid.NewGuid().ToString("N");
        var (codeVerifier, codeChallenge) = GeneratePkceCodes();

        var listener = new HttpListener();
        listener.Prefixes.Add(localRedirectUri);
        listener.Start();

        try
        {
            var nonce = Guid.NewGuid().ToString("N");
            var hasOAuthBackend = !string.IsNullOrWhiteSpace(OAuthProxyUrl) || !string.IsNullOrWhiteSpace(GoogleClientSecret);

            // If an OAuth proxy (Cloudflare Worker) or client secret is configured, use PKCE Authorization Code flow to acquire a refresh token.
            // If neither is configured, fallback to Implicit Token flow.
            var authUrl = hasOAuthBackend
                ? $"https://accounts.google.com/o/oauth2/v2/auth?" +
                  $"client_id={Uri.EscapeDataString(GoogleClientId)}&" +
                  $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                  $"response_type=code&" +
                  $"access_type=offline&" +
                  $"prompt=consent&" +
                  $"code_challenge={Uri.EscapeDataString(codeChallenge)}&" +
                  $"code_challenge_method=S256&" +
                  $"scope={Uri.EscapeDataString("openid email profile https://www.googleapis.com/auth/drive.appdata https://www.googleapis.com/auth/drive.file")}&" +
                  $"nonce={Uri.EscapeDataString(nonce)}&" +
                  $"state={Uri.EscapeDataString(state)}"
                : $"https://accounts.google.com/o/oauth2/v2/auth?" +
                  $"client_id={Uri.EscapeDataString(GoogleClientId)}&" +
                  $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                  $"response_type=token%20id_token&" +
                  $"prompt=select_account&" +
                  $"scope={Uri.EscapeDataString("openid email profile https://www.googleapis.com/auth/drive.appdata https://www.googleapis.com/auth/drive.file")}&" +
                  $"nonce={Uri.EscapeDataString(nonce)}&" +
                  $"state={Uri.EscapeDataString(state)}";

            OpenBrowserUrl(authUrl);

            string code = string.Empty;
            string idToken = string.Empty;
            string accessToken = string.Empty;
            string returnedState = string.Empty;
            string error = string.Empty;
            int expiresInSeconds = 3600;

            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(3), cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                var contextTask = listener.GetContextAsync();
                var completedTask = await Task.WhenAny(contextTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException("Google Sign-In authorization timed out. Please try again.");
                }

                var context = await contextTask;
                var request = context.Request;
                var response = context.Response;

                var qCode = request.QueryString["code"];
                var qError = request.QueryString["error"];
                var qExpiresIn = request.QueryString["expires_in"];
                if (!string.IsNullOrEmpty(qExpiresIn) && int.TryParse(qExpiresIn, out var parsedExp) && parsedExp > 0)
                {
                    expiresInSeconds = parsedExp;
                }

                if (!string.IsNullOrEmpty(qCode) || !string.IsNullOrEmpty(qError))
                {
                    code = qCode ?? string.Empty;
                    idToken = request.QueryString["id_token"] ?? string.Empty;
                    accessToken = request.QueryString["access_token"] ?? string.Empty;
                    returnedState = request.QueryString["state"] ?? string.Empty;
                    error = qError ?? string.Empty;

                    SendHtmlResponse(response, "Signed In", "<h2>Signed in</h2><p>You can close this tab and return to Wadd.</p>");
                    break;
                }
                else if (request.Url?.AbsolutePath == "/callback")
                {
                    code = request.QueryString["code"] ?? string.Empty;
                    idToken = request.QueryString["id_token"] ?? string.Empty;
                    accessToken = request.QueryString["access_token"] ?? string.Empty;
                    returnedState = request.QueryString["state"] ?? string.Empty;
                    error = request.QueryString["error"] ?? string.Empty;

                    SendHtmlResponse(response, "Signed In", "<h2>Signed in</h2><p>You can close this tab and return to Wadd.</p>");
                    break;
                }
                else
                {
                    SendHtmlBridgePage(response);
                }
            }

            if (!string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException($"Google auth error: {error}");
            }

            string googleAccessToken = accessToken;
            string googleRefreshToken = string.Empty;
            string googleIdToken = idToken;

            // Exchange authorization code if present (via Cloudflare Worker proxy or direct endpoint)
            if (!string.IsNullOrWhiteSpace(code) && hasOAuthBackend)
            {
                try
                {
                    var tokenResult = await ExchangeAuthorizationCodeAsync(code, codeVerifier, redirectUri, cancellationToken);
                    googleAccessToken = tokenResult.AccessToken;
                    googleRefreshToken = tokenResult.RefreshToken;
                    if (!string.IsNullOrWhiteSpace(tokenResult.IdToken))
                    {
                        googleIdToken = tokenResult.IdToken;
                    }
                    expiresInSeconds = tokenResult.ExpiresIn;
                }
                catch (Exception ex)
                {
                    Wadd.Core.Logging.AppLogger.LogWarning("GoogleDriveSyncService", "Authorization code exchange failed", ex);
                    if (string.IsNullOrWhiteSpace(googleAccessToken))
                    {
                        throw;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(googleAccessToken))
            {
                throw new InvalidOperationException("Failed to obtain Google access token. Please verify your Google Client ID and try again.");
            }

            var userInfo = await FetchUserInfoAsync(googleAccessToken, cancellationToken);
            var fbSession = !string.IsNullOrWhiteSpace(googleIdToken)
                ? await ExchangeGoogleIdTokenWithFirebaseAsync(googleIdToken, googleAccessToken, redirectUri, cancellationToken)
                : (FirebaseIdToken: string.Empty, FirebaseRefreshToken: string.Empty, LocalId: string.Empty, Email: userInfo.Email, DisplayName: userInfo.Name, OAuthAccessToken: googleAccessToken);

            _authRecord = new UserAuthRecord
            {
                IsSignedIn = true,
                UserEmail = !string.IsNullOrWhiteSpace(userInfo.Email) ? userInfo.Email : fbSession.Email,
                UserName = !string.IsNullOrWhiteSpace(userInfo.Name) ? userInfo.Name : fbSession.DisplayName,
                GoogleClientId = GoogleClientId,
                GoogleClientSecret = GoogleClientSecret,
                OAuthProxyUrl = OAuthProxyUrl,
                OAuthProxySecret = OAuthProxySecret,
                FirebaseApiKey = FirebaseApiKey,
                FirebaseProjectId = FirebaseProjectId,
                AccessToken = googleAccessToken,
                RefreshToken = !string.IsNullOrWhiteSpace(googleRefreshToken) ? googleRefreshToken : (_authRecord?.RefreshToken ?? string.Empty),
                FirebaseIdToken = fbSession.FirebaseIdToken,
                FirebaseRefreshToken = fbSession.FirebaseRefreshToken,
                FirebaseLocalId = fbSession.LocalId,
                AuthenticatedAt = DateTime.UtcNow,
                TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(300, expiresInSeconds - 60))
            };

            SaveAuthRecord();
            NotifyAuthStateChanged();
            return true;
        }
        finally
        {
            try { listener.Stop(); } catch { }
        }
    }

    private static (string CodeVerifier, string CodeChallenge) GeneratePkceCodes()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var codeVerifier = Base64UrlEncode(bytes);

        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var codeChallenge = Base64UrlEncode(challengeBytes);

        return (codeVerifier, codeChallenge);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private async Task<(string AccessToken, string RefreshToken, string IdToken, int ExpiresIn)> ExchangeAuthorizationCodeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        // 1. If Cloudflare Worker OAuth proxy is configured, delegate token exchange to the proxy
        if (!string.IsNullOrWhiteSpace(OAuthProxyUrl))
        {
            var proxyUrl = $"{OAuthProxyUrl}/api/auth/token";
            var payload = new
            {
                code = code,
                code_verifier = codeVerifier,
                redirect_uri = redirectUri,
                client_id = GoogleClientId
            };

            using var proxyReq = new HttpRequestMessage(HttpMethod.Post, proxyUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(OAuthProxySecret))
            {
                proxyReq.Headers.Add("x-app-secret", OAuthProxySecret);
            }

            var proxyResp = await _httpClient.SendAsync(proxyReq, cancellationToken);
            var proxyJson = await proxyResp.Content.ReadAsStringAsync(cancellationToken);

            if (!proxyResp.IsSuccessStatusCode)
            {
                Wadd.Core.Logging.AppLogger.LogError("GoogleDriveSyncService", $"OAuth proxy code exchange failed ({proxyResp.StatusCode}): {proxyJson}");
                throw new InvalidOperationException($"OAuth proxy code exchange failed: {proxyJson}");
            }

            using var proxyDoc = JsonDocument.Parse(proxyJson);
            var proxyRoot = proxyDoc.RootElement;
            var proxyAccessToken = proxyRoot.TryGetProperty("access_token", out var pat) ? pat.GetString() ?? "" : "";
            var proxyRefreshToken = proxyRoot.TryGetProperty("refresh_token", out var prt) ? prt.GetString() ?? "" : "";
            var proxyIdToken = proxyRoot.TryGetProperty("id_token", out var pit) ? pit.GetString() ?? "" : "";
            var proxyExpiresIn = proxyRoot.TryGetProperty("expires_in", out var pexp) ? pexp.GetInt32() : 3600;

            return (proxyAccessToken, proxyRefreshToken, proxyIdToken, proxyExpiresIn);
        }

        // 2. Direct Google OAuth token endpoint (used when client_secret is configured locally)
        var tokenUrl = "https://oauth2.googleapis.com/token";
        var dict = new Dictionary<string, string>
        {
            ["client_id"] = GoogleClientId,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        };

        if (!string.IsNullOrWhiteSpace(GoogleClientSecret))
        {
            dict["client_secret"] = GoogleClientSecret;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(dict)
        };

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Wadd.Core.Logging.AppLogger.LogError("GoogleDriveSyncService", $"Google OAuth code exchange failed ({response.StatusCode}): {json}");
            throw new InvalidOperationException($"Google OAuth code exchange failed: {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() ?? "" : "";
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "";
        var idToken = root.TryGetProperty("id_token", out var it) ? it.GetString() ?? "" : "";
        var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

        return (accessToken, refreshToken, idToken, expiresIn);
    }

    private static void SendHtmlResponse(HttpListenerResponse response, string title, string bodyHtml)
    {
        var html = $@"<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'/>
  <title>{title}</title>
  <style>
    body {{
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
      background: #ffffff;
      color: #0f172a;
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      margin: 0;
    }}
    @media (prefers-color-scheme: dark) {{
      body {{ background: #0f172a; color: #f8fafc; }}
      p {{ color: #94a3b8 !important; }}
    }}
    .card {{ text-align: center; padding: 24px; max-width: 320px; }}
    h2 {{ font-size: 1.25rem; font-weight: 600; margin: 0 0 6px 0; }}
    p {{ color: #64748b; font-size: 0.875rem; margin: 0; }}
  </style>
</head>
<body>
  <div class='card'>
    {bodyHtml}
  </div>
</body>
</html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Close();
    }

    private static void SendHtmlBridgePage(HttpListenerResponse response)
    {
        var html = @"<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'/>
  <title>Wadd</title>
  <style>
    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
      background: #ffffff;
      color: #0f172a;
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      margin: 0;
    }
    @media (prefers-color-scheme: dark) {
      body { background: #0f172a; color: #f8fafc; }
      p { color: #94a3b8 !important; }
    }
    .card { text-align: center; padding: 24px; max-width: 320px; }
    h2 { font-size: 1.25rem; font-weight: 600; margin: 0 0 6px 0; }
    p { color: #64748b; font-size: 0.875rem; margin: 0; }
  </style>
</head>
<body>
  <div class='card'>
    <h2>Connecting...</h2>
    <p>Please wait.</p>
  </div>
  <script>
    var params = new URLSearchParams(window.location.search);
    var hash = new URLSearchParams(window.location.hash.substring(1));
    var idToken = params.get('id_token') || hash.get('id_token') || '';
    var accessToken = params.get('access_token') || hash.get('access_token') || '';
    var code = params.get('code') || hash.get('code') || '';
    var state = params.get('state') || hash.get('state') || '';
    var error = params.get('error') || hash.get('error') || '';
    var expiresIn = params.get('expires_in') || hash.get('expires_in') || '3600';

    fetch('/callback?id_token=' + encodeURIComponent(idToken) + '&access_token=' + encodeURIComponent(accessToken) + '&code=' + encodeURIComponent(code) + '&state=' + encodeURIComponent(state) + '&expires_in=' + encodeURIComponent(expiresIn) + '&error=' + encodeURIComponent(error))
      .then(function() {
        document.body.innerHTML = ""<div class='card'><h2>Signed in</h2><p>You can close this tab and return to Wadd.</p></div>"";
      });
  </script>
</body>
</html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Close();
    }

    private async Task<(string FirebaseIdToken, string FirebaseRefreshToken, string LocalId, string Email, string DisplayName, string OAuthAccessToken)> ExchangeGoogleIdTokenWithFirebaseAsync(string googleIdToken, string googleAccessToken, string requestUri, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(FirebaseApiKey) || (string.IsNullOrWhiteSpace(googleIdToken) && string.IsNullOrWhiteSpace(googleAccessToken)))
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        try
        {
            var firebaseUrl = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key={Uri.EscapeDataString(FirebaseApiKey)}";
            var postBody = !string.IsNullOrWhiteSpace(googleIdToken)
                ? $"id_token={googleIdToken}&providerId=google.com"
                : $"access_token={googleAccessToken}&providerId=google.com";

            var payload = new
            {
                postBody = postBody,
                requestUri = requestUri,
                returnIdpCredential = true,
                returnSecureToken = true
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, firebaseUrl)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;
                var fbIdToken = root.TryGetProperty("idToken", out var idp) ? idp.GetString() ?? "" : "";
                var fbRefreshToken = root.TryGetProperty("refreshToken", out var rfp) ? rfp.GetString() ?? "" : "";
                var localId = root.TryGetProperty("localId", out var lidp) ? lidp.GetString() ?? "" : "";
                var email = root.TryGetProperty("email", out var ep) ? ep.GetString() ?? "" : "";
                var displayName = root.TryGetProperty("displayName", out var dnp) ? dnp.GetString() ?? "" : "";
                var oauthAccessToken = root.TryGetProperty("oauthAccessToken", out var oatp) ? oatp.GetString() ?? "" : "";

                return (fbIdToken, fbRefreshToken, localId, email, displayName, oauthAccessToken);
            }
        }
        catch { }

        return (string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
    }

    private async Task<(string Email, string Name)> FetchUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        var userInfoUrl = "https://www.googleapis.com/oauth2/v2/userinfo";
        using var request = new HttpRequestMessage(HttpMethod.Get, userInfoUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return ("user@google.com", "Google User");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var email = root.TryGetProperty("email", out var emailProp) ? emailProp.GetString() ?? "user@google.com" : "user@google.com";
        var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Google User" : "Google User";

        return (email, name);
    }

    private async Task<bool> TryRefreshTokenAsync(CancellationToken cancellationToken)
    {
        if (_nativeAuthService != null && _nativeAuthService.IsSupported)
        {
            try
            {
                var nativeResult = await _nativeAuthService.SignInAsync(GoogleClientId, cancellationToken);
                if (nativeResult.IsSuccess && !string.IsNullOrWhiteSpace(nativeResult.AccessToken))
                {
                    _authRecord ??= new UserAuthRecord();
                    _authRecord.AccessToken = nativeResult.AccessToken;
                    _authRecord.TokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(50);
                    SaveAuthRecord();
                    return true;
                }
            }
            catch { }
        }

        if (_authRecord == null) return false;

        // Refresh Google Drive Access Token via Cloudflare Worker OAuth Proxy or Google OAuth Refresh Token
        if (!string.IsNullOrWhiteSpace(_authRecord.RefreshToken))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(OAuthProxyUrl))
                {
                    var proxyRefreshUrl = $"{OAuthProxyUrl}/api/auth/refresh";
                    var payload = new
                    {
                        refresh_token = _authRecord.RefreshToken,
                        client_id = GoogleClientId
                    };

                    using var proxyReq = new HttpRequestMessage(HttpMethod.Post, proxyRefreshUrl)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                    };

                    if (!string.IsNullOrWhiteSpace(OAuthProxySecret))
                    {
                        proxyReq.Headers.Add("x-app-secret", OAuthProxySecret);
                    }

                    var proxyResp = await _httpClient.SendAsync(proxyReq, cancellationToken);
                    var proxyJson = await proxyResp.Content.ReadAsStringAsync(cancellationToken);

                    if (proxyResp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(proxyJson);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("access_token", out var atProp))
                        {
                            _authRecord.AccessToken = atProp.GetString() ?? string.Empty;
                            if (root.TryGetProperty("refresh_token", out var newRtProp) && !string.IsNullOrWhiteSpace(newRtProp.GetString()))
                            {
                                _authRecord.RefreshToken = newRtProp.GetString()!;
                            }
                            var expSec = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
                            _authRecord.TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(300, expSec - 60));
                            SaveAuthRecord();
                            return true;
                        }
                    }
                    else
                    {
                        Wadd.Core.Logging.AppLogger.LogWarning("GoogleDriveSyncService", $"OAuth proxy token refresh failed ({proxyResp.StatusCode}): {proxyJson}");
                    }
                }
                else
                {
                    var tokenUrl = "https://oauth2.googleapis.com/token";
                    var dict = new Dictionary<string, string>
                    {
                        ["client_id"] = GoogleClientId,
                        ["refresh_token"] = _authRecord.RefreshToken,
                        ["grant_type"] = "refresh_token"
                    };

                    if (!string.IsNullOrWhiteSpace(GoogleClientSecret))
                    {
                        dict["client_secret"] = GoogleClientSecret;
                    }

                    using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
                    {
                        Content = new FormUrlEncodedContent(dict)
                    };

                    var response = await _httpClient.SendAsync(request, cancellationToken);
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("access_token", out var atProp))
                        {
                            _authRecord.AccessToken = atProp.GetString() ?? string.Empty;
                            if (root.TryGetProperty("refresh_token", out var newRtProp) && !string.IsNullOrWhiteSpace(newRtProp.GetString()))
                            {
                                _authRecord.RefreshToken = newRtProp.GetString()!;
                            }
                            var expSec = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
                            _authRecord.TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(300, expSec - 60));
                            SaveAuthRecord();
                            return true;
                        }
                    }
                    else
                    {
                        Wadd.Core.Logging.AppLogger.LogWarning("GoogleDriveSyncService", $"Google token refresh failed ({response.StatusCode}): {json}");
                    }
                }
            }
            catch (Exception ex)
            {
                Wadd.Core.Logging.AppLogger.LogWarning("GoogleDriveSyncService", "Exception during Google token refresh", ex);
            }
        }

        // Also refresh Firebase session if Firebase API key and refresh token are present
        if (!string.IsNullOrWhiteSpace(_authRecord.FirebaseRefreshToken) && !string.IsNullOrWhiteSpace(FirebaseApiKey))
        {
            try
            {
                var fbTokenUrl = $"https://securetoken.googleapis.com/v1/token?key={Uri.EscapeDataString(FirebaseApiKey)}";
                var dict = new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = _authRecord.FirebaseRefreshToken
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, fbTokenUrl)
                {
                    Content = new FormUrlEncodedContent(dict)
                };

                var response = await _httpClient.SendAsync(request, cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("id_token", out var idProp))
                    {
                        _authRecord.FirebaseIdToken = idProp.GetString() ?? string.Empty;
                    }
                    if (root.TryGetProperty("refresh_token", out var rtProp))
                    {
                        _authRecord.FirebaseRefreshToken = rtProp.GetString() ?? _authRecord.FirebaseRefreshToken;
                    }
                    if (root.TryGetProperty("user_id", out var uidProp))
                    {
                        _authRecord.FirebaseLocalId = uidProp.GetString() ?? _authRecord.FirebaseLocalId;
                    }
                    SaveAuthRecord();
                }
            }
            catch (Exception ex)
            {
                Wadd.Core.Logging.AppLogger.LogWarning("GoogleDriveSyncService", "Exception during Firebase token refresh", ex);
            }
        }

        return false;
    }

    private static void OpenBrowserUrl(string url)
    {
        try
        {
            Avalonia.Controls.TopLevel? topLevel = null;

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                topLevel = Avalonia.Controls.TopLevel.GetTopLevel(desktop.MainWindow);
            }
            else if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime singleView)
            {
                topLevel = Avalonia.Controls.TopLevel.GetTopLevel(singleView.MainView);
            }

            if (topLevel?.Launcher != null)
            {
                topLevel.Launcher.LaunchUriAsync(new Uri(url));
                return;
            }
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogWarning("GoogleDriveSyncService", "Avalonia TopLevel.Launcher failed, falling back to process launcher", ex);
        }

        if (OperatingSystem.IsAndroid())
        {
            try
            {
                var appClass = Type.GetType("Android.App.Application, Mono.Android");
                var contextProp = appClass?.GetProperty("Context", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var context = contextProp?.GetValue(null);

                var intentClass = Type.GetType("Android.Content.Intent, Mono.Android");
                var uriClass = Type.GetType("Android.Net.Uri, Mono.Android");

                var parseMethod = uriClass?.GetMethod("Parse", new[] { typeof(string) });
                var uriObj = parseMethod?.Invoke(null, new object[] { url });

                var actionView = intentClass?.GetField("ActionView")?.GetValue(null);
                var intent = Activator.CreateInstance(intentClass!, new object[] { actionView!, uriObj! });

                var addFlagsMethod = intentClass?.GetMethod("AddFlags");
                var flagNewTask = intentClass?.GetField("FlagsNewTask")?.GetValue(null);
                if (addFlagsMethod != null && flagNewTask != null)
                {
                    addFlagsMethod.Invoke(intent, new object[] { flagNewTask });
                }

                var startActivityMethod = context?.GetType().GetMethod("StartActivity", new[] { intentClass! });
                startActivityMethod?.Invoke(context, new object[] { intent! });
                return;
            }
            catch (Exception ex)
            {
                Wadd.Core.Logging.AppLogger.LogWarning("GoogleDriveSyncService", "Native Android intent launch failed", ex);
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var escapedUrl = url.Replace("&", "^&");
                Process.Start(new ProcessStartInfo("cmd", $"/c start {escapedUrl}") { CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken);
        _authRecord = null;
        _lastSyncTimestampUtc = DateTime.MinValue;
        if (File.Exists(_authFilePath))
        {
            try { File.Delete(_authFilePath); } catch { }
        }
        NotifyAuthStateChanged();
    }

    // =========================================================================
    //  OFFLINE-FIRST INCREMENTAL GOOGLE DRIVE SYNCHRONIZATION ENGINE
    // =========================================================================

    public static readonly TimeSpan TombstoneRetentionWindow = TimeSpan.FromDays(90);

    public const string TasksManifestFilename = "tasks.json";
    public const string GoalsManifestFilename = "goals.json";
    public const string MilestonesManifestFilename = "milestones.json";
    public const string JournalsManifestFilename = "journals.json";
    public const string DateNotesManifestFilename = "datenotes.json";

    private static readonly object _sharedBackoffLock = new();
    private static DateTime _sharedBackoffUntilUtc = DateTime.MinValue;

    private static DateTime EnsureUtc(DateTime dt) => ConflictResolutionEngine.EnsureUtc(dt);

    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly SemaphoreSlim _dbWriteLock = new(1, 1);
    private Timer? _debounceTimer;
    private readonly HashSet<Guid> _pendingPushQueue = new();
    private DateTime _lastSyncTimestampUtc = DateTime.MinValue;

    public void EnqueueLocalMutation(Guid taskId)
    {
        lock (_pendingPushQueue)
        {
            _pendingPushQueue.Add(taskId);
        }
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ =>
        {
            _ = Task.Run(async () =>
            {
                try { await SyncAsync(); } catch { }
            });
        }, null, 4000, Timeout.Infinite);
    }

    public async Task<bool> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSignedIn || _authRecord == null)
        {
            throw new InvalidOperationException("Please sign in with Google in Settings to synchronize your data.");
        }

        // Proactive token refresh if token is expired or close to expiring (within 2 minutes)
        if (_authRecord.TokenExpiresAtUtc.HasValue && DateTime.UtcNow >= _authRecord.TokenExpiresAtUtc.Value.AddMinutes(-2))
        {
            await TryRefreshTokenAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(_authRecord.AccessToken))
        {
            if (!string.IsNullOrWhiteSpace(_authRecord.RefreshToken))
            {
                await TryRefreshTokenAsync(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(_authRecord.AccessToken))
            {
                throw new InvalidOperationException("Google authorization token missing. Please click 'Connect Google Drive Account' in Settings.");
            }
        }

        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var syncStartUtc = DateTime.UtcNow;
            var token = _authRecord.AccessToken;

            // 1. Fetch full remote file metadata list from Google Drive appDataFolder (1 single HTTP GET request ~150-250ms)
            var remoteFiles = await ListAppDataFolderFilesAsync(token, cancellationToken);
            remoteFiles = await DeduplicateRemoteFilesAsync(token, remoteFiles, cancellationToken);
            var remoteFileMap = remoteFiles.ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);

            // 2. High-speed Early-Exit Short-Circuit (Op 2)
            if (_lastSyncTimestampUtc > DateTime.MinValue)
            {
                var manifestNames = new[] { TasksManifestFilename, GoalsManifestFilename, MilestonesManifestFilename, JournalsManifestFilename, DateNotesManifestFilename };
                bool hasRemoteChanges = _authRecord?.MigratedToManifests == true
                    ? remoteFiles.Any(f => manifestNames.Contains(f.Name, StringComparer.OrdinalIgnoreCase) && f.ModifiedTime.HasValue && f.ModifiedTime.Value > _lastSyncTimestampUtc)
                    : remoteFiles.Any(f => f.ModifiedTime.HasValue && f.ModifiedTime.Value > _lastSyncTimestampUtc);

                bool hasLocalPending = false;
                lock (_pendingPushQueue)
                {
                    hasLocalPending = _pendingPushQueue.Count > 0;
                }

                if (!hasLocalPending && _syncLogRepository != null)
                {
                    var pendingLogs = await _syncLogRepository.GetPendingLogsAsync(cancellationToken);
                    hasLocalPending = pendingLogs.Any();
                }

                if (!hasRemoteChanges && !hasLocalPending)
                {
                    bool hasLocalEntityChanges = false;
                    if (_goalService != null)
                    {
                        var localGoals = await _goalService.GetAllGoalsRawAsync(cancellationToken);
                        if (localGoals.Any(g => EnsureUtc(g.UpdatedAt ?? g.CreatedAt) > _lastSyncTimestampUtc))
                        {
                            hasLocalEntityChanges = true;
                        }
                        else
                        {
                            var localMilestones = await _goalService.GetAllMilestonesRawAsync(cancellationToken);
                            if (localMilestones.Any(m => EnsureUtc(m.UpdatedAt ?? DateTime.MinValue) > _lastSyncTimestampUtc))
                            {
                                hasLocalEntityChanges = true;
                            }
                            else
                            {
                                var localJournals = await _goalService.GetAllJournalEntriesRawAsync(cancellationToken);
                                if (localJournals.Any(j => EnsureUtc(j.UpdatedAt ?? j.EntryDate) > _lastSyncTimestampUtc))
                                {
                                    hasLocalEntityChanges = true;
                                }
                            }
                        }
                    }

                    if (!hasLocalEntityChanges)
                    {
                        var localNotes = await _todoService.GetAllDateNotesRawAsync(cancellationToken);
                        if (localNotes.Any(n => EnsureUtc(n.UpdatedAt) > _lastSyncTimestampUtc))
                        {
                            hasLocalEntityChanges = true;
                        }
                    }

                    if (!hasLocalEntityChanges)
                    {
                        // Zero changes detected on both Drive and local SQLite: early exit!
                        HasChangesApplied = false;
                        _lastSyncTimestampUtc = DateTime.UtcNow;
                        if (_authRecord != null)
                        {
                            _authRecord.LastSyncTimestampUtc = _lastSyncTimestampUtc;
                            SaveAuthRecord();
                        }
                        await RefreshConflictCountAsync(cancellationToken);
                        return true;
                    }
                }
            }

            var pendingLocalTaskIds = new HashSet<Guid>();
            lock (_pendingPushQueue)
            {
                foreach (var id in _pendingPushQueue) pendingLocalTaskIds.Add(id);
                _pendingPushQueue.Clear();
            }

            var rawSqliteSvc = _todoService as SQLiteTodoService;
            if (rawSqliteSvc != null && _syncLogRepository != null)
            {
                var pendingLogs = await _syncLogRepository.GetPendingLogsAsync(cancellationToken);
                foreach (var log in pendingLogs)
                {
                    pendingLocalTaskIds.Add(log.RecordId);
                }
            }

            var fileIdMap = new ConcurrentDictionary<string, string>(
                remoteFileMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Id, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            // One-time backward-compatible migration of legacy per-record files to consolidated manifests (Issue #3)
            await EnsureManifestMigrationAsync(token, remoteFiles, remoteFileMap, fileIdMap, cancellationToken);

            // 3. Process all 5 entity sync modules in parallel (Op 1)
            var syncTasks = new[]
            {
                SyncTasksAsync(token, remoteFileMap, fileIdMap, pendingLocalTaskIds, cancellationToken),
                SyncGoalsAsync(token, remoteFileMap, fileIdMap, cancellationToken),
                SyncMilestonesAsync(token, remoteFileMap, fileIdMap, cancellationToken),
                SyncJournalEntriesAsync(token, remoteFileMap, fileIdMap, cancellationToken),
                SyncDateNotesAsync(token, remoteFileMap, fileIdMap, cancellationToken)
            };

            var results = await Task.WhenAll(syncTasks);
            HasChangesApplied = results.Any(r => r);

            // Prune expired tombstones older than retention window (Issue #2)
            await PruneExpiredCloudTombstonesAsync(token, remoteFileMap, fileIdMap, cancellationToken);

            var maxRemoteMod = remoteFiles.Where(f => f.ModifiedTime.HasValue).Select(f => f.ModifiedTime!.Value).DefaultIfEmpty(DateTime.MinValue).Max();
            _lastSyncTimestampUtc = DateTime.UtcNow > maxRemoteMod ? DateTime.UtcNow : maxRemoteMod;
            if (_authRecord != null)
            {
                _authRecord.LastSyncTimestampUtc = _lastSyncTimestampUtc;
                SaveAuthRecord();
            }
            await RefreshConflictCountAsync(cancellationToken);
            return true;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    internal async Task EnsureManifestMigrationAsync(
        string token,
        List<DriveFileItem> remoteFiles,
        Dictionary<string, DriveFileItem> remoteFileMap,
        IDictionary<string, string> fileIdMap,
        CancellationToken cancellationToken)
    {
        if (_authRecord?.MigratedToManifests == true) return;

        var legacyFiles = remoteFiles.Where(f =>
            (f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(f.Name[..^5], out _)) ||
            f.Name.StartsWith("goal_", StringComparison.OrdinalIgnoreCase) ||
            f.Name.StartsWith("milestone_", StringComparison.OrdinalIgnoreCase) ||
            f.Name.StartsWith("journal_", StringComparison.OrdinalIgnoreCase) ||
            f.Name.StartsWith("datenote_", StringComparison.OrdinalIgnoreCase)
        ).ToList();

        if (legacyFiles.Count == 0)
        {
            if (_authRecord != null)
            {
                _authRecord.MigratedToManifests = true;
                SaveAuthRecord();
            }
            return;
        }

        Wadd.Core.Logging.AppLogger.LogInfo("GoogleDriveSyncService", $"Starting one-time migration of {legacyFiles.Count} legacy per-record files to consolidated manifests...");

        var rawSqliteSvc = _todoService as SQLiteTodoService;
        var downloadSemaphore = new SemaphoreSlim(16);
        var downloadedTasks = new ConcurrentBag<TodoItem>();
        var downloadedGoals = new ConcurrentBag<LifeGoal>();
        var downloadedMilestones = new ConcurrentBag<GoalMilestone>();
        var downloadedJournals = new ConcurrentBag<JournalEntry>();
        var downloadedDateNotes = new ConcurrentBag<CalendarDateNote>();

        var downloadTasks = legacyFiles.Select(async file =>
        {
            await downloadSemaphore.WaitAsync(cancellationToken);
            try
            {
                var content = await DownloadAppDataFileAsync(token, file.Id, cancellationToken);
                if (string.IsNullOrWhiteSpace(content)) return;

                if (file.Name.StartsWith("goal_", StringComparison.OrdinalIgnoreCase))
                {
                    var g = JsonSerializer.Deserialize<LifeGoal>(content, JsonOptions);
                    if (g != null) downloadedGoals.Add(g);
                }
                else if (file.Name.StartsWith("milestone_", StringComparison.OrdinalIgnoreCase))
                {
                    var m = JsonSerializer.Deserialize<GoalMilestone>(content, JsonOptions);
                    if (m != null) downloadedMilestones.Add(m);
                }
                else if (file.Name.StartsWith("journal_", StringComparison.OrdinalIgnoreCase))
                {
                    var j = JsonSerializer.Deserialize<JournalEntry>(content, JsonOptions);
                    if (j != null) downloadedJournals.Add(j);
                }
                else if (file.Name.StartsWith("datenote_", StringComparison.OrdinalIgnoreCase))
                {
                    var n = JsonSerializer.Deserialize<CalendarDateNote>(content, JsonOptions);
                    if (n != null) downloadedDateNotes.Add(n);
                }
                else if (Guid.TryParse(file.Name[..^5], out _))
                {
                    var t = JsonSerializer.Deserialize<TodoItem>(content, JsonOptions);
                    if (t != null) downloadedTasks.Add(t);
                }
            }
            catch { }
            finally
            {
                downloadSemaphore.Release();
            }
        });

        await Task.WhenAll(downloadTasks);

        // Merge downloaded items into SQLite
        await _dbWriteLock.WaitAsync(cancellationToken);
        try
        {
            if (downloadedTasks.Count > 0)
            {
                var localTasks = rawSqliteSvc != null
                    ? (await rawSqliteSvc.GetAllRawAsync(cancellationToken)).ToDictionary(t => t.Id)
                    : (await _todoService.GetTodosAsync(cancellationToken)).ToDictionary(t => t.Id);

                var mergedTasks = new List<TodoItem>();
                foreach (var dt in downloadedTasks)
                {
                    localTasks.TryGetValue(dt.Id, out var local);
                    mergedTasks.Add(ConflictResolutionEngine.MergeTask(local, dt));
                }

                if (rawSqliteSvc != null)
                {
                    await rawSqliteSvc.BatchDirectUpsertFromSyncAsync(mergedTasks, cancellationToken);
                }
                else
                {
                    foreach (var t in mergedTasks)
                    {
                        if (localTasks.ContainsKey(t.Id)) await _todoService.UpdateTodoAsync(t, cancellationToken);
                        else await _todoService.AddTodoAsync(t, cancellationToken);
                    }
                }
            }

            if (_goalService != null)
            {
                if (downloadedGoals.Count > 0)
                {
                    var localGoals = (await _goalService.GetAllGoalsRawAsync(cancellationToken)).ToDictionary(g => g.Id);
                    var mergedGoals = downloadedGoals.Select(dg =>
                    {
                        localGoals.TryGetValue(dg.Id, out var local);
                        return ConflictResolutionEngine.MergeGoal(local, dg);
                    }).ToList();
                    await _goalService.BatchDirectUpsertGoalsAsync(mergedGoals, cancellationToken);
                }

                if (downloadedMilestones.Count > 0)
                {
                    var localMilestones = (await _goalService.GetAllMilestonesRawAsync(cancellationToken)).ToDictionary(m => m.Id);
                    var mergedMilestones = downloadedMilestones.Select(dm =>
                    {
                        localMilestones.TryGetValue(dm.Id, out var local);
                        return ConflictResolutionEngine.MergeMilestone(local, dm);
                    }).ToList();
                    await _goalService.BatchDirectUpsertMilestonesAsync(mergedMilestones, cancellationToken);
                }

                if (downloadedJournals.Count > 0)
                {
                    var localJournals = (await _goalService.GetAllJournalEntriesRawAsync(cancellationToken)).ToDictionary(j => j.Id);
                    var mergedJournals = downloadedJournals.Select(dj =>
                    {
                        localJournals.TryGetValue(dj.Id, out var local);
                        return ConflictResolutionEngine.MergeJournalEntry(local, dj);
                    }).ToList();
                    await _goalService.BatchDirectUpsertJournalEntriesAsync(mergedJournals, cancellationToken);
                }
            }

            if (downloadedDateNotes.Count > 0)
            {
                var localNotes = (await _todoService.GetAllDateNotesRawAsync(cancellationToken)).ToDictionary(n => n.DateKey);
                var mergedNotes = downloadedDateNotes.Select(dn =>
                {
                    localNotes.TryGetValue(dn.DateKey, out var local);
                    return ConflictResolutionEngine.MergeDateNote(local, dn);
                }).ToList();
                await _todoService.BatchDirectUpsertDateNotesAsync(mergedNotes, cancellationToken);
            }
        }
        finally
        {
            _dbWriteLock.Release();
        }

        // Delete legacy files from Google Drive without concurrent dictionary mutations
        var deletedFileNames = new ConcurrentBag<string>();
        var deleteTasks = legacyFiles.Select(async file =>
        {
            await downloadSemaphore.WaitAsync(cancellationToken);
            try
            {
                bool deleted = await DeleteAppDataFileAsync(token, file.Id, file.Name, cancellationToken);
                if (deleted)
                {
                    deletedFileNames.Add(file.Name);
                }
                fileIdMap.Remove(file.Name);
            }
            catch { }
            finally
            {
                downloadSemaphore.Release();
            }
        });
        await Task.WhenAll(deleteTasks);

        foreach (var name in deletedFileNames)
        {
            remoteFileMap.Remove(name);
        }

        // Upload initial consolidated manifests with merged content
        var cutoff = DateTime.UtcNow - TombstoneRetentionWindow;

        // 1. Tasks Manifest
        var allLocalTasks = rawSqliteSvc != null
            ? (await rawSqliteSvc.GetAllRawAsync(cancellationToken)).ToList()
            : (await _todoService.GetTodosAsync(cancellationToken)).ToList();
        var tasksToUpload = allLocalTasks
            .Where(t => !t.IsDeleted || (t.DeletedAt.HasValue && t.DeletedAt.Value >= cutoff))
            .ToList();
        await UploadJsonFileToAppDataFolderAsync(token, TasksManifestFilename, JsonSerializer.Serialize(tasksToUpload, JsonOptions), fileIdMap, "Tasks Manifest", cancellationToken);
        if (fileIdMap.TryGetValue(TasksManifestFilename, out var tasksFileId))
        {
            remoteFileMap[TasksManifestFilename] = new DriveFileItem(tasksFileId, TasksManifestFilename, DateTime.UtcNow);
        }

        // 2. Goals & Milestones & Journals Manifests
        if (_goalService != null)
        {
            var allGoals = (await _goalService.GetAllGoalsRawAsync(cancellationToken)).ToList();
            var goalsToUpload = allGoals
                .Where(g => !g.IsDeleted || (g.DeletedAt.HasValue && g.DeletedAt.Value >= cutoff))
                .ToList();
            await UploadJsonFileToAppDataFolderAsync(token, GoalsManifestFilename, JsonSerializer.Serialize(goalsToUpload, JsonOptions), fileIdMap, "Goals Manifest", cancellationToken);
            if (fileIdMap.TryGetValue(GoalsManifestFilename, out var goalsFileId))
            {
                remoteFileMap[GoalsManifestFilename] = new DriveFileItem(goalsFileId, GoalsManifestFilename, DateTime.UtcNow);
            }

            var allMilestones = (await _goalService.GetAllMilestonesRawAsync(cancellationToken)).ToList();
            var milestonesToUpload = allMilestones
                .Where(m => !m.IsDeleted || (m.DeletedAt.HasValue && m.DeletedAt.Value >= cutoff))
                .ToList();
            await UploadJsonFileToAppDataFolderAsync(token, MilestonesManifestFilename, JsonSerializer.Serialize(milestonesToUpload, JsonOptions), fileIdMap, "Milestones Manifest", cancellationToken);
            if (fileIdMap.TryGetValue(MilestonesManifestFilename, out var mFileId))
            {
                remoteFileMap[MilestonesManifestFilename] = new DriveFileItem(mFileId, MilestonesManifestFilename, DateTime.UtcNow);
            }

            var allJournals = (await _goalService.GetAllJournalEntriesRawAsync(cancellationToken)).ToList();
            var journalsToUpload = allJournals
                .Where(j => !j.IsDeleted || (j.DeletedAt.HasValue && j.DeletedAt.Value >= cutoff))
                .ToList();
            await UploadJsonFileToAppDataFolderAsync(token, JournalsManifestFilename, JsonSerializer.Serialize(journalsToUpload, JsonOptions), fileIdMap, "Journals Manifest", cancellationToken);
            if (fileIdMap.TryGetValue(JournalsManifestFilename, out var jFileId))
            {
                remoteFileMap[JournalsManifestFilename] = new DriveFileItem(jFileId, JournalsManifestFilename, DateTime.UtcNow);
            }
        }

        // 3. DateNotes Manifest
        var allNotes = (await _todoService.GetAllDateNotesRawAsync(cancellationToken)).ToList();
        var notesToUpload = allNotes
            .Where(n => !n.IsDeleted || (n.DeletedAt.HasValue && n.DeletedAt.Value >= cutoff))
            .ToList();
        await UploadJsonFileToAppDataFolderAsync(token, DateNotesManifestFilename, JsonSerializer.Serialize(notesToUpload, JsonOptions), fileIdMap, "DateNotes Manifest", cancellationToken);
        if (fileIdMap.TryGetValue(DateNotesManifestFilename, out var dnFileId))
        {
            remoteFileMap[DateNotesManifestFilename] = new DriveFileItem(dnFileId, DateNotesManifestFilename, DateTime.UtcNow);
        }

        _lastSyncTimestampUtc = DateTime.UtcNow;

        if (_authRecord != null)
        {
            _authRecord.MigratedToManifests = true;
            SaveAuthRecord();
        }

        Wadd.Core.Logging.AppLogger.LogInfo("GoogleDriveSyncService", $"Migration to consolidated manifests completed successfully. Cleaned up {legacyFiles.Count} legacy files.");
    }

    private async Task<bool> SyncTasksAsync(
        string token,
        Dictionary<string, DriveFileItem> remoteFileMap,
        IDictionary<string, string> fileIdMap,
        HashSet<Guid> pendingLocalTaskIds,
        CancellationToken cancellationToken)
    {
        bool hasChanges = false;
        var rawSqliteSvc = _todoService as SQLiteTodoService;
        List<TodoItem> localTasksList = rawSqliteSvc != null
            ? (await rawSqliteSvc.GetAllRawAsync(cancellationToken)).ToList()
            : (await _todoService.GetTodosAsync(cancellationToken)).ToList();

        var localTasksMap = localTasksList.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());

        remoteFileMap.TryGetValue(TasksManifestFilename, out var remoteManifestFile);

        bool remoteChanged = remoteManifestFile != null &&
                             remoteManifestFile.ModifiedTime.HasValue &&
                             remoteManifestFile.ModifiedTime.Value > _lastSyncTimestampUtc;

        bool localChanged = pendingLocalTaskIds.Count > 0;
        if (!localChanged && _lastSyncTimestampUtc > DateTime.MinValue)
        {
            localChanged = localTasksList.Any(t => EnsureUtc(t.UpdatedAt ?? t.CreatedAt) > _lastSyncTimestampUtc);
        }

        // If manifest doesn't exist remotely yet: initial upload
        if (remoteManifestFile == null)
        {
            if (localTasksMap.Count > 0)
            {
                var cutoff = DateTime.UtcNow - TombstoneRetentionWindow;
                var toUpload = localTasksMap.Values
                    .Where(t => !t.IsDeleted || (t.DeletedAt.HasValue && t.DeletedAt.Value >= cutoff))
                    .ToList();

                await UploadJsonFileToAppDataFolderAsync(token, TasksManifestFilename, JsonSerializer.Serialize(toUpload, JsonOptions), fileIdMap, "Tasks Manifest", cancellationToken);
                hasChanges = true;
            }

            if (_syncLogRepository != null && pendingLocalTaskIds.Count > 0)
            {
                await _dbWriteLock.WaitAsync(cancellationToken);
                try
                {
                    var pendingLogs = await _syncLogRepository.GetPendingLogsAsync(cancellationToken);
                    var syncedLogIds = pendingLogs.Where(l => pendingLocalTaskIds.Contains(l.RecordId)).Select(l => l.Id).ToList();
                    if (syncedLogIds.Count > 0)
                    {
                        await _syncLogRepository.MarkLogsAsSyncedAsync(syncedLogIds, cancellationToken);
                    }
                }
                finally { _dbWriteLock.Release(); }
            }

            return hasChanges;
        }

        // Neither remote nor local changed: 0 HTTP calls!
        if (!remoteChanged && !localChanged)
        {
            return false;
        }

        // Remote changed or local changed: download remote manifest and merge
        List<TodoItem> remoteTasks = new();
        var remoteContent = await DownloadAppDataFileAsync(token, remoteManifestFile.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(remoteContent))
        {
            try
            {
                remoteTasks = JsonSerializer.Deserialize<List<TodoItem>>(remoteContent, JsonOptions) ?? new();
            }
            catch { }
        }

        var remoteTasksMap = remoteTasks.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
        var allIds = localTasksMap.Keys.Union(remoteTasksMap.Keys).ToList();

        var mergedTasks = new List<TodoItem>();
        var localUpserts = new List<TodoItem>();
        bool remoteNeedsUpload = localChanged;

        foreach (var id in allIds)
        {
            localTasksMap.TryGetValue(id, out var local);
            remoteTasksMap.TryGetValue(id, out var remote);

            var merged = ConflictResolutionEngine.MergeTask(local, remote);
            mergedTasks.Add(merged);

            // Did remote provide a new or newer task than local?
            if (local == null)
            {
                localUpserts.Add(merged);
            }
            else if (remote != null)
            {
                long localTicks = EnsureUtc(local.UpdatedAt ?? local.CreatedAt).Ticks;
                long remoteTicks = EnsureUtc(remote.UpdatedAt ?? remote.CreatedAt).Ticks;
                if (remoteTicks > localTicks || (local.IsDeleted != remote.IsDeleted && remote.IsDeleted))
                {
                    localUpserts.Add(merged);
                }
                else if (localTicks > remoteTicks || (local.IsDeleted != remote.IsDeleted && local.IsDeleted))
                {
                    remoteNeedsUpload = true;
                }
            }
            else
            {
                // Local task that remote does not have
                remoteNeedsUpload = true;
            }
        }

        if (localUpserts.Count > 0)
        {
            hasChanges = true;
            await _dbWriteLock.WaitAsync(cancellationToken);
            try
            {
                if (rawSqliteSvc != null)
                {
                    await rawSqliteSvc.BatchDirectUpsertFromSyncAsync(localUpserts, cancellationToken);
                }
                else
                {
                    foreach (var item in localUpserts)
                    {
                        if (localTasksMap.ContainsKey(item.Id))
                            await _todoService.UpdateTodoAsync(item, cancellationToken);
                        else
                            await _todoService.AddTodoAsync(item, cancellationToken);
                    }
                }
            }
            finally { _dbWriteLock.Release(); }
        }

        if (remoteNeedsUpload)
        {
            hasChanges = true;
            var cutoff = DateTime.UtcNow - TombstoneRetentionWindow;
            var toUpload = mergedTasks
                .Where(t => !t.IsDeleted || (t.DeletedAt.HasValue && t.DeletedAt.Value >= cutoff))
                .ToList();

            // Note: If an entity manifest ever grows extremely large (e.g. > 50,000 tasks / > 25MB),
            // a chunking strategy (tasks_part1.json, tasks_part2.json) could be introduced here.
            await UploadJsonFileToAppDataFolderAsync(token, TasksManifestFilename, JsonSerializer.Serialize(toUpload, JsonOptions), fileIdMap, "Tasks Manifest", cancellationToken);
        }

        if (_syncLogRepository != null && pendingLocalTaskIds.Count > 0)
        {
            await _dbWriteLock.WaitAsync(cancellationToken);
            try
            {
                var pendingLogs = await _syncLogRepository.GetPendingLogsAsync(cancellationToken);
                var syncedLogIds = pendingLogs.Where(l => pendingLocalTaskIds.Contains(l.RecordId)).Select(l => l.Id).ToList();
                if (syncedLogIds.Count > 0)
                {
                    await _syncLogRepository.MarkLogsAsSyncedAsync(syncedLogIds, cancellationToken);
                }
            }
            finally { _dbWriteLock.Release(); }
        }

        return hasChanges;
    }

    internal record DriveFileItem(string Id, string Name, DateTime? ModifiedTime);

    internal async Task<List<DriveFileItem>> ListAppDataFolderFilesAsync(string token, CancellationToken cancellationToken)
    {
        var result = new List<DriveFileItem>();
        string query = "trashed = false";
        string? pageToken = null;

        do
        {
            string url = $"https://www.googleapis.com/drive/v3/files?spaces=appDataFolder&q={Uri.EscapeDataString(query)}&fields=nextPageToken,files(id,name,modifiedTime)&pageSize=1000";
            if (!string.IsNullOrEmpty(pageToken))
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
            await EnsureDriveSuccessAsync(response, "List AppData Folder Files");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("files", out var filesArr))
                {
                    foreach (var file in filesArr.EnumerateArray())
                    {
                        var id = file.TryGetProperty("id", out var ip) ? ip.GetString() : null;
                        var name = file.TryGetProperty("name", out var np) ? np.GetString() : null;
                        DateTime? mod = null;
                        if (file.TryGetProperty("modifiedTime", out var mp) && DateTime.TryParse(mp.GetString(), out var dt))
                        {
                            mod = EnsureUtc(dt);
                        }
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        {
                            result.Add(new DriveFileItem(id, name, mod));
                        }
                    }
                }

                pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var npt) ? npt.GetString() : null;
            }
            else
            {
                break;
            }
        } while (!string.IsNullOrEmpty(pageToken));

        return result;
    }

    private async Task<bool> DeleteAppDataFileAsync(string token, string fileId, string itemDescription, CancellationToken cancellationToken)
    {
        string deleteUrl = $"https://www.googleapis.com/drive/v3/files/{fileId}";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Delete, deleteUrl), cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return true;
        }
        await EnsureDriveSuccessAsync(response, $"Delete {itemDescription} from AppData");
        return false;
    }

    internal async Task<List<DriveFileItem>> DeduplicateRemoteFilesAsync(
        string token,
        List<DriveFileItem> remoteFiles,
        CancellationToken cancellationToken)
    {
        var duplicateGroups = remoteFiles
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicateGroups.Count == 0)
        {
            return remoteFiles;
        }

        Wadd.Core.Logging.AppLogger.LogWarning("GoogleDriveSyncService", $"Detected {duplicateGroups.Count} duplicate filename group(s) in Google Drive AppData folder. Resolving duplicates...");

        var canonicalFiles = new List<DriveFileItem>();
        var manifestNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            TasksManifestFilename,
            GoalsManifestFilename,
            MilestonesManifestFilename,
            JournalsManifestFilename,
            DateNotesManifestFilename
        };

        var rawSqliteSvc = _todoService as SQLiteTodoService;

        foreach (var group in remoteFiles.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() == 1)
            {
                canonicalFiles.Add(group.First());
                continue;
            }

            // Order by ModifiedTime descending, keeping newest file as canonical
            var sorted = group.OrderByDescending(f => f.ModifiedTime ?? DateTime.MinValue).ToList();
            var canonical = sorted.First();
            var duplicates = sorted.Skip(1).ToList();
            canonicalFiles.Add(canonical);

            bool isManifest = manifestNames.Contains(group.Key);

            foreach (var dup in duplicates)
            {
                try
                {
                    // If it's a manifest file, download and merge its contents locally so no data is lost
                    if (isManifest)
                    {
                        var content = await DownloadAppDataFileAsync(token, dup.Id, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            await MergeManifestContentIntoLocalAsync(group.Key, content, rawSqliteSvc, cancellationToken);
                        }
                    }

                    // Delete the older duplicate from Google Drive
                    await DeleteAppDataFileAsync(token, dup.Id, $"{dup.Name} (duplicate)", cancellationToken);
                    Wadd.Core.Logging.AppLogger.LogInfo("GoogleDriveSyncService", $"Resolved and cleaned up duplicate '{dup.Name}' (ID: {dup.Id}) from Google Drive.");
                }
                catch (Exception ex)
                {
                    Wadd.Core.Logging.AppLogger.LogWarning("GoogleDriveSyncService", $"Failed to clean up duplicate '{dup.Name}' (ID: {dup.Id}): {ex.Message}");
                }
            }
        }

        return canonicalFiles;
    }

    private async Task MergeManifestContentIntoLocalAsync(string manifestName, string content, SQLiteTodoService? rawSqliteSvc, CancellationToken cancellationToken)
    {
        await _dbWriteLock.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(manifestName, TasksManifestFilename, StringComparison.OrdinalIgnoreCase))
            {
                var tasks = JsonSerializer.Deserialize<List<TodoItem>>(content, JsonOptions);
                if (tasks != null && tasks.Count > 0)
                {
                    if (rawSqliteSvc != null)
                    {
                        await rawSqliteSvc.BatchDirectUpsertFromSyncAsync(tasks, cancellationToken);
                    }
                    else
                    {
                        foreach (var t in tasks) await _todoService.UpdateTodoAsync(t, cancellationToken);
                    }
                }
            }
            else if (_goalService != null)
            {
                if (string.Equals(manifestName, GoalsManifestFilename, StringComparison.OrdinalIgnoreCase))
                {
                    var goals = JsonSerializer.Deserialize<List<LifeGoal>>(content, JsonOptions);
                    if (goals != null && goals.Count > 0)
                    {
                        await _goalService.BatchDirectUpsertGoalsAsync(goals, cancellationToken);
                    }
                }
                else if (string.Equals(manifestName, MilestonesManifestFilename, StringComparison.OrdinalIgnoreCase))
                {
                    var milestones = JsonSerializer.Deserialize<List<GoalMilestone>>(content, JsonOptions);
                    if (milestones != null && milestones.Count > 0)
                    {
                        await _goalService.BatchDirectUpsertMilestonesAsync(milestones, cancellationToken);
                    }
                }
                else if (string.Equals(manifestName, JournalsManifestFilename, StringComparison.OrdinalIgnoreCase))
                {
                    var journals = JsonSerializer.Deserialize<List<JournalEntry>>(content, JsonOptions);
                    if (journals != null && journals.Count > 0)
                    {
                        await _goalService.BatchDirectUpsertJournalEntriesAsync(journals, cancellationToken);
                    }
                }
            }
            else if (string.Equals(manifestName, DateNotesManifestFilename, StringComparison.OrdinalIgnoreCase))
            {
                var notes = JsonSerializer.Deserialize<List<CalendarDateNote>>(content, JsonOptions);
                if (notes != null && notes.Count > 0)
                {
                    await _todoService.BatchDirectUpsertDateNotesAsync(notes, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogWarning("GoogleDriveSyncService", $"Error merging duplicate manifest {manifestName}: {ex.Message}");
        }
        finally
        {
            _dbWriteLock.Release();
        }
    }

    internal async Task PruneExpiredCloudTombstonesAsync(
        string token,
        Dictionary<string, DriveFileItem> remoteFileMap,
        IDictionary<string, string> fileIdMap,
        CancellationToken cancellationToken)
    {
        try
        {
            var cutoff = DateTime.UtcNow - TombstoneRetentionWindow;

            // 1. Tasks Manifest
            var rawSqliteSvc = _todoService as SQLiteTodoService;
            var localTasks = rawSqliteSvc != null
                ? await rawSqliteSvc.GetAllRawAsync(cancellationToken)
                : await _todoService.GetTodosAsync(cancellationToken);

            bool hasExpiredTasks = localTasks.Any(t => t.IsDeleted && t.DeletedAt.HasValue && t.DeletedAt.Value < cutoff);
            if (hasExpiredTasks && remoteFileMap.TryGetValue(TasksManifestFilename, out var tasksFile))
            {
                var content = await DownloadAppDataFileAsync(token, tasksFile.Id, cancellationToken);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    try
                    {
                        var remoteList = JsonSerializer.Deserialize<List<TodoItem>>(content, JsonOptions) ?? new();
                        if (remoteList.Any(t => t.IsDeleted && t.DeletedAt.HasValue && t.DeletedAt.Value < cutoff))
                        {
                            var pruned = remoteList
                                .Where(t => !t.IsDeleted || (t.DeletedAt.HasValue && t.DeletedAt.Value >= cutoff))
                                .ToList();
                            await UploadJsonFileToAppDataFolderAsync(token, TasksManifestFilename, JsonSerializer.Serialize(pruned, JsonOptions), fileIdMap, "Tasks Manifest (Tombstone Pruning)", cancellationToken);
                            Wadd.Core.Logging.AppLogger.LogInfo("GoogleDriveSyncService", $"Pruned expired tombstones from {TasksManifestFilename} manifest.");
                        }
                    }
                    catch { }
                }
            }

            // 2. Goals & Milestones & Journals Manifests
            if (_goalService != null)
            {
                var localGoals = await _goalService.GetAllGoalsRawAsync(cancellationToken);
                bool hasExpiredGoals = localGoals.Any(g => g.IsDeleted && g.DeletedAt.HasValue && g.DeletedAt.Value < cutoff);
                if (hasExpiredGoals && remoteFileMap.TryGetValue(GoalsManifestFilename, out var goalsFile))
                {
                    var content = await DownloadAppDataFileAsync(token, goalsFile.Id, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        try
                        {
                            var remoteList = JsonSerializer.Deserialize<List<LifeGoal>>(content, JsonOptions) ?? new();
                            if (remoteList.Any(g => g.IsDeleted && g.DeletedAt.HasValue && g.DeletedAt.Value < cutoff))
                            {
                                var pruned = remoteList
                                    .Where(g => !g.IsDeleted || (g.DeletedAt.HasValue && g.DeletedAt.Value >= cutoff))
                                    .ToList();
                                await UploadJsonFileToAppDataFolderAsync(token, GoalsManifestFilename, JsonSerializer.Serialize(pruned, JsonOptions), fileIdMap, "Goals Manifest (Tombstone Pruning)", cancellationToken);
                                Wadd.Core.Logging.AppLogger.LogInfo("GoogleDriveSyncService", $"Pruned expired tombstones from {GoalsManifestFilename} manifest.");
                            }
                        }
                        catch { }
                    }
                }

                var localMilestones = await _goalService.GetAllMilestonesRawAsync(cancellationToken);
                bool hasExpiredMilestones = localMilestones.Any(m => m.IsDeleted && m.DeletedAt.HasValue && m.DeletedAt.Value < cutoff);
                if (hasExpiredMilestones && remoteFileMap.TryGetValue(MilestonesManifestFilename, out var milestonesFile))
                {
                    var content = await DownloadAppDataFileAsync(token, milestonesFile.Id, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        try
                        {
                            var remoteList = JsonSerializer.Deserialize<List<GoalMilestone>>(content, JsonOptions) ?? new();
                            if (remoteList.Any(m => m.IsDeleted && m.DeletedAt.HasValue && m.DeletedAt.Value < cutoff))
                            {
                                var pruned = remoteList
                                    .Where(m => !m.IsDeleted || (m.DeletedAt.HasValue && m.DeletedAt.Value >= cutoff))
                                    .ToList();
                                await UploadJsonFileToAppDataFolderAsync(token, MilestonesManifestFilename, JsonSerializer.Serialize(pruned, JsonOptions), fileIdMap, "Milestones Manifest (Tombstone Pruning)", cancellationToken);
                                Wadd.Core.Logging.AppLogger.LogInfo("GoogleDriveSyncService", $"Pruned expired tombstones from {MilestonesManifestFilename} manifest.");
                            }
                        }
                        catch { }
                    }
                }

                var localJournals = await _goalService.GetAllJournalEntriesRawAsync(cancellationToken);
                bool hasExpiredJournals = localJournals.Any(j => j.IsDeleted && j.DeletedAt.HasValue && j.DeletedAt.Value < cutoff);
                if (hasExpiredJournals && remoteFileMap.TryGetValue(JournalsManifestFilename, out var journalsFile))
                {
                    var content = await DownloadAppDataFileAsync(token, journalsFile.Id, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        try
                        {
                            var remoteList = JsonSerializer.Deserialize<List<JournalEntry>>(content, JsonOptions) ?? new();
                            if (remoteList.Any(j => j.IsDeleted && j.DeletedAt.HasValue && j.DeletedAt.Value < cutoff))
                            {
                                var pruned = remoteList
                                    .Where(j => !j.IsDeleted || (j.DeletedAt.HasValue && j.DeletedAt.Value >= cutoff))
                                    .ToList();
                                await UploadJsonFileToAppDataFolderAsync(token, JournalsManifestFilename, JsonSerializer.Serialize(pruned, JsonOptions), fileIdMap, "Journals Manifest (Tombstone Pruning)", cancellationToken);
                                Wadd.Core.Logging.AppLogger.LogInfo("GoogleDriveSyncService", $"Pruned expired tombstones from {JournalsManifestFilename} manifest.");
                            }
                        }
                        catch { }
                    }
                }
            }

            // 3. Date Notes Manifest
            var localNotes = await _todoService.GetAllDateNotesRawAsync(cancellationToken);
            bool hasExpiredNotes = localNotes.Any(n => n.IsDeleted && n.DeletedAt.HasValue && n.DeletedAt.Value < cutoff);
            if (hasExpiredNotes && remoteFileMap.TryGetValue(DateNotesManifestFilename, out var notesFile))
            {
                var content = await DownloadAppDataFileAsync(token, notesFile.Id, cancellationToken);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    try
                    {
                        var remoteList = JsonSerializer.Deserialize<List<CalendarDateNote>>(content, JsonOptions) ?? new();
                        if (remoteList.Any(n => n.IsDeleted && n.DeletedAt.HasValue && n.DeletedAt.Value < cutoff))
                        {
                            var pruned = remoteList
                                .Where(n => !n.IsDeleted || (n.DeletedAt.HasValue && n.DeletedAt.Value >= cutoff))
                                .ToList();
                            await UploadJsonFileToAppDataFolderAsync(token, DateNotesManifestFilename, JsonSerializer.Serialize(pruned, JsonOptions), fileIdMap, "DateNotes Manifest (Tombstone Pruning)", cancellationToken);
                            Wadd.Core.Logging.AppLogger.LogInfo("GoogleDriveSyncService", $"Pruned expired tombstones from {DateNotesManifestFilename} manifest.");
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogWarning("GoogleDriveSyncService", $"Tombstone pruning encountered an issue: {ex.Message}");
        }
    }

    private async Task<string> DownloadAppDataFileAsync(string token, string fileId, CancellationToken cancellationToken)
    {
        string downloadUrl = $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, downloadUrl), cancellationToken);
        await EnsureDriveSuccessAsync(response, "Download AppData File");
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        return string.Empty;
    }

    private async Task UploadJsonFileToAppDataFolderAsync(string token, string fileName, string payloadJson, IDictionary<string, string>? remoteFileMap, string itemDescription, CancellationToken cancellationToken)
    {
        string? existingFileId = null;
        if (remoteFileMap != null)
        {
            remoteFileMap.TryGetValue(fileName, out existingFileId);
        }
        else
        {
            existingFileId = await FindAppDataFileIdByNameAsync(token, fileName, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(existingFileId))
        {
            string uploadUrl = $"https://www.googleapis.com/upload/drive/v3/files/{existingFileId}?uploadType=media";
            using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Patch, uploadUrl)
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            }, cancellationToken);
            await EnsureDriveSuccessAsync(response, $"Update {itemDescription} in AppData");
        }
        else
        {
            string uploadUrl = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart";
            var boundary = "---WaddBoundary" + Guid.NewGuid().ToString("N");
            var metadataJson = JsonSerializer.Serialize(new
            {
                name = fileName,
                parents = new[] { "appDataFolder" }
            });

            using var response = await SendWithRetryAsync(() =>
            {
                var multipartContent = new MultipartContent("related", boundary);
                multipartContent.Add(new StringContent(metadataJson, Encoding.UTF8, "application/json"));
                multipartContent.Add(new StringContent(payloadJson, Encoding.UTF8, "application/json"));
                return new HttpRequestMessage(HttpMethod.Post, uploadUrl) { Content = multipartContent };
            }, cancellationToken);
            await EnsureDriveSuccessAsync(response, $"Upload {itemDescription} to AppData");

            if (response.IsSuccessStatusCode && remoteFileMap != null)
            {
                try
                {
                    var respJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(respJson);
                    if (doc.RootElement.TryGetProperty("id", out var idProp))
                    {
                        var newId = idProp.GetString();
                        if (!string.IsNullOrWhiteSpace(newId))
                        {
                            remoteFileMap[fileName] = newId;
                        }
                    }
                }
                catch { }
            }
        }
    }

    private async Task<bool> SyncGoalsAsync(string token, Dictionary<string, DriveFileItem> remoteFileMap, IDictionary<string, string> fileIdMap, CancellationToken cancellationToken)
    {
        if (_goalService == null) return false;
        bool hasChanges = false;
        var localGoalsList = (await _goalService.GetAllGoalsRawAsync(cancellationToken)).ToList();
        var localGoalsMap = localGoalsList.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());

        remoteFileMap.TryGetValue(GoalsManifestFilename, out var remoteManifestFile);

        bool remoteChanged = remoteManifestFile != null &&
                             remoteManifestFile.ModifiedTime.HasValue &&
                             remoteManifestFile.ModifiedTime.Value > _lastSyncTimestampUtc;

        bool localChanged = _lastSyncTimestampUtc == DateTime.MinValue ||
                            localGoalsList.Any(g => EnsureUtc(g.UpdatedAt ?? g.CreatedAt) > _lastSyncTimestampUtc);

        if (remoteManifestFile == null)
        {
            if (localGoalsMap.Count > 0)
            {
                var cutoff = DateTime.UtcNow - TombstoneRetentionWindow;
                var toUpload = localGoalsMap.Values
                    .Where(g => !g.IsDeleted || (g.DeletedAt.HasValue && g.DeletedAt.Value >= cutoff))
                    .ToList();

                await UploadJsonFileToAppDataFolderAsync(token, GoalsManifestFilename, JsonSerializer.Serialize(toUpload, JsonOptions), fileIdMap, "Goals Manifest", cancellationToken);
                hasChanges = true;
            }
            return hasChanges;
        }

        if (!remoteChanged && !localChanged) return false;

        List<LifeGoal> remoteGoals = new();
        var remoteContent = await DownloadAppDataFileAsync(token, remoteManifestFile.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(remoteContent))
        {
            try { remoteGoals = JsonSerializer.Deserialize<List<LifeGoal>>(remoteContent, JsonOptions) ?? new(); } catch { }
        }

        var remoteGoalsMap = remoteGoals.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First());
        var allIds = localGoalsMap.Keys.Union(remoteGoalsMap.Keys).ToList();

        var mergedGoals = new List<LifeGoal>();
        var localUpserts = new List<LifeGoal>();
        bool remoteNeedsUpload = localChanged;

        foreach (var id in allIds)
        {
            localGoalsMap.TryGetValue(id, out var local);
            remoteGoalsMap.TryGetValue(id, out var remote);

            var merged = ConflictResolutionEngine.MergeGoal(local, remote);
            mergedGoals.Add(merged);

            if (local == null)
            {
                localUpserts.Add(merged);
            }
            else if (remote != null)
            {
                long localTicks = EnsureUtc(local.UpdatedAt ?? local.CreatedAt).Ticks;
                long remoteTicks = EnsureUtc(remote.UpdatedAt ?? remote.CreatedAt).Ticks;
                if (remoteTicks > localTicks || (local.IsDeleted != remote.IsDeleted && remote.IsDeleted))
                {
                    localUpserts.Add(merged);
                }
                else if (localTicks > remoteTicks || (local.IsDeleted != remote.IsDeleted && local.IsDeleted))
                {
                    remoteNeedsUpload = true;
                }
            }
            else
            {
                remoteNeedsUpload = true;
            }
        }

        if (localUpserts.Count > 0)
        {
            hasChanges = true;
            await _dbWriteLock.WaitAsync(cancellationToken);
            try
            {
                await _goalService.BatchDirectUpsertGoalsAsync(localUpserts, cancellationToken);
            }
            finally { _dbWriteLock.Release(); }
        }

        if (remoteNeedsUpload)
        {
            hasChanges = true;
            var cutoff = DateTime.UtcNow - TombstoneRetentionWindow;
            var toUpload = mergedGoals
                .Where(g => !g.IsDeleted || (g.DeletedAt.HasValue && g.DeletedAt.Value >= cutoff))
                .ToList();

            await UploadJsonFileToAppDataFolderAsync(token, GoalsManifestFilename, JsonSerializer.Serialize(toUpload, JsonOptions), fileIdMap, "Goals Manifest", cancellationToken);
        }

        return hasChanges;
    }

    private async Task<bool> SyncMilestonesAsync(string token, Dictionary<string, DriveFileItem> remoteFileMap, IDictionary<string, string> fileIdMap, CancellationToken cancellationToken)
    {
        if (_goalService == null) return false;
        bool hasChanges = false;
        var localMilestonesList = (await _goalService.GetAllMilestonesRawAsync(cancellationToken)).ToList();
        var localMilestonesMap = localMilestonesList.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());

        remoteFileMap.TryGetValue(MilestonesManifestFilename, out var remoteManifestFile);

        bool remoteChanged = remoteManifestFile != null &&
                             remoteManifestFile.ModifiedTime.HasValue &&
                             remoteManifestFile.ModifiedTime.Value > _lastSyncTimestampUtc;

        bool localChanged = _lastSyncTimestampUtc == DateTime.MinValue ||
                            localMilestonesList.Any(m => EnsureUtc(m.UpdatedAt ?? DateTime.MinValue) > _lastSyncTimestampUtc);

        if (remoteManifestFile == null)
        {
            if (localMilestonesMap.Count > 0)
            {
                var cutoff = DateTime.UtcNow - TombstoneRetentionWindow;
                var toUpload = localMilestonesMap.Values
                    .Where(m => !m.IsDeleted || (m.DeletedAt.HasValue && m.DeletedAt.Value >= cutoff))
                    .ToList();

                await UploadJsonFileToAppDataFolderAsync(token, MilestonesManifestFilename, JsonSerializer.Serialize(toUpload, JsonOptions), fileIdMap, "Milestones Manifest", cancellationToken);
                hasChanges = true;
            }
            return hasChanges;
        }

        if (!remoteChanged && !localChanged) return false;

        List<GoalMilestone> remoteMilestones = new();
        var remoteContent = await DownloadAppDataFileAsync(token, remoteManifestFile.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(remoteContent))
        {
            try { remoteMilestones = JsonSerializer.Deserialize<List<GoalMilestone>>(remoteContent, JsonOptions) ?? new(); } catch { }
        }

        var remoteMilestonesMap = remoteMilestones.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
        var allIds = localMilestonesMap.Keys.Union(remoteMilestonesMap.Keys).ToList();

        var mergedMilestones = new List<GoalMilestone>();
        var localUpserts = new List<GoalMilestone>();
        bool remoteNeedsUpload = localChanged;

        foreach (var id in allIds)
        {
            localMilestonesMap.TryGetValue(id, out var local);
            remoteMilestonesMap.TryGetValue(id, out var remote);

            var merged = ConflictResolutionEngine.MergeMilestone(local, remote);
            mergedMilestones.Add(merged);

            if (local == null)
            {
                localUpserts.Add(merged);
            }
            else if (remote != null)
            {
                long localTicks = EnsureUtc(local.UpdatedAt ?? DateTime.MinValue).Ticks;
                long remoteTicks = EnsureUtc(remote.UpdatedAt ?? DateTime.MinValue).Ticks;
                if (remoteTicks > localTicks || (local.IsDeleted != remote.IsDeleted && remote.IsDeleted))
                {
                    localUpserts.Add(merged);
                }
                else if (localTicks > remoteTicks || (local.IsDeleted != remote.IsDeleted && local.IsDeleted))
                {
                    remoteNeedsUpload = true;
                }
            }
            else
            {
                remoteNeedsUpload = true;
            }
        }

        if (localUpserts.Count > 0)
        {
            hasChanges = true;
            await _dbWriteLock.WaitAsync(cancellationToken);
            try
            {
                await _goalService.BatchDirectUpsertMilestonesAsync(localUpserts, cancellationToken);
            }
            finally { _dbWriteLock.Release(); }
        }

        if (remoteNeedsUpload)
        {
            hasChanges = true;
            var cutoff = DateTime.UtcNow - TombstoneRetentionWindow;
            var toUpload = mergedMilestones
                .Where(m => !m.IsDeleted || (m.DeletedAt.HasValue && m.DeletedAt.Value >= cutoff))
                .ToList();

            await UploadJsonFileToAppDataFolderAsync(token, MilestonesManifestFilename, JsonSerializer.Serialize(toUpload, JsonOptions), fileIdMap, "Milestones Manifest", cancellationToken);
        }

        return hasChanges;
    }

    private async Task<bool> SyncJournalEntriesAsync(string token, Dictionary<string, DriveFileItem> remoteFileMap, IDictionary<string, string> fileIdMap, CancellationToken cancellationToken)
    {
        if (_goalService == null) return false;
        bool hasChanges = false;
        var localEntriesList = (await _goalService.GetAllJournalEntriesRawAsync(cancellationToken)).ToList();
        var localEntriesMap = localEntriesList.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());

        remoteFileMap.TryGetValue(JournalsManifestFilename, out var remoteManifestFile);

        bool remoteChanged = remoteManifestFile != null &&
                             remoteManifestFile.ModifiedTime.HasValue &&
                             remoteManifestFile.ModifiedTime.Value > _lastSyncTimestampUtc;

        bool localChanged = _lastSyncTimestampUtc == DateTime.MinValue ||
                            localEntriesList.Any(j => EnsureUtc(j.UpdatedAt ?? j.EntryDate) > _lastSyncTimestampUtc);

        if (remoteManifestFile == null)
        {
            if (localEntriesMap.Count > 0)
            {
                var cutoff = DateTime.UtcNow - TombstoneRetentionWindow;
                var toUpload = localEntriesMap.Values
                    .Where(j => !j.IsDeleted || (j.DeletedAt.HasValue && j.DeletedAt.Value >= cutoff))
                    .ToList();

                await UploadJsonFileToAppDataFolderAsync(token, JournalsManifestFilename, JsonSerializer.Serialize(toUpload, JsonOptions), fileIdMap, "Journals Manifest", cancellationToken);
                hasChanges = true;
            }
            return hasChanges;
        }

        if (!remoteChanged && !localChanged) return false;

        List<JournalEntry> remoteEntries = new();
        var remoteContent = await DownloadAppDataFileAsync(token, remoteManifestFile.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(remoteContent))
        {
            try { remoteEntries = JsonSerializer.Deserialize<List<JournalEntry>>(remoteContent, JsonOptions) ?? new(); } catch { }
        }

        var remoteEntriesMap = remoteEntries.GroupBy(j => j.Id).ToDictionary(g => g.Key, g => g.First());
        var allIds = localEntriesMap.Keys.Union(remoteEntriesMap.Keys).ToList();

        var mergedEntries = new List<JournalEntry>();
        var localUpserts = new List<JournalEntry>();
        bool remoteNeedsUpload = localChanged;

        foreach (var id in allIds)
        {
            localEntriesMap.TryGetValue(id, out var local);
            remoteEntriesMap.TryGetValue(id, out var remote);

            var merged = ConflictResolutionEngine.MergeJournalEntry(local, remote);
            mergedEntries.Add(merged);

            if (local == null)
            {
                localUpserts.Add(merged);
            }
            else if (remote != null)
            {
                long localTicks = EnsureUtc(local.UpdatedAt ?? local.EntryDate).Ticks;
                long remoteTicks = EnsureUtc(remote.UpdatedAt ?? remote.EntryDate).Ticks;
                if (remoteTicks > localTicks || (local.IsDeleted != remote.IsDeleted && remote.IsDeleted))
                {
                    localUpserts.Add(merged);
                }
                else if (localTicks > remoteTicks || (local.IsDeleted != remote.IsDeleted && local.IsDeleted))
                {
                    remoteNeedsUpload = true;
                }
            }
            else
            {
                remoteNeedsUpload = true;
            }
        }

        if (localUpserts.Count > 0)
        {
            hasChanges = true;
            await _dbWriteLock.WaitAsync(cancellationToken);
            try
            {
                await _goalService.BatchDirectUpsertJournalEntriesAsync(localUpserts, cancellationToken);
            }
            finally { _dbWriteLock.Release(); }
        }

        if (remoteNeedsUpload)
        {
            hasChanges = true;
            var cutoff = DateTime.UtcNow - TombstoneRetentionWindow;
            var toUpload = mergedEntries
                .Where(j => !j.IsDeleted || (j.DeletedAt.HasValue && j.DeletedAt.Value >= cutoff))
                .ToList();

            await UploadJsonFileToAppDataFolderAsync(token, JournalsManifestFilename, JsonSerializer.Serialize(toUpload, JsonOptions), fileIdMap, "Journals Manifest", cancellationToken);
        }

        return hasChanges;
    }

    private async Task<bool> SyncDateNotesAsync(string token, Dictionary<string, DriveFileItem> remoteFileMap, IDictionary<string, string> fileIdMap, CancellationToken cancellationToken)
    {
        bool hasChanges = false;
        var localNotesList = (await _todoService.GetAllDateNotesRawAsync(cancellationToken)).ToList();
        var localNotesMap = localNotesList.GroupBy(x => x.DateKey).ToDictionary(g => g.Key, g => g.First());

        remoteFileMap.TryGetValue(DateNotesManifestFilename, out var remoteManifestFile);

        bool remoteChanged = remoteManifestFile != null &&
                             remoteManifestFile.ModifiedTime.HasValue &&
                             remoteManifestFile.ModifiedTime.Value > _lastSyncTimestampUtc;

        bool localChanged = _lastSyncTimestampUtc == DateTime.MinValue ||
                            localNotesList.Any(n => EnsureUtc(n.UpdatedAt) > _lastSyncTimestampUtc);

        if (remoteManifestFile == null)
        {
            if (localNotesMap.Count > 0)
            {
                var cutoff = DateTime.UtcNow - TombstoneRetentionWindow;
                var toUpload = localNotesMap.Values
                    .Where(n => !n.IsDeleted || (n.DeletedAt.HasValue && n.DeletedAt.Value >= cutoff))
                    .ToList();

                await UploadJsonFileToAppDataFolderAsync(token, DateNotesManifestFilename, JsonSerializer.Serialize(toUpload, JsonOptions), fileIdMap, "DateNotes Manifest", cancellationToken);
                hasChanges = true;
            }
            return hasChanges;
        }

        if (!remoteChanged && !localChanged) return false;

        List<CalendarDateNote> remoteNotes = new();
        var remoteContent = await DownloadAppDataFileAsync(token, remoteManifestFile.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(remoteContent))
        {
            try { remoteNotes = JsonSerializer.Deserialize<List<CalendarDateNote>>(remoteContent, JsonOptions) ?? new(); } catch { }
        }

        var remoteNotesMap = remoteNotes.GroupBy(n => n.DateKey).ToDictionary(g => g.Key, g => g.First());
        var allKeys = localNotesMap.Keys.Union(remoteNotesMap.Keys).ToList();

        var mergedNotes = new List<CalendarDateNote>();
        var localUpserts = new List<CalendarDateNote>();
        bool remoteNeedsUpload = localChanged;

        foreach (var key in allKeys)
        {
            localNotesMap.TryGetValue(key, out var local);
            remoteNotesMap.TryGetValue(key, out var remote);

            var merged = ConflictResolutionEngine.MergeDateNote(local, remote);
            mergedNotes.Add(merged);

            if (local == null)
            {
                localUpserts.Add(merged);
            }
            else if (remote != null)
            {
                long localTicks = EnsureUtc(local.UpdatedAt).Ticks;
                long remoteTicks = EnsureUtc(remote.UpdatedAt).Ticks;
                if (remoteTicks > localTicks || (local.IsDeleted != remote.IsDeleted && remote.IsDeleted))
                {
                    localUpserts.Add(merged);
                }
                else if (localTicks > remoteTicks || (local.IsDeleted != remote.IsDeleted && local.IsDeleted))
                {
                    remoteNeedsUpload = true;
                }
            }
            else
            {
                remoteNeedsUpload = true;
            }
        }

        if (localUpserts.Count > 0)
        {
            hasChanges = true;
            await _dbWriteLock.WaitAsync(cancellationToken);
            try
            {
                await _todoService.BatchDirectUpsertDateNotesAsync(localUpserts, cancellationToken);
            }
            finally { _dbWriteLock.Release(); }
        }

        if (remoteNeedsUpload)
        {
            hasChanges = true;
            var cutoff = DateTime.UtcNow - TombstoneRetentionWindow;
            var toUpload = mergedNotes
                .Where(n => !n.IsDeleted || (n.DeletedAt.HasValue && n.DeletedAt.Value >= cutoff))
                .ToList();

            await UploadJsonFileToAppDataFolderAsync(token, DateNotesManifestFilename, JsonSerializer.Serialize(toUpload, JsonOptions), fileIdMap, "DateNotes Manifest", cancellationToken);
        }

        return hasChanges;
    }

    private async Task<string> FindAppDataFileIdByNameAsync(string token, string fileName, CancellationToken cancellationToken)
    {
        string safeFileName = fileName.Replace("'", "\\'");
        string query = $"name = '{safeFileName}' and trashed = false";
        string searchUrl = $"https://www.googleapis.com/drive/v3/files?spaces=appDataFolder&q={Uri.EscapeDataString(query)}&fields=files(id)";

        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, searchUrl), cancellationToken);
        await EnsureDriveSuccessAsync(response, "Find AppData File");
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("files", out var filesArr) && filesArr.GetArrayLength() > 0)
            {
                return filesArr[0].GetProperty("id").GetString() ?? string.Empty;
            }
        }
        return string.Empty;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken, int maxRetries = 5, int baseDelayMs = 1000)
    {
        var random = new Random();
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            // Coordinated rate-limiter: await shared backoff if any concurrent pipeline encountered 429/5xx
            DateTime waitTarget;
            lock (_sharedBackoffLock)
            {
                waitTarget = _sharedBackoffUntilUtc;
            }
            if (waitTarget > DateTime.UtcNow)
            {
                var waitMs = (int)(waitTarget - DateTime.UtcNow).TotalMilliseconds;
                if (waitMs > 0)
                {
                    await Task.Delay(Math.Min(waitMs, 30000), cancellationToken);
                }
            }

            using var request = createRequest();
            if (!string.IsNullOrWhiteSpace(_authRecord?.AccessToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authRecord.AccessToken);
            }

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (await TryRefreshTokenAsync(cancellationToken))
                {
                    continue;
                }
            }

            if (response.StatusCode == (HttpStatusCode)429 || (int)response.StatusCode >= 500)
            {
                if (attempt == maxRetries - 1) return response;

                int retryAfterSeconds = 0;
                if (response.Headers.RetryAfter?.Delta.HasValue == true)
                {
                    retryAfterSeconds = (int)response.Headers.RetryAfter.Delta.Value.TotalSeconds;
                }

                int jitter = random.Next(100, 500);
                int delay = retryAfterSeconds > 0
                    ? (retryAfterSeconds * 1000) + jitter
                    : (int)(baseDelayMs * Math.Pow(2, attempt)) + jitter;

                lock (_sharedBackoffLock)
                {
                    var newTarget = DateTime.UtcNow.AddMilliseconds(delay);
                    if (newTarget > _sharedBackoffUntilUtc)
                    {
                        _sharedBackoffUntilUtc = newTarget;
                    }
                }

                await Task.Delay(delay, cancellationToken);
                continue;
            }

            return response;
        }
        throw new InvalidOperationException("Google Drive request failed after retries.");
    }

    private static HttpClient CreateOptimizedHttpClient()
    {
        try
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                EnableMultipleHttp2Connections = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 32
            };
            var client = new HttpClient(handler, disposeHandler: true);
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");
            return client;
        }
        catch
        {
            return new HttpClient();
        }
    }

    private async Task EnsureDriveSuccessAsync(HttpResponseMessage response, string actionName)
    {
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || content.Contains("Invalid Credentials") || content.Contains("invalid_token") || content.Contains("authError"))
            {
                if (await TryRefreshTokenAsync(CancellationToken.None))
                {
                    return;
                }

                Wadd.Core.Logging.AppLogger.LogWarning("GoogleDriveSyncService", $"Google session expired on '{actionName}' ({response.StatusCode}): {content}");
                throw new InvalidOperationException("Google session expired. Please click 'Connect Google Drive Account' in Settings to refresh your connection.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || content.Contains("drive.googleapis.com") || content.Contains("API has not been used"))
            {
                Wadd.Core.Logging.AppLogger.LogError("GoogleDriveSyncService", $"Google Drive API is disabled for '{actionName}' ({response.StatusCode}): {content}");
                throw new InvalidOperationException("Google Drive API is disabled in your Google Cloud Console project. Please open Google Cloud Console > Enabled APIs & Services > Enable 'Google Drive API'.");
            }

            Wadd.Core.Logging.AppLogger.LogError("GoogleDriveSyncService", $"Google Drive API {actionName} failed ({response.StatusCode}): {content}");
            throw new InvalidOperationException($"Google Drive API {actionName} failed ({response.StatusCode}): {content}");
        }
    }
}

public class UserAuthRecord
{
    public bool IsSignedIn { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string GoogleClientId { get; set; } = string.Empty;
    public string GoogleClientSecret { get; set; } = string.Empty;
    public string OAuthProxyUrl { get; set; } = string.Empty;
    public string OAuthProxySecret { get; set; } = string.Empty;
    public string FirebaseApiKey { get; set; } = string.Empty;
    public string FirebaseProjectId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string FirebaseIdToken { get; set; } = string.Empty;
    public string FirebaseRefreshToken { get; set; } = string.Empty;
    public string FirebaseLocalId { get; set; } = string.Empty;
    public DateTime AuthenticatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? TokenExpiresAtUtc { get; set; }
    public DateTime? LastSyncTimestampUtc { get; set; }
    public bool MigratedToManifests { get; set; }
}
