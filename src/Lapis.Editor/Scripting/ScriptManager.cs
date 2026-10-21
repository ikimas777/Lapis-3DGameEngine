using System.Diagnostics;
using Lapis.Projects;
using Lapis.Serialization;

namespace Lapis.Editor.Scripting;

/// <summary>
/// Keeps the open project's scripts compiled and loaded. Watches the <c>Assets</c> folder, recompiles
/// in the background after a change, and swaps the new scripts in on the main thread while keeping the
/// scene's data (hot reload).
/// </summary>
internal sealed class ScriptManager(EditorContext context) : IDisposable
{
    private const double DebounceSeconds = 0.3;

    private readonly ScriptHost _host = new();
    private FileSystemWatcher? _watcher;
    private Task<ScriptCompilation>? _compileTask;
    private Stopwatch? _compileTimer;
    private volatile bool _changesPending;
    private double _changeTime;

    /// <summary>The open project, or null.</summary>
    public LapisProject? Project { get; private set; }

    /// <summary>True while scripts are being compiled.</summary>
    public bool IsCompiling => _compileTask is not null;

    /// <summary>Result of the last compilation, or null before the first one.</summary>
    public ScriptCompilation? LastCompilation { get; private set; }

    /// <summary>Where a script type is declared, or null when unknown.</summary>
    public ScriptLocation? FindScript(Type type) =>
        type.Assembly == _host.Assembly && LastCompilation?.TypeLocations.GetValueOrDefault(ComponentTypes.GetName(type)) is { } location
            ? location
            : null;

    /// <summary>
    /// Switches to another project (or none), compiling its scripts right away so its scenes open
    /// with all their components.
    /// </summary>
    public void SetProject(LapisProject? project)
    {
        _watcher?.Dispose();
        _watcher = null;
        Project = project;

        if (project is null)
        {
            return;
        }

        try
        {
            ScriptProjectFiles.Update(project);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Could not update the generated C# project: {exception.Message}");
        }

        Apply(CompileNow(), reloadScene: false);

        Directory.CreateDirectory(project.AssetsDirectory);
        _watcher = new FileSystemWatcher(project.AssetsDirectory, "*.cs")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += OnScriptFileChanged;
        _watcher.Created += OnScriptFileChanged;
        _watcher.Deleted += OnScriptFileChanged;
        _watcher.Renamed += OnScriptFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Asks for a recompile on the next <see cref="Update"/>.</summary>
    public void RequestCompile()
    {
        _changeTime = 0;
        _changesPending = true;
    }

    /// <summary>Call once per frame on the main thread.</summary>
    public void Update(double time)
    {
        _host.Update(time);

        if (_compileTask is { IsCompleted: true } finished)
        {
            _compileTask = null;
            if (finished.IsFaulted)
            {
                Log.Error(finished.Exception!.InnerException ?? finished.Exception, "Compiling scripts failed.");
            }
            else
            {
                Apply(finished.Result, reloadScene: true);
            }
        }

        if (!_changesPending || _compileTask is not null || Project is null)
        {
            return;
        }

        // Editors often save a file in several steps; wait until the changes settle.
        if (_changeTime == 0)
        {
            _changeTime = time;
        }

        if (time - _changeTime < DebounceSeconds)
        {
            return;
        }

        _changesPending = false;
        _changeTime = 0;
        var files = ScriptProjectFiles.FindScripts(Project);
        _compileTimer = Stopwatch.StartNew();
        _compileTask = Task.Run(() => ScriptCompiler.Compile(files));
    }

    public void Dispose() => _watcher?.Dispose();

    private ScriptCompilation CompileNow()
    {
        _compileTimer = Stopwatch.StartNew();
        return ScriptCompiler.Compile(ScriptProjectFiles.FindScripts(Project!));
    }

    private void OnScriptFileChanged(object sender, FileSystemEventArgs e)
    {
        // Runs on a thread-pool thread; Update picks it up on the main thread.
        _changeTime = 0;
        _changesPending = true;
    }

    private void Apply(ScriptCompilation compilation, bool reloadScene)
    {
        LastCompilation = compilation;
        var elapsed = _compileTimer?.ElapsedMilliseconds ?? 0;

        foreach (var diagnostic in compilation.Diagnostics)
        {
            var message = $"{diagnostic.Id}: {diagnostic.Message}";
            var file = diagnostic.FilePath;
            if (file is not null && Project is not null && Path.IsPathRooted(file))
            {
                message = $"{Project.ToProjectPath(file)}({diagnostic.Line},{diagnostic.Column}): {message}";
            }

            switch (diagnostic.IsError, file)
            {
                case (true, not null):
                    Log.Error(message, file, diagnostic.Line);
                    break;
                case (true, null):
                    Log.Error(message);
                    break;
                case (false, not null):
                    Log.Warning(message, file, diagnostic.Line);
                    break;
                default:
                    Log.Warning(message);
                    break;
            }
        }

        if (!compilation.Success)
        {
            Log.Error($"Scripts have {compilation.ErrorCount} error(s); the previous version stays loaded until they are fixed.");
            return;
        }

        // Keep the scene as JSON while the old scripts are swapped for the new ones.
        var sceneJson = reloadScene ? SceneSerializer.ToJson(context.Scene) : null;

        var assembly = _host.Load(compilation.AssemblyBytes!, compilation.SymbolBytes);
        ComponentTypes.SetScriptAssembly(assembly);

        if (sceneJson is not null)
        {
            context.ReplaceScene(SceneSerializer.FromJson(sceneJson));
        }

        _host.UnloadPrevious();

        var scriptCount = compilation.TypeLocations.Count;
        Log.Info($"Scripts compiled in {elapsed} ms ({scriptCount} class{(scriptCount == 1 ? string.Empty : "es")}).");
    }
}
