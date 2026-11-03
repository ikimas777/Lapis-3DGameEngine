using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Commands;
using Lapis.Editor.PropertyDrawers;
using Lapis.Editor.Scripting;
using Lapis.Serialization;

namespace Lapis.Editor.Panels;

/// <summary>
/// Shows and edits the selected entity: name, transform and components.
/// Every edit is recorded in the undo history; a drag or a text edit becomes one step.
/// </summary>
internal sealed unsafe class InspectorPanel(EditorContext context, ScriptManager scripts, AssetInspector assetInspector) : EditorPanel("Inspector")
{
    private const string AddComponentPopup = "##addComponent";
    private const string AddTagPopup = "Add Tag##addTag";
    private const string RemoveTagPopup = "Remove Tag##removeTag";

    private string _componentFilter = string.Empty;
    private string _newTag = string.Empty;
    private bool _openAddTag;
    private string? _tagToRemove;
    private string? _removingTag;

    // Euler angles are cached while editing so values like 180° do not flip to -180° mid-drag.
    private Transform? _eulerOwner;
    private readonly SceneView.UIHandles _uiHandles = new(context);
    private Quaternion _eulerSource;
    private Vector3 _euler;

    protected override void DrawContent()
    {
        if (context.SelectedAsset is { } asset)
        {
            _eulerOwner = null;
            if (context.SelectedAssets.Count > 1)
            {
                ImGui.TextDisabled($"{context.SelectedAssets.Count} assets selected; showing the last one.");
                ImGui.Spacing();
            }

            assetInspector.Draw(asset);
            return;
        }

        if (context.Selection is not { } entity)
        {
            _eulerOwner = null; // do not keep a reloaded scene alive; nothing selected shows an empty Inspector
            return;
        }

        ImGui.PushID(entity.Id.ToString());

        if (context.SelectedEntities.Count is var count and > 1)
        {
            ImGui.TextDisabled($"{count} entities selected; editing the last one. The gizmo moves them all.");
            ImGui.Spacing();
        }

        // A prefab copy only lets its root be renamed, tagged, switched on or off and moved; the rest comes from the prefab.
        var insideInstance = PrefabActions.IsInsideInstance(entity);
        var locked = PrefabActions.IsLocked(entity);
        DrawPrefabBar(entity);
        ImGui.BeginDisabled(insideInstance);
        DrawHeader(entity);
        ImGui.Spacing();
        if (entity.GetComponent<RectTransform>() is null)
        {
            DrawTransform(entity); // UI elements are placed by their RectTransform instead
        }

        ImGui.EndDisabled();

        Component? toRemove = null;
        var index = 0;
        foreach (var component in entity.Components)
        {
            if (component is Transform)
            {
                continue;
            }

            ImGui.PushID(index++);
            var componentLocked = locked && !(component is RectTransform && !insideInstance); // a UI root still moves
            if (!DrawComponent(component, entity.Scene, componentLocked))
            {
                toRemove = component;
            }

            ImGui.PopID();
        }

        if (toRemove is not null)
        {
            context.Execute(ComponentLifetimeCommand.Removing(toRemove));
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.BeginDisabled(locked);
        DrawAddComponent(entity);
        ImGui.EndDisabled();

        ImGui.PopID();
    }

    private void DrawHeader(Entity entity)
    {
        var active = entity.IsActive;
        if (ImGui.Checkbox("##active", ref active))
        {
            entity.IsActive = active;
            context.Record(new SetActiveCommand(entity, active));
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Active");
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        var before = entity.Name;
        var name = before;
        if (ImGui.InputText("##name", ref name, 128))
        {
            entity.Name = name;
            context.Record(new RenameEntityCommand(entity, before, name));
        }

        DrawTag(entity);
    }

    /// <summary>
    /// The Tag list: Untagged, the project's tags (each with an x to remove it from the project) and "Add Tag...".
    /// The project's tags are saved in its .lapis file.
    /// </summary>
    private void DrawTag(Entity entity)
    {
        var project = scripts.Project;
        var projectTags = project?.Tags ?? Projects.LapisProject.DefaultTags;
        List<string> tags = [Entity.Untagged, .. projectTags];
        if (!tags.Contains(entity.tag))
        {
            tags.Add(entity.tag); // set by a script, or the scene comes from another project
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Tag");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##tag", entity.tag))
        {
            var removeWidth = ImGui.GetFrameHeight();
            foreach (var tag in tags)
            {
                ImGui.PushID(tag);
                var removable = project is not null && projectTags.Contains(tag);
                var width = removable ? ImGui.GetContentRegionAvail().X - removeWidth - ImGui.GetStyle().ItemSpacing.X : 0;
                if (ImGui.Selectable(tag, entity.CompareTag(tag), ImGuiSelectableFlags.None, new Vector2(width, 0)) && !entity.CompareTag(tag))
                {
                    SetTag(entity, tag);
                }

                if (removable)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("x"))
                    {
                        _tagToRemove = tag;
                        ImGui.CloseCurrentPopup();
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Remove this tag from the project");
                    }
                }

                ImGui.PopID();
            }

            ImGui.Separator();
            if (ImGui.Selectable("Add Tag..."))
            {
                _newTag = string.Empty;
                _openAddTag = true;
            }

            ImGui.EndCombo();
        }

