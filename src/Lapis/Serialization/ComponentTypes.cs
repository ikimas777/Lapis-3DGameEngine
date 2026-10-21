using System.Collections.Concurrent;
using System.Reflection;

namespace Lapis.Serialization;

/// <summary>
/// Maps component type names stored in scene files to <see cref="Type"/>s in the loaded assemblies.
/// </summary>
/// <remarks>
/// Game scripts are compiled into an assembly that is replaced on every reload. Only the current
/// script assembly (<see cref="ScriptAssembly"/>) is searched; older, unloading ones are ignored.
/// </remarks>
public static class ComponentTypes
{
    private static readonly ConcurrentDictionary<string, Type?> Cache = new(StringComparer.Ordinal);

    /// <summary>The assembly with the current game scripts, or null when none is loaded.</summary>
    public static Assembly? ScriptAssembly { get; private set; }

    /// <summary>Raised after <see cref="SetScriptAssembly"/>, so other caches can be cleared.</summary>
    public static event Action? ScriptAssemblyChanged;

    /// <summary>The name written to scene files for <paramref name="type"/>.</summary>
    public static string GetName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.FullName ?? type.Name;
    }

    /// <summary>
    /// Finds a concrete component type by the name produced by <see cref="GetName"/>, searching
    /// the engine, other loaded assemblies and the current script assembly. Returns null when not found.
    /// </summary>
    public static Type? Resolve(string name) => Cache.GetOrAdd(name, FindType);

    /// <summary>Assemblies that may contain component types, script assembly last.</summary>
    public static IEnumerable<Assembly> SearchedAssemblies()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && !assembly.IsCollectible)
            {
                yield return assembly;
            }
        }

        if (ScriptAssembly is { } scripts)
        {
            yield return scripts;
        }
    }

    /// <summary>
    /// Makes <paramref name="assembly"/> the current script assembly and clears every cache that
    /// may refer to types of the previous one.
    /// </summary>
    public static void SetScriptAssembly(Assembly? assembly)
    {
        ScriptAssembly = assembly;
        ClearCache();
        SerializedMember.ClearCache();
        SceneSerializer.ResetJsonOptions();
        ScriptAssemblyChanged?.Invoke();
    }

    /// <summary>Forgets resolved types.</summary>
    public static void ClearCache() => Cache.Clear();

    private static Type? FindType(string name)
    {
        foreach (var assembly in SearchedAssemblies())
        {
            var type = assembly.GetType(name, throwOnError: false);
            if (type is not null && type.IsSubclassOf(typeof(Component)) && !type.IsAbstract)
            {
                return type;
            }
        }

        return null;
    }
}
