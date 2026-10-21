namespace Lapis;

/// <summary>Identifiers of the meshes built into the engine, for <see cref="MeshRenderer.MeshId"/>.</summary>
public static class BuiltInMeshes
{
    /// <summary>1 × 1 × 1 cube.</summary>
    public const string Cube = "builtin:cube";

    /// <summary>Sphere with a diameter of 1.</summary>
    public const string Sphere = "builtin:sphere";

    /// <summary>1 × 1 square in the XZ plane, facing up.</summary>
    public const string Plane = "builtin:plane";

    /// <summary>All built-in mesh identifiers.</summary>
    public static IReadOnlyList<string> All { get; } = [Cube, Sphere, Plane];

    private static readonly Dictionary<string, Rendering.MeshData> DataCache = new(StringComparer.Ordinal);

    /// <summary>
    /// CPU-side geometry for a mesh id: a built-in shape or a model asset (shared, do not modify).
    /// Null for unknown ids.
    /// </summary>
    internal static Rendering.MeshData? GetData(string meshId)
    {
        if (meshId.StartsWith(Assets.AssetDatabase.ReferencePrefix, StringComparison.Ordinal))
        {
            return Assets.AssetDatabase.TryParseReference(meshId, out var id, out _)
                ? Assets.AssetDatabase.Active?.LoadModel(id)?.Mesh
                : null;
        }

        lock (DataCache)
        {
            if (DataCache.TryGetValue(meshId, out var cached))
            {
                return cached;
            }

            Rendering.MeshData? data = meshId switch
            {
                Cube => Rendering.Primitives.CreateCube(),
                Sphere => Rendering.Primitives.CreateSphere(),
                Plane => Rendering.Primitives.CreatePlane(1f),
                _ => null,
            };

            if (data is not null)
            {
                DataCache[meshId] = data;
            }

            return data;
        }
    }
}

/// <summary>Draws a mesh at the entity's transform.</summary>
public sealed class MeshRenderer : Component
{
    /// <summary>Which mesh to draw: one of <see cref="BuiltInMeshes"/> or a model asset reference.</summary>
    [Assets.AssetReference(Assets.AssetType.Model)]
    public string MeshId { get; set; } = BuiltInMeshes.Cube;

    /// <summary>
    /// A material asset to use instead of <see cref="Color"/>, <see cref="TextureId"/>, <see cref="Metallic"/> and <see cref="Roughness"/>.
    /// Empty to use those settings.
    /// </summary>
    [Assets.AssetReference(Assets.AssetType.Material)]
    public string MaterialId { get; set; } = string.Empty;

    /// <summary>Surface colour (sRGB), multiplied with the texture.</summary>
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

    /// <summary>Whether this object casts shadows from the sun.</summary>
    public bool CastShadows { get; set; } = true;

    /// <summary>The surface to draw with: the material when set and found, otherwise this renderer's own settings.</summary>
    internal (Color Color, string TextureId, float Metallic, float Roughness) ResolveSurface()
    {
        if (MaterialId.Length > 0 && Assets.AssetDatabase.TryParseReference(MaterialId, out var id, out _) &&
            Assets.AssetDatabase.Active?.LoadMaterial(id) is { } material)
        {
            return (material.Color, material.TextureId, material.Metallic, material.Roughness);
        }

        return (Color, TextureId, Metallic, Roughness);
    }
}
