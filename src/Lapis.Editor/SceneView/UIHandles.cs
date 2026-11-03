using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Commands;
using Lapis.Serialization;

namespace Lapis.Editor.SceneView;

/// <summary>
/// UI editing in the Scene view, shown while a UI element or canvas is selected: the game screen (at the Game view's
/// size) fitted into the view with the UI drawn in it, outlines of the elements, and handles on the selected element.
/// Click an element to select it, drag it to move it, drag its edges or corners to resize it. A drag is one undo step.
/// </summary>
internal sealed class UIHandles(EditorContext context)
{
    private const float HandleSize = 8f;
    private const float GrabDistance = 7f;

    private static readonly uint FrameColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.55f));
    private static readonly uint OutlineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.22f));
    private static readonly uint SelectedColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 0.65f, 1f, 1f));
    private static readonly uint HandleFill = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));

    private Drag? _drag;
    private bool _hovering;

    /// <summary>True while the mouse is over an element or dragging, so the view does not pick 3D objects.</summary>
    public bool IsBusy => _hovering || _drag is not null;

    /// <summary>True when the entity is a canvas or a UI element inside one.</summary>
    public static bool IsUI(Entity? entity) =>
        entity is not null && UILayout.CanvasOf(entity) is not null && (entity.GetComponent<RectTransform>() is not null || entity.GetComponent<Canvas>() is not null);

    /// <summary>
    /// Where the game screen of <paramref name="screenSize"/> pixels sits in a view of <paramref name="viewSize"/>
    /// points: the offset of its top-left corner and the points per screen pixel.
    /// </summary>
    public static (Vector2 Offset, float Scale) Fit(Vector2 viewSize, Vector2 screenSize)
    {
        const float Margin = 24f;
        var available = Vector2.Max(viewSize - new Vector2(Margin * 2f), Vector2.One);
        var scale = MathF.Min(available.X / screenSize.X, available.Y / screenSize.Y);
        return ((viewSize - screenSize * scale) * 0.5f, scale);
    }

    /// <summary>The handle under <paramref name="point"/>: -1, 0 or 1 per axis for edges and corners; null for none.</summary>
    internal static (int X, int Y)? HandleAt(Rect rect, Vector2 point, float grab)
    {
        foreach (var (x, y) in Handles)
        {
            if (Vector2.Distance(HandlePosition(rect, x, y), point) <= grab)
            {
                return (x, y);
            }
        }

        return null;
    }

    /// <summary>
    /// Position and size after dragging handle (<paramref name="handleX"/>, <paramref name="handleY"/>) by
    /// <paramref name="delta"/> canvas units; (0, 0) moves the whole element. The opposite edge stays where it is.
    /// </summary>
    internal static (Vector2 Position, Vector2 Size) Resize(Vector2 position, Vector2 size, Vector2 pivot, int handleX, int handleY, Vector2 delta)
    {
        if (handleX == 0 && handleY == 0)
        {
            return (position + delta, size);
        }

        (position.X, size.X) = ResizeAxis(position.X, size.X, pivot.X, handleX, delta.X);
        (position.Y, size.Y) = ResizeAxis(position.Y, size.Y, pivot.Y, handleY, delta.Y);
        return (position, size);
    }

    /// <summary>The anchor presets offered in the Inspector: name, anchor min and anchor max.</summary>
    public static readonly (string Name, Vector2 Min, Vector2 Max)[] AnchorPresets =
    [
        ("Top Left", new(0f, 0f), new(0f, 0f)),
        ("Top", new(0.5f, 0f), new(0.5f, 0f)),
        ("Top Right", new(1f, 0f), new(1f, 0f)),
        ("Left", new(0f, 0.5f), new(0f, 0.5f)),
        ("Center", new(0.5f, 0.5f), new(0.5f, 0.5f)),
        ("Right", new(1f, 0.5f), new(1f, 0.5f)),
        ("Bottom Left", new(0f, 1f), new(0f, 1f)),
        ("Bottom", new(0.5f, 1f), new(0.5f, 1f)),
        ("Bottom Right", new(1f, 1f), new(1f, 1f)),
        ("Stretch Across (Top)", new(0f, 0f), new(1f, 0f)),
        ("Stretch Across (Middle)", new(0f, 0.5f), new(1f, 0.5f)),
        ("Stretch Across (Bottom)", new(0f, 1f), new(1f, 1f)),
        ("Stretch Down (Left)", new(0f, 0f), new(0f, 1f)),
        ("Stretch Down (Center)", new(0.5f, 0f), new(0.5f, 1f)),
        ("Stretch Down (Right)", new(1f, 0f), new(1f, 1f)),
        ("Stretch Both", new(0f, 0f), new(1f, 1f)),
    ];

    /// <summary>
    /// Values that give an element new anchors (and the pivot at the anchor point, or the centre for stretched axes)
    /// while it stays where it is: <paramref name="rect"/> is the element and <paramref name="parent"/> its parent's
    /// rectangle, both in canvas units.
    /// </summary>
    internal static (Vector2 Pivot, Vector2 Position, Vector2 Size) Reanchor(Rect rect, Rect parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var pivot = new Vector2(anchorMin.X == anchorMax.X ? anchorMin.X : 0.5f, anchorMin.Y == anchorMax.Y ? anchorMin.Y : 0.5f);
        var size = rect.Size - (anchorMax - anchorMin) * parent.Size;
        var position = rect.Min + pivot * rect.Size - (parent.Min + Vector2.Lerp(anchorMin, anchorMax, pivot) * parent.Size);
        return (pivot, Round(position), Round(size));
    }

    /// <summary>Applies an anchor preset to <paramref name="transform"/>, keeping it in place (one undo step).</summary>
    public void ApplyAnchorPreset(RectTransform transform, Vector2 anchorMin, Vector2 anchorMax)
    {
        var element = UILayout.Build(context.Scene, context.GameViewSize).FirstOrDefault(e => e.Transform == transform);
        var commands = new List<IEditorCommand>();
        var values = new Dictionary<string, object> { [nameof(RectTransform.AnchorMin)] = anchorMin, [nameof(RectTransform.AnchorMax)] = anchorMax };
        if (element.Transform is not null)
        {
            var ppu = element.PixelsPerUnit;
            var rect = Scale(element.ScreenRect, 1f / ppu);
            var parent = transform.entity.Parent?.GetComponent<RectTransform>() is { } parentTransform
                ? Scale(parentTransform.ScreenRect, 1f / ppu)
                : new Rect(0f, 0f, context.GameViewSize.X / ppu, context.GameViewSize.Y / ppu);
            var (pivot, position, size) = Reanchor(rect, parent, anchorMin, anchorMax);
            values[nameof(RectTransform.Pivot)] = pivot;
            values[nameof(RectTransform.Position)] = position;
            values[nameof(RectTransform.Size)] = size;
        }

        foreach (var member in SerializedMember.For(typeof(RectTransform)))
        {
            if (values.TryGetValue(member.Name, out var value) && !Equals(member.GetValue(transform), value))
            {
                var before = member.GetValue(transform);
                member.SetValue(transform, value);
                commands.Add(new SetMemberCommand(transform, member, before, value));
            }
        }

        if (commands.Count > 0)
        {
            context.History.BeginGesture();
            context.Record(new CompositeCommand($"Anchor {transform.entity.Name}", commands));
        }
    }

    private static Rect Scale(Rect rect, float factor) => new(rect.X * factor, rect.Y * factor, rect.Width * factor, rect.Height * factor);

    /// <summary>Renders the UI preview into the Scene view's framebuffer (bound), sized <paramref name="framebufferSize"/> pixels.</summary>
    public void RenderPreview(Rendering.SceneRenderer renderer, Vector2 viewSize, Vector2 framebufferSize)
    {
        var screen = context.GameViewSize;
        var (offset, scale) = Fit(viewSize, screen);
        var pixelsPerPoint = framebufferSize.X / MathF.Max(viewSize.X, 1f);
        var size = screen * scale * pixelsPerPoint;
        var left = offset.X * pixelsPerPoint;
        var bottom = framebufferSize.Y - (offset.Y * pixelsPerPoint + size.Y); // OpenGL counts from the bottom
        renderer.RenderUI(context.Scene, screen, new Rect(left, bottom, size.X, size.Y));
    }

    /// <summary>Draws the outlines and handles and handles the mouse.</summary>
    /// <param name="origin">Top-left corner of the view on screen.</param>
    /// <param name="viewSize">Size of the view on screen.</param>
    /// <param name="mouseInView">Whether the mouse is over the view (and not over its toolbar).</param>
    public void Draw(Vector2 origin, Vector2 viewSize, bool mouseInView)
    {
        var screen = context.GameViewSize;
        var (offset, scale) = Fit(viewSize, screen);
        var topLeft = origin + offset;
        var elements = UILayout.Build(context.Scene, screen);
        var drawList = ImGui.GetWindowDrawList();
        var mouse = ImGui.GetMousePos();

        Rect ToView(Rect rect) => new(topLeft.X + rect.X * scale, topLeft.Y + rect.Y * scale, rect.Width * scale, rect.Height * scale);

        drawList.AddRect(topLeft, topLeft + screen * scale, FrameColor, 0f, ImDrawFlags.None, 1.5f);
        var label = $"Game screen  {screen.X:0} x {screen.Y:0}";
        drawList.AddText(topLeft + new Vector2(0f, -ImGui.GetTextLineHeight() - 3f), FrameColor, label);

        UIElement? selected = null;
        foreach (var element in elements)
        {
            var rect = ToView(element.ScreenRect);
            if (element.Entity == context.Selection)
            {
                selected = element;
            }
            else
            {
                drawList.AddRect(rect.Min, rect.Max, OutlineColor);
            }
        }

        if (_drag is { } drag)
        {
            if (!drag.Transform.IsAttached || !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                Finish();
            }
            else
            {
                var delta = (mouse - drag.StartMouse) / (scale * drag.PixelsPerUnit);
                var (position, size) = Resize(drag.StartPosition, drag.StartSize, drag.Transform.Pivot, drag.HandleX, drag.HandleY, delta);
                drag.Transform.Position = Round(position);
                drag.Transform.Size = Round(size);
            }
        }

        _hovering = false;
        if (selected is { } current)
        {
            var rect = ToView(current.ScreenRect);
            var handle = _drag is { } active ? (active.HandleX, active.HandleY) : mouseInView ? HandleAt(rect, mouse, GrabDistance) : null;
            _hovering = handle is not null;
            if (handle is { } h && (h.Item1 != 0 || h.Item2 != 0))
            {
                ImGui.SetMouseCursor(h.Item1 == 0 ? ImGuiMouseCursor.ResizeNs : h.Item2 == 0 ? ImGuiMouseCursor.ResizeEw
                    : h.Item1 == h.Item2 ? ImGuiMouseCursor.ResizeNwse : ImGuiMouseCursor.ResizeNesw);
            }

            if (_drag is null && handle is { } grabbed && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                Begin(current, grabbed.Item1, grabbed.Item2, mouse);
            }

            DrawSelection(drawList, rect);
        }

        if (_drag is not null || !mouseInView)
        {
            return;
        }

        // The topmost element under the mouse is selected by a click, and can be dragged right away.
        var screenPoint = (mouse - topLeft) / scale;
        UIElement? under = null;
        for (var i = elements.Count - 1; i >= 0; i--)
        {
            if (elements[i].ScreenRect.Contains(screenPoint))
            {
                under = elements[i];
                break;
            }
        }

        if (under is not { } target)
        {
            return;
        }

        _hovering = true;
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (target.Entity != context.Selection)
            {
                context.Selection = target.Entity;
            }

            Begin(target, 0, 0, mouse);
        }
    }

    /// <summary>Ends a drag, recording it for undo.</summary>
    public void Finish()
    {
        if (_drag is not { } drag)
        {
            return;
        }

        _drag = null;
        if (!drag.Transform.IsAttached)
        {
            return;
        }

        var commands = new List<IEditorCommand>();
        foreach (var member in SerializedMember.For(typeof(RectTransform)))
        {
            object? before = member.Name switch
            {
                nameof(RectTransform.Position) => drag.StartPosition,
                nameof(RectTransform.Size) => drag.StartSize,
                _ => null,
            };

            var now = member.GetValue(drag.Transform);
            if (before is not null && !Equals(before, now))
            {
                commands.Add(new SetMemberCommand(drag.Transform, member, before, now));
            }
        }

        if (commands.Count > 0)
        {
            var verb = drag.HandleX == 0 && drag.HandleY == 0 ? "Move" : "Resize";
            context.History.BeginGesture();
            context.Record(commands.Count == 1 ? commands[0] : new CompositeCommand($"{verb} {drag.Transform.entity.Name}", commands));
        }
    }

    private static readonly (int X, int Y)[] Handles =
        [(-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0)];

    private static Vector2 HandlePosition(Rect rect, int x, int y) => rect.Center + new Vector2(x * rect.Width, y * rect.Height) * 0.5f;

    private static (float Position, float Size) ResizeAxis(float position, float size, float pivot, int handle, float delta) => handle switch
    {
        1 => (position + pivot * delta, size + delta),          // far edge moves, near edge stays
        -1 => (position + (1f - pivot) * delta, size - delta),  // near edge moves, far edge stays
        _ => (position, size),
    };

    private static Vector2 Round(Vector2 value) => new(MathF.Round(value.X), MathF.Round(value.Y));

    private void Begin(UIElement element, int handleX, int handleY, Vector2 mouse) =>
        _drag = new Drag(element.Transform, handleX, handleY, mouse, element.Transform.Position, element.Transform.Size, element.PixelsPerUnit);

    private static void DrawSelection(ImDrawListPtr drawList, Rect rect)
    {
        drawList.AddRect(rect.Min, rect.Max, SelectedColor, 0f, ImDrawFlags.None, 2f);
        var half = new Vector2(HandleSize * 0.5f);
        foreach (var (x, y) in Handles)
        {
            var center = HandlePosition(rect, x, y);
            drawList.AddRectFilled(center - half, center + half, HandleFill);
            drawList.AddRect(center - half, center + half, SelectedColor);
        }
    }

    private sealed record Drag(RectTransform Transform, int HandleX, int HandleY, Vector2 StartMouse, Vector2 StartPosition, Vector2 StartSize, float PixelsPerUnit);
}
