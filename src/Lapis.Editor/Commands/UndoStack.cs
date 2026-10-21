namespace Lapis.Editor.Commands;

/// <summary>
/// A reversible change to the scene. Commands refer to entities by id rather than by object, so
/// they keep working after an entity has been deleted and restored by another command.
/// </summary>
internal interface IEditorCommand
{
    /// <summary>Short description shown in the Edit menu, e.g. "Move Box".</summary>
    string Name { get; }

    /// <summary>Applies (or re-applies) the change.</summary>
    void Execute(Scene scene);

    /// <summary>Reverts the change.</summary>
    void Undo(Scene scene);

    /// <summary>
    /// Absorbs <paramref name="next"/> into this command when both change the same thing, so a drag
    /// becomes a single undo step. Returns false when the commands cannot be merged.
    /// </summary>
    bool TryMerge(IEditorCommand next) => false;
}

/// <summary>
/// Undo / redo history. Consecutive commands recorded during the same gesture (one drag, one
/// text edit) are merged when they target the same value.
/// </summary>
internal sealed class UndoStack
{
    /// <summary>Oldest steps are discarded beyond this many.</summary>
    public const int MaxDepth = 256;

    private readonly List<(IEditorCommand Command, long Serial)> _done = [];
    private readonly Stack<(IEditorCommand Command, long Serial)> _undone = new();
    private long _nextSerial = 1;
    private long _gesture;
    private long _lastRecordedGesture = -1;

    public bool CanUndo => _done.Count > 0;

    public bool CanRedo => _undone.Count > 0;

    public string? UndoName => CanUndo ? _done[^1].Command.Name : null;

    public string? RedoName => CanRedo ? _undone.Peek().Command.Name : null;

    /// <summary>Number of steps that can be undone.</summary>
    public int Count => _done.Count;

    /// <summary>
    /// Identifies the current point in history. Two equal values mean the scene is in the same state,
    /// which lets the editor know whether there are unsaved changes.
    /// </summary>
    public long StateId => _done.Count > 0 ? _done[^1].Serial : 0;

    /// <summary>Starts a new gesture: the next recorded command will not merge with earlier ones.</summary>
    public void BeginGesture() => _gesture++;

    /// <summary>Applies a command and adds it to the history.</summary>
    public void Execute(IEditorCommand command, Scene scene)
    {
        command.Execute(scene);
        Record(command, allowMerge: false);
    }

    /// <summary>Adds a command whose change has already been applied (e.g. by a UI widget).</summary>
    public void Record(IEditorCommand command, bool allowMerge = true)
    {
        _undone.Clear();

        if (allowMerge && _done.Count > 0 && _lastRecordedGesture == _gesture && _done[^1].Command.TryMerge(command))
        {
            _done[^1] = (_done[^1].Command, _nextSerial++);
            return;
        }

        _done.Add((command, _nextSerial++));
        if (_done.Count > MaxDepth)
        {
            _done.RemoveAt(0);
        }

        _lastRecordedGesture = _gesture;
    }

    public bool Undo(Scene scene)
    {
        if (!CanUndo)
        {
            return false;
        }

        var entry = _done[^1];
        _done.RemoveAt(_done.Count - 1);
        entry.Command.Undo(scene);
        _undone.Push(entry);
        BeginGesture();
        return true;
    }

    public bool Redo(Scene scene)
    {
        if (!CanRedo)
        {
            return false;
        }

        var entry = _undone.Pop();
        entry.Command.Execute(scene);
        _done.Add(entry);
        BeginGesture();
        return true;
    }

    public void Clear()
    {
        _done.Clear();
        _undone.Clear();
        BeginGesture();
    }
}
