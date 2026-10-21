namespace Lapis;

/// <summary>
/// A folder in the editor's Hierarchy. Folders only organise root entities (see <see cref="Entity.FolderId"/>):
/// they have no transform and do not affect the game.
/// </summary>
public sealed class SceneFolder(Guid id, string name, Guid? parentId = null)
{
    /// <summary>Unique id, stored in scene files.</summary>
    public Guid Id { get; } = id;

    /// <summary>Name shown in the Hierarchy.</summary>
    public string Name { get; set; } = name;

    /// <summary>The folder this one is inside, or null for the top level.</summary>
    public Guid? ParentId { get; set; } = parentId;
}
