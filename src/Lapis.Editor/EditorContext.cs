using Lapis.Editor.Commands;
using Lapis.Serialization;

namespace Lapis.Editor;

/// <summary>
/// State shared by all editor panels: the open scene, where it is saved, and the selection.
/// </summary>
internal sealed class EditorContext
{
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

    /// <summary>The keyboard shortcuts of the editor's commands (Options > Keybindings).</summary>
    public Keybindings Keys { get; } = new();

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
    /// True while the Scene view shows handles for resizing the selected entity's collider instead of the
    /// transform gizmo (the Collider tool, key 4, or Edit Collider in the Inspector).
    /// </summary>
    public bool EditingCollider { get; set; }

    /// <summary>
    /// Size of the Game view's picture in pixels, as last drawn; the Scene view lays out UI for this screen size.
    /// </summary>
    public System.Numerics.Vector2 GameViewSize { get; set; } = new(1920f, 1080f);

    /// <summary>Whether the Scene view draws the baked navigation mesh (the Navigation tab's checkbox).</summary>
    public bool ShowNavMesh { get; set; } = true;

    /// <summary>
    /// The entity shown in the Inspector and moved by the gizmo: the one selected last, or null. Setting it selects
    /// only that entity. Entities are remembered by id, so one deleted and restored by undo (or reloaded with the
    /// scripts) is selected again.
    /// </summary>
    public Entity? Selection
    {
        get
        {
            for (var i = _selectedIds.Count - 1; i >= 0; i--)
            {
                if (Scene.FindById(_selectedIds[i]) is { } entity)
                {
                    return entity;
                }
            }

            return null;
        }
        set
        {
            _selectedIds.Clear();
            if (value is not null)
            {
                _selectedIds.Add(value.Id);
                ClearAssetSelection();
            }
        }
    }

    /// <summary>Every selected entity that still exists, in the order they were selected (the last is <see cref="Selection"/>).</summary>
    public IReadOnlyList<Entity> SelectedEntities
    {
        get
        {
            var entities = new List<Entity>(_selectedIds.Count);
            foreach (var id in _selectedIds)
            {
                if (Scene.FindById(id) is { } entity)
                {
                    entities.Add(entity);
                }
            }

            return entities;
        }
    }

    /// <summary>
    /// The selected entities without those inside another selected entity: what to move, delete or duplicate, so a
    /// child is not handled twice.
    /// </summary>
    public IReadOnlyList<Entity> SelectedRoots
    {
        get
        {
            var selected = SelectedEntities;
            return [.. selected.Where(entity => !selected.Any(other => other != entity && entity.Transform.IsChildOf(other.Transform)))];
        }
    }

    public bool IsSelected(Entity entity) => _selectedIds.Contains(entity.Id);

    /// <summary>Adds the entity to the selection (or removes it when it is already selected), as with Ctrl+click.</summary>
    public void ToggleSelected(Entity entity)
    {
        if (!_selectedIds.Remove(entity.Id))
        {
            _selectedIds.Add(entity.Id);
        }

        ClearAssetSelection();
    }

    /// <summary>Adds the entity to the selection and makes it the <see cref="Selection"/>.</summary>
    public void AddToSelection(Entity entity)
    {
        _selectedIds.Remove(entity.Id);
        _selectedIds.Add(entity.Id);
        ClearAssetSelection();
    }

    /// <summary>Selects exactly these entities; the last one becomes the <see cref="Selection"/>.</summary>
    public void SetSelection(IEnumerable<Entity> entities)
    {
        _selectedIds.Clear();
        foreach (var entity in entities)
        {
            _selectedIds.Remove(entity.Id);
            _selectedIds.Add(entity.Id);
        }

        if (_selectedIds.Count > 0)
        {
            ClearAssetSelection();
        }
    }

    /// <summary>
    /// The asset shown in the Inspector instead of an entity: the one selected last in the Assets panel, or null.
    /// Setting it selects only that asset (and clears the entity selection).
    /// </summary>
    public Lapis.Assets.AssetInfo? SelectedAsset
    {
        get => _selectedAssets.Count > 0 ? _selectedAssets[^1] : null;
        set
        {
            _selectedAssets.Clear();
            if (value is not null)
            {
                _selectedAssets.Add(value);
                _selectedIds.Clear();
            }
        }
    }

    /// <summary>Every selected asset, in the order they were selected (the last is <see cref="SelectedAsset"/>).</summary>
    public IReadOnlyList<Lapis.Assets.AssetInfo> SelectedAssets => _selectedAssets;

    public bool IsAssetSelected(Lapis.Assets.AssetInfo asset) => _selectedAssets.Exists(selected => selected.Id == asset.Id);

    /// <summary>Selects exactly these assets; the last one becomes the <see cref="SelectedAsset"/>.</summary>
    public void SetAssetSelection(IEnumerable<Lapis.Assets.AssetInfo> assets)
    {
        _selectedAssets.Clear();
        foreach (var asset in assets)
        {
            _selectedAssets.RemoveAll(selected => selected.Id == asset.Id);
            _selectedAssets.Add(asset);
        }

        if (_selectedAssets.Count > 0)
        {
            _selectedIds.Clear();
        }
    }

