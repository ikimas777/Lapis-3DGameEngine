using System.Globalization;
using System.Numerics;
using System.Text;
using Hexa.NET.ImGui;
using Lapis.Editor.Panels;
using Lapis.Editor.Theme;
using Lapis.Serialization;

namespace Lapis.Editor.PropertyDrawers;

/// <summary>
/// Draws labelled editors for values in two columns (label | widget), as used by the Inspector.
/// Every method returns true when the user changed the value.
/// </summary>
internal static unsafe class PropertyGrid
{
    private const float MaxLabelWidth = 140f;

    /// <summary>Draws an editor for a serialized member of <paramref name="target"/> and writes changes back.</summary>
    public static bool Member(object target, SerializedMember member, Scene scene)
    {
        var label = Nicify(member.Name);
        var tooltip = member.GetAttribute<TooltipAttribute>()?.Text;
        var range = member.GetAttribute<RangeAttribute>();
        var type = member.ValueType;
        var value = member.GetValue(target);

        ImGui.PushID(member.Key);
        var changed = false;
        object? newValue = value;

        switch (value)
        {
            case float f:
                changed = range is null ? Float(label, ref f, tooltip: tooltip) : Slider(label, ref f, range.Min, range.Max, tooltip);
                newValue = f;
                break;
            case double d:
                var asFloat = (float)d;
                changed = Float(label, ref asFloat, tooltip: tooltip);
                newValue = (double)asFloat;
                break;
            case int i:
                changed = Int(label, ref i, range, tooltip);
                newValue = i;
                break;
            case bool b:
                changed = Bool(label, ref b, tooltip);
                newValue = b;
                break;
            case Vector2 v2:
                changed = Vector2(label, ref v2, tooltip);
                newValue = v2;
                break;
            case Vector3 v3:
                changed = Vector3(label, ref v3, tooltip: tooltip);
                newValue = v3;
                break;
            case Vector4 v4:
                changed = Vector4(label, ref v4, tooltip);
                newValue = v4;
                break;
            case Quaternion q:
                var euler = MathUtil.ToEulerDegrees(q);
                changed = Vector3(label, ref euler, 0.5f, tooltip);
                newValue = MathUtil.FromEulerDegrees(euler);
                break;
            case Color c:
                changed = Color(label, ref c, tooltip);
                newValue = c;
                break;
            case Enum e:
                changed = EnumCombo(label, ref e, tooltip);
                newValue = e;
                break;
            default:
                if (type == typeof(string) && member.GetAttribute<Lapis.Assets.AssetReferenceAttribute>() is { } assetReference)
                {
                    var reference = (string?)value ?? string.Empty;
                    changed = AssetSlot(label, ref reference, assetReference.Type, tooltip);
                    newValue = reference;
                }
                else if (type == typeof(string))
                {
                    var text = (string?)value ?? string.Empty;
                    changed = Text(label, ref text, tooltip);
                    newValue = text;
                }
                else if (type == typeof(Entity) || typeof(Component).IsAssignableFrom(type))
                {
                    changed = Reference(label, type, ref newValue, scene, tooltip);
                }
                else
                {
                    Label(label, tooltip);
                    ImGui.TextDisabled($"({type.Name} cannot be edited yet)");
                }

                break;
        }

        if (changed)
        {
            member.SetValue(target, newValue);
        }

        ImGui.PopID();
        return changed;
    }

    public static bool Float(string label, ref float value, float speed = 0.05f, string? tooltip = null)
    {
        Label(label, tooltip);
        return ImGui.DragFloat($"##{label}", ref value, speed);
    }

    public static bool Slider(string label, ref float value, float min, float max, string? tooltip = null)
    {
        Label(label, tooltip);
        fixed (float* pointer = &value)
        {
            return ImGui.SliderFloat($"##{label}", pointer, min, max);
        }
    }

    public static bool Int(string label, ref int value, RangeAttribute? range = null, string? tooltip = null)
    {
        Label(label, tooltip);
        return range is null
            ? ImGui.DragInt($"##{label}", ref value, 0.2f)
            : ImGui.DragInt($"##{label}", ref value, 0.2f, (int)range.Min, (int)range.Max);
    }

    public static bool Bool(string label, ref bool value, string? tooltip = null)
    {
        Label(label, tooltip);
        return ImGui.Checkbox($"##{label}", ref value);
    }

    public static bool Text(string label, ref string value, string? tooltip = null)
    {
        Label(label, tooltip);
        return ImGui.InputText($"##{label}", ref value, 256);
    }

