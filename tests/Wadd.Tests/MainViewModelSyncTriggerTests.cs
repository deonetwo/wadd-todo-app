using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Wadd.Services;
using Wadd.UI.ViewModels;
using Xunit;

namespace Wadd.Tests;

public class MockSyncService : ISyncService
{
    public bool IsSignedIn { get; set; }
    public string? UserEmail { get; set; } = "testuser@gmail.com";
    public string? UserName { get; set; } = "Test User";
    public string GoogleClientId { get; set; } = string.Empty;
    public string GoogleClientSecret { get; set; } = string.Empty;
    public string FirebaseApiKey { get; set; } = string.Empty;
    public string FirebaseProjectId { get; set; } = string.Empty;
    public bool HasChangesApplied { get; set; }
    public int UnresolvedConflictCount { get; set; }

    public event EventHandler? ConflictCountChanged
    {
        add { }
        remove { }
    }
    public event EventHandler? AuthStateChanged;

    public int SignInCallCount { get; private set; }
    public int SignOutCallCount { get; private set; }
    public int SyncCallCount { get; private set; }
    public bool SignInResult { get; set; } = true;
    public TaskCompletionSource<bool>? SignInDelayTcs;

    public TaskCompletionSource<bool> SyncCalledTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<bool> SignInAsync(CancellationToken cancellationToken = default)
    {
        SignInCallCount++;
        if (SignInDelayTcs != null)
        {
            using var reg = cancellationToken.Register(() => SignInDelayTcs.TrySetCanceled(cancellationToken));
            await SignInDelayTcs.Task;
        }
        IsSignedIn = SignInResult;
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
        return SignInResult;
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        SignOutCallCount++;
        IsSignedIn = false;
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<bool> SyncAsync(CancellationToken cancellationToken = default)
    {
        SyncCallCount++;
        SyncCalledTcs.TrySetResult(true);
        return Task.FromResult(true);
    }

    public Task RefreshConflictCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

[Collection("AppSettingsTests")]
public class MainViewModelSyncTriggerTests : IDisposable
{
    private readonly string _testDbPath;

    public MainViewModelSyncTriggerTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"wadd_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch { }
    }

    private MainViewModel CreateViewModel(ISyncService syncService)
    {
        var todoSvc = new SQLiteTodoService(_testDbPath);
        var goalSvc = new SQLiteGoalService(todoSvc.DatabaseConnection);
        return new MainViewModel(
            todoSvc,
            new ThemeService(),
            syncService,
            new ExcelExportService(),
            goalSvc,
            new WindowsStartupService(),
            new AiGoalService(new System.Net.Http.HttpClient()),
            new WindowsNotificationService(),
            new AudioService());
    }

    [Fact]
    public async Task SignInWithGoogleAsync_WhenSuccessful_TriggersAutoSync()
    {
        var mockSync = new MockSyncService { IsSignedIn = false, SignInResult = true };
        var vm = CreateViewModel(mockSync);

        Assert.False(vm.IsGoogleSignedIn);

        // Execute sign in
        await vm.SignInWithGoogleCommand.ExecuteAsync(null);

        Assert.True(vm.IsGoogleSignedIn);
        Assert.Equal(1, mockSync.SignInCallCount);

        // Wait briefly for TriggerAutoSyncAsync to run
        var syncFinished = await Task.WhenAny(mockSync.SyncCalledTcs.Task, Task.Delay(2000));
        Assert.Same(mockSync.SyncCalledTcs.Task, syncFinished);
        Assert.True(mockSync.SyncCallCount >= 1);
    }

    [Fact]
    public async Task SignInWithGoogleAsync_WhenFailed_DoesNotTriggerAutoSync()
    {
        var mockSync = new MockSyncService { IsSignedIn = false, SignInResult = false };
        var vm = CreateViewModel(mockSync);

        Assert.False(vm.IsGoogleSignedIn);

        // Execute sign in which fails
        await vm.SignInWithGoogleCommand.ExecuteAsync(null);

        Assert.False(vm.IsGoogleSignedIn);
        Assert.Equal(1, mockSync.SignInCallCount);

        // Verify sync was not triggered
        var syncFinished = await Task.WhenAny(mockSync.SyncCalledTcs.Task, Task.Delay(200));
        Assert.NotSame(mockSync.SyncCalledTcs.Task, syncFinished);
        Assert.Equal(0, mockSync.SyncCallCount);
    }

    [Fact]
    public async Task ScheduleStartupAutoSync_WhenNotSignedIn_DoesNotTriggerSync()
    {
        var mockSync = new MockSyncService { IsSignedIn = false };
        var vm = CreateViewModel(mockSync);

        // Call startup sync with 10ms delay
        vm.ScheduleStartupAutoSync(delayMs: 10);

        var syncFinished = await Task.WhenAny(mockSync.SyncCalledTcs.Task, Task.Delay(200));
        Assert.NotSame(mockSync.SyncCalledTcs.Task, syncFinished);
        Assert.Equal(0, mockSync.SyncCallCount);
    }

    [Fact]
    public async Task ScheduleStartupAutoSync_WhenSignedIn_TriggersAutoSync()
    {
        var mockSync = new MockSyncService { IsSignedIn = true };
        var vm = CreateViewModel(mockSync);

        Assert.True(vm.IsGoogleSignedIn);

        // Explicitly trigger startup sync with minimal delay for fast test execution
        vm.ScheduleStartupAutoSync(delayMs: 20);

        var syncFinished = await Task.WhenAny(mockSync.SyncCalledTcs.Task, Task.Delay(3000));
        Assert.Same(mockSync.SyncCalledTcs.Task, syncFinished);
        Assert.True(mockSync.SyncCallCount >= 1);
    }

    [Fact]
    public void ServiceCollectionExtensions_GetOrCreateServiceProvider_ReusesSharedProvider()
    {
        var sc = new ServiceCollection();
        var mockSync = new MockSyncService();
        sc.AddSingleton<ISyncService>(mockSync);
        var provider = sc.BuildServiceProvider();

        ServiceCollectionExtensions.SetSharedServiceProvider(provider);

        var resolved = ServiceCollectionExtensions.GetOrCreateServiceProvider();
        Assert.Same(provider, resolved);
        Assert.Same(mockSync, resolved.GetRequiredService<ISyncService>());
    }

    [Fact]
    public async Task SignInWithGoogle_ShowsLoadingState_AndResetsWhenDone()
    {
        var mockSync = new MockSyncService
        {
            IsSignedIn = false,
            SignInResult = true,
            SignInDelayTcs = new TaskCompletionSource<bool>()
        };
        var vm = CreateViewModel(mockSync);

        Assert.False(vm.IsGoogleSigningIn);
        Assert.False(vm.IsSyncingOrSigningIn);

        var task = vm.SignInWithGoogleCommand.ExecuteAsync(null);

        // Assert loading state is active while waiting
        Assert.True(vm.IsGoogleSigningIn);
        Assert.True(vm.IsSyncingOrSigningIn);

        // Finish sign-in
        mockSync.SignInDelayTcs.SetResult(true);
        await task;

        // Assert loading state is cleared and sign-in completed
        Assert.False(vm.IsGoogleSigningIn);
        Assert.True(vm.IsGoogleSignedIn);
    }

    [Fact]
    public async Task SignInWithGoogle_CanBeCancelled_AndResetsLoadingState()
    {
        var mockSync = new MockSyncService
        {
            IsSignedIn = false,
            SignInResult = true,
            SignInDelayTcs = new TaskCompletionSource<bool>()
        };
        var vm = CreateViewModel(mockSync);

        Assert.False(vm.IsGoogleSigningIn);

        var task = vm.SignInWithGoogleCommand.ExecuteAsync(null);
        Assert.True(vm.IsGoogleSigningIn);

        // Cancel
        vm.CancelGoogleSignInCommand.Execute(null);
        await task;

        // Verify cancelled and reset
        Assert.False(vm.IsGoogleSigningIn);
        Assert.False(vm.IsGoogleSignedIn);
    }

    [Fact]
    public void IsSyncingOrSigningIn_TrueWhenEitherIsActive()
    {
        var mockSync = new MockSyncService();
        var vm = CreateViewModel(mockSync);

        Assert.False(vm.IsSyncingOrSigningIn);

        vm.IsSyncing = true;
        Assert.True(vm.IsSyncingOrSigningIn);

        vm.IsSyncing = false;
        Assert.False(vm.IsSyncingOrSigningIn);

        vm.IsGoogleSigningIn = true;
        Assert.True(vm.IsSyncingOrSigningIn);

        vm.IsGoogleSigningIn = false;
        Assert.False(vm.IsSyncingOrSigningIn);
    }
}
