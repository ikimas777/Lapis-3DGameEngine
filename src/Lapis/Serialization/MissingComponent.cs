using System.Text.Json.Nodes;

namespace Lapis;

/// <summary>
/// Stands in for a component whose type could not be found when a scene was loaded
/// (for example a script that was renamed or failed to compile). Its data is kept and
/// written back unchanged when the scene is saved.
/// </summary>
public sealed class MissingComponent : Component
{
    /// <summary>The type name stored in the scene file.</summary>
    public string TypeName { get; internal set; } = string.Empty;

    internal JsonObject Data { get; set; } = [];
}
