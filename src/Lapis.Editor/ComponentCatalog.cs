namespace Lapis.Editor;

/// <summary>Lists the component types that can be added from the Inspector.</summary>
internal static class ComponentCatalog
{
    private static IReadOnlyList<Type>? _types;

    /// <summary>Public, concrete component types from all loaded assemblies, sorted by name.</summary>
    public static IReadOnlyList<Type> Types => _types ??= Discover();

    /// <summary>Forgets the list, e.g. after game scripts are reloaded.</summary>
    public static void Refresh() => _types = null;

    private static List<Type> Discover()
    {
        var types = new List<Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

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
