using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Lapis.Editor.Mcp;

/// <summary>
/// The tools the editor offers over MCP. Each call is moved to the editor's main thread (see <see cref="EditorDispatcher"/>),
/// runs in <see cref="McpEditorTools"/> and is listed in the MCP tab.
/// </summary>
[McpServerToolType]
internal sealed class LapisMcpTools(EditorDispatcher dispatcher, McpEditorTools tools, McpActivityLog activity)
{
    /// <summary>How long write_script waits for the scripts to compile.</summary>
    private static readonly TimeSpan CompileTimeout = TimeSpan.FromSeconds(90);

    /// <summary>What the server tells assistants when they connect.</summary>
    public const string Instructions =
        "You are connected to the Lapis game engine editor. Lapis scenes are trees of entities; each entity has a Transform " +
        "and components (MeshRenderer, Light, Camera, RigidBody, colliders and the game's C# scripts, which derive from Behaviour). " +
        "Coordinates are right-handed, Y is up and -Z is forward; one unit is about one metre; rotations are Euler angles in degrees. " +
        "Vectors are JSON arrays [x, y, z] and colours [r, g, b, a] from 0 to 1. Refer to entities by id, by path (\"Parent/Child\") " +
        "or by a unique name. Start with get_scene. Every change is one undo step in the editor (the user can press Ctrl+Z), and " +
        "changes are not saved to disk until save_scene. Use screenshot to check the result visually.";

    // ----- Reading -----

    [McpServerTool(Name = "get_scene", ReadOnly = true), Description("The open scene: its entity tree (ids, names, components), folders, file, unsaved changes, selection and Play mode state.")]
    public Task<string> GetScene() => Run("get_scene", t => t.GetScene());

    [McpServerTool(Name = "get_entity", ReadOnly = true), Description("One entity in detail: transform, children and every component with its property values (in the format set_property takes).")]
    public Task<string> GetEntity([Description("Entity id, path (\"Parent/Child\") or unique name.")] string entity) =>
        Run("get_entity", t => t.GetEntity(entity));

    [McpServerTool(Name = "find_entities", ReadOnly = true), Description("Entities whose name contains a text, that have a component and/or that have a tag.")]
    public Task<string> FindEntities(
        [Description("Part of the name (case-insensitive); empty for any.")] string? name = null,
        [Description("Component type the entity must have, e.g. Light.")] string? component = null,
        [Description("Exact tag the entity must have, e.g. Enemy (case-sensitive).")] string? tag = null) =>
        Run("find_entities", t => t.FindEntities(name, component, tag));

    [McpServerTool(Name = "list_component_types", ReadOnly = true), Description("Every component type that can be added (engine and game scripts) with its properties and their types.")]
    public Task<string> ListComponentTypes() => Run("list_component_types", _ => McpEditorTools.ListComponentTypes());

    [McpServerTool(Name = "get_scene_settings", ReadOnly = true), Description("Scene-wide settings: environment (ambient light, sky, shadows) and physics (gravity, steps per second).")]
    public Task<string> GetSceneSettings() => Run("get_scene_settings", t => t.GetSceneSettings());

    [McpServerTool(Name = "list_assets", ReadOnly = true), Description("Files in the project's Assets folder with their type and the reference used in asset properties.")]
    public Task<string> ListAssets(
        [Description("Only this type: Scene, Script, Model, Texture, Material or Other.")] string? type = null,
        [Description("Part of the path to look for.")] string? search = null) =>
        Run("list_assets", t => t.ListAssets(type, search));

    [McpServerTool(Name = "read_file", ReadOnly = true), Description("Reads a text file from the Assets folder, such as a C# script.")]
    public Task<string> ReadFile([Description("Path relative to the Assets folder, e.g. \"Scripts/Player.cs\".")] string path) =>
        Run("read_file", t => t.ReadFile(path));

    [McpServerTool(Name = "get_console", ReadOnly = true), Description("The latest messages of the editor's Console (logs, warnings, script errors).")]
    public Task<string> GetConsole(
        [Description("How many messages (1-500).")] int count = 30,
        [Description("Only Info, Warning or Error.")] string? level = null) =>
        Run("get_console", t => t.GetConsole(count, level));

