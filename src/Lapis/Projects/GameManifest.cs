using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lapis.Projects;

/// <summary>
/// Describes an exported game: its name and the scene it starts with. Stored as <c>Data/game.json</c>
/// next to the game's executable.
/// </summary>
/// <remarks>
/// Layout of an exported game:
/// <code>
/// MyGame/
///   MyGame.exe        the Lapis player, renamed (just "MyGame" on Linux)
///   Game.dll          the compiled scripts
///   Data/game.json    this manifest
///   Data/Assets/...   scenes, models, textures and materials (with their .lmeta files)
/// </code>
/// </remarks>
public sealed record GameManifest(string Name, string StartScene)
{
    /// <summary>File name of the manifest inside the data folder.</summary>
    public const string FileName = "game.json";

    /// <summary>Name of the data folder next to the executable.</summary>
    public const string DataFolderName = "Data";

    /// <summary>File name of the compiled scripts next to the executable.</summary>
    public const string ScriptsFileName = "Game.dll";

    /// <summary>Value of the <c>format</c> field.</summary>
    public const string FormatName = "lapis.game";

    /// <summary>Reads <c>game.json</c> from a data folder.</summary>
    /// <exception cref="InvalidDataException">The file is missing fields or is not a Lapis game manifest.</exception>
    public static GameManifest Load(string dataFolder)
    {
        var path = Path.Combine(dataFolder, FileName);
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"'{path}' is not valid JSON: {exception.Message}", exception);
        }

        if ((string?)root?["format"] != FormatName || (string?)root["startScene"] is not { Length: > 0 } startScene)
        {
            throw new InvalidDataException($"'{path}' is not a Lapis game manifest.");
        }

        return new GameManifest((string?)root["name"] ?? "Game", startScene);
    }

    /// <summary>Writes <c>game.json</c> into a data folder.</summary>
    public void Save(string dataFolder)
    {
        var root = new JsonObject
        {
            ["format"] = FormatName,
            ["version"] = 1,
            ["name"] = Name,
            ["startScene"] = StartScene,
            ["engineVersion"] = EngineInfo.Version.ToString(3),
        };

        Directory.CreateDirectory(dataFolder);
        File.WriteAllText(Path.Combine(dataFolder, FileName), root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }
}
