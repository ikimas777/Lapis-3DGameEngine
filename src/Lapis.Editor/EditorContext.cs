using Lapis.Serialization;

namespace Lapis.Editor;

/// <summary>
/// State shared by all editor panels: the open scene, where it is saved, and the selection.
/// </summary>
internal sealed class EditorContext
{
    private Entity? _selection;

    public EditorContext(Scene scene, string? scenePath = null)
    {
        Scene = scene;
        ScenePath = scenePath;
    }

    /// <summary>The scene being edited.</summary>
    public Scene Scene { get; private set; }

    /// <summary>File the scene was opened from or last saved to; null for a new scene.</summary>
    public string? ScenePath { get; private set; }

    /// <summary>True when the scene has changes that are not saved.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>The selected entity, or null. Destroyed entities are never returned.</summary>
    public Entity? Selection
    {
        get => _selection is { IsDestroyed: false } ? _selection : null;
        set => _selection = value;
    }

    /// <summary>Raised when another scene is opened or created.</summary>
    public event Action<Scene>? SceneChanged;

    /// <summary>Records that the scene was modified.</summary>
    public void MarkDirty() => IsDirty = true;

    public void NewScene()
    {
        var scene = new Scene { Name = "Untitled" };
        var camera = EntityFactory.CreateCamera(scene);
        camera.Transform.Position = new System.Numerics.Vector3(0f, 2f, 6f);
        camera.Transform.LookAt(System.Numerics.Vector3.Zero);
        EntityFactory.CreateLight(scene, LightType.Directional);
        SetScene(scene, null);
    }

    /// <summary>Opens a scene file. Errors are logged and leave the current scene open.</summary>
    public bool OpenScene(string path)
    {
        try
        {
            var scene = SceneSerializer.Load(path);
            SetScene(scene, Path.GetFullPath(path));
            Log.Info($"Opened scene '{scene.Name}' ({scene.EntityCount} entities) from {ScenePath}");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SceneFormatException)
        {
            Log.Error($"Could not open '{path}': {exception.Message}");
            return false;
        }
    }

    /// <summary>Saves the scene to <paramref name="path"/>, or to <see cref="ScenePath"/> when null.</summary>
    public bool SaveScene(string? path = null)
    {
        path ??= ScenePath;
        if (path is null)
        {
            Log.Warning("The scene has no file yet; use File > Save As.");
            return false;
        }

        try
        {
            if (!path.EndsWith(SceneSerializer.FileExtension, StringComparison.OrdinalIgnoreCase))
            {
                path += SceneSerializer.FileExtension;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            if (ScenePath is null || !string.Equals(Path.GetFullPath(path), ScenePath, StringComparison.OrdinalIgnoreCase))
            {
                Scene.Name = Path.GetFileNameWithoutExtension(path);
            }

            SceneSerializer.Save(Scene, path);
            ScenePath = Path.GetFullPath(path);
            IsDirty = false;
            Log.Info($"Saved scene '{Scene.Name}' to {ScenePath}");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not save '{path}': {exception.Message}");
            return false;
        }
    }

    private void SetScene(Scene scene, string? path)
    {
        Scene = scene;
        ScenePath = path;
        IsDirty = false;
        _selection = null;
        SceneChanged?.Invoke(scene);
    }
}
