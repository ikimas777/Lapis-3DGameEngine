using System.Text.Json;
using System.Text.Json.Nodes;
using Lapis.Assets.Importers;

namespace Lapis.Assets;

/// <summary>Kinds of files in a project's <c>Assets</c> folder.</summary>
public enum AssetType
{
    /// <summary>A file Lapis does not use.</summary>
    Other,

    /// <summary>A <c>.lscene</c> scene.</summary>
    Scene,

    /// <summary>A <c>.cs</c> script.</summary>
    Script,

    /// <summary>A 3D model: <c>.gltf</c>, <c>.glb</c>, <c>.obj</c> or <c>.stl</c>.</summary>
    Model,

    /// <summary>An image: <c>.png</c>, <c>.jpg</c>, <c>.jpeg</c>, <c>.tga</c>, <c>.bmp</c>.</summary>
    Texture,

    /// <summary>A <c>.lmat</c> material.</summary>
    Material,
}

/// <summary>A file in the <c>Assets</c> folder and its stable id.</summary>
public sealed record AssetInfo(Guid Id, string FullPath, AssetType Type)
{
    /// <summary>File name without extension.</summary>
    public string Name => Path.GetFileNameWithoutExtension(FullPath);

    /// <summary>The string stored in scenes and components to refer to this asset.</summary>
    public string Reference => AssetDatabase.ReferenceOf(Id);
}

/// <summary>
/// Keeps track of the files in a project's <c>Assets</c> folder. Every file gets a <c>.lmeta</c> sidecar
/// holding a GUID, so scenes refer to assets by id and references survive renaming and moving
/// (as long as the <c>.lmeta</c> file moves with it).
/// </summary>
public sealed class AssetDatabase
{
    /// <summary>Extension of the sidecar files, including the dot.</summary>
    public const string MetaExtension = ".lmeta";

    /// <summary>Prefix of asset references, e.g. <c>asset:0f8f…</c>.</summary>
    public const string ReferencePrefix = "asset:";

    private readonly Dictionary<Guid, AssetInfo> _byId = [];
    private readonly Dictionary<string, AssetInfo> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, (DateTime Stamp, ImportedModel? Model)> _models = [];
    private readonly Dictionary<Guid, (DateTime Stamp, Material? Material)> _materials = [];

    /// <summary>Creates a database for <paramref name="assetsDirectory"/> and scans it.</summary>
    public AssetDatabase(string assetsDirectory)
    {
        RootDirectory = Path.GetFullPath(assetsDirectory);
        Directory.CreateDirectory(RootDirectory);
        Refresh();
    }

    /// <summary>The database used to resolve asset references while rendering and simulating, or null.</summary>
    public static AssetDatabase? Active { get; set; }

    /// <summary>
    /// Raised after files changed. The argument is the changed asset, or null when anything may have changed;
    /// caches of GPU meshes and textures listen to this.
    /// </summary>
    public static event Action<Guid?>? AssetsChanged;

    /// <summary>The <c>Assets</c> folder.</summary>
    public string RootDirectory { get; }

    /// <summary>All known assets.</summary>
    public IReadOnlyCollection<AssetInfo> Assets => _byId.Values;

    /// <summary>The reference string for an asset id.</summary>
    public static string ReferenceOf(Guid id) => ReferencePrefix + id.ToString("N");

