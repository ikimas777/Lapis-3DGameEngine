using Lapis.Assets;
using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>
/// Creates GPU meshes on demand from mesh identifiers (built-in shapes or model assets) and keeps them for reuse.
/// </summary>
public sealed class MeshLibrary : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, Mesh?> _meshes = new(StringComparer.Ordinal);

    /// <summary>Creates an empty library.</summary>
    public MeshLibrary(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        AssetDatabase.AssetsChanged += OnAssetsChanged;
    }

    /// <summary>Returns the mesh for <paramref name="meshId"/>, or null when the id is unknown.</summary>
    public Mesh? Get(string meshId)
    {
        if (_meshes.TryGetValue(meshId, out var mesh))
        {
            return mesh;
        }

        var data = BuiltInMeshes.GetData(meshId);
        if (data is null && !string.IsNullOrEmpty(meshId))
        {
            Log.Warning($"Mesh '{AssetDatabase.Active?.Find(meshId)?.Name ?? meshId}' was not found.");
        }

        mesh = data is null ? null : new Mesh(_gl, data);
        _meshes[meshId] = mesh; // unknown ids are cached as null so the warning appears once
        return mesh;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        AssetDatabase.AssetsChanged -= OnAssetsChanged;
        foreach (var mesh in _meshes.Values)
        {
            mesh?.Dispose();
        }

        _meshes.Clear();
    }

    private void OnAssetsChanged(Guid? id)
    {
        // Built-in meshes never change; model meshes are reloaded on next use.
        var prefix = id is { } changed ? AssetDatabase.ReferenceOf(changed) : AssetDatabase.ReferencePrefix;
        foreach (var key in _meshes.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            if (id is null && _meshes[key] is not null)
            {
                continue; // a rescan alone does not invalidate a loaded model
            }

            _meshes[key]?.Dispose();
            _meshes.Remove(key);
        }
    }
}
