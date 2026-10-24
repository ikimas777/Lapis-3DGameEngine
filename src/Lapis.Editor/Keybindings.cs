using System.Globalization;
using Hexa.NET.ImGui;

namespace Lapis.Editor;

/// <summary>Editor commands that can be given keyboard shortcuts in Options > Keybindings.</summary>
internal enum EditorAction
{
    NewScene,
    OpenScene,
    SaveScene,
    SaveSceneAs,
    Undo,
    Redo,
    Duplicate,
    Delete,
    PlayStop,
    Pause,
    Step,
    FocusSelection,
    MoveTool,
    RotateTool,
    ScaleTool,
    FlyForward,
    FlyBack,
    FlyLeft,
    FlyRight,
    FlyUp,
    FlyDown,
    RenameFolder,
}

/// <summary>A key with the modifiers that must be held with it, e.g. Ctrl+Shift+S.</summary>
internal readonly record struct KeyChord(ImGuiKey Key, bool Ctrl = false, bool Shift = false, bool Alt = false)
{
    /// <summary>"Ctrl+Shift+S", as shown in menus and stored in the settings.</summary>
    public override string ToString()
    {
        var name = KeyName(Key);
        return $"{(Ctrl ? "Ctrl+" : string.Empty)}{(Shift ? "Shift+" : string.Empty)}{(Alt ? "Alt+" : string.Empty)}{name}";
    }

    /// <summary>Reads text written by <see cref="ToString"/>; null when it is not a valid shortcut.</summary>
    public static KeyChord? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        bool ctrl = false, shift = false, alt = false;
        foreach (var modifier in parts[..^1])
        {
            switch (modifier.ToLowerInvariant())
            {
                case "ctrl":
                    ctrl = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                default:
                    return null;
            }
        }

        return KeyFromName(parts[^1]) is { } key ? new KeyChord(key, ctrl, shift, alt) : null;
    }

    /// <summary>Keys a shortcut can use: everything ImGui names except the modifier keys themselves.</summary>
    public static IEnumerable<ImGuiKey> BindableKeys()
    {
        for (var key = ImGuiKey.NamedKeyBegin; key < ImGuiKey.NamedKeyEnd; key++)
        {
            if (!IsModifier(key) && key is not (ImGuiKey.MouseLeft or ImGuiKey.MouseRight or ImGuiKey.MouseMiddle or
                ImGuiKey.MouseX1 or ImGuiKey.MouseX2 or ImGuiKey.MouseWheelX or ImGuiKey.MouseWheelY) &&
                !key.ToString().StartsWith("Gamepad", StringComparison.Ordinal))
            {
                yield return key;
            }
        }
    }

    public static bool IsModifier(ImGuiKey key) => key is ImGuiKey.LeftCtrl or ImGuiKey.RightCtrl or ImGuiKey.LeftShift or
        ImGuiKey.RightShift or ImGuiKey.LeftAlt or ImGuiKey.RightAlt or ImGuiKey.LeftSuper or ImGuiKey.RightSuper or
        ImGuiKey.ModCtrl or ImGuiKey.ModShift or ImGuiKey.ModAlt or ImGuiKey.ModSuper or ImGuiKey.ReservedForModCtrl or
        ImGuiKey.ReservedForModShift or ImGuiKey.ReservedForModAlt or ImGuiKey.ReservedForModSuper;

    private static string KeyName(ImGuiKey key) => key is >= ImGuiKey.Key0 and <= ImGuiKey.Key9
        ? ((int)(key - ImGuiKey.Key0)).ToString(CultureInfo.InvariantCulture)
        : key.ToString();

    private static ImGuiKey? KeyFromName(string name)
    {
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            return ImGuiKey.Key0 + (name[0] - '0');
        }

        return Enum.TryParse<ImGuiKey>(name, ignoreCase: true, out var key) && BindableKeys().Contains(key) ? key : null;
    }
}

