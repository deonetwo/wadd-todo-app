using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;

namespace Wadd.Services;

/// <summary>
/// Implementation of ISyncService for synchronizing Todo items with Google Drive.
/// Handles Google OAuth 2.0 browser authorization, loopback authentication callbacks,
/// and 2-way state merging into the local SQLite database.
/// </summary>
public class GoogleDriveSyncService : ISyncService
{
    private readonly ITodoService _todoService;
    private readonly HttpClient _httpClient;
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

    public string GoogleClientId
    {
        get => _authRecord?.GoogleClientId ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? string.Empty;
        set
        {
            _authRecord ??= new UserAuthRecord();
            _authRecord.GoogleClientId = value;
            SaveAuthRecord();
        }
    }

    public string WebAppUrl { get; set; }

    public GoogleDriveSyncService(ITodoService todoService, HttpClient? httpClient = null, string? webAppUrl = null)
    {
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        _httpClient = httpClient ?? new HttpClient();

        LoadEnvFile();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "Wadd");
        Directory.CreateDirectory(dir);
        _authFilePath = Path.Combine(dir, "google_user_auth.json");

        WebAppUrl = webAppUrl 
            ?? Environment.GetEnvironmentVariable("WADD_SYNC_URL") 
            ?? string.Empty;

        LoadAuthRecord();
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
                        var trimmed = line.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                        var parts = trimmed.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            var key = parts[0].Trim();
                            var val = parts[1].Trim().Trim('"', '\'');
                            if (!string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                            {
                                Environment.SetEnvironmentVariable(key, val);
                            }
                        }
                    }
                    break;
                }
            }
        }
        catch
        {
            // Ignore env parsing exceptions
        }
    }

    public string GoogleClientSecret
    {
        get => _authRecord?.GoogleClientSecret ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? string.Empty;
        set
        {
            _authRecord ??= new UserAuthRecord();
            _authRecord.GoogleClientSecret = value;
            SaveAuthRecord();
        }
    }

    private void LoadAuthRecord()
    {
        try
        {
            if (File.Exists(_authFilePath))
            {
                var json = File.ReadAllText(_authFilePath);
                _authRecord = JsonSerializer.Deserialize<UserAuthRecord>(json, JsonOptions);

                // Purge obsolete/dummy placeholder records
                if (_authRecord != null && (string.IsNullOrWhiteSpace(_authRecord.UserEmail) 
                    || _authRecord.UserEmail == "Connected Account" 
                    || _authRecord.UserEmail == "connected.google.user@gmail.com" 
                    || _authRecord.UserEmail == "user@gmail.com"
                    || _authRecord.UserEmail == "Google Drive User"))
                {
                    _authRecord = null;
                    try { File.Delete(_authFilePath); } catch { }
                }
            }
        }
        catch
        {
            _authRecord = null;
        }
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
            else if (File.Exists(_authFilePath))
            {
                File.Delete(_authFilePath);
            }
        }
        catch
        {
            // Ignore storage write exceptions
        }
    }

    public async Task<bool> SignInAsync(CancellationToken cancellationToken = default)
    {
        var clientId = string.IsNullOrWhiteSpace(GoogleClientId)
            ? (Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? string.Empty)
            : GoogleClientId;

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Google OAuth Client ID is required. Please set GOOGLE_CLIENT_ID in your .env file.");
        }

        var redirectUri = "http://localhost:5001/";
        var state = Guid.NewGuid().ToString("N");
        var (codeVerifier, codeChallenge) = GeneratePkce();

        var loginHint = !string.IsNullOrWhiteSpace(UserEmail) && UserEmail != "Connected Account" 
            ? $"&login_hint={Uri.EscapeDataString(UserEmail)}" 
            : string.Empty;
        var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fdrive.file%20email%20profile&access_type=offline&prompt=consent&state={state}&code_challenge={codeChallenge}&code_challenge_method=S256{loginHint}";

        using var listener = new HttpListener();
        try
        {
            listener.Prefixes.Add("http://localhost:5001/");
            listener.Prefixes.Add("http://127.0.0.1:5001/");
            listener.Start();
        }
        catch
        {
            // Ignore socket binding exceptions if port is restricted
        }

        // Open system browser popup to Google login site AFTER listener starts
        OpenBrowserUrl(authUrl);

        try
        {
            // Wait for Google callback on loopback listener
            var contextTask = listener.GetContextAsync();
            var completedTask = await Task.WhenAny(contextTask, Task.Delay(45000, cancellationToken));

            if (completedTask == contextTask)
            {
                var context = await contextTask;
                var returnedState = context.Request.QueryString["state"];
                var code = context.Request.QueryString["code"];

                var htmlResponse = "<!DOCTYPE html><html><head><meta charset='utf-8'/></head><body style='font-family:sans-serif;text-align:center;padding-top:60px;background:#0F172A;color:#F8FAFC;'><h2>✅ Wadd Google Drive Connected!</h2><p>You may now close this browser tab and return to Wadd.</p></body></html>";
                var buffer = Encoding.UTF8.GetBytes(htmlResponse);
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, cancellationToken);
                context.Response.OutputStream.Close();

                if (returnedState != state)
                {
                    throw new InvalidOperationException("OAuth authorization state mismatch (CSRF protection trigger).");
                }

                if (!string.IsNullOrWhiteSpace(code))
                {
                    // Exchange authorization code for token with PKCE verifier
                    var (accessToken, refreshToken, email, name) = await ExchangeCodeForTokenAsync(code, codeVerifier, clientId, redirectUri, cancellationToken);

                    _authRecord = new UserAuthRecord
                    {
                        IsSignedIn = true,
                        UserEmail = email,
                        UserName = name,
                        AccessToken = accessToken,
                        RefreshToken = refreshToken,
                        AuthenticatedAt = DateTime.UtcNow
                    };

                    SaveAuthRecord();
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _authRecord = null;
            if (File.Exists(_authFilePath)) { try { File.Delete(_authFilePath); } catch { } }
            throw new InvalidOperationException($"Google sign-in failed: {ex.Message}", ex);
        }

        _authRecord = null;
        if (File.Exists(_authFilePath)) { try { File.Delete(_authFilePath); } catch { } }
        throw new InvalidOperationException("Google sign-in timed out or was cancelled by user. Please try signing in again.");
    }

    private static (string verifier, string challenge) GeneratePkce()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var verifier = Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");

        var challengeBytes = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
        var challenge = Convert.ToBase64String(challengeBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");

        return (verifier, challenge);
    }

    private async Task<bool> TryRefreshTokenAsync(CancellationToken cancellationToken)
    {
        if (_authRecord == null || string.IsNullOrWhiteSpace(_authRecord.RefreshToken))
        {
            return false;
        }

        var clientId = string.IsNullOrWhiteSpace(GoogleClientId)
            ? (Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? string.Empty)
            : GoogleClientId;

        var clientSecret = string.IsNullOrWhiteSpace(GoogleClientSecret)
            ? (Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? string.Empty)
            : GoogleClientSecret;

        if (string.IsNullOrWhiteSpace(clientId)) return false;

        try
        {
            var tokenParams = new List<KeyValuePair<string, string>>
            {
                new("client_id", clientId),
                new("refresh_token", _authRecord.RefreshToken),
                new("grant_type", "refresh_token")
            };

            if (!string.IsNullOrWhiteSpace(clientSecret))
            {
                tokenParams.Add(new("client_secret", clientSecret));
            }

            var content = new FormUrlEncodedContent(tokenParams);
            var response = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
                {
                    _authRecord.AccessToken = tokenProp.GetString() ?? _authRecord.AccessToken;
                    _authRecord.AuthenticatedAt = DateTime.UtcNow;
                    SaveAuthRecord();
                    return true;
                }
            }
        }
        catch
        {
            // Token refresh failure handling
        }

        return false;
    }

    private async Task<(string accessToken, string refreshToken, string email, string name)> ExchangeCodeForTokenAsync(string code, string codeVerifier, string clientId, string redirectUri, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Authorization code is empty.");
        }

        var clientSecret = string.IsNullOrWhiteSpace(GoogleClientSecret)
            ? (Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? string.Empty)
            : GoogleClientSecret;

        var tokenParams = new List<KeyValuePair<string, string>>
        {
            new("client_id", clientId),
            new("code", code),
            new("code_verifier", codeVerifier),
            new("grant_type", "authorization_code"),
            new("redirect_uri", redirectUri)
        };

        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            tokenParams.Add(new("client_secret", clientSecret));
        }

        var content = new FormUrlEncodedContent(tokenParams);
        var response = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google OAuth Token Exchange failed: {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var accessToken = root.TryGetProperty("access_token", out var tokenProp) ? tokenProp.GetString() : code;
        var refreshToken = root.TryGetProperty("refresh_token", out var refreshProp) ? refreshProp.GetString() : string.Empty;

        var email = string.Empty;
        var name = string.Empty;

        // 1. Instantly extract user email & name from Google ID Token JWT payload
        if (root.TryGetProperty("id_token", out var idTokenProp))
        {
            var idToken = idTokenProp.GetString();
            if (!string.IsNullOrWhiteSpace(idToken))
            {
                var jwtProfile = ParseIdTokenPayload(idToken);
                email = jwtProfile.email;
                name = jwtProfile.name;
            }
        }

        // 2. Query Google UserInfo API as secondary fallback
        if (string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(accessToken))
        {
            var userInfo = await FetchGoogleUserInfoAsync(accessToken, cancellationToken);
            email = userInfo.email;
            name = userInfo.name;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Failed to retrieve authenticated user email from Google OAuth profile.");
        }

        return (accessToken ?? code, refreshToken ?? string.Empty, email, name);
    }

    private static (string email, string name) ParseIdTokenPayload(string idToken)
    {
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length >= 2)
            {
                var payloadBase64 = parts[1];
                switch (payloadBase64.Length % 4)
                {
                    case 2: payloadBase64 += "=="; break;
                    case 3: payloadBase64 += "="; break;
                }
                var jsonBytes = Convert.FromBase64String(payloadBase64.Replace('-', '+').Replace('_', '/'));
                var json = Encoding.UTF8.GetString(jsonBytes);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var email = root.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null;
                var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;

                if (!string.IsNullOrWhiteSpace(email))
                {
                    return (email, string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name);
                }
            }
        }
        catch
        {
            // Ignore JWT parsing exception
        }

        return (string.Empty, string.Empty);
    }

    private async Task<(string email, string name)> FetchGoogleUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var email = root.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null;
                    var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        return (email, string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name);
                    }
                }
            }
            catch
            {
                // Ignore userinfo endpoint errors
            }
        }

        return ("Google Drive User", "Google Account");
    }

    private static void OpenBrowserUrl(string url)
    {
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
                url = url.Replace("&", "^&");
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
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
        await Task.Delay(200, cancellationToken);
        _authRecord = null;
        if (File.Exists(_authFilePath))
        {
            try { File.Delete(_authFilePath); } catch { }
        }
    }

    private const string DriveFileName = "wadd_sync_data.json";

    public async Task<bool> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSignedIn || _authRecord == null || string.IsNullOrWhiteSpace(_authRecord.AccessToken))
        {
            throw new InvalidOperationException("Please sign in with Google in Settings to synchronize your data.");
        }

        var token = _authRecord.AccessToken;

        try
        {
            // 1. Search for existing wadd_sync_data.json file in user's Google Drive
            var fileId = await FindGoogleDriveFileIdAsync(token, cancellationToken);
            List<TodoItem>? remoteItems = null;

            if (!string.IsNullOrWhiteSpace(fileId))
            {
                // 2. Download remote file content from Google Drive
                remoteItems = await DownloadGoogleDriveFileAsync(token, fileId, cancellationToken);
            }

            // 3. 2-Way Merge Strategy: Update local database with missing or newer remote items
            if (remoteItems != null && remoteItems.Count > 0)
            {
                foreach (var remoteItem in remoteItems)
                {
                    var localItem = await _todoService.GetByIdAsync(remoteItem.Id, cancellationToken);
                    if (localItem == null)
                    {
                        await _todoService.AddTodoAsync(remoteItem, cancellationToken);
                    }
                    else
                    {
                        var remoteTime = remoteItem.UpdatedAt ?? remoteItem.CreatedAt;
                        var localTime = localItem.UpdatedAt ?? localItem.CreatedAt;

                        if (remoteTime > localTime)
                        {
                            await _todoService.UpdateTodoAsync(remoteItem, cancellationToken);
                        }
                    }
                }
            }

            // 4. Fetch consolidated items from local SQLite database (now containing all merged items)
            var mergedItems = (await _todoService.GetTodosAsync(cancellationToken)).ToList();
            var jsonPayload = JsonSerializer.Serialize(mergedItems, JsonOptions);

            // 5. Upload the complete, authoritative merged dataset back to Google Drive
            if (!string.IsNullOrWhiteSpace(fileId))
            {
                await UpdateGoogleDriveFileAsync(token, fileId, jsonPayload, cancellationToken);
            }
            else
            {
                await CreateGoogleDriveFileAsync(token, jsonPayload, cancellationToken);
            }

            return true;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Google Drive API request failed: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse JSON response from Google Drive: {ex.Message}", ex);
        }
    }

    private async Task<string?> FindGoogleDriveFileIdAsync(string accessToken, CancellationToken cancellationToken)
    {
        var searchUrl = "https://www.googleapis.com/drive/v3/files?q=name%3D%27wadd_sync_data.json%27%20and%20trashed%3Dfalse&fields=files(id%2Cname)";
        using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureDriveSuccessAsync(response, "Search");

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("files", out var filesArr) && filesArr.GetArrayLength() > 0)
        {
            return filesArr[0].GetProperty("id").GetString();
        }

        return null;
    }

    private async Task<List<TodoItem>?> DownloadGoogleDriveFileAsync(string accessToken, string fileId, CancellationToken cancellationToken)
    {
        var downloadUrl = $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media";
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureDriveSuccessAsync(response, "Download");

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(json))
        {
            return JsonSerializer.Deserialize<List<TodoItem>>(json, JsonOptions);
        }

        return null;
    }

    private async Task UpdateGoogleDriveFileAsync(string accessToken, string fileId, string jsonPayload, CancellationToken cancellationToken)
    {
        var uploadUrl = $"https://www.googleapis.com/upload/drive/v3/files/{fileId}?uploadType=media";
        using var request = new HttpRequestMessage(HttpMethod.Patch, uploadUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureDriveSuccessAsync(response, "Update");
    }

    private async Task CreateGoogleDriveFileAsync(string accessToken, string jsonPayload, CancellationToken cancellationToken)
    {
        var uploadUrl = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart";
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var boundary = "---WaddBoundary" + Guid.NewGuid().ToString("N");
        var multipartContent = new MultipartContent("related", boundary);

        // Part 1: File Metadata
        var metadataJson = JsonSerializer.Serialize(new { name = DriveFileName, mimeType = "application/json" });
        var metadataContent = new StringContent(metadataJson, Encoding.UTF8, "application/json");
        multipartContent.Add(metadataContent);

        // Part 2: File Payload
        var fileContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        multipartContent.Add(fileContent);

        request.Content = multipartContent;
        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureDriveSuccessAsync(response, "Upload");
    }

    private async Task EnsureDriveSuccessAsync(HttpResponseMessage response, string actionName)
    {
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || content.Contains("Invalid Credentials") || content.Contains("invalid_token") || content.Contains("authError"))
            {
                // Attempt automatic token renewal using refresh_token
                if (await TryRefreshTokenAsync(CancellationToken.None))
                {
                    return;
                }

                // If refresh failed or credentials revoked, sign out cleanly
                await SignOutAsync();
                throw new InvalidOperationException("Google session expired or credentials revoked. Please sign in with Google again in Settings.");
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
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AuthenticatedAt { get; set; } = DateTime.UtcNow;
}
