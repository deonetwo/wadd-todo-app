using System;
using System.Diagnostics;
using Wadd.Core.Interfaces;
using Wadd.Core.Logging;

namespace Wadd.Services;

public class WindowsStartupService : IStartupService
{
    private const string AppName = "Wadd";
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsAutoStartEnabled()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("WindowsStartupService", "Error checking startup key", ex);
            return false;
        }
    }

    public void SetAutoStart(bool enable, bool startMinimized = false)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null)
                return;

            if (enable)
            {
                var processPath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(processPath))
                {
                    processPath = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrEmpty(processPath))
                    return;

                var command = startMinimized
                    ? $"\"{processPath}\" --autostart --minimized"
                    : $"\"{processPath}\" --autostart";

                key.SetValue(AppName, command);
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("WindowsStartupService", "Error setting startup key", ex);
        }
    }
}
