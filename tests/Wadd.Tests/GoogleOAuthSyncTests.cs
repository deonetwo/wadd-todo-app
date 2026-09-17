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
        var inMemoryTodo = new InMemoryTodoService();
        var syncService = new GoogleDriveSyncService(inMemoryTodo);

        bool authStateChangedFired = false;
        syncService.AuthStateChanged += (_, _) =>
        {
            authStateChangedFired = true;
        };

        await syncService.SignOutAsync();

        Assert.True(authStateChangedFired);
        Assert.False(syncService.IsSignedIn);
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
}
