using System.Numerics;
using System.Text.Json.Nodes;
using Lapis.Assets;
using Lapis.Editor.Commands;
using ModelContextProtocol;

namespace Lapis.Editor.Mcp;

/// <summary>MCP tools for scripts, materials, models and prefabs.</summary>
internal sealed partial class McpEditorTools
{
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

    /// <summary>Applies a material, texture or model to an entity's Mesh Renderer, adds a script's behaviour, or plays a sound from it.</summary>
    public string AssignAsset(string entityReference, string asset)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        RequireEditable(entity, rootAllowed: false);
        var info = ResolveAsset(asset, null);
        var done = info.Type switch
        {
            AssetType.Script => EditorActions.AddScript(context, entity, info),
            AssetType.Audio => EditorActions.AssignSound(context, entity, info),
            _ => EditorActions.AssignToRenderer(context, entity, info),
        };
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
        if (parentEntity is not null)
        {
            RequireEditable(parentEntity, rootAllowed: false);
        }
        var entity = EditorActions.InstantiateModel(context, info, parentEntity, position is null ? Vector3.Zero : ToVector3(position, nameof(position)))
            ?? throw new McpException($"'{asset}' could not be imported; see get_console.");
        RenameLast($"Create {entity.Name}");
        return WithPlayNote(Summary(entity)).ToJsonString();
    }

    public string InstantiatePrefab(string asset, string? parent, float[]? position)
    {
        RequireSceneEdits();
        var info = ResolveAsset(asset, AssetType.Prefab);
        var parentEntity = string.IsNullOrWhiteSpace(parent) ? null : ResolveEntity(parent);
        if (parentEntity is not null)
        {
            RequireEditable(parentEntity, rootAllowed: false);
        }
        var entity = PrefabActions.Instantiate(context, info, parentEntity, position is null ? Vector3.Zero : ToVector3(position, nameof(position)))
            ?? throw new McpException($"'{asset}' could not be placed; see get_console.");
        RenameLast($"Create {entity.Name}");
        return WithPlayNote(Summary(entity)).ToJsonString();
    }

    public string MakePrefab(string entityReference, string? folder)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        var assets = RequireAssets();
        var target = string.IsNullOrWhiteSpace(folder) ? assets.RootDirectory : AssetsPath(folder);
        var asset = PrefabActions.CreatePrefab(context, entity, target)
            ?? throw new McpException($"'{entity.Name}' could not be made into a prefab; see get_console.");
        RenameLast($"Make Prefab {asset.Name}");
        return new JsonObject
        {
            ["prefab"] = Path.GetRelativePath(assets.RootDirectory, asset.FullPath).Replace('\\', '/'),
            ["reference"] = asset.Reference,
            ["entity"] = Summary(entity),
        }.ToJsonString();
    }

    public string UnpackPrefab(string entityReference)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        if (entity.PrefabInstanceRoot is not { } root)
        {
            throw new McpException($"'{entity.Name}' is not part of a prefab copy.");
        }

        PrefabActions.Unpack(context, root);
        RenameLast($"Unpack {root.Name}");
        return WithPlayNote(Summary(root)).ToJsonString();
    }
}
