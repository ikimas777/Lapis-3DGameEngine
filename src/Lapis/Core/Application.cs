using Lapis.Diagnostics;
using Lapis.Rendering;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Lapis;

/// <summary>Settings used to create the application window.</summary>
public sealed record ApplicationOptions
{
    /// <summary>Window title.</summary>
    public string Title { get; init; } = EngineInfo.Name;

    /// <summary>Initial window width in pixels.</summary>
    public int Width { get; init; } = 1600;

    /// <summary>Initial window height in pixels.</summary>
    public int Height { get; init; } = 900;

    /// <summary>Synchronise presentation with the monitor refresh rate.</summary>
    public bool VSync { get; init; } = true;

    /// <summary>Samples per pixel for anti-aliasing of the window itself (0 = off).</summary>
    public int Samples { get; init; }

    /// <summary>Start in fullscreen mode.</summary>
    public bool Fullscreen { get; init; }

    /// <summary>Create an OpenGL debug context and route driver messages to the <see cref="Log"/>.</summary>
    public bool DebugContext { get; init; }
#if DEBUG
        = true;
#endif
}

/// <summary>
/// Owns the window, the OpenGL 4.5 context and the frame loop.
/// Derive from it and override the <c>On*</c> methods.
/// </summary>
public abstract class Application : IDisposable
{
    private readonly IWindow _window;
    private readonly bool _debugContext;
    private GL? _gl;
    private IInputContext? _inputContext;
    private bool _disposed;

    /// <summary>Creates the window. Nothing is shown until <see cref="Run"/> is called.</summary>
    protected Application(ApplicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _debugContext = options.DebugContext;

        var flags = ContextFlags.ForwardCompatible;
        if (options.DebugContext)
        {
            flags |= ContextFlags.Debug;
        }

        var windowOptions = WindowOptions.Default with
        {
            Title = options.Title,
            Size = new Vector2D<int>(options.Width, options.Height),
            VSync = options.VSync,
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, flags, GLVersion),
            PreferredDepthBufferBits = 24,
            PreferredStencilBufferBits = 8,
            Samples = options.Samples,
            WindowState = options.Fullscreen ? WindowState.Fullscreen : WindowState.Normal,
        };

        _window = Silk.NET.Windowing.Window.Create(windowOptions);
        _window.Load += HandleLoad;
        _window.Update += HandleUpdate;
        _window.Render += HandleRender;
        _window.FramebufferResize += HandleFramebufferResize;
        _window.Closing += HandleClosing;
    }

    /// <summary>
    /// The OpenGL version asked for: 4.5 on Windows and Linux, 4.1 on macOS (the newest Apple provides).
    /// The renderer only uses OpenGL 4.1 features, so it draws the same image on every system.
    /// Setting the environment variable <c>LAPIS_GL=4.1</c> asks for 4.1 anywhere, to test what macOS gets.
    /// </summary>
    public static APIVersion GLVersion =>
        OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("LAPIS_GL") == "4.1" ? new APIVersion(4, 1) : new APIVersion(4, 5);

    /// <summary>The OpenGL API. Available from <see cref="OnLoad"/> onwards.</summary>
    protected internal GL GL => _gl ?? throw new InvalidOperationException("OpenGL is not available before OnLoad.");

    /// <summary>Keyboard and mouse devices. Available from <see cref="OnLoad"/> onwards.</summary>
    protected internal IInputContext InputContext =>
        _inputContext ?? throw new InvalidOperationException("Input is not available before OnLoad.");

    /// <summary>The native window.</summary>
    protected internal IWindow Window => _window;

    /// <summary>Size of the drawable area in pixels.</summary>
    public Vector2D<int> FramebufferSize => _window.FramebufferSize;

    /// <summary>Opens the window and runs the frame loop until the window closes.</summary>
    public void Run() => _window.Run();

    /// <summary>Requests the window to close at the end of the current frame.</summary>
    public void Close() => _window.Close();

    /// <summary>Called once after the window and OpenGL context are created.</summary>
    protected virtual void OnLoad()
    {
    }

    /// <summary>Called once per frame, after <see cref="Time"/> has advanced.</summary>
    protected virtual void OnUpdate()
    {
    }

    /// <summary>Called once per frame to draw. The back buffer is presented afterwards.</summary>
    protected virtual void OnRender()
    {
    }

    /// <summary>Called when the drawable area changes size.</summary>
    protected virtual void OnFramebufferResize(int width, int height)
    {
    }

    /// <summary>Called once before the window closes, while OpenGL is still available.</summary>
    protected virtual void OnUnload()
    {
    }

    private void HandleLoad()
    {
        _gl = _window.CreateOpenGL();
        _inputContext = _window.CreateInput();

        if (_debugContext)
        {
            GLDebug.Enable(_gl);
        }

        // System.Numerics projection matrices produce depth in [0, 1] while OpenGL expects [-1, 1]; every
        // vertex shader converts it (see the end of lit.vert), so window depth stays in [0, 1] as before.

        Log.Info($"{EngineInfo.Name} {EngineInfo.InformationalVersion}");
        Log.Info($"OpenGL {_gl.GetStringS(StringName.Version)} on {_gl.GetStringS(StringName.Renderer)}");

        Time.Reset();
        OnLoad();
        HandleFramebufferResize(_window.FramebufferSize);
    }

    private void HandleUpdate(double deltaSeconds)
    {
        EngineMetrics.BeginFrame(deltaSeconds);
        Time.Advance(deltaSeconds);
        OnUpdate();
    }

    private void HandleRender(double deltaSeconds)
    {
        OnRender();
        EngineMetrics.EndFrame();
    }

    private void HandleFramebufferResize(Vector2D<int> size)
    {
        if (size.X <= 0 || size.Y <= 0)
        {
            return; // minimised
        }

        GL.Viewport(0, 0, (uint)size.X, (uint)size.Y);
        OnFramebufferResize(size.X, size.Y);
    }

    private void HandleClosing()
    {
        OnUnload();
        _inputContext?.Dispose();
        _gl?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the window.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _window.Dispose();
        }

        _disposed = true;
    }
}
