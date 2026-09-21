using System;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Avalonia;
using Avalonia.Android;
using Wadd.UI;

namespace Wadd.Android;

[Application]
public class AndroidApplication : AvaloniaAndroidApplication<App>
{
    public AndroidApplication(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
        .WithInterFont();
    }
}

[Activity(
    Label = "Wadd",
    Theme = "@style/MainTheme",
    Icon = "@drawable/icon",
    MainLauncher = true,
    WindowSoftInputMode = SoftInput.AdjustResize | SoftInput.StateHidden,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
[IntentFilter(
    new[] { global::Android.Content.Intent.ActionView },
    Categories = new[] { global::Android.Content.Intent.CategoryDefault, global::Android.Content.Intent.CategoryBrowsable },
    DataScheme = "com.wadd.todoapp",
    DataPathPrefix = "/oauth2redirect")]
public class MainActivity : AvaloniaMainActivity, Wadd.Core.Interfaces.INotificationPermissionRequester
{
    public const int RcSignIn = 9001;
    public const int RcNotification = 1010;
    public static MainActivity? Instance { get; private set; }
    public System.Threading.Tasks.TaskCompletionSource<Wadd.Core.Interfaces.NativeAuthResult>? PendingAuthTcs { get; set; }
    public System.Threading.Tasks.TaskCompletionSource<bool>? PendingNotificationPermissionTcs { get; set; }

    public static System.Threading.Tasks.TaskCompletionSource<string>? PendingWebOAuthTcs { get; set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Instance = this;
        SQLitePCL.Batteries_V2.Init();
        global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
        {
            Wadd.Core.Logging.AppLogger.LogCritical("AndroidEnvironment", "Unhandled Android runtime exception", e.Exception);
        };

        base.OnCreate(savedInstanceState);
        HandleIntent(Intent);
        RequestNotificationPermissionIfRequired();
        Wadd.Core.Helpers.WaddDatabaseNotifier.DataChanged += OnDatabaseChanged;
        TaskAlarmScheduler.RescheduleAll(this);

        try
        {
            var services = Wadd.Services.ServiceCollectionExtensions.GetOrCreateServiceProvider();
            var syncService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Wadd.Core.Interfaces.ISyncService>(services);
            if (syncService != null)
            {
                syncService.AuthStateChanged += (s, e) =>
                {
                    if (syncService.IsSignedIn)
                    {
                        SchedulePeriodicSync(this);
                    }
                    else
                    {
                        CancelPeriodicSync(this);
                    }
                };

                if (syncService.IsSignedIn)
                {
                    SchedulePeriodicSync(this);
                }
            }
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("MainActivity", "Error configuring sync service hooks in OnCreate", ex);
        }
    }

    public static void SchedulePeriodicSync(global::Android.Content.Context context)
    {
        try
        {
            var services = Wadd.Services.ServiceCollectionExtensions.GetOrCreateServiceProvider();
            var syncService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Wadd.Core.Interfaces.ISyncService>(services);
            if (syncService == null || !syncService.IsSignedIn)
            {
                CancelPeriodicSync(context);
                return;
            }

            var constraints = new AndroidX.Work.Constraints.Builder()
                .SetRequiredNetworkType(AndroidX.Work.NetworkType.Connected!)
                .Build();

            var syncWorkRequest = new AndroidX.Work.PeriodicWorkRequest.Builder(
                typeof(SyncWorker),
                15,
                Java.Util.Concurrent.TimeUnit.Minutes!)
                .SetConstraints(constraints)
                .Build();

            AndroidX.Work.WorkManager.GetInstance(context).EnqueueUniquePeriodicWork(
                SyncWorker.WorkName,
                AndroidX.Work.ExistingPeriodicWorkPolicy.Keep!,
                (AndroidX.Work.PeriodicWorkRequest)syncWorkRequest);

            Wadd.Core.Logging.AppLogger.LogInfo("MainActivity", "Scheduled unique periodic background sync via WorkManager (15 min interval).");
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("MainActivity", "Failed to schedule background sync via WorkManager", ex);
        }
    }

    public static void CancelPeriodicSync(global::Android.Content.Context context)
    {
        try
        {
            AndroidX.Work.WorkManager.GetInstance(context).CancelUniqueWork(SyncWorker.WorkName);
            Wadd.Core.Logging.AppLogger.LogInfo("MainActivity", "Cancelled unique periodic background sync.");
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("MainActivity", "Failed to cancel background sync via WorkManager", ex);
        }
    }

    private void OnDatabaseChanged(object? sender, EventArgs e)
    {
        try
        {
            TaskAlarmScheduler.RescheduleAll(this);
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("MainActivity", "Error rescheduling alarms on database change", ex);
        }
    }

    protected override void OnNewIntent(global::Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleIntent(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        try
        {
            TodayTasksWidgetProvider.TriggerRefresh(this);
            Wadd.Core.Helpers.WaddDatabaseNotifier.NotifyDataChanged();
            TaskAlarmScheduler.RescheduleAll(this);
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("MainActivity", "Error refreshing widget on resume", ex);
        }
    }

    private void HandleIntent(global::Android.Content.Intent? intent)
    {
        if (intent?.DataString != null && intent.DataString.StartsWith("com.wadd.todoapp://oauth2redirect"))
        {
            PendingWebOAuthTcs?.TrySetResult(intent.DataString);
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, global::Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == RcSignIn && PendingAuthTcs != null)
        {
            var tcs = PendingAuthTcs;
            PendingAuthTcs = null;

            if (resultCode == Result.Canceled || data == null)
            {
                tcs.TrySetResult(new Wadd.Core.Interfaces.NativeAuthResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Google Sign-In was cancelled by user."
                });
                return;
            }

            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var task = global::Android.Gms.Auth.Api.SignIn.GoogleSignIn.GetSignedInAccountFromIntent(data);
                    if (task != null && task.IsSuccessful && task.Result is global::Android.Gms.Auth.Api.SignIn.GoogleSignInAccount account)
                    {
                        string? accessToken = null;
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(account.Email))
                            {
                                var acct = account.Account ?? new global::Android.Accounts.Account(account.Email, "com.google");
                                accessToken = global::Android.Gms.Auth.GoogleAuthUtil.GetToken(
                                    this,
                                    acct,
                                    "oauth2:https://www.googleapis.com/auth/drive.file email profile");
                            }
                        }
                        catch (Exception ex)
                        {
                            Wadd.Core.Logging.AppLogger.LogWarning("MainActivity", "GoogleAuthUtil.GetToken error", ex);
                        }

                        tcs.TrySetResult(new Wadd.Core.Interfaces.NativeAuthResult
                        {
                            IsSuccess = true,
                            IdToken = account.IdToken,
                            AccessToken = !string.IsNullOrWhiteSpace(accessToken) ? accessToken : account.ServerAuthCode,
                            Email = account.Email,
                            DisplayName = account.DisplayName
                        });
                    }
                    else
                    {
                        var ex = task?.Exception;
                        var detail = ex?.Message ?? "Google Sign-In failed or was cancelled by user.";
                        if (ex is Java.Lang.Exception jEx)
                        {
                            var className = jEx.Class?.Name ?? jEx.GetType().Name;
                            detail += $" [{className}: {jEx.Message}]";
                        }
                        tcs.TrySetResult(new Wadd.Core.Interfaces.NativeAuthResult
                        {
                            IsSuccess = false,
                            ErrorMessage = detail
                        });
                    }
                }
                catch (Exception ex)
                {
                    Wadd.Core.Logging.AppLogger.LogError("MainActivity", "Google Sign-In callback error", ex);
                    tcs.TrySetResult(new Wadd.Core.Interfaces.NativeAuthResult
                    {
                        IsSuccess = false,
                        ErrorMessage = ex.Message
                    });
                }
            });
        }
    }

    public void RequestNotificationPermissionIfRequired()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var settings = Wadd.Core.Helpers.AppSettingsHelper.LoadSettings();
            if (settings.EnableNotifications && CheckSelfPermission("android.permission.POST_NOTIFICATIONS") != Permission.Granted)
            {
                RequestPermissions(new[] { "android.permission.POST_NOTIFICATIONS" }, RcNotification);
            }
        }
    }

    public System.Threading.Tasks.Task<bool> RequestNotificationPermissionAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return System.Threading.Tasks.Task.FromResult(true);
        }

        if (CheckSelfPermission("android.permission.POST_NOTIFICATIONS") == Permission.Granted)
        {
            return System.Threading.Tasks.Task.FromResult(true);
        }

        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        PendingNotificationPermissionTcs = tcs;

        RunOnUiThread(() =>
        {
            try
            {
                RequestPermissions(new[] { "android.permission.POST_NOTIFICATIONS" }, RcNotification);
            }
            catch (Exception ex)
            {
                Wadd.Core.Logging.AppLogger.LogError("MainActivity", "RequestNotificationPermissionAsync error", ex);
                tcs.TrySetResult(false);
            }
        });

        return tcs.Task;
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == RcNotification)
        {
            bool granted = grantResults != null && grantResults.Length > 0 && grantResults[0] == Permission.Granted;
            PendingNotificationPermissionTcs?.TrySetResult(granted);
            PendingNotificationPermissionTcs = null;
        }
    }

    protected override void OnPause()
    {
        base.OnPause();
        App.SaveThemeAndSettings();
        TaskAlarmScheduler.RescheduleAll(this);
    }

    protected override void OnStop()
    {
        base.OnStop();
        App.SaveThemeAndSettings();
        TaskAlarmScheduler.RescheduleAll(this);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        Wadd.Core.Helpers.WaddDatabaseNotifier.DataChanged -= OnDatabaseChanged;
        App.SaveThemeAndSettings();
    }
}
