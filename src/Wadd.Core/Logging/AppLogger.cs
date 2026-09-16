using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Wadd.Core.Helpers;

namespace Wadd.Core.Logging;

/// <summary>
/// Centralized, thread-safe logger for Wadd across Core, Services, Desktop, and Android platforms.
/// Provides persistent daily file logging with automatic rotation, in-memory ring-buffering for diagnostics,
/// and trace forwarding.
/// </summary>
public static class AppLogger
{
    private static readonly object SyncLock = new();
    private static readonly List<LogEntry> RecentLogs = new();
    private const int MaxRecentEntries = 300;
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int RetentionDays = 7;
    private static DateTime _lastCleanupDate = DateTime.MinValue;

    public static event Action<LogEntry>? LogEmitted;

    public static void LogDebug(string source, string message)
    {
        Log(LogLevel.Debug, source, message);
    }

    public static void LogInfo(string source, string message)
    {
        Log(LogLevel.Info, source, message);
    }

    public static void LogWarning(string source, string message, Exception? ex = null)
    {
        Log(LogLevel.Warning, source, message, ex);
    }

    public static void LogError(string source, string message, Exception? ex = null)
    {
        Log(LogLevel.Error, source, message, ex);
    }

    public static void LogCritical(string source, string message, Exception? ex = null)
    {
        Log(LogLevel.Critical, source, message, ex);
    }

    public static void Log(LogLevel level, string source, string message, Exception? ex = null)
    {
        string? exDetails = null;
        if (ex != null)
        {
            var sb = new StringBuilder();
            sb.Append($"{ex.GetType().FullName}: {ex.Message}");
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                sb.AppendLine();
                sb.Append(ex.StackTrace);
            }
            if (ex.InnerException != null)
            {
                sb.AppendLine();
                sb.Append($" ---> Inner Exception: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                if (!string.IsNullOrWhiteSpace(ex.InnerException.StackTrace))
                {
                    sb.AppendLine();
                    sb.Append(ex.InnerException.StackTrace);
                }
            }
            exDetails = sb.ToString();
        }

        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Source = source,
            Message = message,
            ExceptionDetails = exDetails
        };

        var entryString = entry.ToString();

        lock (SyncLock)
        {
            // 1. Maintain in-memory ring buffer
            if (RecentLogs.Count >= MaxRecentEntries)
            {
                RecentLogs.RemoveAt(0);
            }
            RecentLogs.Add(entry);

            // 2. Output to System.Diagnostics.Trace
            try
            {
                Trace.WriteLine(entryString);
            }
            catch
            {
                // Ignore trace sink failure
            }

            // 3. Write to persistent log file with auto-rotation
            try
            {
                WriteToFile(entryString);
                PerformRetentionCleanupIfNeeded();
            }
            catch
            {
                // Non-fatal if disk write fails
            }
        }

        // Notify subscribers outside of lock
        try
        {
            LogEmitted?.Invoke(entry);
        }
        catch
        {
            // Ignore subscriber errors
        }
    }

    private static void WriteToFile(string formattedLine)
    {
        var logFile = AppDataHelper.GetCurrentLogFilePath();
        var fileInfo = new FileInfo(logFile);

        // Rollover if single daily file exceeds 5MB
        if (fileInfo.Exists && fileInfo.Length > MaxFileSizeBytes)
        {
            var rolloverPath = Path.Combine(
                AppDataHelper.GetLogsDirectory(),
                $"wadd-{DateTime.Now:yyyy-MM-dd-HHmmss}.log");
            File.Move(logFile, rolloverPath);
        }

        File.AppendAllText(logFile, formattedLine + Environment.NewLine, Encoding.UTF8);
    }

    private static void PerformRetentionCleanupIfNeeded()
    {
        var today = DateTime.Today;
        if (today == _lastCleanupDate) return;

        _lastCleanupDate = today;
        try
        {
            var logsDir = AppDataHelper.GetLogsDirectory();
            var directory = new DirectoryInfo(logsDir);
            if (!directory.Exists) return;

            var cutoff = today.AddDays(-RetentionDays);
            foreach (var file in directory.GetFiles("wadd-*.log"))
            {
                if (file.LastWriteTime < cutoff)
                {
                    file.Delete();
                }
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    public static IReadOnlyList<LogEntry> GetRecentLogs()
    {
        lock (SyncLock)
        {
            return RecentLogs.ToList();
        }
    }

    public static string GetRecentLogsText()
    {
        lock (SyncLock)
        {
            return string.Join(Environment.NewLine, RecentLogs.Select(x => x.ToString()));
        }
    }

    public static void ClearRecentLogs()
    {
        lock (SyncLock)
        {
            RecentLogs.Clear();
        }
    }
}
