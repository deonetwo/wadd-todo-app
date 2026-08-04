using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;

namespace Wadd.Services;

public class CloudSyncMetadata
{
    public long LatestRevision { get; set; } = 1;
    public int SchemaVersion { get; set; } = 1;
    public DateTime LastSync { get; set; } = DateTime.UtcNow;
    public List<DeviceMetadata> RegisteredDevices { get; set; } = new();
}

public class GoogleDriveSyncService : ISyncService
{
    private readonly ITodoService _todoService;
    private readonly ISyncLogRepository _syncLogRepository;
    private readonly IConflictRepository _conflictRepository;
    private readonly IDeviceService _deviceService;
    private readonly ConflictResolutionEngine _conflictEngine;
    private readonly HttpClient _httpClient;
    private UserAuthRecord? _authRecord;
    private readonly string _authFilePath;

    private const string SyncFolderName = "Wadd ToDo Sync Data";
    const string WarningFileName = "⚠️_WARNING_DO_NOT_DELETE_WADD_SYNC_FOLDER.txt";
    private const string MetadataFileName = "wadd_cloud_metadata.json";
    private const string LogsFileName = "wadd_sync_logs.json";

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
        get
        {
            var envVal = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
            if (!string.IsNullOrWhiteSpace(envVal)) return envVal.Trim();
            return _authRecord?.GoogleClientId ?? string.Empty;
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
            return _authRecord?.GoogleClientSecret ?? string.Empty;
        }
        set
        {
            _authRecord ??= new UserAuthRecord();
            _authRecord.GoogleClientSecret = value;
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
        ConflictResolutionEngine? conflictEngine = null)
    {
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        _httpClient = httpClient ?? new HttpClient();

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
                        var trimmed = line.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                        var parts = trimmed.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            var key = parts[0].Trim();
                            var val = parts[1].Trim().Trim('"', '\'');
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

        var redirectUri = "http://localhost:5001/";
        var state = Guid.NewGuid().ToString("N");
        var codeVerifier = GenerateCryptoRandomString(32);
        var codeChallenge = CreateCodeChallenge(codeVerifier);

        var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        try
        {
            var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                          $"client_id={Uri.EscapeDataString(GoogleClientId)}&" +
                          $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                          $"response_type=code&" +
                          $"scope={Uri.EscapeDataString("openid email profile https://www.googleapis.com/auth/drive.file")}&" +
                          $"code_challenge={Uri.EscapeDataString(codeChallenge)}&" +
                          $"code_challenge_method=S256&" +
                          $"state={Uri.EscapeDataString(state)}&" +
                          $"access_type=offline&" +
                          $"prompt=consent";

            OpenBrowserUrl(authUrl);

            var contextTask = listener.GetContextAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);

            var completedTask = await Task.WhenAny(contextTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                throw new TimeoutException("Google Sign-In authorization timed out. Please try again.");
            }

            var context = await contextTask;
            var request = context.Request;
            var response = context.Response;

            var code = request.QueryString["code"];
            var returnedState = request.QueryString["state"];
            var error = request.QueryString["error"];

            if (!string.IsNullOrEmpty(error))
            {
                SendHtmlResponse(response, "Authorization Failed", $"<h3>Google Sign-in failed: {WebUtility.HtmlEncode(error)}</h3><p>You can close this window and return to Wadd.</p>");
                throw new InvalidOperationException($"Google auth error: {error}");
            }

            if (returnedState != state || string.IsNullOrEmpty(code))
            {
                SendHtmlResponse(response, "Invalid Response", "<h3>Invalid authorization state or missing code.</h3><p>You can close this window.</p>");
                throw new InvalidOperationException("Invalid authorization state received from Google callback.");
            }

            SendHtmlResponse(response, "Sign-in Successful!", "<h2 style='color:#2563eb;'>Authentication Successful!</h2><p>Wadd ToDo has been successfully connected to your Google Drive account.</p><p>You may now close this browser tab and return to Wadd.</p>");

            var tokenRecord = await ExchangeCodeForTokensAsync(code, codeVerifier, redirectUri, cancellationToken);
            var userInfo = await FetchUserInfoAsync(tokenRecord.AccessToken, cancellationToken);

            _authRecord = new UserAuthRecord
            {
                IsSignedIn = true,
                UserEmail = userInfo.Email,
                UserName = userInfo.Name,
                GoogleClientId = GoogleClientId,
                AccessToken = tokenRecord.AccessToken,
                RefreshToken = tokenRecord.RefreshToken,
                AuthenticatedAt = DateTime.UtcNow
            };

            SaveAuthRecord();
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

    private async Task<(string AccessToken, string RefreshToken)> ExchangeCodeForTokensAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
    {
        var tokenUrl = "https://oauth2.googleapis.com/token";
        var dict = new Dictionary<string, string>
        {
            ["client_id"] = GoogleClientId,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        };

        var clientSecret = GoogleClientSecret;
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            dict["client_secret"] = clientSecret;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(dict)
        };

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token exchange failed ({response.StatusCode}): {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var accessToken = root.TryGetProperty("access_token", out var atProp) ? atProp.GetString() ?? string.Empty : string.Empty;
        var refreshToken = root.TryGetProperty("refresh_token", out var rtProp) ? rtProp.GetString() ?? string.Empty : string.Empty;

        return (accessToken, refreshToken);
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
        if (_authRecord == null || string.IsNullOrWhiteSpace(_authRecord.RefreshToken)) return false;

        try
        {
            var tokenUrl = "https://oauth2.googleapis.com/token";
            var dict = new Dictionary<string, string>
            {
                ["client_id"] = GoogleClientId,
                ["refresh_token"] = _authRecord.RefreshToken,
                ["grant_type"] = "refresh_token"
            };

            var clientSecret = GoogleClientSecret;
            if (!string.IsNullOrWhiteSpace(clientSecret))
            {
                dict["client_secret"] = clientSecret;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
            {
                Content = new FormUrlEncodedContent(dict)
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("access_token", out var atProp))
                {
                    _authRecord.AccessToken = atProp.GetString() ?? string.Empty;
                    SaveAuthRecord();
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    private static string GenerateCryptoRandomString(int length)
    {
        byte[] randomBytes = new byte[length];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        return Convert.ToBase64String(randomBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "")
            .Substring(0, length);
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        return Convert.ToBase64String(challengeBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private static void OpenBrowserUrl(string url)
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(desktop.MainWindow);
                if (topLevel?.Launcher != null)
                {
                    topLevel.Launcher.LaunchUriAsync(new Uri(url));
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[WARN] Avalonia TopLevel.Launcher failed, falling back to process launcher: {ex.Message}");
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
        await Task.Delay(200, cancellationToken);
        _authRecord = null;
        if (File.Exists(_authFilePath))
        {
            try { File.Delete(_authFilePath); } catch { }
        }
    }

    // =========================================================================
    //  OFFLINE-FIRST INCREMENTAL MULTI-DEVICE SYNCHRONIZATION ENGINE
    // =========================================================================

    public async Task<bool> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSignedIn || _authRecord == null || string.IsNullOrWhiteSpace(_authRecord.AccessToken))
        {
            throw new InvalidOperationException("Please sign in with Google in Settings to synchronize your data.");
        }

        var token = _authRecord.AccessToken;

        try
        {
            // 1. Ensure parent folder "Wadd ToDo Sync Data" exists on Google Drive
            var folderId = await EnsureSyncFolderExistsAsync(token, cancellationToken);

            // 2. Ensure warning file exists inside folder
            await EnsureWarningFileExistsAsync(token, folderId, cancellationToken);

            // 3. Fetch remote metadata and remote sync logs
            var metadataFileId = await FindFolderFileIdAsync(token, folderId, MetadataFileName, cancellationToken);
            var logsFileId = await FindFolderFileIdAsync(token, folderId, LogsFileName, cancellationToken);

            CloudSyncMetadata cloudMetadata = new();
            if (!string.IsNullOrWhiteSpace(metadataFileId))
            {
                var metaJson = await DownloadFileContentAsync(token, metadataFileId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(metaJson))
                {
                    cloudMetadata = JsonSerializer.Deserialize<CloudSyncMetadata>(metaJson, JsonOptions) ?? new CloudSyncMetadata();
                }
            }

            List<SyncLog> cloudLogs = new();
            if (!string.IsNullOrWhiteSpace(logsFileId))
            {
                var logsJson = await DownloadFileContentAsync(token, logsFileId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(logsJson))
                {
                    cloudLogs = JsonSerializer.Deserialize<List<SyncLog>>(logsJson, JsonOptions) ?? new List<SyncLog>();
                }
            }

            // Register current device metadata in cloud metadata
            var deviceMeta = _deviceService.GetDeviceMetadata();
            var existingDevIndex = cloudMetadata.RegisteredDevices.FindIndex(d => d.DeviceId == deviceMeta.DeviceId);
            if (existingDevIndex >= 0)
                cloudMetadata.RegisteredDevices[existingDevIndex] = deviceMeta;
            else
                cloudMetadata.RegisteredDevices.Add(deviceMeta);

            // 4. Fetch local pending logs
            var localPendingLogs = (await _syncLogRepository.GetPendingLogsAsync(cancellationToken)).ToList();

            // 5. Build combined map of records modified locally or remotely
            var recordIdsToProcess = new HashSet<Guid>();
            foreach (var log in localPendingLogs) recordIdsToProcess.Add(log.RecordId);
            foreach (var log in cloudLogs) recordIdsToProcess.Add(log.RecordId);

            // Fetch all local items (including soft deleted)
            var localItemsDict = new Dictionary<Guid, TodoItem>();
            var rawSqliteSvc = _todoService as SQLiteTodoService;

            if (rawSqliteSvc != null)
            {
                var allRaw = await rawSqliteSvc.GetAllRawAsync(cancellationToken);
                localItemsDict = allRaw.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            }
            else
            {
                var allItems = await _todoService.GetTodosAsync(cancellationToken);
                localItemsDict = allItems.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            }

            // Create dictionary of latest remote state per record ID by replaying cloud logs
            var cloudItemsDict = ReplayLogsToSnapshot(cloudLogs);

            // 6. Process each record for incremental sync & conflict detection
            var mergedLogsToUpload = new List<SyncLog>(cloudLogs);
            var logsToMarkSynced = new List<Guid>();

            foreach (var log in localPendingLogs)
            {
                if (!mergedLogsToUpload.Any(l => l.Id == log.Id))
                {
                    mergedLogsToUpload.Add(log);
                }
                logsToMarkSynced.Add(log.Id);
            }

            foreach (var recordId in recordIdsToProcess)
            {
                bool hasLocal = localItemsDict.TryGetValue(recordId, out var localItem);
                bool hasCloud = cloudItemsDict.TryGetValue(recordId, out var cloudItem);

                if (hasLocal && !hasCloud)
                {
                    // Created locally, upload to cloud
                    continue; 
                }
                else if (!hasLocal && hasCloud)
                {
                    // Created on remote device, download to local
                    if (rawSqliteSvc != null)
                    {
                        await rawSqliteSvc.DirectUpsertFromSyncAsync(cloudItem!, cancellationToken);
                    }
                    else
                    {
                        await _todoService.AddTodoAsync(cloudItem!, cancellationToken);
                    }
                }
                else if (hasLocal && hasCloud)
                {
                    // Modified on both sides -> Conflict detection / Field merge
                    var localMod = localItem!.UpdatedAt ?? localItem.CreatedAt;
                    var cloudMod = cloudItem!.UpdatedAt ?? cloudItem.CreatedAt;

                    if (localItem.Version > cloudItem.Version && localMod > cloudMod)
                    {
                        // Local is strictly newer
                        continue;
                    }
                    else if (cloudItem.Version > localItem.Version && cloudMod > localMod)
                    {
                        // Cloud is strictly newer
                        if (rawSqliteSvc != null)
                        {
                            await rawSqliteSvc.DirectUpsertFromSyncAsync(cloudItem, cancellationToken);
                        }
                    }
                    else
                    {
                        // Independent changes -> Attempt Field-by-Field Merge
                        var mergeResult = _conflictEngine.MergeTodoItems(localItem, cloudItem, null);
                        if (!mergeResult.HasConflict)
                        {
                            // Auto-merge successful! Apply merged state locally and add to cloud logs
                            if (rawSqliteSvc != null)
                            {
                                await rawSqliteSvc.DirectUpsertFromSyncAsync(mergeResult.MergedItem, cancellationToken);
                            }

                            var autoMergeLog = new SyncLog
                            {
                                Id = Guid.NewGuid(),
                                TableName = "TodoItem",
                                RecordId = mergeResult.MergedItem.Id,
                                Operation = mergeResult.MergedItem.IsDeleted ? SyncOperation.Delete : SyncOperation.Update,
                                PayloadJson = JsonSerializer.Serialize(mergeResult.MergedItem, JsonOptions),
                                Timestamp = DateTime.UtcNow,
                                DeviceId = _deviceService.GetDeviceId(),
                                Synced = true
                            };
                            mergedLogsToUpload.Add(autoMergeLog);
                        }
                        else
                        {
                            // Overlapping field conflict! Store in Conflict Repository for user resolution
                            var conflict = new SyncConflict
                            {
                                Id = Guid.NewGuid(),
                                TableName = "TodoItem",
                                RecordId = recordId,
                                LocalVersionJson = JsonSerializer.Serialize(localItem, JsonOptions),
                                CloudVersionJson = JsonSerializer.Serialize(cloudItem, JsonOptions),
                                LocalUpdatedAt = localItem.UpdatedAt ?? localItem.CreatedAt,
                                CloudUpdatedAt = cloudItem.UpdatedAt ?? cloudItem.CreatedAt,
                                OriginatingDeviceId = _deviceService.GetDeviceId(),
                                ConflictingFieldsJson = JsonSerializer.Serialize(mergeResult.ConflictingFields, JsonOptions),
                                CreatedAt = DateTime.UtcNow,
                                Status = ConflictStatus.Unresolved
                            };

                            await _conflictRepository.AddConflictAsync(conflict, cancellationToken);
                        }
                    }
                }
            }

            // Mark local logs as synced
            await _syncLogRepository.MarkLogsAsSyncedAsync(logsToMarkSynced, cancellationToken);

            // Update cloud metadata & sync logs inside "Wadd ToDo Sync Data" folder
            cloudMetadata.LatestRevision = mergedLogsToUpload.Count;
            cloudMetadata.LastSync = DateTime.UtcNow;

            var updatedMetaJson = JsonSerializer.Serialize(cloudMetadata, JsonOptions);
            var updatedLogsJson = JsonSerializer.Serialize(mergedLogsToUpload, JsonOptions);

            if (!string.IsNullOrWhiteSpace(metadataFileId))
                await UpdateGoogleDriveFileAsync(token, metadataFileId, updatedMetaJson, cancellationToken);
            else
                await CreateFileInFolderAsync(token, folderId, MetadataFileName, updatedMetaJson, cancellationToken);

            if (!string.IsNullOrWhiteSpace(logsFileId))
                await UpdateGoogleDriveFileAsync(token, logsFileId, updatedLogsJson, cancellationToken);
            else
                await CreateFileInFolderAsync(token, folderId, LogsFileName, updatedLogsJson, cancellationToken);

            await RefreshConflictCountAsync(cancellationToken);
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

    private static Dictionary<Guid, TodoItem> ReplayLogsToSnapshot(List<SyncLog> logs)
    {
        var dict = new Dictionary<Guid, TodoItem>();
        foreach (var log in logs.OrderBy(l => l.Timestamp))
        {
            if (string.IsNullOrWhiteSpace(log.PayloadJson)) continue;
            try
            {
                var item = JsonSerializer.Deserialize<TodoItem>(log.PayloadJson, JsonOptions);
                if (item != null)
                {
                    dict[item.Id] = item;
                }
            }
            catch { }
        }
        return dict;
    }

    private async Task<string> EnsureSyncFolderExistsAsync(string accessToken, CancellationToken cancellationToken)
    {
        var searchUrl = $"https://www.googleapis.com/drive/v3/files?q=name%3D%27{Uri.EscapeDataString(SyncFolderName)}%27%20and%20mimeType%3D%27application%2Fvnd.google-apps.folder%27%20and%20trashed%3Dfalse&fields=files(id%2Cname)";
        using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureDriveSuccessAsync(response, "Search Sync Folder");

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("files", out var filesArr) && filesArr.GetArrayLength() > 0)
        {
            var firstFile = filesArr[0];
            if (firstFile.TryGetProperty("id", out var idProp) && !string.IsNullOrEmpty(idProp.GetString()))
            {
                return idProp.GetString()!;
            }
        }

        // Create folder if not found
        var createUrl = "https://www.googleapis.com/drive/v3/files";
        using var createReq = new HttpRequestMessage(HttpMethod.Post, createUrl);
        createReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var folderMeta = JsonSerializer.Serialize(new
        {
            name = SyncFolderName,
            mimeType = "application/vnd.google-apps.folder"
        });
        createReq.Content = new StringContent(folderMeta, Encoding.UTF8, "application/json");

        var createResp = await _httpClient.SendAsync(createReq, cancellationToken);
        await EnsureDriveSuccessAsync(createResp, "Create Sync Folder");

        var createJson = await createResp.Content.ReadAsStringAsync(cancellationToken);
        using var createDoc = JsonDocument.Parse(createJson);
        if (createDoc.RootElement.TryGetProperty("id", out var newIdProp) && !string.IsNullOrEmpty(newIdProp.GetString()))
        {
            return newIdProp.GetString()!;
        }
        throw new InvalidOperationException($"Failed to create sync folder on Google Drive: {createJson}");
    }

    private async Task EnsureWarningFileExistsAsync(string accessToken, string folderId, CancellationToken cancellationToken)
    {
        var fileId = await FindFolderFileIdAsync(accessToken, folderId, WarningFileName, cancellationToken);
        if (string.IsNullOrWhiteSpace(fileId))
        {
            var warningContent = "⚠️ WARNING: This folder contains synchronization data for Wadd To-Do Application.\nDo NOT delete or modify files inside this folder, as doing so will disconnect sync and may result in loss of un-synced data.";
            await CreateFileInFolderAsync(accessToken, folderId, WarningFileName, warningContent, cancellationToken);
        }
    }

    private async Task<string?> FindFolderFileIdAsync(string accessToken, string folderId, string fileName, CancellationToken cancellationToken)
    {
        var searchUrl = $"https://www.googleapis.com/drive/v3/files?q=name%3D%27{Uri.EscapeDataString(fileName)}%27%20and%20%27{folderId}%27%20in%20parents%20and%20trashed%3Dfalse&fields=files(id%2Cname)";
        using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureDriveSuccessAsync(response, $"Search {fileName}");

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("files", out var filesArr) && filesArr.GetArrayLength() > 0)
        {
            var firstFile = filesArr[0];
            if (firstFile.TryGetProperty("id", out var fileIdProp))
            {
                return fileIdProp.GetString();
            }
        }

        return null;
    }

    private async Task<string> DownloadFileContentAsync(string accessToken, string fileId, CancellationToken cancellationToken)
    {
        var downloadUrl = $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media";
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureDriveSuccessAsync(response, "Download File Content");

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task CreateFileInFolderAsync(string accessToken, string folderId, string fileName, string content, CancellationToken cancellationToken)
    {
        var uploadUrl = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart";
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var boundary = "---WaddBoundary" + Guid.NewGuid().ToString("N");
        var multipartContent = new MultipartContent("related", boundary);

        var metadataJson = JsonSerializer.Serialize(new
        {
            name = fileName,
            parents = new[] { folderId }
        });
        multipartContent.Add(new StringContent(metadataJson, Encoding.UTF8, "application/json"));
        multipartContent.Add(new StringContent(content, Encoding.UTF8, "application/json"));

        request.Content = multipartContent;
        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureDriveSuccessAsync(response, $"Upload {fileName}");
    }

    private async Task UpdateGoogleDriveFileAsync(string accessToken, string fileId, string content, CancellationToken cancellationToken)
    {
        var uploadUrl = $"https://www.googleapis.com/upload/drive/v3/files/{fileId}?uploadType=media";
        using var request = new HttpRequestMessage(HttpMethod.Patch, uploadUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(content, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureDriveSuccessAsync(response, "Update File");
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