    /// <summary>Adds the asset to the selection, or removes it when it is already selected (Ctrl+click).</summary>
    public void ToggleAssetSelected(Lapis.Assets.AssetInfo asset)
    {
        if (_selectedAssets.RemoveAll(selected => selected.Id == asset.Id) == 0)
        {
            _selectedAssets.Add(asset);
            _selectedIds.Clear();
        }
    }

    private readonly List<Guid> _selectedIds = [];
    private readonly List<Lapis.Assets.AssetInfo> _selectedAssets = [];

    private void ClearAssetSelection() => _selectedAssets.Clear();

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

        // An undone asset change may have moved or removed selected assets.
        if (_selectedAssets.Count > 0)
        {
            var assets = Assets;
            var current = _selectedAssets.Select(asset => assets?.Find(asset.Id)).OfType<Lapis.Assets.AssetInfo>().ToList();
            _selectedAssets.Clear();
            _selectedAssets.AddRange(current);
        }
    }

    public void NewScene()
    {
        if (EditingPrefab is not null)
        {
            Log.Warning("Go back to the scene before creating a new one.");
            return;
        }

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
        if (EditingPrefab is { } prefab)
        {
            Log.Warning($"Go back to the scene before opening another one (you are editing the prefab '{prefab.Name}').");
            return false;
        }

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
        if (EditingPrefab is not null)
        {
            return SavePrefab(); // Ctrl+S while a prefab is open saves the prefab
        }

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
    }

    private EditState? _editState;

    private sealed record EditState(Scene Scene, UndoStack History, bool WasDirty);

    // ---------------------------------------------------------------- prefab editing (Open Prefab)

    private PrefabState? _prefabState;

    private sealed record PrefabState(Lapis.Assets.AssetInfo Asset, Scene Scene, string? ScenePath, UndoStack History, long SavedStateId, List<Guid> Selection);

    /// <summary>The prefab open for editing instead of the scene, or null while the scene is edited.</summary>
    public Lapis.Assets.AssetInfo? EditingPrefab => _prefabState?.Asset;

    /// <summary>
    /// Opens a prefab for editing: the Hierarchy and Scene view show only its entities, with their own undo history,
    /// until <see cref="ExitPrefab"/>. The scene, its history, unsaved changes and selection are kept meanwhile.
    /// </summary>
    /// <returns>False (with a message) when the prefab cannot be read, or while playing.</returns>
    public bool EnterPrefab(Lapis.Assets.AssetInfo asset)
    {
        if (IsPlaying)
        {
            Log.Warning("Stop Play mode before opening a prefab.");
            return false;
        }

        if (EditingPrefab is not null)
        {
            ExitPrefab(save: false);
        }

        Scene prefabScene;
        try
        {
            prefabScene = new Scene { Name = asset.Name };
            SceneSerializer.EntitiesFromJson(prefabScene, File.ReadAllText(asset.FullPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SceneFormatException)
        {
            Log.Error($"Could not open the prefab '{asset.Name}': {exception.Message}");
            return false;
        }

        _prefabState = new PrefabState(asset, Scene, ScenePath, History, _savedStateId, [.. _selectedIds]);
        Scene = prefabScene;
        ScenePath = null;
        History = new UndoStack { MaxDepth = History.MaxDepth };
        _savedStateId = History.StateId;
        Selection = prefabScene.RootEntities.FirstOrDefault();
        SceneChanged?.Invoke(prefabScene);
        Log.Info($"Editing the prefab '{asset.Name}'. Save (Ctrl+S) updates every copy; Back to Scene returns.");
        return true;
    }

    /// <summary>Writes the open prefab to its file. A prefab must have exactly one top-level entity.</summary>
    public bool SavePrefab()
    {
        if (_prefabState is not { } state)
        {
            return false;
        }

        var roots = Scene.RootEntities.ToList();
        if (roots.Count != 1)
        {
            Log.Warning($"A prefab has exactly one top-level entity; put the other {roots.Count - 1} under '{roots.FirstOrDefault()?.Name}' to save it.");
            return false;
        }

        try
        {
            File.WriteAllText(state.Asset.FullPath, SceneSerializer.PrefabToJson(roots[0], fromScene: false));
            Assets?.NotifyChanged(state.Asset.Id);
            _savedStateId = History.StateId;
            Log.Info($"Saved the prefab '{state.Asset.Name}'.");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not save the prefab '{state.Asset.Name}': {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns to the scene (optionally saving the prefab first). The scene's copies of the prefab are rebuilt, so they
    /// show what was saved.
    /// </summary>
    public void ExitPrefab(bool save)
    {
        if (_prefabState is not { } state || (save && IsDirty && !SavePrefab()))
        {
            return;
        }

        _prefabState = null;
        ScenePath = state.ScenePath;
        History = state.History;
        _savedStateId = state.SavedStateId;

        // Rebuilt through the scene format: instances are read from their (saved) prefab files.
        Scene = SceneSerializer.FromJson(SceneSerializer.ToJson(state.Scene));
        History.BeginGesture();
        _selectedIds.Clear();
        _selectedIds.AddRange(state.Selection);
        SceneChanged?.Invoke(Scene);
    }

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
