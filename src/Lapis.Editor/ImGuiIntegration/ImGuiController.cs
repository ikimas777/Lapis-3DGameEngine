using System.Numerics;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Lapis.Editor.ImGuiIntegration;

/// <summary>
/// Owns the Dear ImGui context: forwards window input to ImGui, starts frames and renders them.
/// </summary>
internal sealed unsafe class ImGuiController : IDisposable
{
    private readonly IWindow _window;
    private readonly IInputContext _input;
    private readonly ImGuiContextPtr _context;
    private readonly ImGuiRenderer _renderer;
    private readonly nint _iniPath;
    private readonly IKeyboard? _keyboard;

    /// <param name="window">The application window.</param>
    /// <param name="gl">OpenGL API for the window's context.</param>
    /// <param name="input">Input devices of the window.</param>
    /// <param name="iniPath">Where ImGui stores the window layout; null disables saving.</param>
    public ImGuiController(IWindow window, GL gl, IInputContext input, string? iniPath)
    {
        _window = window;
        _input = input;
        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
        ImGuizmo.SetImGuiContext(_context);

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable;
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;

        _iniPath = iniPath is null ? 0 : Marshal.StringToCoTaskMemUTF8(iniPath);
        io.IniFilename = (byte*)_iniPath;

        _renderer = new ImGuiRenderer(gl);

        foreach (var keyboard in input.Keyboards)
        {
            _keyboard ??= keyboard;
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
            keyboard.KeyChar += OnKeyChar;
        }

        foreach (var mouse in input.Mice)
        {
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.Scroll += OnScroll;
        }
    }

    /// <summary>
    /// Stops passing mouse movement, clicks and the wheel to the UI, e.g. while a running game has the mouse.
    /// Button releases still go through.
    /// </summary>
    public bool IgnoreMouse { get; set; }

    /// <summary>Starts a new ImGui frame. Call once per frame before any ImGui widgets.</summary>
    public void NewFrame(float deltaTime)
    {
        var io = ImGui.GetIO();
        var windowSize = _window.Size;
        var framebufferSize = _window.FramebufferSize;

        io.DisplaySize = new Vector2(windowSize.X, windowSize.Y);
        if (windowSize.X > 0 && windowSize.Y > 0)
        {
            io.DisplayFramebufferScale = new Vector2((float)framebufferSize.X / windowSize.X, (float)framebufferSize.Y / windowSize.Y);
        }

        io.DeltaTime = deltaTime > 0f ? deltaTime : 1f / 60f;

        ImGui.NewFrame();
        ImGuizmo.BeginFrame();
    }

    /// <summary>Finishes the frame and draws it into the default framebuffer.</summary>
    public void Render()
    {
        UpdateMouseCursor();
        ImGui.Render();
        var size = _window.FramebufferSize;
        _renderer.Render(ImGui.GetDrawData(), size.X, size.Y);
    }

    public void Dispose()
    {
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown -= OnKeyDown;
            keyboard.KeyUp -= OnKeyUp;
            keyboard.KeyChar -= OnKeyChar;
        }

        foreach (var mouse in _input.Mice)
        {
            mouse.MouseMove -= OnMouseMove;
            mouse.MouseDown -= OnMouseDown;
            mouse.MouseUp -= OnMouseUp;
            mouse.Scroll -= OnScroll;
        }

        _renderer.Dispose();
        ImGui.DestroyContext(_context);

        if (_iniPath != 0)
        {
            Marshal.FreeCoTaskMem(_iniPath);
        }
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode) => SendKey(keyboard, key, true);

    private void OnKeyUp(IKeyboard keyboard, Key key, int scancode) => SendKey(keyboard, key, false);

    private static void SendKey(IKeyboard keyboard, Key key, bool down)
    {
        var io = ImGui.GetIO();
        io.AddKeyEvent(ImGuiKey.ModCtrl, keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight));
        io.AddKeyEvent(ImGuiKey.ModShift, keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight));
        io.AddKeyEvent(ImGuiKey.ModAlt, keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight));
        io.AddKeyEvent(ImGuiKey.ModSuper, keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight));

        var imguiKey = ImGuiKeyMap.ToImGui(key);
        if (imguiKey != ImGuiKey.None)
        {
            io.AddKeyEvent(imguiKey, down);
        }
    }

    private static void OnKeyChar(IKeyboard keyboard, char character) => ImGui.GetIO().AddInputCharacter(character);

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (!IgnoreMouse)
        {
            ImGui.GetIO().AddMousePosEvent(position.X, position.Y);
        }
    }

    private void OnMouseDown(IMouse mouse, Silk.NET.Input.MouseButton button)
    {
        if (!IgnoreMouse)
        {
            SendMouseButton(button, true);
        }
    }

    // Releases always go through, so a button pressed before the mouse was ignored is not left held.
    private static void OnMouseUp(IMouse mouse, Silk.NET.Input.MouseButton button) => SendMouseButton(button, false);

    private static void SendMouseButton(Silk.NET.Input.MouseButton button, bool down)
    {
        var index = button switch
        {
            Silk.NET.Input.MouseButton.Left => 0,
            Silk.NET.Input.MouseButton.Right => 1,
            Silk.NET.Input.MouseButton.Middle => 2,
            Silk.NET.Input.MouseButton.Button4 => 3,
            Silk.NET.Input.MouseButton.Button5 => 4,
            _ => -1,
        };

        if (index >= 0)
        {
            ImGui.GetIO().AddMouseButtonEvent(index, down);
        }
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        if (!IgnoreMouse)
        {
            ImGui.GetIO().AddMouseWheelEvent(wheel.X, wheel.Y);
        }
    }

    private void UpdateMouseCursor()
    {
        var cursor = ImGui.GetMouseCursor();
        var standard = cursor switch
        {
            ImGuiMouseCursor.TextInput => StandardCursor.IBeam,
            ImGuiMouseCursor.ResizeAll => StandardCursor.ResizeAll,
            ImGuiMouseCursor.ResizeNs => StandardCursor.VResize,
            ImGuiMouseCursor.ResizeEw => StandardCursor.HResize,
            ImGuiMouseCursor.ResizeNesw => StandardCursor.NeswResize,
            ImGuiMouseCursor.ResizeNwse => StandardCursor.NwseResize,
            ImGuiMouseCursor.Hand => StandardCursor.Hand,
            ImGuiMouseCursor.NotAllowed => StandardCursor.NotAllowed,
            _ => StandardCursor.Default,
        };

        foreach (var mouse in _input.Mice)
        {
            if (mouse.Cursor.StandardCursor != standard)
            {
                mouse.Cursor.StandardCursor = standard;
            }
        }
    }
}
