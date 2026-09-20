using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;

namespace Wadd.Services;

/// <summary>
/// Windows desktop notification service supporting native Action Center toast alerts,
/// audio chimes, and in-app notification dispatch.
/// </summary>
public class WindowsNotificationService : INotificationService
{
    public static event Action<string, string>? NotificationTriggered;

    private static bool _isAumidRegistered;
    private static readonly object _aumidLock = new();

    internal static Action<string, string, bool, string?> ToastDispatcher { get; set; } = NativeWinRtToastDispatcher;
    internal static Action AlertSoundPlayer { get; set; } = PlayAlertSound;

    public bool IsSupported => OperatingSystem.IsWindows();

    public Task<bool> RequestPermissionAsync()
    {
        // Windows desktop applications do not require a runtime permission prompt
        return Task.FromResult(true);
    }

    public async Task ShowNotificationAsync(string title, string message, string? tag = null)
    {
        var settings = AppSettingsHelper.LoadSettings();
        if (!settings.EnableNotifications)
        {
            return;
        }

        // 1. Trigger in-app notification event for live UI display
        try
        {
            NotificationTriggered?.Invoke(title, message);
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("WindowsNotificationService", "Error triggering in-app notification", ex);
        }

        // 2. Play custom notification sound (notification.mp3)
        if (settings.PlayNotificationSound)
        {
            try
            {
                var audioService = new AudioService();
                audioService.PlayNotificationSound();
            }
            catch (Exception ex)
            {
                Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Failed to play notification sound", ex);
            }
        }

        // 3. Dispatch native Windows toast banner (with silent audio so it does not collide with notification.mp3)
        if (OperatingSystem.IsWindows() && settings.WindowsToastNotifications)
        {
            await Task.Run(() => DispatchNativeWindowsToast(title, message, false, tag));
        }
    }

