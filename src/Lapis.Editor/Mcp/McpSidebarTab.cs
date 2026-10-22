using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Panels;
using Lapis.Editor.Theme;

namespace Lapis.Editor.Mcp;

/// <summary>
/// The MCP page of the Scene view's sidebar: starts and stops the server, shows how to connect an assistant, which
/// kinds of changes it may make, and its latest tool calls.
/// </summary>
internal sealed class McpSidebarTab(McpServerHost server, McpSettings settings, McpActivityLog activity, Action saveSettings) : ISidebarTab
{
    private static readonly Vector4 Green = new(0.42f, 0.78f, 0.42f, 1f);

    public string Title => "MCP";

    public void Draw()
    {
        DrawStatus();
        ImGui.Spacing();
        DrawConnect();
        ImGui.Spacing();
        DrawPermissions();
        ImGui.Spacing();
        DrawActivity();
    }

    private void DrawStatus()
    {
        var (color, text) = server.State switch
        {
            McpServerState.Running => (Green, "Running"),
            McpServerState.Starting => (EditorTheme.Warning, "Starting..."),
            McpServerState.Failed => (EditorTheme.Error, "Could not start"),
            _ => (EditorTheme.TextDim, "Stopped"),
        };

        var drawList = ImGui.GetWindowDrawList();
        var position = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();
        drawList.AddCircleFilled(position + new Vector2(5f, lineHeight * 0.5f), 4.5f, ImGui.GetColorU32(color));
        ImGui.SetCursorScreenPos(position + new Vector2(16f, 0f));
        ImGui.TextUnformatted(text);

        if (server.State == McpServerState.Failed && server.Error is { } error)
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(EditorTheme.Error, error);
            ImGui.PopTextWrapPos();
        }

        var running = server.State is McpServerState.Running or McpServerState.Starting;
        if (ImGui.Button(running ? "Stop Server" : "Start Server", new Vector2(-1, 0)))
        {
            _ = running ? server.StopAsync() : server.StartAsync(settings.Port);
        }

        ImGui.BeginDisabled(running);
        var port = settings.Port;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputInt("##port", ref port, 0) && port is > 1024 and < 65536)
        {
            settings.Port = port;
            saveSettings();
        }

        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(running ? "Stop the server to change the port." : $"Port (default {McpSettings.DefaultPort})");
        }

        var startWithEditor = settings.StartWithEditor;
        if (ImGui.Checkbox("Start with the editor", ref startWithEditor))
        {
            settings.StartWithEditor = startWithEditor;
            saveSettings();
        }
    }

    private void DrawConnect()
    {
        ImGui.SeparatorText("Connect an assistant");
        var url = McpServerHost.UrlFor(settings.Port);
        ImGui.TextDisabled(url);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Only programs on this computer can connect.");
        }

        if (ImGui.Button("Copy for Claude Code", new Vector2(-1, 0)))
        {
            ImGui.SetClipboardText($"claude mcp add --transport http lapis {url}");
            Log.Info("Copied the Claude Code command; run it in a terminal in your project folder.");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"claude mcp add --transport http lapis {url}");
        }

        if (ImGui.Button("Copy for Claude Desktop", new Vector2(-1, 0)))
        {
            ImGui.SetClipboardText(
                "{\n  \"mcpServers\": {\n    \"lapis\": {\n      \"command\": \"npx\",\n" +
                $"      \"args\": [\"-y\", \"mcp-remote\", \"{url}\"]\n    }}\n  }}\n}}");
            Log.Info("Copied the Claude Desktop configuration; add it to claude_desktop_config.json (needs Node.js).");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("A server entry for claude_desktop_config.json. It uses mcp-remote, which needs Node.js.");
        }
    }

    private void DrawPermissions()
    {
        ImGui.SeparatorText("The assistant may");
        Permission("Edit the scene", settings.AllowSceneEdits, value => settings.AllowSceneEdits = value,
            "Create, change and delete entities, components and scene settings. Every change can be undone with Ctrl+Z.");
        Permission("Change files", settings.AllowFileChanges, value => settings.AllowFileChanges = value,
            "Write scripts and materials in the Assets folder and save the scene.");
        Permission("Run Play mode", settings.AllowPlayMode, value => settings.AllowPlayMode = value,
            "Start, pause and stop the game in the editor.");
        ImGui.TextDisabled("Reading the scene is always allowed.");
    }

    private void Permission(string label, bool value, Action<bool> set, string tooltip)
    {
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            saveSettings();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private void DrawActivity()
    {
        ImGui.SeparatorText($"Activity ({activity.TotalCalls} calls)");
        var entries = activity.Snapshot();
        if (entries.Count == 0)
        {
            ImGui.TextDisabled(server.State == McpServerState.Running ? "Waiting for an assistant..." : "No calls yet.");
            return;
        }

        if (ImGui.BeginChild("##mcpActivity", new Vector2(-1, -1)))
        {
            foreach (var entry in entries)
            {
                ImGui.TextDisabled(entry.Time.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                ImGui.SameLine();
                ImGui.TextColored(entry.Succeeded ? Green : EditorTheme.Error, entry.Tool);
                if (entry.Error is { } error && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(error);
                }
            }
        }

        ImGui.EndChild();
    }
}
