using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Launcher;
using Silk.NET.OpenGL;

namespace Lapis.Editor;

/// <summary>The project launcher shown before a project is open, and closing a project to return to it.</summary>
internal sealed unsafe partial class EditorApplication : ILauncherHost
{
    private const string UnsavedChangesPopup = "Unsaved Changes";

    private bool _inLauncher;
    private ProjectLauncher? _launcher;

    nint ILauncherHost.WindowHandle => Window.Native?.Win32?.Hwnd ?? 0;

    bool ILauncherHost.OpenProject(string path) => OpenProject(path);

    bool ILauncherHost.CreateProject(string folder, string name) => CreateProject(folder, name);

    private void RenderLauncher()
    {
        _launcher ??= new ProjectLauncher(_settings, this);
        _launcher.Draw();

        var size = FramebufferSize;
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, (uint)size.X, (uint)size.Y);
        GL.ClearColor(0.12f, 0.12f, 0.12f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        _imgui!.Render();

        if (launchOptions.CapturePath is { } capturePath && Time.FrameCount >= launchOptions.CaptureAfterFrames)
        {
            Rendering.FramebufferCapture.SaveBackBufferAsPng(GL, size.X, size.Y, capturePath);
            Log.Info($"Saved screenshot to {Path.GetFullPath(capturePath)}");
            Close();
        }
    }

    /// <summary>Project > Close Project: asks about unsaved changes, then returns to the launcher.</summary>
    private void RequestCloseProject()
    {
        _play.Stop();
        if (_context.IsDirty)
        {
            _popupToOpen = UnsavedChangesPopup;
            return;
        }

        CloseProject();
    }

    private void CloseProject()
    {
        _play.Stop();
        _scripts.SetProject(null);
        _assetWatcher?.Dispose();
        _assetWatcher = null;
        Lapis.Assets.AssetDatabase.Active = null;
        _context.SelectedAsset = null;
        _context.NewScene();
        SaveSettings();
        _launcher?.Error = null;
        _inLauncher = true;
        Log.Info("Closed the project.");
    }

    private void DrawUnsavedChangesPopup(Vector2 center)
    {
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal(UnsavedChangesPopup, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        ImGui.TextUnformatted($"Save changes to '{_context.Scene.Name}' before closing the project?");
        ImGui.Spacing();

        if (ImGui.Button("Save", new Vector2(120, 0)))
        {
            if (_context.ScenePath is null)
            {
                Log.Warning("This scene has never been saved; use File > Save As first.");
            }
            else if (_context.SaveScene())
            {
                ImGui.CloseCurrentPopup();
                CloseProject();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Don't Save", new Vector2(120, 0)))
        {
            ImGui.CloseCurrentPopup();
            CloseProject();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }
}
