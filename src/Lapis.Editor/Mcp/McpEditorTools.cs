using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lapis.Assets;
using Lapis.Editor.Commands;
using Lapis.Editor.Panels;
using Lapis.Editor.SceneView;
using Lapis.Editor.Scripting;
using Lapis.Serialization;
using ModelContextProtocol;

namespace Lapis.Editor.Mcp;

/// <summary>Parts of the running editor the MCP tools use besides the scene; each may be missing (e.g. in tests).</summary>
internal sealed class McpEditorServices
{
    public PlayMode? Play { get; init; }

    public ScriptManager? Scripts { get; init; }

    public EditorCamera? Camera { get; init; }

    /// <summary>The Console's messages, oldest first.</summary>
    public Func<IReadOnlyList<ConsoleLine>>? Console { get; init; }

    /// <summary>Returns the "scene" or "game" view as PNG bytes, or null when that view has not been drawn.</summary>
    public Func<string, byte[]?>? CaptureView { get; init; }
}

/// <summary>
/// What the MCP tools do, on the editor's main thread. Every change goes through the undo history as one step named
/// "MCP: ...", so the user can take back anything an assistant did with Ctrl+Z. Errors meant for the assistant are
/// thrown as <see cref="McpException"/>.
/// </summary>
internal sealed class McpEditorTools(EditorContext context, McpSettings settings, McpEditorServices services)
{
    private static readonly string[] TextExtensions = [".cs", ".lmat", ".lscene", ".json", ".txt", ".md"];

    private const int MaxReadBytes = 256 * 1024;

    private static JsonSerializerOptions Json => SceneSerializer.JsonOptions;

    // ----- Reading -----

    public string GetScene()
    {
        var scene = context.Scene;
        var result = new JsonObject
        {
            ["scene"] = scene.Name,
            ["file"] = ProjectRelative(context.ScenePath),
            ["unsavedChanges"] = context.IsDirty,
            ["playMode"] = (services.Play?.State ?? PlayState.Stopped).ToString(),
            ["selection"] = context.Selection?.Id.ToString(),
            ["folders"] = new JsonArray([.. scene.Folders.Select(folder => (JsonNode)new JsonObject
            {
                ["id"] = folder.Id.ToString(),
                ["path"] = FolderPath(scene, folder),
            })]),
            ["entities"] = new JsonArray([.. scene.RootEntities.Select(TreeNode)]),
        };
        return result.ToJsonString();
    }

    public string GetEntity(string entityReference)
    {
        var entity = ResolveEntity(entityReference);
        var transform = entity.Transform;
        var result = Summary(entity);
        result["active"] = entity.IsActive;
        result["activeInHierarchy"] = entity.IsActiveInHierarchy;
        result["parent"] = entity.Parent?.Id.ToString();
        result["folder"] = entity.FolderPath;
        result["transform"] = new JsonObject
        {
            ["position"] = Vector(transform.LocalPosition),
            ["rotation"] = Vector(transform.LocalEulerAngles),
            ["scale"] = Vector(transform.LocalScale),
            ["worldPosition"] = Vector(transform.Position),
        };
        result["children"] = new JsonArray([.. entity.Children.Select(child => (JsonNode)Summary(child))]);

        var components = new JsonArray();
        foreach (var component in entity.Components)
        {
            if (component is Transform)
            {
                continue;
            }

            if (component is MissingComponent missing)
            {
                components.Add(new JsonObject { ["type"] = missing.TypeName, ["missing"] = true });
                continue;
            }

            var properties = new JsonObject();
            foreach (var member in SerializedMember.For(component.GetType()))
            {
                if (member.GetAttribute<HideInEditorAttribute>() is null)
                {
                    properties[member.Key] = WriteValue(member.GetValue(component), member.ValueType);
                }
            }

            components.Add(new JsonObject { ["type"] = component.GetType().Name, ["properties"] = properties });
        }

        result["components"] = components;
        return result.ToJsonString();
    }

