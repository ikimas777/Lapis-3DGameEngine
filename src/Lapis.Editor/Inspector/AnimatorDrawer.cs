using Hexa.NET.ImGui;
using Lapis.Editor.PropertyDrawers;
using Lapis.Editor.Theme;
using Lapis.Serialization;

namespace Lapis.Editor.Inspector;

/// <summary>Animators: the start animation chosen from the model's animations, and preview buttons.</summary>
internal sealed class AnimatorDrawer(EditorContext context) : ComponentDrawer
{
    public override Type ComponentType => typeof(Animator);

    /// <summary>The start animation as a list of the model's animations.</summary>
    public override bool TryDrawMember(Component component, SerializedMember member, out bool changed)
    {
        changed = false;
        if (member.Name != nameof(Animator.Clip))
        {
            return false;
        }

        var animator = (Animator)component;
        var clips = animator.ClipNames;
        PropertyGrid.Label("Clip", "Animation played when the game starts.");
        if (clips.Count == 0)
        {
            ImGui.TextDisabled("The Mesh Renderer's model has no animations.");
            return true;
        }

        var current = animator.Clip.Length == 0 ? $"{clips[0]} (first)" : animator.Clip;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##clip", current))
        {
            foreach (var clip in clips)
            {
                if (ImGui.Selectable(clip, clip == animator.Clip))
                {
                    animator.Clip = clip;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return true;
    }

    /// <summary>Buttons that play each animation in the Scene view without running the game.</summary>
    public override void DrawAfter(Component component)
    {
        var animator = (Animator)component;
        var clips = animator.ClipNames;
        if (clips.Count == 0 || context.IsPlaying)
        {
            return;
        }

        PropertyGrid.Label("Preview", "Play an animation in the Scene view (the game is not started).");
        foreach (var clip in clips)
        {
            var playing = animator.Previewing && animator.CurrentClip == clip;
            if (playing)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.Accent);
            }

            if (ImGui.Button($"{clip}##preview"))
            {
                animator.Previewing = true;
                animator.Play(clip, fade: 0.2f);
            }

            if (playing)
            {
                ImGui.PopStyleColor();
            }

            ImGui.SameLine();
        }

        ImGui.BeginDisabled(!animator.Previewing);
        if (ImGui.Button("Stop##preview"))
        {
            animator.Previewing = false;
            animator.ResetPose();
        }

        ImGui.EndDisabled();
    }
}
