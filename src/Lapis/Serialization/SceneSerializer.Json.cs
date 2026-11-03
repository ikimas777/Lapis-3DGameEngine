using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Lapis.Serialization;
/// <summary>Scene format: JSON documents, value conversion and formatting.</summary>
public static partial class SceneSerializer
{
    private static JsonObject ParseDocument(string json, string expectedFormat)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw new SceneFormatException("A scene file must contain a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new SceneFormatException($"The scene file is not valid JSON: {exception.Message}", exception);
        }

        if ((string?)root["format"] != expectedFormat)
        {
            throw new SceneFormatException($"Not a Lapis {(expectedFormat == FormatName ? "scene" : "entity fragment")} (expected format '{expectedFormat}').");
        }

        var version = (int?)root["version"] ?? 0;
        if (version is < 1 or > FormatVersion)
        {
            throw new SceneFormatException($"Unsupported scene format version {version}; this build reads up to {FormatVersion}.");
        }

        return root;
    }

    private static T ReadValue<T>(JsonNode? node, T fallback)
    {
        if (node is null)
        {
            return fallback;
        }

        try
        {
            return node.Deserialize<T>(JsonOptions) ?? fallback;
        }
        catch (JsonException exception)
        {
            throw new SceneFormatException($"Invalid {typeof(T).Name} value: {exception.Message}", exception);
        }
    }

    // Keep vectors, rotations and colours on one line so scene files stay short and diff well.
    private static string Format(JsonObject root) =>
        NumberArray().Replace(root.ToJsonString(JsonOptions), match =>
            "[" + string.Join(", ", match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries)) + "]");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        options.Converters.Add(new Vector2JsonConverter());
        options.Converters.Add(new Vector3JsonConverter());
        options.Converters.Add(new Vector4JsonConverter());
        options.Converters.Add(new QuaternionJsonConverter());
        options.Converters.Add(new ColorJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    [GeneratedRegex(@"\[\s*(-?[0-9][0-9.eE+-]*(?:\s*,\s*-?[0-9][0-9.eE+-]*)*)\s*\]")]
    private static partial Regex NumberArray();
}
