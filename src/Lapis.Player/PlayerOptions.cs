using System.Globalization;
using Lapis.Projects;

namespace Lapis.Player;

/// <summary>Command-line options of the player.</summary>
/// <param name="GameFolder">Folder with the executable, <c>Game.dll</c> and <c>Data</c>.</param>
/// <param name="ScenePath">Scene to start instead of the manifest's start scene (relative to <c>Data</c>).</param>
/// <param name="Fullscreen">Start in fullscreen mode.</param>
/// <param name="CapturePath">When set, save a screenshot to this PNG file and exit.</param>
/// <param name="CaptureAfterFrames">Frames to run before capturing.</param>
internal sealed record PlayerOptions(
    string GameFolder,
    string? ScenePath = null,
    bool Fullscreen = false,
    string? CapturePath = null,
    int CaptureAfterFrames = 60)
{
    public const string Usage = """
        Usage: <game>.exe [options]

          --game <folder>        Folder of an exported game (default: the folder of this program)
          --scene <path>         Start with this scene (relative to Data, e.g. Assets/Scenes/Level2.lscene)
          --fullscreen           Start in fullscreen (Alt+Enter switches while running)
          --capture <file.png>   Run a few frames, save a screenshot and exit
          --frames <count>       Frames to run before capturing (default 60)
        """;

    /// <summary>The <c>Data</c> folder of the game.</summary>
    public string DataFolder => Path.Combine(GameFolder, GameManifest.DataFolderName);

    /// <summary>Parses arguments; returns null when the help text should be shown.</summary>
    public static PlayerOptions? Parse(string[] args)
    {
        // The executable may be a single-file bundle extracted to a temporary folder, so use the real path.
        var options = new PlayerOptions(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game":
                    options = options with { GameFolder = Path.GetFullPath(Value(args, ref i)) };
                    break;
                case "--scene":
                    options = options with { ScenePath = Value(args, ref i) };
                    break;
                case "--fullscreen":
                    options = options with { Fullscreen = true };
                    break;
                case "--capture":
                    options = options with { CapturePath = Value(args, ref i) };
                    break;
                case "--frames":
                    options = options with { CaptureAfterFrames = int.Parse(Value(args, ref i), CultureInfo.InvariantCulture) };
                    break;
                case "--help" or "-h":
                    return null;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        return options;
    }

    private static string Value(string[] args, ref int index) =>
        index + 1 < args.Length ? args[++index] : throw new ArgumentException($"Option '{args[index]}' needs a value.");
}
