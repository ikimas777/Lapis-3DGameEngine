using Lapis.Serialization;

namespace Lapis.Editor;

/// <summary>Lists the component types that can be added from the Inspector.</summary>
internal static class ComponentCatalog
{
    private static IReadOnlyList<Type>? _types;

    static ComponentCatalog() => ComponentTypes.ScriptAssemblyChanged += Refresh;

    /// <summary>Public, concrete component types from the engine and the game scripts, sorted by name.</summary>
    public static IReadOnlyList<Type> Types => _types ??= Discover();

    /// <summary>Forgets the list, e.g. after game scripts are reloaded.</summary>
    public static void Refresh() => _types = null;

    /// <summary>True when the type comes from the game's scripts rather than the engine.</summary>
    public static bool IsScript(Type type) => type.Assembly == ComponentTypes.ScriptAssembly;

    private static List<Type> Discover()
    {
        var types = new List<Type>();
        foreach (var assembly in ComponentTypes.SearchedAssemblies())
        {
            Type[] exported;
            try
            {
                exported = assembly.GetExportedTypes();
            }
            catch (NotSupportedException)
            {
                continue;
            }

            types.AddRange(exported.Where(type =>
                type.IsSubclassOf(typeof(Component)) &&
                !type.IsAbstract &&
                type != typeof(Transform) &&
                type != typeof(MissingComponent)));
        }

        types.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return types;
    }
}
