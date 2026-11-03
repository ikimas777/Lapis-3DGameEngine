namespace Lapis.Editor.Commands;

/// <summary>
/// Links an entity to a prefab (it becomes an instance) or unlinks it (Unpack). Prefab links of entities inside it
/// are removed with the same step, since an instance holds no other instances.
/// </summary>
internal sealed class SetPrefabLinkCommand : IEditorCommand
{
    private readonly Guid _entityId;
    private readonly string? _prefab;
    private readonly List<(Guid Id, string? Prefab)> _before = [];

    public SetPrefabLinkCommand(Entity entity, string? prefab, string name)
    {
        _entityId = entity.Id;
        _prefab = prefab;
        Name = name;
        Collect(entity);

        void Collect(Entity current)
        {
            _before.Add((current.Id, current.Prefab));
            foreach (var child in current.Children)
            {
                Collect(child);
            }
        }
    }

    public string Name { get; }

    public void Execute(Scene scene)
    {
        foreach (var (id, _) in _before)
        {
            CommandTarget.Entity(scene, id).Prefab = null;
        }

        CommandTarget.Entity(scene, _entityId).Prefab = _prefab;
    }

    public void Undo(Scene scene)
    {
        foreach (var (id, prefab) in _before)
        {
            CommandTarget.Entity(scene, id).Prefab = prefab;
        }
    }
}
