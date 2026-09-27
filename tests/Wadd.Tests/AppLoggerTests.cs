using System;
using System.IO;
using System.Linq;
using Wadd.Core.Helpers;
using Wadd.Core.Logging;
using Xunit;

namespace Wadd.Tests;

[Collection("AppLoggerTests")]
public class AppLoggerTests
{
    [Fact]
    public void LogEntry_ToString_FormatsCorrectlyWithoutException()
    {
        var timestamp = new DateTime(2026, 9, 16, 14, 30, 0, DateTimeKind.Local);
        var entry = new LogEntry
        {
            Timestamp = timestamp,
            Level = LogLevel.Info,
            Source = "SyncService",
            Message = "Sync completed successfully."
        };

        var formatted = entry.ToString();

        Assert.Equal("[2026-09-16 14:30:00.000] [INFO ] [SyncService] Sync completed successfully.", formatted);
        Assert.Null(entry.ExceptionDetails);
    }

    [Fact]
    public void LogEntry_ToString_FormatsCorrectlyWithException()
    {
        var timestamp = new DateTime(2026, 9, 16, 14, 30, 0, DateTimeKind.Local);
        var ex = new InvalidOperationException("Connection timeout");
        var entry = new LogEntry
        {
            Timestamp = timestamp,
            Level = LogLevel.Error,
            Source = "NetworkService",
            Message = "Failed to connect to host",
            ExceptionDetails = $"{ex.GetType().FullName}: {ex.Message}"
        };

        var formatted = entry.ToString();

        Assert.Contains("[2026-09-16 14:30:00.000] [ERROR] [NetworkService] Failed to connect to host", formatted);
        Assert.Contains("Exception: System.InvalidOperationException: Connection timeout", formatted);
    }

    [Fact]
    public void AppLogger_LogMethods_AddEntriesToRecentLogs()
    {
        AppLogger.ClearRecentLogs();

        AppLogger.LogDebug("TestModule", "Debug message");
        AppLogger.LogInfo("TestModule", "Info message");
        AppLogger.LogWarning("TestModule", "Warning message");
        AppLogger.LogError("TestModule", "Error message", new Exception("Test exception"));
        AppLogger.LogCritical("TestModule", "Critical crash");

        var recentLogs = AppLogger.GetRecentLogs();

        Assert.True(recentLogs.Count >= 5);
        Assert.Contains(recentLogs, l => l.Level == LogLevel.Debug && l.Message == "Debug message");
        Assert.Contains(recentLogs, l => l.Level == LogLevel.Info && l.Message == "Info message");
        Assert.Contains(recentLogs, l => l.Level == LogLevel.Warning && l.Message == "Warning message");
        Assert.Contains(recentLogs, l => l.Level == LogLevel.Error && l.Message == "Error message" && l.ExceptionDetails != null);
        Assert.Contains(recentLogs, l => l.Level == LogLevel.Critical && l.Message == "Critical crash");
    }

    [Fact]
    public void AppLogger_GetRecentLogsText_ReturnsFormattedString()
    {
        AppLogger.ClearRecentLogs();
        AppLogger.LogInfo("AuthEngine", "User session verified");

        var text = AppLogger.GetRecentLogsText();

        Assert.Contains("[INFO ] [AuthEngine] User session verified", text);
    }

    [Fact]
    public void AppLogger_ClearRecentLogs_EmptiesRingBuffer()
    {
        AppLogger.LogInfo("TestModule", "Temporary message");
        Assert.NotEmpty(AppLogger.GetRecentLogs());

        AppLogger.ClearRecentLogs();

        Assert.Empty(AppLogger.GetRecentLogs());
    }

    [Fact]
    public void AppLogger_LogEmitted_EventFiresOnLog()
    {
        LogEntry? capturedEntry = null;
        Action<LogEntry> handler = entry =>
        {
            if (entry.Source == "EventSource") capturedEntry = entry;
        };

        AppLogger.LogEmitted += handler;
        try
        {
            AppLogger.LogWarning("EventSource", "Event test message");

            Assert.NotNull(capturedEntry);
            Assert.Equal(LogLevel.Warning, capturedEntry!.Level);
            Assert.Equal("EventSource", capturedEntry.Source);
            Assert.Equal("Event test message", capturedEntry.Message);
        }
        finally
        {
            AppLogger.LogEmitted -= handler;
        }
    }

    [Fact]
    public void AppLogger_RingBuffer_RespectsMaxLimit()
    {
        AppLogger.ClearRecentLogs();

        // Push 350 entries (buffer limit is 300)
        for (int i = 0; i < 350; i++)
        {
            AppLogger.LogInfo("StressTest", $"Item {i}");
        }

        var logs = AppLogger.GetRecentLogs();
        Assert.Equal(300, logs.Count);
        Assert.Equal("Item 349", logs.Last().Message);

        var firstStress = logs.First(l => l.Source == "StressTest");
        Assert.StartsWith("Item ", firstStress.Message);
        int itemNum = int.Parse(firstStress.Message.Substring("Item ".Length));
        Assert.True(itemNum >= 50 && itemNum <= 60);
    }

    [Fact]
    public void AppLogger_WritesToFile_InLogsDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "WaddLoggerTest_" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("WADD_DATA_DIR", tempPath);

        try
        {
            var logFile = AppDataHelper.GetCurrentLogFilePath();
            var logDir = AppDataHelper.GetLogsDirectory();

            Assert.StartsWith(tempPath, logDir);
            Assert.EndsWith(".log", logFile);

            AppLogger.LogInfo("DiskTest", "Writing test log entry to disk");

            Assert.True(File.Exists(logFile), $"Log file should exist at: {logFile}");
            var content = File.ReadAllText(logFile);
            Assert.Contains("[INFO ] [DiskTest] Writing test log entry to disk", content);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WADD_DATA_DIR", null);
            if (Directory.Exists(tempPath))
            {
                try { Directory.Delete(tempPath, true); } catch { }
            }
        }
    }
}
