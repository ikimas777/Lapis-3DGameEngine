using System.Globalization;

namespace Lapis.Monitor;

/// <summary>Command-line options of Lapis Monitor.</summary>
/// <param name="ProcessId">Process to attach to; null attaches to the first Lapis editor or game found.</param>
/// <param name="CapturePath">Save a screenshot of the monitor to this PNG and quit.</param>
/// <param name="CaptureAfterSeconds">How long to run before taking the screenshot.</param>
internal sealed record MonitorOptions(int? ProcessId = null, string? CapturePath = null, double CaptureAfterSeconds = 5)
{
    public const string Usage = """
        Usage: Lapis.Monitor [options]

        Shows live performance data of a running Lapis editor or exported game, raises alerts and
        records every run. Without options it waits for Lapis.Editor (or Lapis.Runtime) and attaches
        by itself; it keeps running across editor restarts.

        Options:
          --pid <id>                  Attach to this process (any .NET process)
          --capture <file.png> [s]    Save a screenshot after s seconds (default 5) and quit
          --help                      Show this help

        Sessions are saved in %LOCALAPPDATA%\Lapis\Monitor\Sessions (~/.local/share/Lapis/Monitor/Sessions on Linux).
        """;

    /// <summary>Parses arguments; returns null when the help text should be shown.</summary>
    /// <exception cref="ArgumentException">An option is unknown or its value is missing.</exception>
    public static MonitorOptions? Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var options = new MonitorOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid":
                    options = options with { ProcessId = int.Parse(Value(args, ref i), CultureInfo.InvariantCulture) };
                    break;
                case "--capture":
                    options = options with { CapturePath = Value(args, ref i) };
                    if (i + 1 < args.Length && double.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var seconds))
                    {
                        options = options with { CaptureAfterSeconds = seconds };
                        i++;
                    }

                    break;
                case "--help" or "-h":
                    return null;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        return options;
    }

    private static string Value(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"Option '{args[i]}' needs a value.");
        }

        return args[++i];
    }
}