/// <summary>
/// The keyboard shortcuts of the editor's commands: the defaults, the user's changes (saved in the editor settings) and
/// checks for whether a command's shortcut was pressed this frame. Each command has up to two shortcuts.
/// </summary>
internal sealed class Keybindings
{
    /// <summary>How many shortcuts one command can have.</summary>
    public const int SlotCount = 2;

    private static readonly Dictionary<EditorAction, KeyChord[]> Defaults = new()
    {
        [EditorAction.NewScene] = [new(ImGuiKey.N, Ctrl: true)],
        [EditorAction.OpenScene] = [new(ImGuiKey.O, Ctrl: true)],
        [EditorAction.SaveScene] = [new(ImGuiKey.S, Ctrl: true)],
        [EditorAction.SaveSceneAs] = [new(ImGuiKey.S, Ctrl: true, Shift: true)],
        [EditorAction.Undo] = [new(ImGuiKey.Z, Ctrl: true)],
        [EditorAction.Redo] = [new(ImGuiKey.Y, Ctrl: true), new(ImGuiKey.Z, Ctrl: true, Shift: true)],
        [EditorAction.Duplicate] = [new(ImGuiKey.D, Ctrl: true)],
        [EditorAction.Delete] = [new(ImGuiKey.Delete)],
        [EditorAction.PlayStop] = [new(ImGuiKey.P, Ctrl: true)],
        [EditorAction.Pause] = [new(ImGuiKey.P, Ctrl: true, Shift: true)],
        [EditorAction.Step] = [new(ImGuiKey.P, Ctrl: true, Alt: true)],
        [EditorAction.FocusSelection] = [new(ImGuiKey.F)],
        [EditorAction.MoveTool] = [new(ImGuiKey.Key1)],
        [EditorAction.RotateTool] = [new(ImGuiKey.Key2)],
        [EditorAction.ScaleTool] = [new(ImGuiKey.Key3)],
        [EditorAction.FlyForward] = [new(ImGuiKey.W)],
        [EditorAction.FlyBack] = [new(ImGuiKey.S)],
        [EditorAction.FlyLeft] = [new(ImGuiKey.A)],
        [EditorAction.FlyRight] = [new(ImGuiKey.D)],
        [EditorAction.FlyUp] = [new(ImGuiKey.E)],
        [EditorAction.FlyDown] = [new(ImGuiKey.Q)],
        [EditorAction.RenameFolder] = [new(ImGuiKey.F2)],
    };

    private readonly Dictionary<EditorAction, KeyChord?[]> _bindings = [];

    public Keybindings() => RestoreDefaults();

    /// <summary>The binding being changed in Options > Keybindings; shortcuts are ignored while one is.</summary>
    public static bool IsCapturing { get; set; }

    /// <summary>Text shown for a command in the Options window and the menus.</summary>
    public static string Label(EditorAction action) => action switch
    {
        EditorAction.NewScene => "New scene",
        EditorAction.OpenScene => "Open scene",
        EditorAction.SaveScene => "Save scene",
        EditorAction.SaveSceneAs => "Save scene as",
        EditorAction.Undo => "Undo",
        EditorAction.Redo => "Redo",
        EditorAction.Duplicate => "Duplicate the selection",
        EditorAction.Delete => "Delete the selection",
        EditorAction.PlayStop => "Play or stop",
        EditorAction.Pause => "Pause",
        EditorAction.Step => "Step one frame",
        EditorAction.FocusSelection => "Focus the selection (Scene view)",
        EditorAction.MoveTool => "Move tool (Scene view)",
        EditorAction.RotateTool => "Rotate tool (Scene view)",
        EditorAction.ScaleTool => "Scale tool (Scene view)",
        EditorAction.FlyForward => "Fly forward",
        EditorAction.FlyBack => "Fly back",
        EditorAction.FlyLeft => "Fly left",
        EditorAction.FlyRight => "Fly right",
        EditorAction.FlyUp => "Fly up",
        EditorAction.FlyDown => "Fly down",
        EditorAction.RenameFolder => "Rename entity or folder (Hierarchy)",
        _ => action.ToString(),
    };

