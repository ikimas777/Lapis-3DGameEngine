using Hexa.NET.ImGui;

namespace Lapis.Editor.Panels;

/// <summary>A dockable editor window.</summary>
internal abstract class EditorPanel(string title)
{
    /// <summary>Window title; also the ImGui window id used by the dock layout.</summary>
    public string Title { get; } = title;

    /// <summary>Hidden panels are not drawn; they are shown and hidden from the View menu.</summary>
    public bool IsOpen { get; set; } = true;

    /// <summary>False to skip the panel this frame (checked before <c>ImGui.Begin</c>).</summary>
    protected virtual bool CanDraw => true;

    /// <summary>Id of the dock node the panel was in last frame (0 when floating or not drawn yet).</summary>
    public uint DockId { get; private set; }

    /// <summary>Extra ImGui window flags.</summary>
    protected virtual ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.None;

    /// <summary>Draws the panel if it is open.</summary>
    public void Draw()
    {
        if (!IsOpen || !CanDraw)
        {
            return;
        }

        BeforeBegin();
        var visible = ImGui.Begin(Title, WindowFlags);
        DockId = ImGui.GetWindowDockID();
        AfterBegin();
        if (visible)
        {
            DrawContent();
        }

        ImGui.End();
    }

    /// <summary>Draws the panel contents inside its window.</summary>
    protected abstract void DrawContent();

    /// <summary>Called before <c>ImGui.Begin</c>, e.g. to push style variables.</summary>
    protected virtual void BeforeBegin()
    {
    }

    /// <summary>Called right after <c>ImGui.Begin</c>, e.g. to pop style variables.</summary>
    protected virtual void AfterBegin()
    {
    }
}