    /// <summary>
    /// Splits a reference such as <c>asset:0f8f…</c> or <c>asset:0f8f…#1</c> into the asset id and the
    /// optional part after <c>#</c> (e.g. an image embedded in a model).
    /// </summary>
    public static bool TryParseReference(string? reference, out Guid id, out string? fragment)
    {
        id = Guid.Empty;
        fragment = null;
        if (reference is null || !reference.StartsWith(ReferencePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var body = reference.AsSpan(ReferencePrefix.Length);
        var hash = body.IndexOf('#');
        if (hash >= 0)
        {
            fragment = body[(hash + 1)..].ToString();
            body = body[..hash];
        }

        return Guid.TryParse(body, out id);
    }

    /// <summary>The type of asset a file name stands for.</summary>
    public static AssetType TypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".lscene" => AssetType.Scene,
        ".cs" => AssetType.Script,
        ".gltf" or ".glb" or ".obj" or ".stl" => AssetType.Model,
        ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp" => AssetType.Texture,
        ".lmat" => AssetType.Material,
        _ => AssetType.Other,
    };

    /// <summary>The asset with this id, or null.</summary>
    public AssetInfo? Find(Guid id) => _byId.GetValueOrDefault(id);

    /// <summary>The asset a reference string points to, or null.</summary>
    public AssetInfo? Find(string? reference) => TryParseReference(reference, out var id, out _) ? Find(id) : null;

    /// <summary>The asset stored at a path, or null.</summary>
    public AssetInfo? FindByPath(string fullPath) => _byPath.GetValueOrDefault(Path.GetFullPath(fullPath));

    /// <summary>
    /// Rescans the folder: creates <c>.lmeta</c> files for new files, removes those whose file is gone,
    /// and drops cached imports of files that changed.
    /// </summary>
    public void Refresh()
    {
        _byId.Clear();
        _byPath.Clear();

        foreach (var file in Directory.EnumerateFiles(RootDirectory, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(MetaExtension, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(file[..^MetaExtension.Length]) && !Directory.Exists(file[..^MetaExtension.Length]))
                {
                    TryDelete(file); // its asset was deleted or moved without it
                }

                continue;
            }

            var id = ReadOrCreateMeta(file);
            if (_byId.ContainsKey(id))
            {
                // A copied file kept its .lmeta: give the copy its own id.
                id = Guid.NewGuid();
                WriteMeta(file, id);
            }

            var info = new AssetInfo(id, Path.GetFullPath(file), TypeOf(file));
            _byId[id] = info;
            _byPath[info.FullPath] = info;
        }

        AssetsChanged?.Invoke(null);
    }

    /// <summary>Moves or renames an asset file together with its <c>.lmeta</c>, keeping its id.</summary>
    public AssetInfo Move(AssetInfo asset, string newFullPath)
    {
        ArgumentNullException.ThrowIfNull(asset);
        newFullPath = Path.GetFullPath(newFullPath);
        if (File.Exists(newFullPath))
        {
            throw new IOException($"'{Path.GetFileName(newFullPath)}' already exists.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(newFullPath)!);
        File.Move(asset.FullPath, newFullPath);
        var meta = asset.FullPath + MetaExtension;
        if (File.Exists(meta))
        {
            File.Move(meta, newFullPath + MetaExtension, overwrite: true);
        }

        Refresh();
        return Find(asset.Id)!;
    }

    /// <summary>Loads (and caches) a model asset; null when the asset is missing or cannot be read.</summary>
    public ImportedModel? LoadModel(Guid id)
    {
        if (Find(id) is not { Type: AssetType.Model } asset || !File.Exists(asset.FullPath))
        {
            return null;
        }

        var stamp = File.GetLastWriteTimeUtc(asset.FullPath);
        if (_models.TryGetValue(id, out var cached) && cached.Stamp == stamp)
        {
            return cached.Model;
        }

        ImportedModel? model = null;
        try
        {
            model = ModelImporter.Import(asset.FullPath, this);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            Log.Error($"Could not import '{Path.GetFileName(asset.FullPath)}': {exception.Message}", asset.FullPath, 0);
        }

        _models[id] = (stamp, model);
        return model;
    }

    /// <summary>Loads (and caches) a material asset; null when missing or invalid.</summary>
    public Material? LoadMaterial(Guid id)
    {
        if (Find(id) is not { Type: AssetType.Material } asset || !File.Exists(asset.FullPath))
        {
            return null;
        }

        var stamp = File.GetLastWriteTimeUtc(asset.FullPath);
        if (_materials.TryGetValue(id, out var cached) && cached.Stamp == stamp)
        {
            return cached.Material;
        }

        Material? material = null;
        try
        {
            material = Material.Load(asset.FullPath);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            Log.Error($"Could not read material '{Path.GetFileName(asset.FullPath)}': {exception.Message}", asset.FullPath, 0);
        }

        _materials[id] = (stamp, material);
        return material;
    }

    /// <summary>
    /// The encoded image bytes for a texture reference: an image file, or an image embedded in a model
    /// (<c>asset:model#index</c>). Null when not found.
    /// </summary>
    public byte[]? LoadImageBytes(string reference)
    {
        if (!TryParseReference(reference, out var id, out var fragment) || Find(id) is not { } asset)
        {
            return null;
        }

        if (fragment is null)
        {
            return asset.Type == AssetType.Texture && File.Exists(asset.FullPath) ? File.ReadAllBytes(asset.FullPath) : null;
        }

        return int.TryParse(fragment, out var index) && LoadModel(id) is { } model && index >= 0 && index < model.EmbeddedImages.Count
            ? model.EmbeddedImages[index]
            : null;
    }

    /// <summary>Tells caches that an asset's content changed (e.g. a material was edited in the Inspector).</summary>
    public void NotifyChanged(Guid id)
    {
        _models.Remove(id);
        _materials.Remove(id);
        AssetsChanged?.Invoke(id);
    }

    private static Guid ReadOrCreateMeta(string file)
    {
        var meta = file + MetaExtension;
        if (File.Exists(meta))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(meta)) is JsonObject root && Guid.TryParse((string?)root["guid"], out var existing))
                {
                    return existing;
                }
            }
            catch (JsonException)
            {
                // fall through and write a fresh one
            }
        }

        var id = Guid.NewGuid();
        WriteMeta(file, id);
        return id;
    }

    private static void WriteMeta(string file, Guid id)
    {
        var root = new JsonObject { ["guid"] = id.ToString() };
        File.WriteAllText(file + MetaExtension, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
