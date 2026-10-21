using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>
/// Creates GPU meshes on demand from mesh identifiers and keeps them for reuse.
/// </summary>
public sealed class MeshLibrary(GL gl) : IDisposable
{
    private readonly Dictionary<string, Mesh?> _meshes = new(StringComparer.Ordinal);

    /// <summary>Returns the mesh for <paramref name="meshId"/>, or null when the id is unknown.</summary>
    public Mesh? Get(string meshId)
    {
        if (_meshes.TryGetValue(meshId, out var mesh))
        {
            return mesh;
        }

        var data = meshId switch
        {
            BuiltInMeshes.Cube => Primitives.CreateCube(),
            BuiltInMeshes.Sphere => Primitives.CreateSphere(),
            BuiltInMeshes.Plane => Primitives.CreatePlane(1f),
            _ => null,
        };

        if (data is null)
        {
            Log.Warning($"Unknown mesh '{meshId}'.");
        }

        mesh = data is null ? null : new Mesh(gl, data);
        _meshes[meshId] = mesh; // unknown ids are cached as null so the warning appears once
        return mesh;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var mesh in _meshes.Values)
        {
            mesh?.Dispose();
        }

        _meshes.Clear();
    }
}