    public Task CancelNotificationAsync(string tag)
    {
        // Native Windows toasts are self-expiring; no-op
        return Task.CompletedTask;
    }

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    private static void PlayAlertSound()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                MessageBeep(0x40); // MB_ICONASTERISK (Windows chime)
            }
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Could not play notification chime", ex);
        }
    }

    public static string? EnsureLogoFileOnDisk()
    {
        try
        {
            var appDataSquare = AppDataHelper.GetWaddFilePath("logo_square.png");
            var appDataLegacy = AppDataHelper.GetWaddFilePath("logo.png");

            // 1. Check if logo_square.png already exists in LocalAppData and is non-empty
            if (File.Exists(appDataSquare) && new FileInfo(appDataSquare).Length > 0)
            {
                return Path.GetFullPath(appDataSquare);
            }

            // 2. Check application directory or Assets subfolder
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var localAsset = Path.Combine(appDir, "Assets", "logo_square.png");
            if (File.Exists(localAsset))
            {
                File.Copy(localAsset, appDataSquare, true);
                try { File.Copy(localAsset, appDataLegacy, true); } catch { }
                return Path.GetFullPath(appDataSquare);
            }

            // 3. Extract from Avalonia embedded asset resource
            try
            {
                var uri = new Uri("avares://Wadd.UI/Assets/logo_square.png");
                if (Avalonia.Platform.AssetLoader.Exists(uri))
                {
                    using var stream = Avalonia.Platform.AssetLoader.Open(uri);
                    using var fs = File.Create(appDataSquare);
                    stream.CopyTo(fs);
                    try { File.Copy(appDataSquare, appDataLegacy, true); } catch { }
                    return Path.GetFullPath(appDataSquare);
                }
            }
            catch { }

            // 4. Walk up directory tree to find repository source asset
            var dir = new DirectoryInfo(appDir);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Wadd.UI", "Assets", "logo_square.png");
                if (File.Exists(candidate))
                {
                    File.Copy(candidate, appDataSquare, true);
                    try { File.Copy(candidate, appDataLegacy, true); } catch { }
                    return Path.GetFullPath(appDataSquare);
                }
                dir = dir.Parent;
            }
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Could not prepare logo on disk", ex);
        }

        return null;
    }

    public static void EnsureAumidRegistered(string? logoPath)
    {
        if (_isAumidRegistered || !OperatingSystem.IsWindows()) return;
        lock (_aumidLock)
        {
            if (_isAumidRegistered) return;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\Wadd.Todo");
                if (key != null)
                {
                    key.SetValue("DisplayName", "Wadd ToDo");
                    if (!string.IsNullOrWhiteSpace(logoPath))
                    {
                        key.SetValue("IconUri", logoPath);
                    }
                    key.SetValue("ShowInSettings", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
                _isAumidRegistered = true;
            }
            catch (Exception ex)
            {
                Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Could not register AUMID in registry", ex);
            }
        }
    }

    public static void DispatchNativeWindowsToast(string title, string message, bool playSound, string? tag = null)
    {
        try
        {
            ToastDispatcher(title, message, playSound, tag);
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Error sending native Windows toast", ex);
            try
            {
                AlertSoundPlayer();
            }
            catch (Exception soundEx)
            {
                Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Could not play fallback alert sound", soundEx);
            }
        }
    }

    private static void NativeWinRtToastDispatcher(string title, string message, bool playSound, string? tag = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var logoPath = EnsureLogoFileOnDisk();
        EnsureAumidRegistered(logoPath);

        var safeTitle = System.Security.SecurityElement.Escape(title) ?? "Wadd Reminder";
        var safeMessage = System.Security.SecurityElement.Escape(message) ?? string.Empty;
        var audioXml = playSound ? string.Empty : "<audio silent=\"true\"/>";
        var toastXml = $"<toast scenario=\"reminder\"><visual><binding template=\"ToastGeneric\"><text>{safeTitle}</text><text>{safeMessage}</text></binding></visual>{audioXml}</toast>";

        var uniqueTag = string.IsNullOrWhiteSpace(tag) ? Guid.NewGuid().ToString() : tag;

        IntPtr hXmlDocClass = IntPtr.Zero;
        IntPtr hXml = IntPtr.Zero;
        IntPtr hToastClass = IntPtr.Zero;
        IntPtr hManagerClass = IntPtr.Zero;
        IntPtr hAumid = IntPtr.Zero;
        IntPtr hTag = IntPtr.Zero;
        IntPtr hGroup = IntPtr.Zero;
        IntPtr hPropValueClass = IntPtr.Zero;
        IntPtr pToast = IntPtr.Zero;
        IntPtr pExpireProp = IntPtr.Zero;

        try
        {
            // 1. Load XML into XmlDocument
            hXmlDocClass = CreateHString("Windows.Data.Xml.Dom.XmlDocument");
            var xmlDocObj = RoActivateInstance(hXmlDocClass);
            var xmlDoc = (IXmlDocumentIO)xmlDocObj;

            hXml = CreateHString(toastXml);
            xmlDoc.LoadXml(hXml);

            // 2. Create ToastNotification via IToastNotificationFactory
            hToastClass = CreateHString("Windows.UI.Notifications.ToastNotification");
            var toastFactoryIid = typeof(IToastNotificationFactory).GUID;
            var toastFactory = (IToastNotificationFactory)RoGetActivationFactory(hToastClass, ref toastFactoryIid);

            var pXmlDoc = Marshal.GetIUnknownForObject(xmlDocObj);
            try
            {
                pToast = toastFactory.CreateToastNotification(pXmlDoc);
            }
            finally
            {
                if (pXmlDoc != IntPtr.Zero) Marshal.Release(pXmlDoc);
            }

            // 3. Set Tag and Group (IToastNotification2)
            var toastObj = Marshal.GetObjectForIUnknown(pToast);
            if (toastObj is IToastNotification2 toast2)
            {
                hTag = CreateHString(uniqueTag);
                hGroup = CreateHString("WaddTasks");
                toast2.put_Tag(hTag);
                toast2.put_Group(hGroup);
            }

            // 4. Set ExpirationTime (2 days)
            try
            {
                if (toastObj is IToastNotification toast)
                {
                    hPropValueClass = CreateHString("Windows.Foundation.PropertyValue");
                    var propValueStaticsIid = typeof(IPropertyValueStatics).GUID;
                    var propFactory = (IPropertyValueStatics)RoGetActivationFactory(hPropValueClass, ref propValueStaticsIid);
                    var expireFileTime = DateTime.UtcNow.AddDays(2).ToFileTimeUtc();
                    propFactory.CreateDateTime(expireFileTime, out pExpireProp);
                    if (pExpireProp != IntPtr.Zero)
                    {
                        toast.put_ExpirationTime(pExpireProp);
                    }
                }
            }
            catch (Exception ex)
            {
                Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Could not set toast expiration time", ex);
            }

            // 5. Show via ToastNotificationManager with AUMID "Wadd.Todo"
            hManagerClass = CreateHString("Windows.UI.Notifications.ToastNotificationManager");
            var managerIid = typeof(IToastNotificationManagerStatics).GUID;
            var manager = (IToastNotificationManagerStatics)RoGetActivationFactory(hManagerClass, ref managerIid);

            hAumid = CreateHString("Wadd.Todo");
            var notifier = manager.CreateToastNotifierWithId(hAumid);
            notifier.Show(pToast);
        }
        finally
        {
            if (pExpireProp != IntPtr.Zero) Marshal.Release(pExpireProp);
            if (pToast != IntPtr.Zero) Marshal.Release(pToast);
            if (hPropValueClass != IntPtr.Zero) WindowsDeleteString(hPropValueClass);
            if (hGroup != IntPtr.Zero) WindowsDeleteString(hGroup);
            if (hTag != IntPtr.Zero) WindowsDeleteString(hTag);
            if (hAumid != IntPtr.Zero) WindowsDeleteString(hAumid);
            if (hManagerClass != IntPtr.Zero) WindowsDeleteString(hManagerClass);
            if (hToastClass != IntPtr.Zero) WindowsDeleteString(hToastClass);
            if (hXml != IntPtr.Zero) WindowsDeleteString(hXml);
            if (hXmlDocClass != IntPtr.Zero) WindowsDeleteString(hXmlDocClass);
        }
    }

    private static IntPtr CreateHString(string str)
    {
        WindowsCreateString(str, str.Length, out var hstr);
        return hstr;
    }

    #region WinRT COM Interop Definitions

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = false)]
    [return: MarshalAs(UnmanagedType.IUnknown)]
    private static extern object RoGetActivationFactory(IntPtr activatableClassId, [In] ref Guid iid);

    [DllImport("combase.dll", PreserveSig = false)]
    [return: MarshalAs(UnmanagedType.IUnknown)]
    private static extern object RoActivateInstance(IntPtr activatableClassId);

    [ComImport]
    [Guid("6CD0E74E-EE65-4489-9EBF-CA43E87BA637")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IXmlDocumentIO
    {
        void GetIids(out uint iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
        void LoadXml([In] IntPtr xml);
    }

    [ComImport]
    [Guid("04124B20-82C6-4229-B109-FD9ED4662B53")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastNotificationFactory
    {
        void GetIids(out uint iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
        IntPtr CreateToastNotification([In] IntPtr xmlContent);
    }

    [ComImport]
    [Guid("997E2675-059E-4E60-8B06-1760917C8B80")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastNotification
    {
        void GetIids(out uint iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
        IntPtr get_Content();
        void put_ExpirationTime([In] IntPtr expirationTime);
        IntPtr get_ExpirationTime();
    }

    [ComImport]
    [Guid("9DFB9FD1-143A-490E-90BF-B9FBA7132DE7")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastNotification2
    {
        void GetIids(out uint iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
        void put_Tag([In] IntPtr value);
        IntPtr get_Tag();
        void put_Group([In] IntPtr value);
        IntPtr get_Group();
        void put_SuppressPopup([MarshalAs(UnmanagedType.I1)] bool value);
    }

    [ComImport]
    [Guid("75927B93-03F3-41EC-91D3-6E5BAC1B38E7")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastNotifier
    {
        void GetIids(out uint iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
        void Show([In] IntPtr notification);
        void Hide([In] IntPtr notification);
        int Setting { get; }
        void AddToSchedule(IntPtr scheduledToast);
        void RemoveFromSchedule(IntPtr scheduledToast);
        IntPtr GetScheduledToastNotifications();
    }

    [ComImport]
    [Guid("50AC103F-D235-4598-BBEF-98FE4D1A3AD4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IToastNotificationManagerStatics
    {
        void GetIids(out uint iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
        IToastNotifier CreateToastNotifier();
        IToastNotifier CreateToastNotifierWithId([In] IntPtr applicationId);
        IntPtr GetTemplateContent(int type);
    }

    [ComImport]
    [Guid("629BDBC8-D932-4FF4-96B9-8D96C5C1E858")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyValueStatics
    {
        void GetIids(out uint iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
        void CreateEmpty(out IntPtr prop);
        void CreateUInt8(byte val, out IntPtr prop);
        void CreateInt16(short val, out IntPtr prop);
        void CreateUInt16(ushort val, out IntPtr prop);
        void CreateInt32(int val, out IntPtr prop);
        void CreateUInt32(uint val, out IntPtr prop);
        void CreateInt64(long val, out IntPtr prop);
        void CreateUInt64(ulong val, out IntPtr prop);
        void CreateSingle(float val, out IntPtr prop);
        void CreateDouble(double val, out IntPtr prop);
        void CreateChar16(char val, out IntPtr prop);
        void CreateBoolean([MarshalAs(UnmanagedType.I1)] bool val, out IntPtr prop);
        void CreateString(IntPtr val, out IntPtr prop);
        void CreateInspectable(IntPtr val, out IntPtr prop);
        void CreateGuid(Guid val, out IntPtr prop);
        void CreateDateTime(long universalTime, out IntPtr prop);
    }

    #endregion
}
