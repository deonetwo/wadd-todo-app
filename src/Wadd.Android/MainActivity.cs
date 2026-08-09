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
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        SQLitePCL.Batteries_V2.Init();
        base.OnCreate(savedInstanceState);
    }
}
