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
public class MainActivity : AvaloniaMainActivity
{
    public const int RcSignIn = 9001;
    public static MainActivity? Instance { get; private set; }
    public System.Threading.Tasks.TaskCompletionSource<Wadd.Core.Interfaces.NativeAuthResult>? PendingAuthTcs { get; set; }

    public static System.Threading.Tasks.TaskCompletionSource<string>? PendingWebOAuthTcs { get; set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Instance = this;
        SQLitePCL.Batteries_V2.Init();
        base.OnCreate(savedInstanceState);
        HandleIntent(Intent);
    }

    protected override void OnNewIntent(global::Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleIntent(intent);
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
                            var acct = account.Account ?? new global::Android.Accounts.Account(account.Email ?? "", "com.google");
                            accessToken = global::Android.Gms.Auth.GoogleAuthUtil.GetToken(
                                this,
                                acct,
                                "oauth2:https://www.googleapis.com/auth/drive.file email profile");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Trace.WriteLine($"[WARN] GoogleAuthUtil.GetToken error: {ex.Message}");
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
                    tcs.TrySetResult(new Wadd.Core.Interfaces.NativeAuthResult
                    {
                        IsSuccess = false,
                        ErrorMessage = ex.Message
                    });
                }
            });
        }
    }

    protected override void OnPause()
    {
        base.OnPause();
        App.SaveThemeAndSettings();
    }

    protected override void OnStop()
    {
        base.OnStop();
        App.SaveThemeAndSettings();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        App.SaveThemeAndSettings();
    }
}
