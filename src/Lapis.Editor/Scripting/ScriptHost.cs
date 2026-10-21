using System.Reflection;
using System.Runtime.Loader;

namespace Lapis.Editor.Scripting;

/// <summary>
/// Loads compiled scripts into a collectible <see cref="AssemblyLoadContext"/> so that a newer
/// version can replace them without restarting the editor.
/// </summary>
internal sealed class ScriptHost
{
    private const int MaxUnloadChecks = 10;

    private readonly List<(WeakReference Context, int Checks)> _unloading = [];
    private ScriptLoadContext? _context;
    private double _nextUnloadCheck;

    /// <summary>The currently loaded script assembly, or null.</summary>
    public Assembly? Assembly { get; private set; }

    /// <summary>
    /// Loads a new script assembly next to the current one. Call <see cref="UnloadPrevious"/> once
    /// nothing refers to the old scripts any more.
    /// </summary>
    public Assembly Load(byte[] assemblyBytes, byte[]? symbolBytes)
    {
        var context = new ScriptLoadContext();
        using var assemblyStream = new MemoryStream(assemblyBytes);
        using var symbolStream = symbolBytes is null ? null : new MemoryStream(symbolBytes);
        var assembly = context.LoadFromStream(assemblyStream, symbolStream);

        _previous = _context;
        _context = context;
        Assembly = assembly;
        return assembly;
    }

    private ScriptLoadContext? _previous;

    /// <summary>Starts unloading the previously loaded scripts.</summary>
    public void UnloadPrevious()
    {
        if (_previous is null)
        {
            return;
        }

        _unloading.Add((new WeakReference(_previous), 0));
        _previous.Unload();
        _previous = null;
    }

    /// <summary>
    /// Periodically checks that old script assemblies were really unloaded and warns if something
    /// keeps them alive (which would slowly leak memory with every reload).
    /// </summary>
    public void Update(double time)
    {
        if (_unloading.Count == 0 || time < _nextUnloadCheck)
        {
            return;
        }

        _nextUnloadCheck = time + 1.0;
        GC.Collect();
        GC.WaitForPendingFinalizers();

        for (var i = _unloading.Count - 1; i >= 0; i--)
        {
            var (context, checks) = _unloading[i];
            if (!context.IsAlive)
            {
                _unloading.RemoveAt(i);
            }
            else if (checks + 1 >= MaxUnloadChecks)
            {
                _unloading.RemoveAt(i);
                Log.Warning("Old script code is still in memory after a reload; something still refers to it. Restart the editor if memory use grows.");
            }
            else
            {
                _unloading[i] = (context, checks + 1);
            }
        }
    }

    private sealed class ScriptLoadContext() : AssemblyLoadContext("Lapis game scripts", isCollectible: true)
    {
        // Returning null makes the engine and the .NET libraries resolve from the default context,
        // so scripts share the editor's Lapis types.
        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }
}