    [McpServerTool(Name = "screenshot", ReadOnly = true), Description("A picture of the Scene view (editor camera) or the Game view (the scene's main camera).")]
    public async Task<ContentBlock> Screenshot([Description("\"scene\" or \"game\".")] string view = "scene")
    {
        var png = await Invoke("screenshot", t => t.Screenshot(view)).ConfigureAwait(false);
        return ImageContentBlock.FromBytes(png, "image/png");
    }

    // ----- Entities -----

    [McpServerTool(Name = "create_entity"), Description("Creates an entity: empty, a shape (with a collider) or a camera or light. Returns its id.")]
    public Task<string> CreateEntity(
        [Description("Name; the shape's name when empty.")] string? name = null,
        [Description("empty, cube, sphere, plane, camera, directional_light or point_light.")] string shape = "empty",
        [Description("Parent entity; empty for the top level.")] string? parent = null,
        [Description("Local position [x, y, z].")] float[]? position = null,
        [Description("Local rotation in degrees [x, y, z].")] float[]? rotation = null,
        [Description("Local scale [x, y, z].")] float[]? scale = null,
        [Description("Give a shape a RigidBody so it falls and collides when the game runs.")] bool physics = false) =>
        Run("create_entity", t => t.CreateEntity(name, shape, parent, position, rotation, scale, physics));

    [McpServerTool(Name = "delete_entity", Destructive = true), Description("Deletes an entity and its children.")]
    public Task<string> DeleteEntity([Description("Entity id, path or unique name.")] string entity) =>
        Run("delete_entity", t => t.DeleteEntity(entity));

    [McpServerTool(Name = "duplicate_entity"), Description("Copies an entity with its children and components next to the original.")]
    public Task<string> DuplicateEntity([Description("Entity id, path or unique name.")] string entity) =>
        Run("duplicate_entity", t => t.DuplicateEntity(entity));

    [McpServerTool(Name = "rename_entity"), Description("Renames an entity.")]
    public Task<string> RenameEntity([Description("Entity id, path or unique name.")] string entity, [Description("New name.")] string name) =>
        Run("rename_entity", t => t.RenameEntity(entity, name));

    [McpServerTool(Name = "set_tag"), Description("Sets an entity's tag (one per entity), which scripts use to find it, e.g. Player or Enemy. An empty tag means Untagged.")]
    public Task<string> SetTag([Description("Entity id, path or unique name.")] string entity, [Description("The tag, e.g. Enemy.")] string tag) =>
        Run("set_tag", t => t.SetTag(entity, tag));

    [McpServerTool(Name = "set_active"), Description("Activates or deactivates an entity (inactive entities and their children are not drawn or updated).")]
    public Task<string> SetActive([Description("Entity id, path or unique name.")] string entity, bool active) =>
        Run("set_active", t => t.SetActive(entity, active));

    [McpServerTool(Name = "set_transform"), Description("Sets an entity's position, rotation and/or scale; leave out what should not change.")]
    public Task<string> SetTransform(
        [Description("Entity id, path or unique name.")] string entity,
        [Description("Position [x, y, z].")] float[]? position = null,
        [Description("Rotation in degrees [x, y, z].")] float[]? rotation = null,
        [Description("Scale [x, y, z] (always relative to the parent).")] float[]? scale = null,
        [Description("True for world position and rotation, false (default) for relative to the parent.")] bool world = false) =>
        Run("set_transform", t => t.SetTransform(entity, position, rotation, scale, world));

    [McpServerTool(Name = "move_entity"), Description("Moves an entity in the hierarchy, keeping where it is in the world: under a parent, before or after another entity, or into a folder. Give one of parent, before, after or folder.")]
    public Task<string> MoveEntity(
        [Description("Entity id, path or unique name.")] string entity,
        [Description("New parent; \"\" for the top level.")] string? parent = null,
        [Description("Put it just before this entity, on the same level.")] string? before = null,
        [Description("Put it just after this entity, on the same level.")] string? after = null,
        [Description("Hierarchy folder path at the top level, e.g. \"Level/Props\"; \"\" for none.")] string? folder = null) =>
        Run("move_entity", t => t.MoveEntity(entity, parent, before, after, folder));

