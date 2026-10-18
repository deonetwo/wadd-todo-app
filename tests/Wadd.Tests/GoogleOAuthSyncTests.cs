using System;
using System.Text.Json;
using System.Threading.Tasks;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Wadd.Services;
using Xunit;

namespace Wadd.Tests;

public class GoogleOAuthSyncTests
{
    [Fact]
    public void UserAuthRecord_Serialization_PreservesAllActiveFields()
    {
        var original = new UserAuthRecord
        {
            IsSignedIn = true,
            UserEmail = "testuser@gmail.com",
            UserName = "Test User",
            GoogleClientId = "test-client-id.apps.googleusercontent.com",
            GoogleClientSecret = "test-client-secret-123",
            OAuthProxyUrl = "https://wadd-oauth-proxy.workers.dev",
            OAuthProxySecret = "secret-app-123",
            AccessToken = "ya29.test-access-token",
            RefreshToken = "1//04-test-refresh-token-permanent",
            FirebaseApiKey = "AIzaSyTest",
            FirebaseProjectId = "test-project",
            FirebaseIdToken = "fb-id-token-123",
            FirebaseRefreshToken = "fb-refresh-token-123",
            FirebaseLocalId = "fb-local-id-123",
            AuthenticatedAt = DateTime.UtcNow,
            TokenExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<UserAuthRecord>(json);

        Assert.NotNull(restored);
        Assert.True(restored.IsSignedIn);
        Assert.Equal(original.UserEmail, restored.UserEmail);
        Assert.Equal(original.UserName, restored.UserName);
        Assert.Equal(original.GoogleClientId, restored.GoogleClientId);
        Assert.Equal(original.GoogleClientSecret, restored.GoogleClientSecret);
        Assert.Equal(original.OAuthProxyUrl, restored.OAuthProxyUrl);
        Assert.Equal(original.OAuthProxySecret, restored.OAuthProxySecret);
        Assert.Equal(original.AccessToken, restored.AccessToken);
        Assert.Equal(original.RefreshToken, restored.RefreshToken);
        Assert.Equal(original.FirebaseApiKey, restored.FirebaseApiKey);
        Assert.Equal(original.FirebaseProjectId, restored.FirebaseProjectId);
        Assert.Equal(original.FirebaseIdToken, restored.FirebaseIdToken);
        Assert.Equal(original.FirebaseRefreshToken, restored.FirebaseRefreshToken);
        Assert.Equal(original.FirebaseLocalId, restored.FirebaseLocalId);
        Assert.Equal(original.TokenExpiresAtUtc?.ToString("o"), restored.TokenExpiresAtUtc?.ToString("o"));
    }

    [Fact]
    public void UserAuthRecord_ProactiveExpirationCheck_AccuratelyIdentifiesExpiringToken()
    {
        var validRecord = new UserAuthRecord
        {
            IsSignedIn = true,
            AccessToken = "ya29.valid",
            RefreshToken = "1//04.refresh",
            TokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(45)
        };

        var expiredRecord = new UserAuthRecord
        {
            IsSignedIn = true,
            AccessToken = "ya29.expired",
            RefreshToken = "1//04.refresh",
            TokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };

        var nearExpiredRecord = new UserAuthRecord
        {
            IsSignedIn = true,
            AccessToken = "ya29.near_expired",
            RefreshToken = "1//04.refresh",
            TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(60) // within 2 minutes
        };

        // Check if token is valid and not expiring soon
        bool isValid = validRecord.TokenExpiresAtUtc.HasValue &&
                       DateTime.UtcNow < validRecord.TokenExpiresAtUtc.Value.AddMinutes(-2);
        Assert.True(isValid);

        // Check if expiredRecord is detected as expired
        bool isExpiredNeedsRefresh = expiredRecord.TokenExpiresAtUtc.HasValue &&
                                     DateTime.UtcNow >= expiredRecord.TokenExpiresAtUtc.Value.AddMinutes(-2);
        Assert.True(isExpiredNeedsRefresh);

        // Check if nearExpiredRecord is detected as needing refresh
        bool isNearExpiredNeedsRefresh = nearExpiredRecord.TokenExpiresAtUtc.HasValue &&
                                         DateTime.UtcNow >= nearExpiredRecord.TokenExpiresAtUtc.Value.AddMinutes(-2);
        Assert.True(isNearExpiredNeedsRefresh);
    }

    [Fact]
    public async Task GoogleDriveSyncService_SignOutAsync_FiresAuthStateChanged()
    {
        var authPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wadd_auth_{Guid.NewGuid():N}.json");
        try
        {
            var inMemoryTodo = new InMemoryTodoService();
            var syncService = new GoogleDriveSyncService(inMemoryTodo, authFilePath: authPath);

            bool authStateChangedFired = false;
            syncService.AuthStateChanged += (_, _) =>
            {
                authStateChangedFired = true;
            };

            await syncService.SignOutAsync();

            Assert.True(authStateChangedFired);
            Assert.False(syncService.IsSignedIn);
        }
        finally
        {
            try { if (System.IO.File.Exists(authPath)) System.IO.File.Delete(authPath); } catch { }
        }
    }

    [Fact]
    public void GoogleDriveTaskJson_SerializationAndDeserialization_PreservesFullTaskData()
    {
        var taskId = Guid.NewGuid();
        var item = new TodoItem
        {
            Id = taskId,
            Title = "Cloud Task Sync",
            Description = "Testing Google Drive AppData integration",
            Priority = Wadd.Core.Enums.TodoPriority.High,
            DueDate = DateTime.UtcNow.AddDays(2),
            ReminderAt = DateTime.UtcNow.AddHours(4),
            IsCompleted = false,
            Category = "Productivity",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow
        };

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var taskJson = JsonSerializer.Serialize(item, jsonOptions);

        var deserializedTask = JsonSerializer.Deserialize<TodoItem>(taskJson, jsonOptions);
        Assert.NotNull(deserializedTask);
        Assert.Equal(item.Id, deserializedTask.Id);
        Assert.Equal(item.Title, deserializedTask.Title);
        Assert.Equal(item.Description, deserializedTask.Description);
        Assert.Equal(item.Priority, deserializedTask.Priority);
        Assert.Equal(item.Category, deserializedTask.Category);
        Assert.Equal(item.ReminderAt, deserializedTask.ReminderAt);
    }

    [Fact]
    public void ExtractProxyErrorMessage_ParsesMessageAndErrorDescriptionCleanly()
    {
        var jsonWithMsg = "{\"error\":\"access_denied\",\"message\":\"Access denied. The email address is not authorized for cloud sync.\"}";
        var extractedMsg = GoogleDriveSyncService.ExtractProxyErrorMessage(jsonWithMsg, "OAuth proxy failed");
        Assert.Equal("Access denied. The email address is not authorized for cloud sync.", extractedMsg);

        var jsonWithDesc = "{\"error\":\"unauthorized\",\"error_description\":\"Invalid application proxy secret\"}";
        var extractedDesc = GoogleDriveSyncService.ExtractProxyErrorMessage(jsonWithDesc, "OAuth proxy failed");
        Assert.Equal("Invalid application proxy secret", extractedDesc);
    }

    [Fact]
    public async Task GoogleDriveSyncService_NativeSignIn_RejectsWhenOAuthProxyReturnsForbidden()
    {
        var authPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wadd_auth_{Guid.NewGuid():N}.json");
        try
        {
            var inMemoryTodo = new InMemoryTodoService();
            var handler = new FakeHttpMessageHandler((req, _) =>
            {
                var resp = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
                {
                    Content = new System.Net.Http.StringContent(
                        "{\"error\":\"access_denied\",\"message\":\"Access denied. Not authorized for cloud sync.\"}",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
                return Task.FromResult(resp);
            });
            var httpClient = new System.Net.Http.HttpClient(handler);
            var fakeNativeAuth = new FakeNativeGoogleAuthService();

            var syncService = new GoogleDriveSyncService(
                inMemoryTodo,
                httpClient: httpClient,
                nativeAuthService: fakeNativeAuth,
                authFilePath: authPath);

            syncService.GoogleClientId = "test-client-id.apps.googleusercontent.com";
            syncService.OAuthProxyUrl = "https://wadd-oauth-proxy.workers.dev";

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => syncService.SignInAsync());
            Assert.Equal("Access denied. Not authorized for cloud sync.", ex.Message);
            Assert.False(syncService.IsSignedIn);
        }
        finally
        {
            try { if (System.IO.File.Exists(authPath)) System.IO.File.Delete(authPath); } catch { }
        }
    }

    [Fact]
    public async Task GoogleDriveSyncService_NativeSignIn_SetsSubscriptionExpiredWhenTrialExpired()
    {
        var authPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wadd_auth_{Guid.NewGuid():N}.json");
        try
        {
            var inMemoryTodo = new InMemoryTodoService();
            var handler = new FakeHttpMessageHandler((req, _) =>
            {
                var resp = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
                {
                    Content = new System.Net.Http.StringContent(
                        "{\"error\":\"subscription_expired\",\"plan\":\"trial\",\"upgrade_url\":\"https://lunoit.biz.id/wadd\",\"message\":\"Your 14-day cloud sync trial has expired.\"}",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
                return Task.FromResult(resp);
            });
            var httpClient = new System.Net.Http.HttpClient(handler);
            var fakeNativeAuth = new FakeNativeGoogleAuthService();

            var syncService = new GoogleDriveSyncService(
                inMemoryTodo,
                httpClient: httpClient,
                nativeAuthService: fakeNativeAuth,
                authFilePath: authPath);

            syncService.GoogleClientId = "test-client-id.apps.googleusercontent.com";
            syncService.OAuthProxyUrl = "https://wadd-oauth-proxy.workers.dev";

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => syncService.SignInAsync());
            Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(syncService.IsSubscriptionExpired);
            Assert.Equal("trial", syncService.SubscriptionPlan);
            Assert.Equal("https://lunoit.biz.id/wadd", syncService.SubscriptionUpgradeUrl);
        }
        finally
        {
            try { if (System.IO.File.Exists(authPath)) System.IO.File.Delete(authPath); } catch { }
        }
    }

    [Fact]
    public async Task GoogleDriveSyncService_SignOut_PreservesLastSyncedEmailAndKeepsLocalData()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wadd_acct_signout_{Guid.NewGuid():N}.db");
        var authPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wadd_auth_{Guid.NewGuid():N}.json");
        try
        {
            var todoService = new SQLiteTodoService(dbPath);
            var goalService = new SQLiteGoalService(todoService.DatabaseConnection);
            var syncService = new GoogleDriveSyncService(todoService, goalService: goalService, authFilePath: authPath);

            await todoService.CreateAsync(new TodoItem { Title = "User A Task" });
            await goalService.SaveGoalAsync(new LifeGoal { Title = "User A Goal" });

            syncService.SetAuthRecordForTesting(new UserAuthRecord
            {
                IsSignedIn = true,
                UserEmail = "userA@gmail.com",
                LastSyncedUserEmail = "userA@gmail.com",
                AccessToken = "token-a"
            });

            await syncService.SignOutAsync();

            Assert.False(syncService.IsSignedIn);
            Assert.Equal("userA@gmail.com", syncService.LastSyncedUserEmail);
            Assert.Single(await todoService.GetAllAsync());
            Assert.Single(await goalService.GetGoalsAsync());
        }
        finally
        {
            try { if (System.IO.File.Exists(dbPath)) System.IO.File.Delete(dbPath); } catch { }
            try { if (System.IO.File.Exists(authPath)) System.IO.File.Delete(authPath); } catch { }
        }
    }

    [Fact]
    public async Task GoogleDriveSyncService_SignInWithSameOrFirstEmail_PreservesLocalData()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wadd_acct_same_{Guid.NewGuid():N}.db");
        var authPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wadd_auth_{Guid.NewGuid():N}.json");
        try
        {
            var okHandler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent("{\"access_token\":\"ok\",\"expires_in\":3600}", System.Text.Encoding.UTF8, "application/json")
            }));
            var todoService = new SQLiteTodoService(dbPath);
            var goalService = new SQLiteGoalService(todoService.DatabaseConnection);
            var fakeNativeAuth = new FakeNativeGoogleAuthService("userA@gmail.com");
            var syncService = new GoogleDriveSyncService(
                todoService,
                httpClient: new System.Net.Http.HttpClient(okHandler),
                nativeAuthService: fakeNativeAuth,
                goalService: goalService,
                authFilePath: authPath);

