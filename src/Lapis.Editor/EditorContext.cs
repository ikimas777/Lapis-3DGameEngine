using Lapis.Editor.Commands;
using Lapis.Serialization;

namespace Lapis.Editor;

/// <summary>
/// State shared by all editor panels: the open scene, where it is saved, and the selection.
/// </summary>
internal sealed class EditorContext
{
    private Entity? _selection;
    private Guid? _selectionId;
    private long _savedStateId;

    public EditorContext(Scene scene, string? scenePath = null)
    {
        Scene = scene;
        ScenePath = scenePath;
    }

    /// <summary>The scene being edited.</summary>
    public Scene Scene { get; private set; }

    /// <summary>File the scene was opened from or last saved to; null for a new scene.</summary>
    public string? ScenePath { get; private set; }

    /// <summary>Undo / redo history of the open scene.</summary>
    public UndoStack History { get; private set; } = new();

    /// <summary>How many steps the undo history keeps (also for the history used while playing).</summary>
    public int UndoLimit
    {
        get => History.MaxDepth;
        set
        {
            History.MaxDepth = value;
            if (_editState is { } edit)
            {
                edit.History.MaxDepth = value;
            }
        }
    }

    /// <summary>True when the scene has changes that are not saved.</summary>
    public bool IsDirty => _editState is { } edit ? edit.WasDirty : History.StateId != _savedStateId;

    /// <summary>True while the game is running on a copy of the scene (see <see cref="EnterPlay"/>).</summary>
    public bool IsPlaying => _editState is not null;

    /// <summary>
    /// The selected entity, or null. When the selected entity is deleted and later restored by undo,
    /// the restored entity becomes selected again.
    /// </summary>
    public Entity? Selection
    {
        get
        {
            if (_selection is not { IsDestroyed: false } && _selectionId is { } id && Scene.FindById(id) is { } restored)
            {
                _selection = restored;
            }

            return _selection is { IsDestroyed: false } ? _selection : null;
        }
        set
        {
            _selection = value;
            _selectionId = value?.Id;
            if (value is not null)
            {
                SelectedAsset = null;
            }
        }
    }

    /// <summary>
    /// The asset selected in the Assets panel, shown in the Inspector instead of an entity. Selecting an
    /// entity clears it, and selecting an asset clears the entity selection.
    /// </summary>
    public Lapis.Assets.AssetInfo? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            _selectedAsset = value;
            if (value is not null)
            {
                _selection = null;
                _selectionId = null;
            }
        }
    }

    private Lapis.Assets.AssetInfo? _selectedAsset;

    /// <summary>The open project's assets, or null when no project is open.</summary>
#pragma warning disable CA1822 // an instance property keeps call sites short (context.Assets)
    public Lapis.Assets.AssetDatabase? Assets => Lapis.Assets.AssetDatabase.Active;
#pragma warning restore CA1822

    /// <summary>Raised when another scene is opened or created.</summary>
    public event Action<Scene>? SceneChanged;

    /// <summary>Applies a command and adds it to the undo history.</summary>
    public void Execute(IEditorCommand command) => History.Execute(command, Scene);

    /// <summary>Adds an already-applied change to the undo history.</summary>
    public void Record(IEditorCommand command) => History.Record(command);

    public void Undo() => Step("Undo", History.UndoName, () => History.Undo(Scene));

    public void Redo() => Step("Redo", History.RedoName, () => History.Redo(Scene));

    private void Step(string verb, string? name, Func<bool> step)
    {
        if (name is null)
        {
            return;
        }

        try
        {
            if (step())
            {
                Log.Info($"{verb}: {name}");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or
            System.Text.Json.JsonException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            // Something the history refers to changed outside it (a script member was removed, a file was moved).
            History.Clear();
            Log.Error($"{verb} of '{name}' failed: {exception.Message} The undo history was cleared.");
        }

        // An undone asset change may have moved or removed the selected asset.
        if (SelectedAsset is { } asset)
        {
            SelectedAsset = Assets?.Find(asset.Id);
        }
    }

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
        if (IsPlaying)
        {
            Log.Warning("Stop Play mode before saving; changes made while playing are discarded when it stops.");
            return false;
        }

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
            _savedStateId = History.StateId;
            Log.Info($"Saved scene '{Scene.Name}' to {ScenePath}");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not save '{path}': {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Swaps in a reloaded copy of the current scene (after scripts were recompiled). The file path,
    /// unsaved-changes state, selection and undo history are kept: commands find entities by id and keep
    /// values as JSON, so they work on the reloaded copy and with the new script types.
    /// </summary>
    public void ReplaceScene(Scene scene)
    {
        Scene = scene;
        History.BeginGesture();
        _selection = _selectionId is { } id ? scene.FindById(id) : null;
    }

    /// <summary>
    /// Switches to <paramref name="playScene"/> (a copy of the edited scene) with its own undo history.
    /// The edited scene, its history and its unsaved-changes state are kept for <see cref="ExitPlay"/>.
    /// </summary>
    public void EnterPlay(Scene playScene)
    {
        if (IsPlaying)
        {
            throw new InvalidOperationException("Play mode is already active.");
        }

        _editState = new EditState(Scene, History, IsDirty);
        Scene = playScene;
        History = new UndoStack { MaxDepth = History.MaxDepth };
        _selection = _selectionId is { } id ? playScene.FindById(id) : null;
    }

    /// <summary>Returns to the edited scene, discarding the play copy and everything done to it.</summary>
    public void ExitPlay()
    {
        if (_editState is not { } edit)
        {
            return;
        }

        _editState = null;
        Scene = edit.Scene;
        History = edit.History;
        _selection = _selectionId is { } id ? edit.Scene.FindById(id) : null;
    }

    private EditState? _editState;

    private sealed record EditState(Scene Scene, UndoStack History, bool WasDirty);

    private void SetScene(Scene scene, string? path)
    {
        Scene = scene;
        ScenePath = path;
        History.Clear();
        _savedStateId = History.StateId;
        Selection = null;
        SceneChanged?.Invoke(scene);
    }
}