    public static bool Vector2(string label, ref Vector2 value, string? tooltip = null)
    {
        Label(label, tooltip);
        fixed (Vector2* pointer = &value)
        {
            return ImGui.DragFloat2($"##{label}", (float*)pointer, 0.05f);
        }
    }

    public static bool Vector3(string label, ref Vector3 value, float speed = 0.05f, string? tooltip = null)
    {
        Label(label, tooltip);
        fixed (Vector3* pointer = &value)
        {
            return ImGui.DragFloat3($"##{label}", (float*)pointer, speed);
        }
    }

    public static bool Vector4(string label, ref Vector4 value, string? tooltip = null)
    {
        Label(label, tooltip);
        fixed (Vector4* pointer = &value)
        {
            return ImGui.DragFloat4($"##{label}", (float*)pointer, 0.05f);
        }
    }

    public static bool Color(string label, ref Color value, string? tooltip = null)
    {
        Label(label, tooltip);
        var vector = value.ToVector4();
        if (!ImGui.ColorEdit4($"##{label}", ref vector, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.AlphaPreviewHalf))
        {
            return false;
        }

        value = new Color(vector.X, vector.Y, vector.Z, vector.W);
        return true;
    }

    public static bool EnumCombo(string label, ref Enum value, string? tooltip = null)
    {
        Label(label, tooltip);
        var changed = false;
        if (ImGui.BeginCombo($"##{label}", Nicify(value.ToString())))
        {
            foreach (Enum option in Enum.GetValues(value.GetType()))
            {
                var selected = option.Equals(value);
                if (ImGui.Selectable(Nicify(option.ToString()), selected))
                {
                    changed = !selected;
                    value = option;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    /// <summary>
    /// A field that refers to an entity or component. Assign by dropping an entity from the
    /// Hierarchy or by clicking it and searching the scene; clear with the right-click menu.
    /// </summary>
    public static bool Reference(string label, Type type, ref object? value, Scene scene, string? tooltip = null)
    {
        Label(label, tooltip);

        var display = value switch
        {
            Entity entity => entity.Name,
            Component component when component.IsAttached => $"{component.entity.Name} ({type.Name})",
            _ => $"None ({type.Name})",
        };

        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
        var open = ImGui.Button($"{display}##reference", new Vector2(-1, 0));
        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Drag an entity from the Hierarchy here, or click to search the scene. Right-click to clear.");
        }

        var changed = false;
        if (DragDrop.AcceptEntity(scene) is { } dropped)
        {
            object? resolved = type == typeof(Entity) ? dropped : dropped.GetComponent(type);
            if (resolved is null)
            {
                Log.Warning($"'{dropped.Name}' has no {type.Name}.");
            }
            else
            {
                value = resolved;
                changed = true;
            }
        }

        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Clear"))
            {
                value = null;
                changed = true;
            }

            ImGui.EndPopup();
        }

        if (open)
        {
            ImGui.OpenPopup("##pickEntity");
        }

        if (BeginPicker("##pickEntity"))
        {
            if (ImGui.Selectable("None"))
            {
                value = null;
                changed = true;
            }

            foreach (var entity in scene.AllEntities)
            {
                object? candidate = type == typeof(Entity) ? entity : entity.GetComponent(type);
                if (candidate is null || !MatchesPicker(entity.Name))
                {
                    continue;
                }

                var current = value is Entity e ? e == entity : value is Component c && c.IsAttached && c.entity == entity;
                if (ImGui.Selectable($"{entity.Name}##{entity.Id}", current))
                {
                    value = candidate;
                    changed = true;
                }

                if (ImGui.IsItemHovered() && entity.Parent is not null)
                {
                    ImGui.SetTooltip(EntityPath(entity));
                }
            }

            EndPicker(close: changed);
        }

        return changed;
    }

    private static string _pickerFilter = string.Empty;

    /// <summary>
    /// Opens a choose-from-list popup with a search field at the top (focused when it opens). Filter the items with
    /// <see cref="MatchesPicker"/>; call <see cref="EndPicker"/> when this returns true.
    /// </summary>
    private static bool BeginPicker(string id)
    {
        if (!ImGui.BeginPopup(id))
        {
            return false;
        }

        if (ImGui.IsWindowAppearing())
        {
            _pickerFilter = string.Empty;
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##search", "Search...", ref _pickerFilter, 128);
        ImGui.BeginChild("##items", new Vector2(260, 300));
        return true;
    }

    /// <param name="close">True when an item was chosen.</param>
    private static void EndPicker(bool close)
    {
        ImGui.EndChild();
        if (close)
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private static bool MatchesPicker(string name) =>
        _pickerFilter.Length == 0 || name.Contains(_pickerFilter.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>"Parent/Child/Entity", shown so entities with the same name can be told apart.</summary>
    private static string EntityPath(Entity entity) =>
        entity.Parent is { } parent ? $"{EntityPath(parent)}/{entity.Name}" : entity.Name;

    /// <summary>
    /// A slot holding an asset reference. Accepts assets dragged from the Assets panel; clicking it opens a
    /// list of matching assets (plus the built-in shapes for meshes); right-click clears it.
    /// </summary>
    public static bool AssetSlot(string label, ref string reference, Lapis.Assets.AssetType type, string? tooltip = null)
    {
        Label(label, tooltip);
        var assets = Lapis.Assets.AssetDatabase.Active;

        var display = reference.Length == 0
            ? $"None ({type})"
            : reference.StartsWith("builtin:", StringComparison.Ordinal)
                ? $"{Nicify(reference["builtin:".Length..])} (built-in)"
                : assets?.Find(reference) is { } found
                    ? Path.GetFileName(found.FullPath) + (Lapis.Assets.AssetDatabase.TryParseReference(reference, out _, out var part) && part is not null ? $" #{part}" : string.Empty)
                    : "Missing asset";

        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
        var open = ImGui.Button($"{display}##slot", new Vector2(-1, 0));
        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Drag an asset here from the Assets panel, or click to choose. Right-click to clear.");
        }

        var changed = false;
        if (DragDrop.AcceptAsset(assets) is { } dropped)
        {
            if (dropped.Type == type)
            {
                reference = dropped.Reference;
                changed = true;
            }
            else
            {
                Log.Warning($"'{Path.GetFileName(dropped.FullPath)}' is a {dropped.Type}, but this field needs a {type}.");
            }
        }

        if (open)
        {
            ImGui.OpenPopup("##choose");
        }

        if (BeginPicker("##choose"))
        {
            if (ImGui.Selectable("None"))
            {
                reference = string.Empty;
                changed = true;
            }

            if (type == Lapis.Assets.AssetType.Model)
            {
                foreach (var builtIn in BuiltInMeshes.All)
                {
                    var name = $"{Nicify(builtIn["builtin:".Length..])} (built-in)";
                    if (MatchesPicker(name) && ImGui.Selectable(name, builtIn == reference))
                    {
                        reference = builtIn;
                        changed = true;
                    }
                }
            }

            if (assets is not null)
            {
                foreach (var asset in assets.Assets.Where(a => a.Type == type && MatchesPicker(Path.GetFileName(a.FullPath)))
                    .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (ImGui.Selectable($"{Path.GetFileName(asset.FullPath)}##{asset.Id}", asset.Reference == reference))
                    {
                        reference = asset.Reference;
                        changed = true;
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(Path.GetRelativePath(assets.RootDirectory, asset.FullPath));
                    }
                }
            }

            EndPicker(close: changed);
        }

        if (ImGui.BeginPopupContextItem("##slotContext"))
        {
            if (ImGui.MenuItem("Clear"))
            {
                reference = string.Empty;
                changed = true;
            }

            ImGui.EndPopup();
        }

        return changed;
    }

    /// <summary>Draws the label column and prepares the widget column.</summary>
    public static void Label(string label, string? tooltip = null)
    {
        var labelWidth = MathF.Min(MaxLabelWidth, ImGui.GetContentRegionAvail().X * 0.4f);
        var start = ImGui.GetCursorPosX();

        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.Text] * new Vector4(0.85f, 0.85f, 0.85f, 1f));
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        if (tooltip is not null && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        ImGui.SameLine(start + labelWidth);
        ImGui.SetNextItemWidth(-1);
    }

    /// <summary>Turns <c>fieldOfView</c>, <c>FieldOfView</c> or <c>_fieldOfView</c> into "Field Of View".</summary>
    public static string Nicify(string name)
    {
        var trimmed = name.TrimStart('_');
        var builder = new StringBuilder(trimmed.Length + 8);
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (i == 0)
            {
                builder.Append(char.ToUpper(c, CultureInfo.InvariantCulture));
                continue;
            }

            var previous = trimmed[i - 1];
            var startsWord = char.IsUpper(c) && (char.IsLower(previous) || (i + 1 < trimmed.Length && char.IsLower(trimmed[i + 1]) && char.IsUpper(previous)));
            var startsNumber = char.IsDigit(c) && !char.IsDigit(previous);
            if (startsWord || startsNumber)
            {
                builder.Append(' ');
            }

            builder.Append(c == '_' ? ' ' : c);
        }

        return builder.ToString();
    }

    /// <summary>Colour for warning text in property sections.</summary>
    public static Vector4 WarningColor => EditorTheme.Warning;
}
