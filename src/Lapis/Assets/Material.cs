using System.Text.Json;
using System.Text.Json.Nodes;
using Lapis.Serialization;

namespace Lapis;

/// <summary>
/// Surface settings shared by many objects, stored as a <c>.lmat</c> file in the project. A
/// <see cref="MeshRenderer"/> with a material uses it instead of its own colour and texture.
/// </summary>
public sealed class Material
{
    /// <summary>Value of the <c>format</c> field.</summary>
    public const string FormatName = "lapis.material";

    /// <summary>File extension, including the dot.</summary>
    public const string FileExtension = ".lmat";

    /// <summary>Surface colour (sRGB); multiplied with the texture.</summary>
    public Color Color { get; set; } = Color.White;

    /// <summary>Texture asset reference, or empty for none.</summary>
    [Assets.AssetReference(Assets.AssetType.Texture)]
    public string TextureId { get; set; } = string.Empty;

    /// <summary>0 = non-metal (plastic, wood, stone), 1 = metal.</summary>
    [Range(0, 1)]
    public float Metallic { get; set; }

    /// <summary>0 = mirror-smooth with sharp highlights, 1 = completely matte.</summary>
    [Range(0, 1)]
    public float Roughness { get; set; } = 0.5f;

    /// <summary>Reads a material file.</summary>
    /// <exception cref="InvalidDataException">The file is not a material.</exception>
    public static Material Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        if ((string?)root?["format"] != FormatName)
        {
            throw new InvalidDataException("Not a Lapis material file.");
        }

        var material = new Material();
        if (root!["color"] is { } color)
        {
            material.Color = color.Deserialize<Color>(SceneSerializer.JsonOptions);
        }

        material.TextureId = (string?)root["texture"] ?? string.Empty;
        material.Metallic = (float?)root["metallic"] ?? 0f;
        material.Roughness = (float?)root["roughness"] ?? 0.5f;
        return material;
    }

    /// <summary>Writes the material to a file.</summary>
    public void Save(string path)
    {
        var root = new JsonObject
        {
            ["format"] = FormatName,
            ["version"] = 1,
            ["color"] = JsonSerializer.SerializeToNode(Color, SceneSerializer.JsonOptions),
            ["texture"] = TextureId,
            ["metallic"] = Metallic,
            ["roughness"] = Roughness,
        };

        File.WriteAllText(path, root.ToJsonString(SceneSerializer.JsonOptions) + Environment.NewLine);
    }
}
