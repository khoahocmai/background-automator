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

    /// <summary>
    /// Formats the log item for clipboard copying according to chronological diagnostics specification:
    /// HH:mm:ss.fff [LEVEL] FullMessage
    /// </summary>
    public string FormatForClipboard()
    {
        string line = $"{TimestampFormatted} [{LevelString}] {Message}";
        if (!string.IsNullOrEmpty(ExceptionDetails))
        {
            line += $" -> {ExceptionDetails}";
        }
        return line;
    }

    public LogMessageItem(LogEntry entry)
    {
        Timestamp = entry.Timestamp;
        Level = entry.Level;
        Message = entry.Message;
        ExceptionDetails = entry.Exception?.ToString();
    }
}
