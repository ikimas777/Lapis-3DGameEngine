using System.Globalization;

namespace Lapis.Editor;

/// <summary>Command-line options for the editor.</summary>
/// <param name="CapturePath">When set, save a screenshot to this PNG file and exit.</param>
/// <param name="CaptureAfterFrames">Number of frames to render before capturing.</param>
internal sealed record EditorLaunchOptions(string? CapturePath = null, int CaptureAfterFrames = 30)
{
    public const string Usage = """
        Usage: Lapis.Editor [options]

          --capture <file.png>   Render, save a screenshot and exit
          --frames <count>       Frames to render before capturing (default 30)
          --help                 Show this help
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