            await todoService.CreateAsync(new TodoItem { Title = "Offline Task" });
            await goalService.SaveGoalAsync(new LifeGoal { Title = "Offline Goal" });

            // 1. First-time login (no previous LastSyncedUserEmail): keeps local data
            syncService.SetAuthRecordForTesting(new UserAuthRecord
            {
                GoogleClientId = "test-client-id.apps.googleusercontent.com"
            });
            var signedInFirst = await syncService.SignInAsync();
            Assert.True(signedInFirst);
            Assert.Single(await todoService.GetAllAsync());
            Assert.Single(await goalService.GetGoalsAsync());

            // 2. Sign out and re-login with the SAME email (userA@gmail.com): keeps local data
            await syncService.SignOutAsync();
            syncService.GoogleClientId = "test-client-id.apps.googleusercontent.com";
            var signedInAgain = await syncService.SignInAsync();
            Assert.True(signedInAgain);
            Assert.Equal("userA@gmail.com", syncService.UserEmail);
            Assert.Single(await todoService.GetAllAsync());
            Assert.Single(await goalService.GetGoalsAsync());
        }
        finally
        {
            try { if (System.IO.File.Exists(dbPath)) System.IO.File.Delete(dbPath); } catch { }
            try { if (System.IO.File.Exists(authPath)) System.IO.File.Delete(authPath); } catch { }
        }
    }

    [Fact]
    public async Task GoogleDriveSyncService_SignInWithDifferentEmail_ClearsPreviousAccountLocalData()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wadd_acct_switch_{Guid.NewGuid():N}.db");
        var authPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wadd_auth_{Guid.NewGuid():N}.json");
        try
        {
            var okHandler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent("{\"access_token\":\"ok\",\"expires_in\":3600}", System.Text.Encoding.UTF8, "application/json")
            }));
            var todoService = new SQLiteTodoService(dbPath);
            var goalService = new SQLiteGoalService(todoService.DatabaseConnection);
            var fakeNativeAuth = new FakeNativeGoogleAuthService("userB@gmail.com");
            var syncService = new GoogleDriveSyncService(
                todoService,
                httpClient: new System.Net.Http.HttpClient(okHandler),
                nativeAuthService: fakeNativeAuth,
                goalService: goalService,
                authFilePath: authPath);

            // Populate User A's local data
            await todoService.CreateAsync(new TodoItem { Title = "User A Private Task" });
            await todoService.SaveDateNoteAsync(DateTime.Today, "User A Note");
            await todoService.SaveCustomTagAsync("UserATag");
            var goalA = await goalService.SaveGoalAsync(new LifeGoal { Title = "User A Goal" });
            await goalService.SaveMilestoneAsync(new GoalMilestone { GoalId = goalA.Id, Title = "User A Milestone" });
            await goalService.SaveJournalEntryAsync(new JournalEntry { GoalId = goalA.Id, Title = "User A Journal", Content = "Secret" });

            syncService.SetAuthRecordForTesting(new UserAuthRecord
            {
                IsSignedIn = true,
                UserEmail = "userA@gmail.com",
                LastSyncedUserEmail = "userA@gmail.com",
                GoogleClientId = "test-client-id.apps.googleusercontent.com",
                LastSyncTimestampUtc = DateTime.UtcNow,
                MigratedToManifests = true
            });

            // Sign out from User A, then sign in as User B
            await syncService.SignOutAsync();
            Assert.Equal("userA@gmail.com", syncService.LastSyncedUserEmail);

            var signedInB = await syncService.SignInAsync();
            Assert.True(signedInB);
            Assert.Equal("userB@gmail.com", syncService.UserEmail);
            Assert.Equal("userB@gmail.com", syncService.LastSyncedUserEmail);
            Assert.False(syncService.MigratedToManifests);

            // Verify all of User A's local data and pending sync logs were cleared before User B's sync
            Assert.Empty(await todoService.GetAllRawAsync());
            Assert.Empty(await todoService.GetAllDateNotesRawAsync());
            Assert.Empty(await todoService.GetCustomTagsAsync());
            Assert.Empty(await goalService.GetAllGoalsRawAsync());
            Assert.Empty(await goalService.GetAllMilestonesRawAsync());
            Assert.Empty(await goalService.GetAllJournalEntriesRawAsync());
            Assert.Empty(await todoService.SyncLogRepository.GetPendingLogsAsync());
        }
        finally
        {
            try { if (System.IO.File.Exists(dbPath)) System.IO.File.Delete(dbPath); } catch { }
            try { if (System.IO.File.Exists(authPath)) System.IO.File.Delete(authPath); } catch { }
        }
    }

    [Fact]
    public async Task GoogleDriveSyncService_SignInWithDifferentEmail_WhenCancelledByUser_KeepsPreviousAccountLocalData()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wadd_acct_cancel_{Guid.NewGuid():N}.db");
        var authPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wadd_auth_{Guid.NewGuid():N}.json");
        try
        {
            var okHandler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent("{\"access_token\":\"ok\",\"expires_in\":3600}", System.Text.Encoding.UTF8, "application/json")
            }));
            var todoService = new SQLiteTodoService(dbPath);
            var goalService = new SQLiteGoalService(todoService.DatabaseConnection);
            var fakeNativeAuth = new FakeNativeGoogleAuthService("userB@gmail.com");
            var syncService = new GoogleDriveSyncService(
                todoService,
                httpClient: new System.Net.Http.HttpClient(okHandler),
                nativeAuthService: fakeNativeAuth,
                goalService: goalService,
                authFilePath: authPath);

            await todoService.CreateAsync(new TodoItem { Title = "User A Private Task" });
            await goalService.SaveGoalAsync(new LifeGoal { Title = "User A Goal" });

            syncService.SetAuthRecordForTesting(new UserAuthRecord
            {
                IsSignedIn = true,
                UserEmail = "userA@gmail.com",
                LastSyncedUserEmail = "userA@gmail.com",
                GoogleClientId = "test-client-id.apps.googleusercontent.com"
            });

            await syncService.SignOutAsync();

            string? promptedPrevEmail = null;
            string? promptedNewEmail = null;
            syncService.ConfirmAccountSwitchAsync = (prev, next) =>
            {
                promptedPrevEmail = prev;
                promptedNewEmail = next;
                return Task.FromResult(false);
            };

            await Assert.ThrowsAsync<OperationCanceledException>(() => syncService.SignInAsync());

            Assert.Equal("userA@gmail.com", promptedPrevEmail);
            Assert.Equal("userB@gmail.com", promptedNewEmail);
            Assert.False(syncService.IsSignedIn);
            Assert.Equal("userA@gmail.com", syncService.LastSyncedUserEmail);
            Assert.Single(await todoService.GetAllAsync());
            Assert.Single(await goalService.GetGoalsAsync());
        }
        finally
        {
            try { if (System.IO.File.Exists(dbPath)) System.IO.File.Delete(dbPath); } catch { }
            try { if (System.IO.File.Exists(authPath)) System.IO.File.Delete(authPath); } catch { }
        }
    }

    private sealed class FakeNativeGoogleAuthService : INativeGoogleAuthService
    {
        private readonly string _email;

        public FakeNativeGoogleAuthService(string email = "blocked@example.com")
        {
            _email = email;
        }

        public bool IsSupported => true;

        public Task<NativeAuthResult> SignInAsync(string googleClientId, System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NativeAuthResult
            {
                IsSuccess = true,
                IdToken = "fake-id-token",
                AccessToken = "fake-access-token",
                Email = _email,
                DisplayName = "Test User"
            });
        }

        public Task<NativeAuthResult> TrySilentSignInAsync(string googleClientId, System.Threading.CancellationToken cancellationToken = default)
        {
            return SignInAsync(googleClientId, cancellationToken);
        }
    }

    private sealed class FakeHttpMessageHandler : System.Net.Http.HttpMessageHandler
    {
        private readonly Func<System.Net.Http.HttpRequestMessage, System.Threading.CancellationToken, Task<System.Net.Http.HttpResponseMessage>> _responder;

        public FakeHttpMessageHandler(Func<System.Net.Http.HttpRequestMessage, System.Threading.CancellationToken, Task<System.Net.Http.HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken)
        {
            return _responder(request, cancellationToken);
        }
    }
}
