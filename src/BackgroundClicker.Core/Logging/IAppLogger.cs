namespace BackgroundClicker.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public sealed record LogEntry(DateTime Timestamp, LogLevel Level, string Message, Exception? Exception = null)
{
    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss.fff}] [{Level.ToString().ToUpperInvariant()}] {Message}" +
        (Exception != null ? $" (Error: {Exception.Message})" : string.Empty);
}

public interface IAppLogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
    void Debug(string message) => Log(LogLevel.Debug, message);
    void Info(string message) => Log(LogLevel.Info, message);
    void Warning(string message) => Log(LogLevel.Warning, message);
    void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);

    event Action<LogEntry>? MessageLogged;
    IReadOnlyList<LogEntry> Entries { get; }
    void Clear();
}

public sealed class InMemoryLogger : IAppLogger
{
    private readonly object _lock = new();
    private readonly List<LogEntry> _entries = new();
    private readonly int _maxEntries;

    public InMemoryLogger(int maxEntries = 1000)
    {
        _maxEntries = maxEntries;
    }

    public event Action<LogEntry>? MessageLogged;

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList();
            }
        }
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        var entry = new LogEntry(DateTime.Now, level, message, exception);
        lock (_lock)
        {
            if (_entries.Count >= _maxEntries)
            {
                _entries.RemoveAt(0);
            }
            _entries.Add(entry);
        }

        try
        {
            MessageLogged?.Invoke(entry);
        }
        catch
        {
            // Do not allow listener exceptions to bubble up
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}
