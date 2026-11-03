using System.Numerics;
using System.Text.Json.Nodes;
using ModelContextProtocol;

namespace Lapis.Editor.Mcp;

/// <summary>MCP tools for the editor itself: selection, camera, undo, saving and opening scenes, Play mode.</summary>
internal sealed partial class McpEditorTools
{
    public string Select(string? entityReference, bool focus)
    {
        if (string.IsNullOrWhiteSpace(entityReference))
        {
            context.Selection = null;
            return "{\"selection\":null}";
        }

        var entity = ResolveEntity(entityReference);
        context.Selection = entity;
        if (focus && services.Camera is { } camera)
        {
            var scale = entity.Transform.LossyScale;
            camera.Focus(entity.Transform.Position, MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z)));
        }

        return new JsonObject { ["selection"] = Summary(entity) }.ToJsonString();
    }

    /// <summary>Points the Scene view's camera from <paramref name="position"/> at <paramref name="lookAt"/>.</summary>
    public string SetCamera(float[]? position, float[]? lookAt)
    {
        var camera = services.Camera ?? throw new McpException("The Scene view is not available.");
        var target = lookAt is null ? camera.Pivot : ToVector3(lookAt, nameof(lookAt));
        var from = position is null ? camera.Position : ToVector3(position, nameof(position));
        var offset = target - from;
        if (offset.LengthSquared() < 1e-6f)
        {
            throw new McpException("The camera position and the point it looks at must differ.");
        }

        var forward = Vector3.Normalize(offset);
        camera.Pivot = target;
        camera.Distance = offset.Length();
        camera.Pitch = MathF.Asin(forward.Y);
        camera.Yaw = MathF.Atan2(-forward.X, -forward.Z);
        return new JsonObject { ["position"] = Vector(camera.Position), ["lookAt"] = Vector(camera.Pivot) }.ToJsonString();
    }

    public string Undo(int steps) => Step(steps, undo: true);

    public string Redo(int steps) => Step(steps, undo: false);

    public string SaveScene(string? path)
    {
        RequireFileChanges();
        if (context.IsPlaying)
        {
            throw new McpException("Stop Play mode before saving.");
        }

        var target = string.IsNullOrWhiteSpace(path) ? null : AssetsPath(path);
        if (target is null && context.ScenePath is null)
        {
            throw new McpException("The scene has no file yet; give a path such as \"Scenes/Level.lscene\".");
        }

        if (!context.SaveScene(target))
        {
            throw new McpException("The scene could not be saved; see get_console.");
        }

        context.Assets?.Refresh();
        return new JsonObject { ["saved"] = ProjectRelative(context.ScenePath) }.ToJsonString();
    }

    public string OpenScene(string path, bool discardChanges)
    {
        if (context.IsPlaying)
        {
            throw new McpException("Stop Play mode before opening another scene.");
        }

        if (context.IsDirty && !discardChanges)
        {
            throw new McpException("The open scene has unsaved changes. Save it first, or pass discardChanges: true.");
        }

        var fullPath = AssetsPath(path);
        if (!context.OpenScene(fullPath))
        {
            throw new McpException($"'{path}' could not be opened; see get_console.");
        }

        return GetScene();
    }

    public string SetPlayMode(string action)
    {
        if (!settings.AllowPlayMode)
        {
            throw new McpException("Play mode control is turned off in the MCP tab of the Scene view.");
        }

        var play = services.Play ?? throw new McpException("Play mode is not available.");
        switch (action.Trim().ToLowerInvariant())
        {
            case "play":
                if (!play.IsActive)
                {
                    play.Play();
                }

                break;
            case "stop":
                play.Stop();
                break;
            case "pause":
                play.TogglePause();
                break;
            case "step":
                play.Step();
                break;
            default:
                throw new McpException("The action must be play, stop, pause or step.");
        }

        return new JsonObject { ["playMode"] = play.State.ToString() }.ToJsonString();
    }
}
