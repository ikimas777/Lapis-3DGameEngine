using Lapis.Projects;

namespace Lapis.Player;

internal static class Program
{
    private static int Main(string[] args)
    {
        PlayerOptions? options;
        try
        {
            options = PlayerOptions.Parse(args);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(PlayerOptions.Usage);
            return 2;
        }

        if (options is null)
        {
            Console.WriteLine(PlayerOptions.Usage);
            return 0;
        }

        // The exported game has no console window, so messages also go to a log file next to it.
        using var log = PlayerLog.Open(Path.Combine(options.GameFolder, "log.txt"));

        try
        {
            var manifest = GameManifest.Load(options.DataFolder);
            using var app = new PlayerApplication(options, manifest);
            app.Run();
            return 0;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "The game stopped because of an unhandled error.");
            return 1;
        }
    }
}

/// <summary>Copies log messages to a text file.</summary>
internal sealed class PlayerLog : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly Lock _sync = new();

    private PlayerLog(StreamWriter writer)
    {
        _writer = writer;
        Log.MessageLogged += OnMessage;
    }

    public static PlayerLog? Open(string path)
    {
        try
        {
            return new PlayerLog(new StreamWriter(path, append: false) { AutoFlush = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null; // e.g. the game was installed to a read-only folder
        }
    }

    public void Dispose()
    {
        Log.MessageLogged -= OnMessage;
        lock (_sync)
        {
            _writer.Dispose();
        }
    }

    private void OnMessage(LogEntry entry)
    {
        lock (_sync)
        {
            _writer.WriteLine($"[{entry.Timestamp:HH:mm:ss}] {entry.Level,-7} {entry.Message}");
        }
    }
}
