using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using Lapis.Editor.ImGuiIntegration;
using Lapis.Editor.Theme;
using Lapis.Rendering;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Lapis.Monitor;

/// <summary>
/// Lapis Monitor: attaches to a running Lapis editor (or an exported game), shows its performance counters
/// live, raises alerts and records every run. Started from the editor (Debug > Lapis Monitor) or a terminal.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        MonitorOptions? options;
        try
        {
            options = MonitorOptions.Parse(args);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(MonitorOptions.Usage);
            return 2;
        }

        if (options is null)
        {
            Console.WriteLine(MonitorOptions.Usage);
            return 0;
        }

        try
        {
            Run(options);
            return 0;
        }
        catch (Exception exception)
        {
            // The published monitor has no console window, so keep the reason in a file.
            Directory.CreateDirectory(SessionRecorder.DataDirectory);
            File.AppendAllText(Path.Combine(SessionRecorder.DataDirectory, "monitor.log"), $"[{DateTime.Now:u}] {exception}{Environment.NewLine}");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Run(MonitorOptions options)
    {
        var store = new MetricStore();
        var alerts = new AlertEngine();
        using var recorder = new SessionRecorder();
        using var client = new CounterClient();

        client.Attached += (name, pid) =>
        {
            store.Clear();
            alerts.Reset();
            recorder.Begin(name, pid);
        };
        client.Detached += () => recorder.End(store);
        client.SampleReceived += sample =>
        {
            store.Add(sample);
            recorder.Write(sample);
            alerts.Check(sample);
        };
        alerts.Raised += recorder.Write;
        if (options.ProcessId is { } pid)
        {
            client.Request(pid);
        }

        using var window = Window.Create(WindowOptions.Default with
        {
            Title = "Lapis Monitor",
            Size = new Vector2D<int>(1500, 950),
            VSync = true,
            FramesPerSecond = 30,
            UpdatesPerSecond = 30,
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, Lapis.Application.GLVersion),
        });

        GL? gl = null;
        IInputContext? input = null;
        ImGuiController? imgui = null;
        MonitorUi? ui = null;
        var titleTimer = 0.0;
        var runTime = 0.0;

        window.Load += () =>
        {
            gl = window.CreateOpenGL();
            input = window.CreateInput();
            imgui = new ImGuiController(window, gl, input, Path.Combine(SessionRecorder.DataDirectory, "monitor-layout.ini"));
            ImPlot.SetImGuiContext(ImGui.GetCurrentContext());
            ImPlot.CreateContext();
            EditorTheme.Apply();
            ui = new MonitorUi(client, store, alerts, recorder);
            client.Start();
        };

        window.Render += delta =>
        {
            imgui!.NewFrame((float)delta);
            ui!.Draw();

            var size = window.FramebufferSize;
            gl!.Viewport(0, 0, (uint)size.X, (uint)size.Y);
            gl.ClearColor(0.12f, 0.12f, 0.12f, 1f);
            gl.Clear(ClearBufferMask.ColorBufferBit);
            imgui.Render();

            runTime += delta;
            if (options.CapturePath is { } capture && runTime >= options.CaptureAfterSeconds)
            {
                FramebufferCapture.SaveBackBufferAsPng(gl, size.X, size.Y, capture);
                window.Close();
            }

            titleTimer += delta;
            if (titleTimer > 1)
            {
                titleTimer = 0;
                window.Title = client.IsAttached ? $"Lapis Monitor — {client.ProcessName} ({client.ProcessId})" : "Lapis Monitor — waiting";
            }
        };

        window.FramebufferResize += size => gl?.Viewport(0, 0, (uint)size.X, (uint)size.Y);

        window.Closing += () =>
        {
            client.Dispose();
            recorder.End(store);
            ImPlot.DestroyContext(ImPlot.GetCurrentContext());
            imgui?.Dispose();
            input?.Dispose();
            gl?.Dispose();
        };

        window.Run();
    }
}
