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
    private bool _unrecordedChanges;

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

    /// <summary>True when the scene has changes that are not saved.</summary>
    public bool IsDirty => _editState is { } edit ? edit.WasDirty : _unrecordedChanges || History.StateId != _savedStateId;

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
        }
    }

    /// <summary>Raised when another scene is opened or created.</summary>
    public event Action<Scene>? SceneChanged;

    /// <summary>Records a change that is not in the undo history.</summary>
    public void MarkDirty() => _unrecordedChanges = true;

    /// <summary>Applies a command and adds it to the undo history.</summary>
    public void Execute(IEditorCommand command) => History.Execute(command, Scene);

    /// <summary>Adds an already-applied change to the undo history.</summary>
    public void Record(IEditorCommand command) => History.Record(command);

    public void Undo()
    {
        if (History.UndoName is { } name && History.Undo(Scene))
        {
            Log.Info($"Undo: {name}");
        }
    }

    public void Redo()
    {
        if (History.RedoName is { } name && History.Redo(Scene))
        {
            Log.Info($"Redo: {name}");
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
            _unrecordedChanges = false;
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
    /// unsaved-changes state and selection are kept; the undo history is cleared because it may refer
    /// to the old script types.
    /// </summary>
    public void ReplaceScene(Scene scene)
    {
        var wasDirty = IsDirty;
        var selectedId = _selectionId;

        Scene = scene;
        History.Clear();
        _savedStateId = History.StateId;
        _unrecordedChanges = wasDirty;
        _selection = selectedId is { } id ? scene.FindById(id) : null;
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
        History = new UndoStack();
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
        _unrecordedChanges = false;
        Selection = null;
        SceneChanged?.Invoke(scene);
    }
}
