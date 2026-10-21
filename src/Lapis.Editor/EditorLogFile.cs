namespace Lapis.Editor;

/// <summary>
/// Copies every <see cref="Log"/> message to <c>%LOCALAPPDATA%\Lapis\editor.log</c>.
/// The previous session's log is kept as <c>editor.previous.log</c>.
/// </summary>
internal sealed class EditorLogFile : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly Lock _sync = new();

    private EditorLogFile(StreamWriter writer)
    {
        _writer = writer;
        Log.MessageLogged += OnMessageLogged;
    }

    /// <summary>Full path of the current log file.</summary>
    public static string LogPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), EngineInfo.Name, "editor.log");

    /// <summary>Starts writing the log file; returns null when the file cannot be created.</summary>
    public static EditorLogFile? Open()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath))
            {
                File.Copy(LogPath, Path.ChangeExtension(LogPath, ".previous.log"), overwrite: true);
            }

            var writer = new StreamWriter(LogPath, append: false) { AutoFlush = true };
            return new EditorLogFile(writer);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not open the log file: {exception.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        Log.MessageLogged -= OnMessageLogged;
        lock (_sync)
        {
            _writer.Dispose();
        }
    }

    private void OnMessageLogged(LogEntry entry)
    {
        lock (_sync)
        {
            _writer.WriteLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {entry.Level,-7} {entry.Message}");
        }
    }
}
