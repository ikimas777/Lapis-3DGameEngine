using System.Collections.Concurrent;

namespace Lapis.Serialization;

/// <summary>
/// Maps component type names stored in scene files to <see cref="Type"/>s in the loaded assemblies.
/// </summary>
public static class ComponentTypes
{
    private static readonly ConcurrentDictionary<string, Type?> Cache = new(StringComparer.Ordinal);

    /// <summary>The name written to scene files for <paramref name="type"/>.</summary>
    public static string GetName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.FullName ?? type.Name;
    }

    /// <summary>
    /// Finds a concrete component type by the name produced by <see cref="GetName"/>, searching
    /// every loaded assembly. Returns null when no such type is loaded.
    /// </summary>
    public static Type? Resolve(string name) => Cache.GetOrAdd(name, FindType);

    /// <summary>Forgets resolved types, e.g. after game scripts are reloaded.</summary>
    public static void ClearCache() => Cache.Clear();

    private static Type? FindType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            var type = assembly.GetType(name, throwOnError: false);
            if (type is not null && type.IsSubclassOf(typeof(Component)) && !type.IsAbstract)
            {
                return type;
            }
        }

        return null;
    }
}
