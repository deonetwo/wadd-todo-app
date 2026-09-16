using System;

namespace Wadd.Core.Logging;

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public LogLevel Level { get; set; } = LogLevel.Info;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ExceptionDetails { get; set; }

    public override string ToString()
    {
        var levelStr = Level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT ",
            _ => "INFO "
        };

        var baseMsg = $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] [{Source}] {Message}";
        if (!string.IsNullOrWhiteSpace(ExceptionDetails))
        {
            baseMsg += $"{Environment.NewLine}  Exception: {ExceptionDetails}";
        }

        return baseMsg;
    }
}
