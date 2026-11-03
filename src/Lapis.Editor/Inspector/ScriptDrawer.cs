using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.PropertyDrawers;
using Lapis.Editor.Scripting;

namespace Lapis.Editor.Inspector;

/// <summary>Scripts: a button with the script's file that opens it in VS Code.</summary>
internal sealed class ScriptDrawer(ScriptManager scripts) : ComponentDrawer
{
    public override Type ComponentType => typeof(Behaviour);

    public override void DrawBefore(Component component)
    {
        if (scripts.FindScript(component.GetType()) is not { } script)
        {
            return;
        }

        PropertyGrid.Label("Script");
        if (ImGui.Button($"{Path.GetFileName(script.FilePath)}##script", new Vector2(-1, 0)))
        {
            CodeEditor.Open(script.FilePath, script.Line, scripts.Project?.Directory);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Open the script in VS Code");
        }
    }
}