    public string FindEntities(string? name, string? componentType)
    {
        var type = string.IsNullOrWhiteSpace(componentType) ? null : ResolveComponentType(componentType);
        var found = context.Scene.AllEntities
            .Where(entity => string.IsNullOrWhiteSpace(name) || entity.Name.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(entity => type is null || entity.GetComponent(type) is not null)
            .Take(200)
            .Select(entity => (JsonNode)Summary(entity));
        return new JsonArray([.. found]).ToJsonString();
    }

    public static string ListComponentTypes() => new JsonArray([.. ComponentCatalog.Types.Select(type => (JsonNode)new JsonObject
    {
        ["type"] = type.Name,
        ["script"] = ComponentCatalog.IsScript(type),
        ["properties"] = new JsonArray([.. SerializedMember.For(type)
            .Where(member => member.GetAttribute<HideInEditorAttribute>() is null)
            .Select(member => (JsonNode)JsonValue.Create($"{member.Key}: {FriendlyTypeName(member.ValueType)}"))]),
    })]).ToJsonString();

    public string ListAssets(string? type, string? search)
    {
        var assets = RequireAssets();
        AssetType? wanted = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            wanted = Enum.TryParse<AssetType>(type, ignoreCase: true, out var parsed)
                ? parsed
                : throw new McpException($"Unknown asset type '{type}'. Use one of: {string.Join(", ", Enum.GetNames<AssetType>())}.");
        }

        var found = assets.Assets
            .Where(asset => wanted is null || asset.Type == wanted)
            .Select(asset => (Asset: asset, Path: AssetsRelative(assets, asset.FullPath)))
            .Where(item => string.IsNullOrWhiteSpace(search) || item.Path.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .Select(item => (JsonNode)new JsonObject
            {
                ["path"] = item.Path,
                ["type"] = item.Asset.Type.ToString(),
                ["reference"] = item.Asset.Reference,
            });
        return new JsonArray([.. found]).ToJsonString();
    }

    public string ReadFile(string path)
    {
        var fullPath = AssetsPath(path);
        if (!TextExtensions.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase))
        {
            throw new McpException($"Only text files can be read ({string.Join(", ", TextExtensions)}).");
        }

        if (!File.Exists(fullPath))
        {
            throw new McpException($"'{path}' does not exist.");
        }

        if (new FileInfo(fullPath).Length > MaxReadBytes)
        {
            throw new McpException($"'{path}' is larger than {MaxReadBytes / 1024} KB.");
        }

        return File.ReadAllText(fullPath);
    }

    public string GetConsole(int count, string? level)
    {
        var lines = services.Console?.Invoke() ?? [];
        LogLevel? wanted = string.IsNullOrWhiteSpace(level) ? null
            : Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed) ? parsed
            : throw new McpException($"Unknown level '{level}'. Use one of: {string.Join(", ", Enum.GetNames<LogLevel>())}.");

