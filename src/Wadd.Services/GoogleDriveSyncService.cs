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
    public event EventHandler? AuthStateChanged;

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

    public string WebAppUrl { get; set; }

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
        INativeGoogleAuthService? nativeAuthService = null)
    {
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        _httpClient = httpClient ?? CreateOptimizedHttpClient();
        _nativeAuthService = nativeAuthService;

        if (_todoService is SQLiteTodoService sqliteService)
        {
            _syncLogRepository = syncLogRepository ?? sqliteService.SyncLogRepository;
            _deviceService = deviceService ?? sqliteService.DeviceService;
            _conflictRepository = conflictRepository ?? new SQLiteConflictRepository(sqliteService.DatabaseConnection);
        }
        else
        {
            _deviceService = deviceService ?? new DeviceService();
            _syncLogRepository = syncLogRepository ?? new SQLiteSyncLogRepository(Wadd.Core.Helpers.AppDataHelper.GetWaddFilePath("wadd.db"));
            _conflictRepository = conflictRepository ?? new SQLiteConflictRepository(Wadd.Core.Helpers.AppDataHelper.GetWaddFilePath("wadd.db"));
        }

        _conflictEngine = conflictEngine ?? new ConflictResolutionEngine();

        LoadEnvFile();

        _authFilePath = Wadd.Core.Helpers.AppDataHelper.GetWaddFilePath("google_user_auth.json");

        WebAppUrl = webAppUrl 
            ?? Environment.GetEnvironmentVariable("WADD_SYNC_URL") 
            ?? string.Empty;

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
                Trace.WriteLine($"[ERROR] Native Google Sign-In failed: {nativeResult.ErrorMessage}");
                throw new InvalidOperationException($"Native Google Sign-In failed: {nativeResult.ErrorMessage}");
            }
        }

        var localRedirectUri = "http://localhost:5001/";
        var redirectUri = localRedirectUri;
        var state = Guid.NewGuid().ToString("N");

        var listener = new HttpListener();
        listener.Prefixes.Add(localRedirectUri);
        listener.Start();

        try
        {
            var nonce = Guid.NewGuid().ToString("N");
            var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                          $"client_id={Uri.EscapeDataString(GoogleClientId)}&" +
                          $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                          $"response_type=token%20id_token&" +
                          $"scope={Uri.EscapeDataString("openid email profile https://www.googleapis.com/auth/drive.appdata https://www.googleapis.com/auth/drive.file")}&" +
                          $"prompt=select_account&" +
                          $"nonce={Uri.EscapeDataString(nonce)}&" +
                          $"state={Uri.EscapeDataString(state)}";

            OpenBrowserUrl(authUrl);

            string idToken = string.Empty;
            string accessToken = string.Empty;
            string returnedState = string.Empty;
            string error = string.Empty;

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

                if (request.Url?.AbsolutePath == "/callback")
                {
                    idToken = request.QueryString["id_token"] ?? string.Empty;
                    accessToken = request.QueryString["access_token"] ?? string.Empty;
                    returnedState = request.QueryString["state"] ?? string.Empty;
                    error = request.QueryString["error"] ?? string.Empty;

                    SendHtmlResponse(response, "Sign-in Successful!", "<h2 style='color:#0d9488;'>Authentication Successful!</h2><p>Wadd ToDo has been successfully connected.</p><p>You may now close this browser tab and return to Wadd.</p>");
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
            string googleIdToken = idToken;
            int expiresInSeconds = 3600;

            if (string.IsNullOrWhiteSpace(googleAccessToken) && !string.IsNullOrWhiteSpace(googleIdToken))
            {
                var fbEarly = await ExchangeGoogleIdTokenWithFirebaseAsync(googleIdToken, string.Empty, redirectUri, cancellationToken);
                if (!string.IsNullOrWhiteSpace(fbEarly.OAuthAccessToken))
                {
                    googleAccessToken = fbEarly.OAuthAccessToken;
                }
            }

            if (string.IsNullOrWhiteSpace(googleAccessToken))
            {
                throw new InvalidOperationException("Failed to obtain Google access token. Please verify your Google Client ID and try again.");
            }

            var userInfo = await FetchUserInfoAsync(googleAccessToken, cancellationToken);
            var fbSession = await ExchangeGoogleIdTokenWithFirebaseAsync(googleIdToken, googleAccessToken, redirectUri, cancellationToken);

            _authRecord = new UserAuthRecord
            {
                IsSignedIn = true,
                UserEmail = !string.IsNullOrWhiteSpace(fbSession.Email) ? fbSession.Email : userInfo.Email,
                UserName = !string.IsNullOrWhiteSpace(fbSession.DisplayName) ? fbSession.DisplayName : userInfo.Name,
                GoogleClientId = GoogleClientId,
                GoogleClientSecret = GoogleClientSecret,
                FirebaseApiKey = FirebaseApiKey,
                FirebaseProjectId = FirebaseProjectId,
                AccessToken = !string.IsNullOrWhiteSpace(googleAccessToken) ? googleAccessToken : fbSession.OAuthAccessToken,
                RefreshToken = _authRecord?.RefreshToken ?? string.Empty,
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

    private static void SendHtmlResponse(HttpListenerResponse response, string title, string bodyHtml)
    {
        var html = $@"<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'/>
  <title>{title}</title>
  <style>
    body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background: #f8f9fa; color: #1e293b; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }}
    .card {{ background: white; padding: 2.5rem; border-radius: 12px; box-shadow: 0 10px 25px rgba(0,0,0,0.08); text-align: center; max-width: 420px; }}
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
  <title>Authenticating Wadd...</title>
  <style>
    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #f8f9fa; color: #1e293b; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }
    .card { background: white; padding: 2.5rem; border-radius: 12px; box-shadow: 0 10px 25px rgba(0,0,0,0.08); text-align: center; max-width: 420px; }
  </style>
</head>
<body>
  <div class='card'>
    <h2 style='color:#0d9488;'>Completing Authentication...</h2>
    <p>Please wait while Wadd connects your account.</p>
  </div>
  <script>
    var params = new URLSearchParams(window.location.search);
    var hash = new URLSearchParams(window.location.hash.substring(1));
    var idToken = params.get('id_token') || hash.get('id_token') || '';
    var accessToken = params.get('access_token') || hash.get('access_token') || '';
    var code = params.get('code') || hash.get('code') || '';
    var state = params.get('state') || hash.get('state') || '';
    var error = params.get('error') || hash.get('error') || '';

    fetch('/callback?id_token=' + encodeURIComponent(idToken) + '&access_token=' + encodeURIComponent(accessToken) + '&code=' + encodeURIComponent(code) + '&state=' + encodeURIComponent(state) + '&error=' + encodeURIComponent(error))
      .then(function() {
        document.body.innerHTML = ""<div class='card'><h2 style='color:#0d9488;'>Authentication Successful!</h2><p>You can close this tab and return to Wadd.</p></div>"";
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

        // 1. Try refreshing Google Access Token via Google OAuth Refresh Token
        if (!string.IsNullOrWhiteSpace(_authRecord.RefreshToken))
        {
            try
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
                    Trace.WriteLine($"[WARN] Google token refresh failed ({response.StatusCode}): {json}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[WARN] Exception during Google token refresh: {ex.Message}");
            }
        }

        // 2. Try refreshing Firebase Id Token via Firebase Refresh Token
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
                    var expSec = root.TryGetProperty("expires_in", out var exp) ? int.Parse(exp.GetString() ?? "3600") : 3600;
                    _authRecord.TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(300, expSec - 60));
                    SaveAuthRecord();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[WARN] Exception during Firebase token refresh: {ex.Message}");
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
            Trace.WriteLine($"[WARN] Avalonia TopLevel.Launcher failed, falling back to process launcher: {ex.Message}");
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
                Trace.WriteLine($"[WARN] Native Android intent launch failed: {ex.Message}");
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
        if (File.Exists(_authFilePath))
        {
            try { File.Delete(_authFilePath); } catch { }
        }
        NotifyAuthStateChanged();
    }

    // =========================================================================
    //  OFFLINE-FIRST INCREMENTAL MULTI-DEVICE SYNCHRONIZATION ENGINE
    // =========================================================================

    private readonly SemaphoreSlim _syncLock = new(1, 1);
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
            // If refresh token exists, attempt refresh
            if (!string.IsNullOrWhiteSpace(_authRecord.RefreshToken))
            {
                await TryRefreshTokenAsync(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(_authRecord.AccessToken))
            {
                throw new InvalidOperationException("Google authorization token missing. Please sign in with Google in Settings.");
            }
        }

        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var syncStartUtc = DateTime.UtcNow;
            var token = _authRecord.AccessToken;

            // 1. Fetch full remote file metadata list from appDataFolder (1 single HTTP GET request ~150ms)
            var remoteFiles = await ListAppDataFolderFilesAsync(token, cancellationToken);
            var remoteFileMap = remoteFiles.ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);

            var rawSqliteSvc = _todoService as SQLiteTodoService;
            List<TodoItem> localTasksList = rawSqliteSvc != null
                ? (await rawSqliteSvc.GetAllRawAsync(cancellationToken)).ToList()
                : (await _todoService.GetTodosAsync(cancellationToken)).ToList();

            var localTasksMap = localTasksList.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());

            // 2. Identify local tasks with pending un-synced edits
            var pendingLocalTaskIds = new HashSet<Guid>();
            lock (_pendingPushQueue)
            {
                foreach (var id in _pendingPushQueue) pendingLocalTaskIds.Add(id);
                _pendingPushQueue.Clear();
            }

            if (rawSqliteSvc != null)
            {
                var pendingLogs = await _syncLogRepository.GetPendingLogsAsync(cancellationToken);
                foreach (var log in pendingLogs)
                {
                    pendingLocalTaskIds.Add(log.RecordId);
                }
            }

            // 3. Process remote updates in parallel (skip downloading tasks that have pending local edits)
            var filesToDownload = remoteFiles
                .Where(f => f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .Where(remoteFile =>
                {
                    string idStr = remoteFile.Name[..^5];
                    if (!Guid.TryParse(idStr, out var taskId)) return false;

                    // If user modified this task locally and hasn't pushed it, do NOT overwrite with old cloud file
                    if (pendingLocalTaskIds.Contains(taskId)) return false;

                    if (!localTasksMap.TryGetValue(taskId, out var localTask))
                    {
                        return true; // New task on cloud -> must download
                    }

                    if (!remoteFile.ModifiedTime.HasValue) return true;

                    DateTime localUpdated = (localTask.UpdatedAt ?? localTask.CreatedAt).ToUniversalTime();
                    DateTime remoteUpdated = remoteFile.ModifiedTime.Value.ToUniversalTime();

                    return remoteUpdated > localUpdated.AddSeconds(1);
                })
                .ToList();

            if (filesToDownload.Count > 0)
            {
                var downloadSemaphore = new SemaphoreSlim(16);
                var remoteDownloadTasks = filesToDownload
                    .Select(async remoteFile =>
                    {
                        string idStr = remoteFile.Name[..^5];
                        if (!Guid.TryParse(idStr, out var taskId)) return null;

                        await downloadSemaphore.WaitAsync(cancellationToken);
                        try
                        {
                            var remoteContent = await DownloadAppDataFileAsync(token, remoteFile.Id, cancellationToken);
                            if (string.IsNullOrWhiteSpace(remoteContent)) return null;

                            var remoteTask = JsonSerializer.Deserialize<TodoItem>(remoteContent, JsonOptions);
                            return remoteTask != null ? (taskId, remoteTask) : ((Guid taskId, TodoItem remoteTask)?)null;
                        }
                        catch
                        {
                            return null;
                        }
                        finally
                        {
                            downloadSemaphore.Release();
                        }
                    });

                var remoteResults = await Task.WhenAll(remoteDownloadTasks);
                var mergedTasksToUpsert = new List<TodoItem>();

                foreach (var result in remoteResults)
                {
                    if (!result.HasValue) continue;
                    var (taskId, remoteTask) = result.Value;

                    localTasksMap.TryGetValue(taskId, out var localTask);
                    var mergedTask = ConflictResolutionEngine.MergeTask(localTask, remoteTask);
                    mergedTasksToUpsert.Add(mergedTask);
                    localTasksMap[taskId] = mergedTask;
                }

                if (mergedTasksToUpsert.Count > 0)
                {
                    if (rawSqliteSvc != null)
                    {
                        await rawSqliteSvc.BatchDirectUpsertFromSyncAsync(mergedTasksToUpsert, cancellationToken);
                    }
                    else
                    {
                        foreach (var mergedTask in mergedTasksToUpsert)
                        {
                            if (localTasksMap.ContainsKey(mergedTask.Id))
                                await _todoService.UpdateTodoAsync(mergedTask, cancellationToken);
                            else
                                await _todoService.AddTodoAsync(mergedTask, cancellationToken);
                        }
                    }
                }
            }

            // 4. Determine tasks to push (Smart Filter)
            var tasksToPush = new HashSet<Guid>(pendingLocalTaskIds);

            // Add tasks missing from cloud or modified locally after cloud file modification time
            foreach (var (taskId, localTask) in localTasksMap)
            {
                string fileName = $"{taskId}.json";
                if (!remoteFileMap.TryGetValue(fileName, out var remoteFile))
                {
                    tasksToPush.Add(taskId); // Missing on cloud -> push
                }
                else if (remoteFile.ModifiedTime.HasValue)
                {
                    DateTime localUpdated = (localTask.UpdatedAt ?? localTask.CreatedAt).ToUniversalTime();
                    DateTime remoteUpdated = remoteFile.ModifiedTime.Value.ToUniversalTime();

                    if (localUpdated > remoteUpdated.AddSeconds(1))
                    {
                        tasksToPush.Add(taskId); // Local task is newer -> push
                    }
                }
            }

            if (tasksToPush.Count > 0)
            {
                var fileIdMap = remoteFileMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Id, StringComparer.OrdinalIgnoreCase);
                var uploadSemaphore = new SemaphoreSlim(16);
                var uploadTasks = tasksToPush
                    .Where(taskId => localTasksMap.ContainsKey(taskId))
                    .Select(async taskId =>
                    {
                        var taskToUpload = localTasksMap[taskId];
                        await uploadSemaphore.WaitAsync(cancellationToken);
                        try
                        {
                            await UploadTaskToAppDataFolderAsync(token, taskToUpload, fileIdMap, cancellationToken);
                        }
                        finally
                        {
                            uploadSemaphore.Release();
                        }
                    });

                await Task.WhenAll(uploadTasks);

                if (_syncLogRepository != null)
                {
                    var pendingLogs = await _syncLogRepository.GetPendingLogsAsync(cancellationToken);
                    var syncedLogIds = pendingLogs.Where(l => tasksToPush.Contains(l.RecordId)).Select(l => l.Id).ToList();
                    if (syncedLogIds.Count > 0)
                    {
                        await _syncLogRepository.MarkLogsAsSyncedAsync(syncedLogIds, cancellationToken);
                    }
                }
            }

            _lastSyncTimestampUtc = syncStartUtc;
            await RefreshConflictCountAsync(cancellationToken);
            return true;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private record DriveFileItem(string Id, string Name, DateTime? ModifiedTime);

    private async Task<List<DriveFileItem>> ListAppDataFolderFilesAsync(string token, CancellationToken cancellationToken)
    {
        var result = new List<DriveFileItem>();
        string query = "trashed = false";
        string url = $"https://www.googleapis.com/drive/v3/files?spaces=appDataFolder&q={Uri.EscapeDataString(query)}&fields=files(id,name,modifiedTime)&pageSize=1000";

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
                        mod = dt.ToUniversalTime();
                    }
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                    {
                        result.Add(new DriveFileItem(id, name, mod));
                    }
                }
            }
        }
        return result;
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

    private async Task UploadTaskToAppDataFolderAsync(string token, TodoItem task, Dictionary<string, string>? remoteFileMap, CancellationToken cancellationToken)
    {
        string fileName = $"{task.Id}.json";
        string? existingFileId = null;
        if (remoteFileMap != null)
        {
            remoteFileMap.TryGetValue(fileName, out existingFileId);
        }
        else
        {
            existingFileId = await FindAppDataFileIdByNameAsync(token, fileName, cancellationToken);
        }

        string payloadJson = JsonSerializer.Serialize(task, JsonOptions);

        if (!string.IsNullOrWhiteSpace(existingFileId))
        {
            string uploadUrl = $"https://www.googleapis.com/upload/drive/v3/files/{existingFileId}?uploadType=media";
            using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Patch, uploadUrl)
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            }, cancellationToken);
            await EnsureDriveSuccessAsync(response, $"Update Task {task.Id} in AppData");
        }
        else
        {
            string uploadUrl = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart";
            var boundary = "---WaddTaskBoundary" + Guid.NewGuid().ToString("N");
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
            await EnsureDriveSuccessAsync(response, $"Upload Task {task.Id} to AppData");
        }
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
                int jitter = random.Next(0, 200);
                int delay = (int)(baseDelayMs * Math.Pow(2, attempt)) + jitter;
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

                throw new InvalidOperationException("Google session expired. Please click 'Connect Google Drive Account' in Settings to refresh your connection.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || content.Contains("drive.googleapis.com") || content.Contains("API has not been used"))
            {
                throw new InvalidOperationException("Google Drive API is disabled in your Google Cloud Console project. Please open Google Cloud Console > Enabled APIs & Services > Enable 'Google Drive API'.");
            }

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
    public string FirebaseApiKey { get; set; } = string.Empty;
    public string FirebaseProjectId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string FirebaseIdToken { get; set; } = string.Empty;
    public string FirebaseRefreshToken { get; set; } = string.Empty;
    public string FirebaseLocalId { get; set; } = string.Empty;
    public DateTime AuthenticatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? TokenExpiresAtUtc { get; set; }
}
