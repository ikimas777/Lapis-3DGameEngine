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
}

/// <summary>Draws a mesh at the entity's transform.</summary>
public sealed class MeshRenderer : Component
{
    /// <summary>
    /// Which mesh to draw. One of <see cref="BuiltInMeshes"/> for now; imported assets are referenced
    /// the same way once the asset pipeline exists.
    /// </summary>
    public string MeshId { get; set; } = BuiltInMeshes.Cube;

    /// <summary>Surface colour (sRGB).</summary>
    public Color Color { get; set; } = Color.White;

    /// <summary>Size of specular highlights; higher is smaller and sharper.</summary>
    public float Shininess { get; set; } = 32f;
}