        var selected = lines.Where(line => wanted is null || line.Entry.Level == wanted).TakeLast(Math.Clamp(count, 1, 500));
        return new JsonArray([.. selected.Select(line => (JsonNode)new JsonObject
        {
            ["time"] = line.Entry.Timestamp.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["level"] = line.Entry.Level.ToString(),
            ["message"] = line.Entry.Message,
            ["file"] = line.Entry.FilePath is { } file ? $"{ProjectRelative(file)}:{line.Entry.Line}" : null,
        })]).ToJsonString();
    }

    /// <summary>The Scene view (as the editor camera sees it) or the Game view (as the main camera sees it) as PNG.</summary>
    public byte[] Screenshot(string view)
    {
        view = string.IsNullOrWhiteSpace(view) ? "scene" : view.Trim().ToLowerInvariant();
        if (view is not ("scene" or "game"))
        {
            throw new McpException("The view must be \"scene\" or \"game\".");
        }

        return services.CaptureView?.Invoke(view)
            ?? throw new McpException($"The {view} view is not shown in the editor; open its tab first.");
    }

    // ----- Entities -----

    public string CreateEntity(string? name, string? shape, string? parent, float[]? position, float[]? rotation, float[]? scale, bool physics)
    {
        RequireSceneEdits();
        var scene = context.Scene;
        var parentEntity = string.IsNullOrWhiteSpace(parent) ? null : ResolveEntity(parent);
        var mesh = (shape ?? "empty").Trim().ToLowerInvariant() switch
        {
            "cube" => BuiltInMeshes.Cube,
            "sphere" => BuiltInMeshes.Sphere,
            "plane" => BuiltInMeshes.Plane,
            _ => null,
        };

        var entity = (shape ?? "empty").Trim().ToLowerInvariant() switch
        {
            "empty" or "" => EntityFactory.CreateEmpty(scene, parentEntity),
            "cube" or "sphere" or "plane" => physics
                ? EntityFactory.CreatePhysicsMesh(scene, mesh!, parentEntity)
                : EntityFactory.CreateMesh(scene, mesh!, parentEntity),
            "camera" => EntityFactory.CreateCamera(scene, parentEntity),
            "directional_light" => EntityFactory.CreateLight(scene, LightType.Directional, parentEntity),
            "point_light" => EntityFactory.CreateLight(scene, LightType.Point, parentEntity),
            _ => throw new McpException($"Unknown shape '{shape}'. Use empty, cube, sphere, plane, camera, directional_light or point_light."),
        };

        if (!string.IsNullOrWhiteSpace(name))
        {
            entity.Name = name.Trim();
        }

        ApplyTransform(entity.Transform, position, rotation, scale);
        Record(EntityLifetimeCommand.Created($"Create {entity.Name}", entity));
        return WithPlayNote(Summary(entity)).ToJsonString();
    }

    public string DeleteEntity(string entityReference)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        Execute(EntityLifetimeCommand.Deleting([entity]));
        return WithPlayNote(new JsonObject { ["deleted"] = Summary(entity) }).ToJsonString();
    }

    public string DuplicateEntity(string entityReference)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        var json = SceneSerializer.EntitiesToJson([entity]);
        var copy = SceneSerializer.EntitiesFromJson(context.Scene, json, entity.Parent, assignNewIds: true)[0];
        copy.Transform.SetSiblingIndex(entity.Transform.SiblingIndex + 1);
        Record(EntityLifetimeCommand.Created($"Duplicate {entity.Name}", copy));
        return WithPlayNote(Summary(copy)).ToJsonString();
    }

    public string RenameEntity(string entityReference, string name)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new McpException("The name cannot be empty.");
        }

        Execute(new RenameEntityCommand(entity, entity.Name, name.Trim()));
        return WithPlayNote(Summary(entity)).ToJsonString();
    }

    public string SetActive(string entityReference, bool active)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        if (entity.IsActive != active)
        {
            Execute(new SetActiveCommand(entity, active));
        }

        return WithPlayNote(Summary(entity)).ToJsonString();
    }

    public string SetTransform(string entityReference, float[]? position, float[]? rotation, float[]? scale, bool world)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        var transform = entity.Transform;
        var before = TransformState.Of(transform);
        if (world)
        {
            if (position is not null)
            {
                transform.Position = ToVector3(position, nameof(position));
            }

            if (rotation is not null)
            {
                transform.EulerAngles = ToVector3(rotation, nameof(rotation));
            }

            if (scale is not null)
            {
                transform.LocalScale = ToVector3(scale, nameof(scale)); // scale is always local
            }
        }
        else
        {
            ApplyTransform(transform, position, rotation, scale);
        }

        Record(new SetTransformCommand(entity, before, TransformState.Of(transform)));
        return GetEntityTransform(entity);
    }

    public string MoveEntity(string entityReference, string? parent, string? before, string? after, string? folder)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        if (!string.IsNullOrWhiteSpace(before) || !string.IsNullOrWhiteSpace(after))
        {
            var anchor = ResolveEntity(!string.IsNullOrWhiteSpace(before) ? before : after!);
            if (anchor == entity || anchor.Transform.IsChildOf(entity.Transform))
            {
                throw new McpException($"Cannot place '{entity.Name}' next to itself or one of its own children.");
            }

            Execute(new PlaceEntityCommand(entity, anchor, !string.IsNullOrWhiteSpace(before) ? Placement.Before : Placement.After));
        }
        else if (folder is not null)
        {
            var folderId = folder.Trim().Length == 0 ? (Guid?)null
                : context.Scene.FindFolders(folder.Trim()).FirstOrDefault()?.Id ?? throw new McpException($"No folder '{folder}'. See the folders in get_scene.");
            Execute(new MoveToFolderCommand(entity, folderId));
        }
        else if (parent is not null)
        {
            var newParent = parent.Trim().Length == 0 ? null : ResolveEntity(parent);
            if (newParent == entity || (newParent is not null && newParent.Transform.IsChildOf(entity.Transform)))
            {
                throw new McpException($"Cannot move '{entity.Name}' under itself or one of its own children.");
            }

            if (entity.Parent != newParent)
            {
                Execute(new ReparentCommand(entity, newParent));
            }
        }
        else
        {
            throw new McpException("Give a parent (\"\" for the top level), before, after or folder.");
        }

        var result = Summary(entity);
        result["parent"] = entity.Parent?.Id.ToString();
        result["folder"] = entity.FolderPath;
        return WithPlayNote(result).ToJsonString();
    }

    // ----- Components -----

    public string AddComponent(string entityReference, string componentType, JsonElement? properties)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        var type = ResolveComponentType(componentType);
        var component = entity.AddComponent(type);
        try
        {
            if (properties is { ValueKind: JsonValueKind.Object } values)
            {
                foreach (var property in values.EnumerateObject())
                {
                    var member = ResolveMember(type, property.Name);
                    member.SetValue(component, ReadValue(property.Value, member.ValueType));
                }
            }
        }
        catch
        {
            entity.RemoveComponent(component);
            throw;
        }

        Record(ComponentLifetimeCommand.Added(component));
        return WithPlayNote(new JsonObject { ["entity"] = Summary(entity), ["added"] = type.Name }).ToJsonString();
    }

    public string RemoveComponent(string entityReference, string componentType)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        var component = FindComponent(entity, componentType);
        Execute(ComponentLifetimeCommand.Removing(component));
        return WithPlayNote(new JsonObject { ["entity"] = Summary(entity), ["removed"] = component.GetType().Name }).ToJsonString();
    }

    public string SetProperty(string entityReference, string componentType, string property, JsonElement value)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        var component = FindComponent(entity, componentType);
        var member = ResolveMember(component.GetType(), property);
        var before = member.GetValue(component);
        member.SetValue(component, ReadValue(value, member.ValueType));
        Record(new SetMemberCommand(component, member, before, member.GetValue(component)));
        return WithPlayNote(new JsonObject
        {
            ["entity"] = Summary(entity),
            ["component"] = component.GetType().Name,
            [member.Key] = WriteValue(member.GetValue(component), member.ValueType),
        }).ToJsonString();
    }

    public string SetSceneSetting(string section, string property, JsonElement value)
    {
        RequireSceneEdits();
        object target = section.Trim().ToLowerInvariant() switch
        {
            "environment" => context.Scene.Environment,
            "physics" => context.Scene.Physics,
            _ => throw new McpException("The section must be \"environment\" or \"physics\"."),
        };

        var member = ResolveMember(target.GetType(), property);
        var before = member.GetValue(target);
        member.SetValue(target, ReadValue(value, member.ValueType));
        Record(new SetSceneSettingCommand(target, member, before, member.GetValue(target)));
        return WithPlayNote(new JsonObject { [member.Key] = WriteValue(member.GetValue(target), member.ValueType) }).ToJsonString();
    }

    public string GetSceneSettings()
    {
        JsonObject Members(object target)
        {
            var values = new JsonObject();
            foreach (var member in SerializedMember.For(target.GetType()))
            {
                values[member.Key] = WriteValue(member.GetValue(target), member.ValueType);
            }

            return values;
        }

        return new JsonObject
        {
            ["environment"] = Members(context.Scene.Environment),
            ["physics"] = Members(context.Scene.Physics),
        }.ToJsonString();
    }

    // ----- Assets -----

    /// <summary>Creates or replaces a script. Returns the full path; compile with <see cref="RequestCompile"/>.</summary>
    public string WriteScript(string path, string code)
    {
        RequireFileChanges();
        var assets = RequireAssets();
        var fullPath = AssetsPath(Path.HasExtension(path) ? path : path + ".cs");
        if (!fullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException("A script's path must end with .cs.");
        }

        if (File.Exists(fullPath))
        {
            var before = File.ReadAllText(fullPath);
            if (before != code)
            {
                Execute(new ReplaceFileCommand(fullPath, before, code));
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, code);
            assets.Refresh();
            Record(AssetFilesCommand.Created($"Create {Path.GetFileName(fullPath)}", [fullPath]));
        }

        return fullPath;
    }

    /// <summary>The compilation the scripts have now (compare with a later call to see a new one finish).</summary>
    public (bool IsCompiling, object? Compilation) CompileState() =>
        (services.Scripts?.IsCompiling ?? false, services.Scripts?.LastCompilation);

    public void RequestCompile() => services.Scripts?.RequestCompile();

    /// <summary>The result of the last compilation, with the errors (and warnings) of every script.</summary>
    public string CompileReport(string? scriptPath)
    {
        if (services.Scripts?.LastCompilation is not { } compilation)
        {
            return new JsonObject { ["compiled"] = false, ["note"] = "No project is open, so scripts are not compiled." }.ToJsonString();
        }

        var diagnostics = compilation.Diagnostics
            .Select(diagnostic => (JsonNode)new JsonObject
            {
                ["severity"] = diagnostic.IsError ? "error" : "warning",
                ["id"] = diagnostic.Id,
                ["message"] = diagnostic.Message,
                ["file"] = ProjectRelative(diagnostic.FilePath),
                ["line"] = diagnostic.Line,
                ["column"] = diagnostic.Column,
            });

        return new JsonObject
        {
            ["script"] = ProjectRelative(scriptPath),
            ["success"] = compilation.Success,
            ["diagnostics"] = new JsonArray([.. diagnostics]),
        }.ToJsonString();
    }

    public string CreateMaterial(string path, float[]? color, float? metallic, float? roughness, string? texture)
    {
        RequireFileChanges();
        var assets = RequireAssets();
        var fullPath = AssetsPath(Path.HasExtension(path) ? path : path + Material.FileExtension);
        if (File.Exists(fullPath))
        {
            throw new McpException($"'{path}' already exists.");
        }

        var material = new Material();
        if (color is not null)
        {
            material.Color = color.Length switch
            {
                3 => new Color(color[0], color[1], color[2]),
                4 => new Color(color[0], color[1], color[2], color[3]),
                _ => throw new McpException("A colour has 3 or 4 numbers (red, green, blue[, alpha]) from 0 to 1."),
            };
        }

        material.Metallic = metallic ?? material.Metallic;
        material.Roughness = roughness ?? material.Roughness;
        if (!string.IsNullOrWhiteSpace(texture))
        {
            material.TextureId = ResolveAsset(texture, AssetType.Texture).Reference;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        material.Save(fullPath);
        assets.Refresh();
        Record(AssetFilesCommand.Created($"Create {Path.GetFileName(fullPath)}", [fullPath]));
        var created = assets.FindByPath(fullPath)!;
        return new JsonObject { ["path"] = AssetsRelative(assets, fullPath), ["reference"] = created.Reference }.ToJsonString();
    }

    /// <summary>Applies a material, texture or model to an entity's Mesh Renderer, or adds a script's behaviour.</summary>
    public string AssignAsset(string entityReference, string asset)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        var info = ResolveAsset(asset, null);
        var done = info.Type == AssetType.Script
            ? EditorActions.AddScript(context, entity, info)
            : EditorActions.AssignToRenderer(context, entity, info);
        if (!done)
        {
            throw new McpException(info.Type == AssetType.Script
                ? $"The script could not be added; see get_console for why (it must compile and its class must match the file name)."
                : $"'{entity.Name}' needs a MeshRenderer for a {info.Type}; add one with add_component first.");
        }

        RenameLast($"Assign {Path.GetFileName(info.FullPath)} to {entity.Name}");
        return WithPlayNote(new JsonObject { ["entity"] = Summary(entity), ["assigned"] = AssetsRelative(RequireAssets(), info.FullPath) }).ToJsonString();
    }

    public string InstantiateModel(string asset, string? parent, float[]? position)
    {
        RequireSceneEdits();
        var info = ResolveAsset(asset, AssetType.Model);
        var parentEntity = string.IsNullOrWhiteSpace(parent) ? null : ResolveEntity(parent);
        var entity = EditorActions.InstantiateModel(context, info, parentEntity, position is null ? Vector3.Zero : ToVector3(position, nameof(position)))
            ?? throw new McpException($"'{asset}' could not be imported; see get_console.");
        RenameLast($"Create {entity.Name}");
        return WithPlayNote(Summary(entity)).ToJsonString();
    }

    // ----- Editor -----

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

    // ----- Helpers -----

    /// <summary>
    /// Finds an entity by id, by path ("Parent/Child") or by name. A name used by several entities is an error that
    /// lists them, so the assistant can use an id instead.
    /// </summary>
    public Entity ResolveEntity(string reference)
    {
        var scene = context.Scene;
        reference = reference.Trim();
        if (Guid.TryParse(reference, out var id))
        {
            return scene.FindById(id) ?? throw new McpException($"No entity has the id {id}.");
        }

        if (reference.Contains('/'))
        {
            var parts = reference.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            IEnumerable<Entity> level = scene.RootEntities;
            Entity? current = null;
            foreach (var part in parts)
            {
                current = level.FirstOrDefault(entity => entity.Name == part)
                    ?? throw new McpException($"No entity '{part}' in the path '{reference}'.");
                level = current.Children;
            }

            return current ?? throw new McpException("The path is empty.");
        }

        var matches = scene.AllEntities.Where(entity => entity.Name == reference).Take(10).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new McpException($"No entity is named '{reference}'. Use get_scene or find_entities to see the names."),
            _ => throw new McpException($"Several entities are named '{reference}': " +
                string.Join(", ", matches.Select(entity => $"{EntityPath(entity)} ({entity.Id})")) + ". Use an id or a path."),
        };
    }

    private void RequireSceneEdits()
    {
        if (!settings.AllowSceneEdits)
        {
            throw new McpException("Scene edits are turned off in the MCP tab of the Scene view.");
        }
    }

    private void RequireFileChanges()
    {
        if (!settings.AllowFileChanges)
        {
            throw new McpException("File changes are turned off in the MCP tab of the Scene view.");
        }
    }

    private AssetDatabase RequireAssets() => context.Assets ?? throw new McpException("No project is open in the editor.");

    /// <summary>Applies an already-made change's command as a separate undo step named "MCP: ...".</summary>
    private void Record(IEditorCommand command)
    {
        context.History.BeginGesture();
        context.Record(new McpCommand(command));
    }

    private void Execute(IEditorCommand command) => context.Execute(new McpCommand(command));

    /// <summary>Marks the step just recorded by a shared editor action as made by an assistant.</summary>
    private void RenameLast(string name) => context.History.RenameLast(command => new McpCommand(command, name));

    private string Step(int steps, bool undo)
    {
        RequireSceneEdits();
        var names = new JsonArray();
        for (var i = 0; i < Math.Clamp(steps, 1, 50); i++)
        {
            var name = undo ? context.History.UndoName : context.History.RedoName;
            if (name is null)
            {
                break;
            }

            if (undo)
            {
                context.Undo();
            }
            else
            {
                context.Redo();
            }

            names.Add(name);
        }

        return new JsonObject { [undo ? "undone" : "redone"] = names }.ToJsonString();
    }

    private Type ResolveComponentType(string name)
    {
        name = name.Trim();
        var compact = name.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (string.Equals(compact, nameof(Transform), StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException("Every entity has a Transform; change it with set_transform.");
        }

        return ComponentCatalog.Types.FirstOrDefault(type => string.Equals(type.Name, compact, StringComparison.OrdinalIgnoreCase))
            ?? ComponentCatalog.Types.FirstOrDefault(type => string.Equals(type.FullName, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException($"Unknown component type '{name}'. See list_component_types.");
    }

    private Component FindComponent(Entity entity, string componentType)
    {
        var type = ResolveComponentType(componentType);
        return entity.GetComponent(type) ?? throw new McpException($"'{entity.Name}' has no {type.Name}.");
    }

    private static SerializedMember ResolveMember(Type type, string name)
    {
        var members = SerializedMember.For(type);
        return members.FirstOrDefault(member => string.Equals(member.Key, name, StringComparison.OrdinalIgnoreCase))
            ?? members.FirstOrDefault(member => string.Equals(member.Name, name.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException($"{type.Name} has no property '{name}'. It has: {string.Join(", ", members.Select(member => member.Key))}.");
    }

    private object? ReadValue(JsonElement value, Type type)
    {
        if (type == typeof(Entity) || typeof(Component).IsAssignableFrom(type))
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                throw new McpException("A reference to an entity is given as its id, path or name (a string), or null.");
            }

            var entity = ResolveEntity(value.GetString()!);
            return type == typeof(Entity) ? entity
                : entity.GetComponent(type) ?? throw new McpException($"'{entity.Name}' has no {type.Name}.");
        }

        if (type == typeof(string) && value.ValueKind == JsonValueKind.String && TryAssetReference(value.GetString()!) is { } reference)
        {
            return reference;
        }

        try
        {
            return value.Deserialize(type, Json);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new McpException($"The value {value.GetRawText()} does not fit a {FriendlyTypeName(type)}: {exception.Message}");
        }
    }

    /// <summary>Lets asset fields take a path in the Assets folder as well as an asset reference.</summary>
    private string? TryAssetReference(string text)
    {
        if (context.Assets is not { } assets || text.Length == 0 || text.StartsWith("builtin:", StringComparison.Ordinal) ||
            AssetDatabase.TryParseReference(text, out _, out _))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(assets.RootDirectory, text));
        return assets.FindByPath(fullPath)?.Reference;
    }

    private static JsonNode? WriteValue(object? value, Type type) => value switch
    {
        null => null,
        Entity entity => entity.IsDestroyed ? null : JsonValue.Create(entity.Id.ToString()),
        Component component => component.IsAttached ? JsonValue.Create(component.entity.Id.ToString()) : null,
        _ => JsonSerializer.SerializeToNode(value, type, Json),
    };

    private AssetInfo ResolveAsset(string reference, AssetType? type)
    {
        var assets = RequireAssets();
        var asset = assets.Find(reference) ?? assets.FindByPath(AssetsPath(reference))
            ?? throw new McpException($"No asset '{reference}'. See list_assets.");
        if (type is { } wanted && asset.Type != wanted)
        {
            throw new McpException($"'{reference}' is a {asset.Type}, not a {wanted}.");
        }

        return asset;
    }

    /// <summary>A path inside the project's Assets folder (relative to it, or absolute inside it).</summary>
    private string AssetsPath(string path)
    {
        var root = Path.GetFullPath(RequireAssets().RootDirectory);
        var trimmed = path.Trim().Replace('\\', '/');
        if (trimmed.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["Assets/".Length..];
        }

        var fullPath = Path.GetFullPath(Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(root, trimmed));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException("The path must be inside the project's Assets folder.");
        }

        return fullPath;
    }

    private static string AssetsRelative(AssetDatabase assets, string fullPath) =>
        Path.GetRelativePath(assets.RootDirectory, fullPath).Replace('\\', '/');

    private string? ProjectRelative(string? fullPath)
    {
        if (fullPath is null)
        {
            return null;
        }

        var projectRoot = services.Scripts?.Project?.Directory ?? (context.Assets is { } assets ? Path.GetDirectoryName(assets.RootDirectory) : null);
        return projectRoot is null ? fullPath : Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
    }

    private JsonObject WithPlayNote(JsonObject result)
    {
        if (context.IsPlaying)
        {
            result["note"] = "Play mode is running: this changed the playing copy of the scene and is discarded when Play stops.";
        }

        return result;
    }

    private string GetEntityTransform(Entity entity)
    {
        var transform = entity.Transform;
        var result = Summary(entity);
        result["position"] = Vector(transform.LocalPosition);
        result["rotation"] = Vector(transform.LocalEulerAngles);
        result["scale"] = Vector(transform.LocalScale);
        result["worldPosition"] = Vector(transform.Position);
        return WithPlayNote(result).ToJsonString();
    }

    private static void ApplyTransform(Transform transform, float[]? position, float[]? rotation, float[]? scale)
    {
        if (position is not null)
        {
            transform.LocalPosition = ToVector3(position, nameof(position));
        }

        if (rotation is not null)
        {
            transform.LocalEulerAngles = ToVector3(rotation, nameof(rotation));
        }

        if (scale is not null)
        {
            transform.LocalScale = ToVector3(scale, nameof(scale));
        }
    }

    private static Vector3 ToVector3(float[] values, string name) => values.Length == 3
        ? new Vector3(values[0], values[1], values[2])
        : throw new McpException($"{name} needs 3 numbers [x, y, z].");

    private static JsonArray Vector(Vector3 value) =>
        [MathF.Round(value.X, 4), MathF.Round(value.Y, 4), MathF.Round(value.Z, 4)];

    private static JsonObject Summary(Entity entity) => new()
    {
        ["id"] = entity.Id.ToString(),
        ["name"] = entity.Name,
        ["path"] = EntityPath(entity),
    };

    private static JsonNode TreeNode(Entity entity)
    {
        var node = new JsonObject { ["id"] = entity.Id.ToString(), ["name"] = entity.Name };
        if (!entity.IsActive)
        {
            node["active"] = false;
        }

        if (entity.Parent is null && entity.FolderPath is { } folder)
        {
            node["folder"] = folder;
        }

        var components = entity.Components.Where(component => component is not Transform)
            .Select(component => component is MissingComponent missing ? $"{missing.TypeName} (missing)" : component.GetType().Name)
            .ToList();
        if (components.Count > 0)
        {
            node["components"] = new JsonArray([.. components.Select(name => (JsonNode)JsonValue.Create(name))]);
        }

        if (entity.Transform.Children.Count > 0)
        {
            node["children"] = new JsonArray([.. entity.Children.Select(TreeNode)]);
        }

        return node;
    }

    private static string EntityPath(Entity entity) =>
        entity.Parent is { } parent ? $"{EntityPath(parent)}/{entity.Name}" : entity.Name;

    private static string FolderPath(Scene scene, SceneFolder folder) =>
        scene.FindFolder(folder.ParentId) is { } parent ? $"{FolderPath(scene, parent)}/{folder.Name}" : folder.Name;

    private static string FriendlyTypeName(Type type) => type switch
    {
        _ when type == typeof(float) || type == typeof(double) => "number",
        _ when type == typeof(int) => "integer",
        _ when type == typeof(bool) => "boolean",
        _ when type == typeof(string) => "string",
        _ when type == typeof(Vector2) => "[x, y]",
        _ when type == typeof(Vector3) => "[x, y, z]",
        _ when type == typeof(Vector4) || type == typeof(Quaternion) => "[x, y, z, w]",
        _ when type == typeof(Color) => "[r, g, b, a] (0-1)",
        _ when type == typeof(Entity) => "entity (id, path or name)",
        _ when typeof(Component).IsAssignableFrom(type) => $"entity with {type.Name} (id, path or name)",
        { IsEnum: true } => $"one of {string.Join(" / ", Enum.GetNames(type))}",
        _ => type.Name,
    };
}

/// <summary>An undo step made by an assistant: shown as "MCP: ..." in the Edit menu, never merged with the user's edits.</summary>
internal sealed class McpCommand(IEditorCommand inner, string? name = null) : IEditorCommand
{
    public string Name { get; } = $"MCP: {name ?? inner.Name}";

    public bool ChangesScene => inner.ChangesScene;

    public void Execute(Scene scene) => inner.Execute(scene);

    public void Undo(Scene scene) => inner.Undo(scene);
}
