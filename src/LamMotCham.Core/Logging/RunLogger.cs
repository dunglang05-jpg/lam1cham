namespace LamMotCham.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message, string? TaskId);

public interface IRunLogger
{
    void Log(LogLevel level, string message, string? taskId = null);
}

public static class RunLoggerExtensions
{
    public static void Info(this IRunLogger logger, string message, string? taskId = null) =>
        logger.Log(LogLevel.Info, message, taskId);

    public static void Warning(this IRunLogger logger, string message, string? taskId = null) =>
        logger.Log(LogLevel.Warning, message, taskId);

    public static void Error(this IRunLogger logger, string message, string? taskId = null) =>
        logger.Log(LogLevel.Error, message, taskId);
}

/// <summary>Mọi logger kế thừa lớp này để bảo đảm nội dung luôn được redact trước khi ghi.</summary>
public abstract class RedactingRunLogger : IRunLogger
{
    private readonly TimeProvider _time;

    protected RedactingRunLogger(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
    }

    public void Log(LogLevel level, string message, string? taskId = null)
    {
        var entry = new LogEntry(_time.GetUtcNow(), level, Redactor.Redact(message), taskId);
        Write(entry);
    }

    protected abstract void Write(LogEntry entry);
}

/// <summary>Giữ log trong bộ nhớ với giới hạn số dòng (dùng cho UI và test).</summary>
public sealed class InMemoryRunLogger : RedactingRunLogger
{
    private readonly Queue<LogEntry> _entries = new();
    private readonly int _capacity;
    private readonly Lock _gate = new();

    public InMemoryRunLogger(int capacity = 5_000, TimeProvider? timeProvider = null)
        : base(timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return [.. _entries];
        }
    }

    protected override void Write(LogEntry entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
        }
    }
}

/// <summary>Chuyển log đến nhiều đích, ví dụ file và bộ nhớ.</summary>
public sealed class CompositeRunLogger(params IRunLogger[] loggers) : IRunLogger
{
    private readonly IRunLogger[] _loggers = loggers;

    public void Log(LogLevel level, string message, string? taskId = null)
    {
        foreach (var logger in _loggers)
        {
            logger.Log(level, message, taskId);
        }
    }
}