    /// <summary>The fly keys are held (Shift goes faster), so they are bound to a plain key without modifiers.</summary>
    public static bool IsHeldKey(EditorAction action) => action is >= EditorAction.FlyForward and <= EditorAction.FlyDown;

    /// <summary>The shortcut in a slot (0 or 1), or null when that slot is empty.</summary>
    public KeyChord? Get(EditorAction action, int slot) => _bindings[action][slot];

    public void Set(EditorAction action, int slot, KeyChord? chord) => _bindings[action][slot] = chord;

    /// <summary>The first shortcut as text for menus ("Ctrl+Z"), or an empty string.</summary>
    public string Shortcut(EditorAction action) => _bindings[action].FirstOrDefault(chord => chord is not null)?.ToString() ?? string.Empty;

    public bool IsDefault(EditorAction action) => Matches(_bindings[action], DefaultSlots(action));

    public void RestoreDefault(EditorAction action) => _bindings[action] = DefaultSlots(action);

    public void RestoreDefaults()
    {
        foreach (var action in Enum.GetValues<EditorAction>())
        {
            RestoreDefault(action);
        }
    }

    /// <summary>Other commands that use <paramref name="chord"/> too (they would both run).</summary>
    public IEnumerable<EditorAction> Conflicts(EditorAction action, KeyChord chord) =>
        _bindings.Where(pair => pair.Key != action && pair.Value.Contains(chord)).Select(pair => pair.Key);

    /// <summary>True when one of the command's shortcuts was pressed this frame (with exactly its modifiers).</summary>
    public bool Pressed(EditorAction action)
    {
        if (IsCapturing)
        {
            return false;
        }

        var io = ImGui.GetIO();
        foreach (var chord in _bindings[action])
        {
            if (chord is { } c && ImGui.IsKeyPressed(c.Key, false) && c.Ctrl == io.KeyCtrl && c.Shift == io.KeyShift && c.Alt == io.KeyAlt)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True while one of the command's keys is held (modifiers are ignored).</summary>
    public bool Down(EditorAction action) =>
        !IsCapturing && _bindings[action].Any(chord => chord is { } c && ImGui.IsKeyDown(c.Key));

    /// <summary>The user's changes, as stored in the settings file: command name to "Ctrl+Z, Ctrl+Shift+Z".</summary>
    public Dictionary<string, string> ToSettings()
    {
        var changed = new Dictionary<string, string>();
        foreach (var (action, slots) in _bindings)
        {
            if (!IsDefault(action))
            {
                changed[action.ToString()] = string.Join(", ", slots.Select(chord => chord?.ToString() ?? string.Empty));
            }
        }

        return changed;
    }

    /// <summary>Applies changes read from the settings file; unknown commands and shortcuts are ignored.</summary>
    public void Load(Dictionary<string, string>? settings)
    {
        RestoreDefaults();
        foreach (var (name, value) in settings ?? [])
        {
            if (!Enum.TryParse<EditorAction>(name, out var action))
            {
                continue;
            }

            var parts = value.Split(',', StringSplitOptions.TrimEntries);
            var slots = new KeyChord?[SlotCount];
            for (var i = 0; i < Math.Min(parts.Length, SlotCount); i++)
            {
                slots[i] = KeyChord.Parse(parts[i]);
            }

            _bindings[action] = slots;
        }
    }

    private static KeyChord?[] DefaultSlots(EditorAction action)
    {
        var slots = new KeyChord?[SlotCount];
        var defaults = Defaults[action];
        for (var i = 0; i < defaults.Length; i++)
        {
            slots[i] = defaults[i];
        }

        return slots;
    }

    private static bool Matches(KeyChord?[] a, KeyChord?[] b) => a.SequenceEqual(b);
}