    // ----- Components -----

    [McpServerTool(Name = "add_component"), Description("Adds a component (see list_component_types), optionally setting properties at once.")]
    public Task<string> AddComponent(
        [Description("Entity id, path or unique name.")] string entity,
        [Description("Component type, e.g. Light, RigidBody or a script class.")] string component,
        [Description("Optional JSON object of property values, e.g. {\"range\": 10}.")] JsonElement? properties = null) =>
        Run("add_component", t => t.AddComponent(entity, component, properties));

    [McpServerTool(Name = "remove_component", Destructive = true), Description("Removes a component from an entity.")]
    public Task<string> RemoveComponent([Description("Entity id, path or unique name.")] string entity, [Description("Component type.")] string component) =>
        Run("remove_component", t => t.RemoveComponent(entity, component));

    [McpServerTool(Name = "set_property"), Description("Sets one property of a component. Values are JSON: numbers, true/false, strings, [x, y, z] vectors, [r, g, b, a] colours, enum names as strings, entity references as id/path/name strings, asset properties as an asset path or reference.")]
    public Task<string> SetProperty(
        [Description("Entity id, path or unique name.")] string entity,
        [Description("Component type, e.g. MeshRenderer.")] string component,
        [Description("Property name as shown by get_entity, e.g. color.")] string property,
        [Description("The new value as JSON.")] JsonElement value) =>
        Run("set_property", t => t.SetProperty(entity, component, property, value));

    [McpServerTool(Name = "set_scene_setting"), Description("Sets one scene-wide setting (see get_scene_settings).")]
    public Task<string> SetSceneSetting(
        [Description("\"environment\" or \"physics\".")] string section,
        [Description("Property name, e.g. ambientIntensity or gravity.")] string property,
        [Description("The new value as JSON.")] JsonElement value) =>
        Run("set_scene_setting", t => t.SetSceneSetting(section, property, value));

    // ----- Assets -----

    [McpServerTool(Name = "write_script"), Description("Creates or replaces a C# script in the Assets folder, waits for the scripts to compile and returns the errors and warnings. A behaviour's class must derive from Lapis.Behaviour and have the same name as the file.")]
    public async Task<string> WriteScript(
        [Description("Path relative to the Assets folder, e.g. \"Scripts/Spinner.cs\".")] string path,
        [Description("The whole C# source.")] string code)
    {
        var previous = await Invoke("write_script", t => t.CompileState().Compilation).ConfigureAwait(false);
        var fullPath = await Invoke("write_script", t =>
        {
            var written = t.WriteScript(path, code);
            t.RequestCompile();
            return written;
        }, log: false).ConfigureAwait(false);

        var waited = TimeSpan.Zero;
        while (waited < CompileTimeout)
        {
            await Task.Delay(250).ConfigureAwait(false);
            waited += TimeSpan.FromMilliseconds(250);
            var (compiling, current) = await dispatcher.InvokeAsync(tools.CompileState).ConfigureAwait(false);
            if (!compiling && !ReferenceEquals(current, previous))
            {
                break;
            }

            if (current is null && !compiling)
            {
                break; // no project, nothing compiles
            }
        }

        return await Run("write_script", t => t.CompileReport(fullPath)).ConfigureAwait(false);
    }

    [McpServerTool(Name = "create_material"), Description("Creates a material asset (.lmat) that can be assigned to Mesh Renderers.")]
    public Task<string> CreateMaterial(
        [Description("Path relative to the Assets folder, e.g. \"Materials/Red.lmat\".")] string path,
        [Description("Colour [r, g, b] or [r, g, b, a] from 0 to 1.")] float[]? color = null,
        [Description("0 (not metal) to 1 (metal).")] float? metallic = null,
        [Description("0 (smooth, shiny) to 1 (rough).")] float? roughness = null,
        [Description("Optional texture asset path.")] string? texture = null) =>
        Run("create_material", t => t.CreateMaterial(path, color, metallic, roughness, texture));