        DrawAddTagPopup(entity, project);
        DrawRemoveTagPopup(entity.Scene, project);
    }

    private void DrawAddTagPopup(Entity entity, Projects.LapisProject? project)
    {
        if (_openAddTag)
        {
            ImGui.OpenPopup(AddTagPopup);
            _openAddTag = false;
        }

        if (!ImGui.BeginPopupModal(AddTagPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }

        ImGui.TextUnformatted("New tag:");
        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(260);
        var entered = ImGui.InputText("##newTag", ref _newTag, 64, ImGuiInputTextFlags.EnterReturnsTrue);
        var newTag = _newTag.Trim();
        var valid = newTag.Length > 0 && newTag != Entity.Untagged;

        ImGui.BeginDisabled(!valid);
        if ((ImGui.Button("Add", new Vector2(120, 0)) || entered) && valid)
        {
            if (project?.AddTag(newTag) == true)
            {
                project.Save();
            }

            SetTag(entity, newTag);
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// <summary>Asks before removing a tag from the project; objects in the open scene that use it become Untagged.</summary>
    private void DrawRemoveTagPopup(Scene scene, Projects.LapisProject? project)
    {
        if (_tagToRemove is not null)
        {
            ImGui.OpenPopup(RemoveTagPopup);
            _removingTag = _tagToRemove;
            _tagToRemove = null;
        }

        if (!ImGui.BeginPopupModal(RemoveTagPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }

        var tag = _removingTag ?? string.Empty;
        var users = scene.AllEntities.Where(entity => entity.CompareTag(tag)).ToList();
        ImGui.TextUnformatted($"Remove the tag \"{tag}\" from the project?");
        ImGui.TextDisabled(users.Count == 0
            ? "No object in this scene uses it."
            : $"{users.Count} object(s) in this scene use it and become {Entity.Untagged} (Ctrl+Z gives the tag back to them).");

        if (ImGui.Button("Remove", new Vector2(120, 0)))
        {
            if (project?.RemoveTag(tag) == true)
            {
                project.Save();
            }

            if (users.Count > 0)
            {
                context.Execute(new CompositeCommand($"Remove tag {tag}", [.. users.Select(entity => (IEditorCommand)new SetTagCommand(entity, tag, Entity.Untagged))]));
            }

            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void SetTag(Entity entity, string tag)
    {
        var before = entity.tag;
        entity.tag = tag;
        context.Record(new SetTagCommand(entity, before, entity.tag));
    }

    private void DrawTransform(Entity entity)
    {
        if (!ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        var transform = entity.Transform;
        var before = TransformState.Of(transform);
        var changed = false;

        var position = transform.LocalPosition;
        if (PropertyGrid.Vector3("Position", ref position))
        {
            transform.LocalPosition = position;
            changed = true;
        }

        if (_eulerOwner != transform || _eulerSource != transform.LocalRotation)
        {
            _eulerOwner = transform;
            _eulerSource = transform.LocalRotation;
            _euler = transform.LocalEulerAngles;
        }

        if (PropertyGrid.Vector3("Rotation", ref _euler, 0.5f))
        {
            transform.LocalEulerAngles = _euler;
            _eulerSource = transform.LocalRotation;
            changed = true;
        }

        var scale = transform.LocalScale;
        if (PropertyGrid.Vector3("Scale", ref scale, 0.02f))
        {
            transform.LocalScale = scale;
            changed = true;
        }

        if (changed)
        {
            context.Record(new SetTransformCommand(entity, before, TransformState.Of(transform)));
        }

        DrawSetOrigin(entity);
        ImGui.Spacing();
    }

    /// <summary>"Set Origin" button: moves the entity's pivot to its centre, its bottom or back to the mesh's own pivot.</summary>
    private void DrawSetOrigin(Entity entity)
    {
        const string popup = "##setOrigin";
        ImGui.Spacing();
        if (ImGui.Button("Set Origin", new Vector2(-1, 0)))
        {
            ImGui.OpenPopup(popup);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Moves the pivot (where the gizmo is and what Position refers to) without moving the object.");
        }

        if (!ImGui.BeginPopup(popup))
        {
            return;
        }

        var hasMesh = OriginTool.LocalBounds(entity) is not null;
        if (!hasMesh)
        {
            ImGui.TextDisabled("Nothing to measure: no mesh on this entity or its children.");
            ImGui.Separator();
        }

        void Item(string label, OriginMode mode, string tooltip)
        {
            if (ImGui.MenuItem(label, string.Empty, false, OriginTool.CanSetOrigin(entity, mode)))
            {
                OriginTool.SetOrigin(context, entity, mode);
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(tooltip);
            }
        }

        Item("Center", OriginMode.Center, "The middle of the object (with its children).");
        Item("Bottom Center", OriginMode.BottomCenter, "Centred and at the bottom, so the object stands on its origin.");
        ImGui.Separator();
        Item("Reset to Mesh Pivot", OriginMode.MeshPivot, "Back to the pivot the mesh came with.");
        ImGui.EndPopup();
    }

    /// <summary>A menu of common anchors; the element stays where it is on screen.</summary>
    private void DrawAnchorPresets(RectTransform transform)
    {
        PropertyGrid.Label("Anchor Preset", "Pin the element to a corner, an edge or the centre of its parent, or stretch it with the parent. It stays where it is.");
        var current = SceneView.UIHandles.AnchorPresets.FirstOrDefault(preset => preset.Min == transform.AnchorMin && preset.Max == transform.AnchorMax).Name ?? "Custom";
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo("##anchorPreset", current))
        {
            return;
        }

        foreach (var (name, min, max) in SceneView.UIHandles.AnchorPresets)
        {
            if (ImGui.Selectable(name, name == current))
            {
                _uiHandles.ApplyAnchorPreset(transform, min, max);
            }
        }

        ImGui.EndCombo();
    }

    /// <returns>False when the user asked to remove the component.</returns>
    private bool DrawComponent(Component component, Scene scene, bool locked)
    {
        var keep = true;
        var title = component is MissingComponent missing
            ? $"Missing: {missing.TypeName}"
            : PropertyGrid.Nicify(component.GetType().Name);

        // A locked component (from a prefab) has no remove button, and its fields are shown but cannot be edited.
        var open = locked ? ImGui.CollapsingHeader(title, ImGuiTreeNodeFlags.DefaultOpen) : ImGui.CollapsingHeader(title, &keep, ImGuiTreeNodeFlags.DefaultOpen);
        if (!open)
        {
            return keep;
        }

        ImGui.BeginDisabled(locked);
        DrawComponentBody(component, scene);
        ImGui.EndDisabled();
        ImGui.Spacing();
        return keep;
    }

    /// <summary>For a prefab copy: which prefab it comes from, and Open / Unpack / Select buttons.</summary>
    private void DrawPrefabBar(Entity entity)
    {
        if (entity.PrefabInstanceRoot is not { } root)
        {
            return;
        }

        var asset = context.Assets?.Find(root.Prefab);
        var name = asset?.Name ?? "missing prefab";
        ImGui.TextColored(Theme.EditorTheme.Prefab, root == entity ? $"Prefab: {name}" : $"Part of the prefab copy '{root.Name}' ({name})");
        ImGui.PushTextWrapPos(0);
        ImGui.TextDisabled(root == entity
            ? "Its parts come from the prefab. Open the prefab to change every copy, or unpack this copy to change only it."
            : "Nothing here can be changed in the scene. Open the prefab to change every copy, or unpack this copy.");
        ImGui.PopTextWrapPos();

        var width = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2) / 3f;
        ImGui.BeginDisabled(asset is null);
        if (ImGui.Button("Open Prefab", new Vector2(width, 0)))
        {
            context.EnterPrefab(asset!);
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Unpack", new Vector2(width, 0)))
        {
            PrefabActions.Unpack(context, root);
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(asset is null);
        if (ImGui.Button("Select Asset", new Vector2(width, 0)))
        {
            context.SelectedAsset = asset;
        }

        ImGui.EndDisabled();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawComponentBody(Component component, Scene scene)
    {
        if (component is MissingComponent)
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(PropertyGrid.WarningColor, "This component's type could not be found. Its data is kept and saved unchanged.");
            ImGui.PopTextWrapPos();
            return;
        }

        if (scripts.FindScript(component.GetType()) is { } script)
        {
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

        if (component is RectTransform rectTransform)
        {
            DrawAnchorPresets(rectTransform);
        }

        if (component is Collider collider && SceneView.ColliderHandles.CanEdit(collider))
        {
            // Same switch as the Scene view's Collider tool (key 4): dots on the collider's sides to drag.
            var editing = context.EditingCollider;
            if (editing)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, Theme.EditorTheme.Accent);
            }

            if (ImGui.Button(editing ? "Editing Collider (drag the dots in the Scene view)##editCollider" : "Edit Collider##editCollider", new Vector2(-1, 0)))
            {
                context.EditingCollider = !editing;
            }

            if (editing)
            {
                ImGui.PopStyleColor();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Resize the collider by dragging the dots on its sides in the Scene view (4)");
            }
        }

        foreach (var member in SerializedMember.For(component.GetType()))
        {
            if (!member.IsShownIn(component))
            {
                continue;
            }

            var before = member.GetValue(component);
            var changed = component is Animator animator && member.Name == nameof(Animator.Clip)
                ? DrawClipChoice(animator)
                : PropertyGrid.Member(component, member, scene);
            if (changed)
            {
                context.Record(new SetMemberCommand(component, member, before, member.GetValue(component)));
            }
        }

        if (component is Animator previewed)
        {
            DrawAnimationPreview(previewed);
        }
    }

    /// <summary>The start animation as a list of the model's animations.</summary>
    private static bool DrawClipChoice(Animator animator)
    {
        var clips = animator.ClipNames;
        PropertyGrid.Label("Clip", "Animation played when the game starts.");
        if (clips.Count == 0)
        {
            ImGui.TextDisabled("The Mesh Renderer's model has no animations.");
            return false;
        }

        var current = animator.Clip.Length == 0 ? $"{clips[0]} (first)" : animator.Clip;
        var changed = false;
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

        return changed;
    }

    /// <summary>Buttons that play each animation in the Scene view without running the game.</summary>
    private void DrawAnimationPreview(Animator animator)
    {
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
                ImGui.PushStyleColor(ImGuiCol.Button, Theme.EditorTheme.Accent);
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

    private void DrawAddComponent(Entity entity)
    {
        var width = MathF.Min(240f, ImGui.GetContentRegionAvail().X);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X - width) * 0.5f);
        if (ImGui.Button("Add Component", new Vector2(width, 0)))
        {
            _componentFilter = string.Empty;
            ImGui.OpenPopup(AddComponentPopup);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Or drop a script from the Assets panel here.");
        }

        if (DragDrop.AcceptAsset(context.Assets) is { Type: Lapis.Assets.AssetType.Script } script)
        {
            EditorActions.AddScript(context, entity, script);
        }

        if (!ImGui.BeginPopup(AddComponentPopup))
        {
            return;
        }

        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##filter", "Search...", ref _componentFilter, 64);

        foreach (var type in ComponentCatalog.Types)
        {
            if (_componentFilter.Length > 0 && !type.Name.Contains(_componentFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ImGui.Selectable(PropertyGrid.Nicify(type.Name)))
            {
                context.History.BeginGesture();

                // UI parts need a RectTransform to be placed; add one first when the entity has none.
                var isUIPart = type == typeof(Image) || type == typeof(Text) || type == typeof(Button);
                var rect = isUIPart && entity.GetComponent<RectTransform>() is null ? entity.AddComponent<RectTransform>() : null;
                var component = entity.AddComponent(type);
                context.Record(rect is null
                    ? ComponentLifetimeCommand.Added(component)
                    : new CompositeCommand($"Add {type.Name}", [ComponentLifetimeCommand.Added(rect), ComponentLifetimeCommand.Added(component)]));
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.IsItemHovered() && type.Namespace is { } ns)
            {
                ImGui.SetTooltip(ns);
            }
        }

        ImGui.EndPopup();
    }
}
