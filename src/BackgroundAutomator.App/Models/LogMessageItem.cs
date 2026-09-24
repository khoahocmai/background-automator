using BackgroundAutomator.Core.Logging;

namespace BackgroundAutomator.App.Models;

public sealed class LogMessageItem
{
    public DateTime Timestamp { get; }
    public string TimestampFormatted => Timestamp.ToString("HH:mm:ss.fff");
    public LogLevel Level { get; }
    public string LevelString => Level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        _ => "INFO"
    };

    public string LevelColor => Level switch
    {
        LogLevel.Debug => "#808080",
        LogLevel.Info => "#0078D4",
        LogLevel.Warning => "#D83B01",
        LogLevel.Error => "#E81123",
        _ => "#000000"
    };

    public string Message { get; }
    public string? ExceptionDetails { get; }
    public string FormattedLine => $"[{TimestampFormatted}] [{LevelString}] {Message}{(string.IsNullOrEmpty(ExceptionDetails) ? "" : $" -> {ExceptionDetails}")}";

    public LogMessageItem(LogEntry entry)
    {
        Timestamp = entry.Timestamp;
        Level = entry.Level;
        Message = entry.Message;
        ExceptionDetails = entry.Exception?.ToString();
    }
}
