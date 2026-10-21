using Lapis.Assets;
using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>
/// Loads textures for asset references on demand and keeps them on the GPU. Entries are dropped when
/// the asset changes on disk.
/// </summary>
public sealed class TextureLibrary : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.Ordinal);

    /// <summary>Creates the library and its plain white fallback texture.</summary>
    public TextureLibrary(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        White = Texture2D.FromPixels(gl, [255, 255, 255, 255], 1, 1);
        AssetDatabase.AssetsChanged += OnAssetsChanged;
    }

    /// <summary>A 1 × 1 white texture, used when an object has no texture.</summary>
    public Texture2D White { get; }

    /// <summary>The texture for a reference, or null when it is empty, missing or not an image.</summary>
    public Texture2D? Get(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        if (_textures.TryGetValue(reference, out var cached))
        {
            return cached;
        }

        Texture2D? texture = null;
        if (AssetDatabase.Active?.LoadImageBytes(reference) is { } bytes)
        {
            try
            {
                texture = Texture2D.FromImage(_gl, bytes);
            }
            catch (InvalidDataException exception)
            {
                Log.Warning($"Texture {AssetDatabase.Active.Find(reference)?.Name ?? reference}: {exception.Message}");
            }
        }

        _textures[reference] = texture; // also remember failures, so they are not retried every frame
        return texture;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        AssetDatabase.AssetsChanged -= OnAssetsChanged;
        Clear();
        White.Dispose();
    }

    private void OnAssetsChanged(Guid? id)
    {
        if (id is null)
        {
            // A rescan can mean any file changed; drop failed lookups and keep loaded images unless their file changed.
            foreach (var key in _textures.Where(entry => entry.Value is null).Select(entry => entry.Key).ToList())
            {
                _textures.Remove(key);
            }

            return;
        }

        var prefix = AssetDatabase.ReferenceOf(id.Value);
        foreach (var key in _textures.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _textures[key]?.Dispose();
            _textures.Remove(key);
        }
    }

    private void Clear()
    {
        foreach (var texture in _textures.Values)
        {
            texture?.Dispose();
        }

        _textures.Clear();
    }
}