    [McpServerTool(Name = "assign_asset"), Description("Applies a material, texture or model to an entity's Mesh Renderer, or adds the behaviour of a script asset to the entity.")]
    public Task<string> AssignAsset(
        [Description("Entity id, path or unique name.")] string entity,
        [Description("Asset path relative to the Assets folder, or its reference.")] string asset) =>
        Run("assign_asset", t => t.AssignAsset(entity, asset));

    [McpServerTool(Name = "instantiate_model"), Description("Adds a model asset (.gltf, .glb, .obj, .stl) to the scene as a new entity.")]
    public Task<string> InstantiateModel(
        [Description("Model asset path relative to the Assets folder.")] string asset,
        [Description("Parent entity; empty for the top level.")] string? parent = null,
        [Description("Position [x, y, z] (top level only).")] float[]? position = null) =>
        Run("instantiate_model", t => t.InstantiateModel(asset, parent, position));

    // ----- Editor -----

    [McpServerTool(Name = "select"), Description("Selects an entity in the editor (shown in the Inspector) and optionally moves the Scene view camera to it.")]
    public Task<string> Select(
        [Description("Entity id, path or unique name; empty to clear the selection.")] string? entity = null,
        [Description("Also move the Scene view camera to the entity.")] bool focus = true) =>
        Run("select", t => t.Select(entity, focus));

    [McpServerTool(Name = "set_camera"), Description("Points the Scene view camera, e.g. before a screenshot.")]
    public Task<string> SetCamera(
        [Description("Camera position [x, y, z].")] float[]? position = null,
        [Description("Point to look at [x, y, z].")] float[]? lookAt = null) =>
        Run("set_camera", t => t.SetCamera(position, lookAt));

    [McpServerTool(Name = "undo"), Description("Undoes the last steps in the editor (the assistant's or the user's).")]
    public Task<string> Undo([Description("How many steps (1-50).")] int steps = 1) => Run("undo", t => t.Undo(steps));

    [McpServerTool(Name = "redo"), Description("Redoes steps undone with undo.")]
    public Task<string> Redo([Description("How many steps (1-50).")] int steps = 1) => Run("redo", t => t.Redo(steps));

    [McpServerTool(Name = "save_scene"), Description("Saves the open scene to its file, or to a new path in the Assets folder.")]
    public Task<string> SaveScene([Description("Optional new path, e.g. \"Scenes/Level.lscene\".")] string? path = null) =>
        Run("save_scene", t => t.SaveScene(path));

    [McpServerTool(Name = "open_scene", Destructive = true), Description("Opens another scene file from the Assets folder.")]
    public Task<string> OpenScene(
        [Description("Scene path relative to the Assets folder.")] string path,
        [Description("Open even if the current scene has unsaved changes (they are lost).")] bool discardChanges = false) =>
        Run("open_scene", t => t.OpenScene(path, discardChanges));

    [McpServerTool(Name = "play_mode"), Description("Runs the game in the editor: play, stop, pause (toggle) or step one frame. Changes made while playing are discarded on stop.")]
    public Task<string> PlayMode([Description("play, stop, pause or step.")] string action) =>
        Run("play_mode", t => t.SetPlayMode(action));

    private Task<string> Run(string tool, Func<McpEditorTools, string> work) => Invoke(tool, work);

    /// <summary>Runs <paramref name="work"/> on the main thread, logging the call and turning failures into tool errors.</summary>
    private async Task<T> Invoke<T>(string tool, Func<McpEditorTools, T> work, bool log = true)
    {
        Lapis.Diagnostics.EngineMetrics.McpCall();
        try
        {
            var result = await dispatcher.InvokeAsync(() => work(tools)).ConfigureAwait(false);
            if (log)
            {
                activity.Add(new McpActivity(DateTime.Now, tool, true, null));
            }

            return result;
        }
        catch (McpException exception)
        {
            activity.Add(new McpActivity(DateTime.Now, tool, false, exception.Message));
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity.Add(new McpActivity(DateTime.Now, tool, false, exception.Message));
            throw new McpException($"{tool} failed: {exception.Message}", exception);
        }
    }
}
