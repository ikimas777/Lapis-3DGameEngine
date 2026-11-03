using System.Numerics;
using Lapis.Assets;
using Lapis.Editor.Commands;
using Lapis.Serialization;

namespace Lapis.Editor;

/// <summary>
/// Prefabs in the editor: making one from the Hierarchy, placing copies (instances), unpacking them and the rules
/// that keep instances in sync with their prefab (only an instance root's name, tag, active flag, folder and
/// transform can be changed in the scene; everything else is edited in the prefab with Open Prefab).
/// </summary>
internal static class PrefabActions
{
    /// <summary>
    /// True when <paramref name="entity"/>'s components and children cannot be changed in the scene because they
    /// come from a prefab. For an instance root, its transform, name, tag and active flag can still be changed.
    /// </summary>
    public static bool IsLocked(Entity entity) => entity.IsPartOfPrefab;

    /// <summary>True for entities inside an instance (not its root): nothing about them can be changed in the scene.</summary>
    public static bool IsInsideInstance(Entity entity) => entity.Prefab is null && entity.IsPartOfPrefab;

    /// <summary>The message shown when something locked by a prefab is about to be changed.</summary>
    public static string LockedMessage(Entity entity) =>
        $"'{entity.Name}' is part of the prefab '{PrefabName(entity.PrefabInstanceRoot!)}'. Open the prefab to change every copy, or unpack this copy to change only it.";

    /// <summary>File name (without extension) of the prefab an instance root comes from.</summary>
    public static string PrefabName(Entity instanceRoot) =>
        AssetDatabase.Active?.Find(instanceRoot.Prefab) is { } asset ? asset.Name : "missing prefab";

    /// <summary>
    /// Saves <paramref name="entity"/> and its children as a new <c>.lprefab</c> in <paramref name="folder"/> and turns
    /// the entity into an instance of it (one undo step for the scene; the file stays). Returns the new asset.
    /// </summary>
    public static AssetInfo? CreatePrefab(EditorContext context, Entity entity, string folder)
    {
        if (context.Assets is not { } assets)
        {
            Log.Warning("Open a project to make prefabs.");
            return null;
        }

        if (IsInsideInstance(entity))
        {
            Log.Warning(LockedMessage(entity));
            return null;
        }

        var path = UniquePath(Path.Combine(folder, SafeFileName(entity.Name) + SceneSerializer.PrefabExtension));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, SceneSerializer.PrefabToJson(entity));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not save the prefab '{Path.GetFileName(path)}': {exception.Message}");
            return null;
        }

        assets.Refresh();
        if (assets.FindByPath(path) is not { } asset)
        {
            return null;
        }

        context.Execute(new SetPrefabLinkCommand(entity, asset.Reference, $"Make Prefab {asset.Name}"));
        Log.Info($"Made the prefab '{asset.Name}' ({Path.GetRelativePath(assets.RootDirectory, path)}); '{entity.Name}' is now a copy of it.");
        return asset;
    }

    /// <summary>Places a copy of a prefab under <paramref name="parent"/>, or at <paramref name="position"/> at the top level.</summary>
    public static Entity? Instantiate(EditorContext context, AssetInfo prefab, Entity? parent, Vector3 position)
    {
        if (parent is not null && IsLocked(parent))
        {
            Log.Warning(LockedMessage(parent));
            return null;
        }

        Entity copy;
        try
        {
            copy = SceneSerializer.InstantiatePrefab(context.Scene, prefab.Reference, parent);
        }
        catch (SceneFormatException exception)
        {
            Log.Error($"Could not place '{prefab.Name}': {exception.Message}");
            return null;
        }

        if (parent is null)
        {
            copy.Transform.Position = position;
        }
        else
        {
            copy.Transform.LocalPosition = Vector3.Zero;
        }

        context.Selection = copy;
        EditorActions.RecordCreated(context, copy);
        return copy;
    }

    /// <summary>Turns an instance into ordinary entities that can be changed freely; the prefab is no longer linked.</summary>
    public static void Unpack(EditorContext context, Entity entity)
    {
        if (entity.PrefabInstanceRoot is not { } root)
        {
            return;
        }

        context.Execute(new SetPrefabLinkCommand(root, null, $"Unpack {root.Name}"));
        Log.Info($"Unpacked '{root.Name}': it is no longer linked to its prefab.");
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string([.. name.Select(character => invalid.Contains(character) ? '_' : character)]).Trim();
        return safe.Length == 0 ? "Prefab" : safe;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(folder, $"{name} {i}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}
