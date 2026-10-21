namespace Lapis;

/// <summary>Severity of a <see cref="LogEntry"/>.</summary>
public enum LogLevel
{
    /// <summary>General information.</summary>
    Info,

    /// <summary>Something unexpected that does not stop the engine.</summary>
    Warning,

    /// <summary>A failure that needs attention.</summary>
    Error,
}

/// <summary>
/// A single message written to the <see cref="Log"/>. <see cref="FilePath"/> and <see cref="Line"/> point
/// at the source of the problem when known (for example a script compile error).
/// </summary>
public readonly record struct LogEntry(LogLevel Level, string Message, DateTime Timestamp, string? FilePath = null, int Line = 0);

/// <summary>
/// Engine-wide log. Messages go to the standard output and to <see cref="MessageLogged"/>
/// subscribers (for example the editor console).
/// </summary>
public static class Log
{
    private static readonly Lock Sync = new();

    /// <summary>Raised for every message, on the thread that logged it.</summary>
    public static event Action<LogEntry>? MessageLogged;

    /// <summary>Logs an informational message.</summary>
    public static void Info(string message) => Write(LogLevel.Info, message);

    /// <summary>Logs a warning.</summary>
    public static void Warning(string message) => Write(LogLevel.Warning, message);

    /// <summary>Logs a warning about a location in a file (1-based line).</summary>
    public static void Warning(string message, string filePath, int line) => Write(LogLevel.Warning, message, filePath, line);

    /// <summary>Logs an error.</summary>
    public static void Error(string message) => Write(LogLevel.Error, message);

    /// <summary>Logs an error about a location in a file (1-based line).</summary>
    public static void Error(string message, string filePath, int line) => Write(LogLevel.Error, message, filePath, line);

    /// <summary>Logs an error with exception details.</summary>
    public static void Error(Exception exception, string? message = null) =>
        Write(LogLevel.Error, message is null ? exception.ToString() : $"{message}{Environment.NewLine}{exception}");

    private static void Write(LogLevel level, string message, string? filePath = null, int line = 0)
    {
        var entry = new LogEntry(level, message, DateTime.Now, filePath, line);

        lock (Sync)
        {
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = level switch
            {
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                _ => previousColor,
            };
            Console.WriteLine($"[{entry.Timestamp:HH:mm:ss}] {level,-7} {message}");
            Console.ForegroundColor = previousColor;
        }

        MessageLogged?.Invoke(entry);
    }
}
