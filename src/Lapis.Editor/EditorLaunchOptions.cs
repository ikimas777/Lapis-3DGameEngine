using System.Globalization;

namespace Lapis.Editor;

/// <summary>Command-line options for the editor.</summary>
/// <param name="ScenePath">Scene file to open; the demo scene is used when null.</param>
/// <param name="SaveScenePath">When set, save the opened scene to this file at startup.</param>
/// <param name="GameView">Render through the scene's main camera instead of the editor camera.</param>
/// <param name="CapturePath">When set, save a screenshot to this PNG file and exit.</param>
/// <param name="CaptureAfterFrames">Number of frames to render before capturing.</param>
internal sealed record EditorLaunchOptions(
    string? ScenePath = null,
    string? SaveScenePath = null,
    bool GameView = false,
    string? CapturePath = null,
    int CaptureAfterFrames = 30)
{
    public const string Usage = """
        Usage: Lapis.Editor [options]

          --scene <file.lscene>       Open a scene file (default: built-in demo scene)
          --save-scene <file.lscene>  Save the opened scene to a file at startup
          --game-view                 Look through the scene's main camera
          --capture <file.png>        Render, save a screenshot and exit
          --frames <count>            Frames to render before capturing (default 30)
          --help                      Show this help
        """;

    /// <summary>Parses arguments; returns null when the help text should be shown.</summary>
    /// <exception cref="ArgumentException">An option is unknown or missing its value.</exception>
    public static EditorLaunchOptions? Parse(string[] args)
    {
        var options = new EditorLaunchOptions();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scene":
                    options = options with { ScenePath = ReadValue(args, ref i) };
                    break;
                case "--save-scene":
                    options = options with { SaveScenePath = ReadValue(args, ref i) };
                    break;
                case "--game-view":
                    options = options with { GameView = true };
                    break;
                case "--capture":
                    options = options with { CapturePath = ReadValue(args, ref i) };
                    break;
                case "--frames":
                    options = options with { CaptureAfterFrames = int.Parse(ReadValue(args, ref i), CultureInfo.InvariantCulture) };
                    break;
                case "--help" or "-h":
                    return null;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        return options;
    }

    private static string ReadValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Option '{args[index]}' needs a value.");
        }

        return args[++index];
    }
}
