# Working with AI assistants (MCP)

The editor has a built-in [Model Context Protocol](https://modelcontextprotocol.io/) server. An AI assistant that
supports MCP, such as Claude Code or Claude Desktop, can connect to it and work in the open scene: look at the
entities, create and change them, write scripts, take screenshots and run the game. You see every change in the
editor as it happens, and you can take any of them back with **Ctrl+Z**.

## Starting the server

1. Click the **MCP** tab at the right edge of the Scene view. Its page slides out over the view.
2. Click **Start Server**. The dot turns green and the page shows the address,
   `http://127.0.0.1:47800/mcp` by default.

- **Start with the editor** starts the server whenever a project opens.
- The port can be changed while the server is stopped.
- The server only accepts connections from this computer.

## Connecting an assistant

Set the assistant up once with one of the entries below; after that it connects whenever the server is running.
They use the default port, 47800; if you changed the port in the MCP tab, change it here too. Start the server in
the editor before the assistant uses it (in Claude Code, `/mcp` reconnects if the server started later).

### Claude Code

Run this in a terminal in your project folder:

```
claude mcp add --transport http lapis http://127.0.0.1:47800/mcp
```

It is saved for you and this folder only (in `~/.claude.json`). Add `--scope user` to have it in every project, or
`--scope project` to save it in `.mcp.json` in the project folder, which can be committed so everyone working on the
project gets it. You can also write `.mcp.json` yourself:

```json
{
  "mcpServers": {
    "lapis": {
      "type": "http",
      "url": "http://127.0.0.1:47800/mcp"
    }
  }
}
```

`claude mcp list` shows the configured servers and `claude mcp remove lapis` removes this one. The Code tab of the
Claude desktop app uses the same settings.

### Claude Desktop

Open **Settings > Developer > Edit Config** in Claude Desktop. In `claude_desktop_config.json`, add this entry inside
`"mcpServers"` (next to any servers already there, separated by a comma), save, and restart Claude Desktop:

```json
"lapis": {
  "command": "npx",
  "args": ["-y", "mcp-remote", "http://127.0.0.1:47800/mcp"]
}
```

A file with no other servers looks like this:

```json
{
  "mcpServers": {
    "lapis": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://127.0.0.1:47800/mcp"]
    }
  }
}
```

Claude Desktop starts the programs listed there itself; `mcp-remote` passes its requests on to the editor. It needs
[Node.js](https://nodejs.org/).

### Codex (OpenAI)

Run this in a terminal:

```
codex mcp add lapis --url http://127.0.0.1:47800/mcp
```

or add this to `~/.codex/config.toml` (or `.codex/config.toml` in the project folder):

```toml
[mcp_servers.lapis]
url = "http://127.0.0.1:47800/mcp"
```

The Codex command line, its IDE extension and the ChatGPT desktop app's Codex use the same settings. `codex mcp list`
shows the configured servers. Codex's MCP commands are still marked experimental; if the command is not accepted,
see `codex mcp add --help`.

### Other assistants

Any MCP client that supports the Streamable HTTP transport connects to `http://127.0.0.1:47800/mcp`.

ChatGPT in the browser cannot connect: it runs on OpenAI's servers, which cannot reach a server on your computer.
Exposing the server to the internet (with a tunnel) is not recommended, because anyone who knows the address could
then control the editor.

## What the assistant may do

The page has three switches. Reading the scene is always allowed.

| Switch | Allows |
|---|---|
| Edit the scene | Creating, changing, moving and deleting entities and components; scene settings; undo and redo |
| Change files | Writing scripts and materials in the Assets folder; saving the scene |
| Run Play mode | Starting, pausing, stepping and stopping the game |

Every change the assistant makes is one step in the undo history, named **MCP: ...** in the **Edit** menu. Changes are
not saved to disk until the assistant (or you) saves the scene. Scripts and materials are files, so they are written
at once; undoing them restores the previous file.

The **Activity** list shows the latest calls; a red name failed, and hovering it shows why.

## Tools for AI

The server offers 32 tools. Assistants read this list (with the same descriptions) when they connect.

**Conventions**

- **Entities** are referred to by id (from `get_scene`), by path (`"Level/Room/Lamp"`) or by a unique name. When several
  entities share a name, the tool fails and lists them with their paths and ids.
- **Values** use the same JSON as scene files: numbers, `true` / `false`, strings, vectors `[x, y, z]`, colours
  `[r, g, b, a]` from 0 to 1, enums by name (`"Point"`). References to entities are their id, path or name; asset
  properties take a path in the Assets folder (`"Materials/Red.lmat"`) or an asset reference.
- **Units:** right-handed, Y up, -Z forward; one unit is about one metre; rotations are Euler angles in degrees.
- **Paths** of files are relative to the project's `Assets` folder and cannot point outside it.
- **Permission** is the switch in the MCP tab a tool needs (see above); tools marked "—" only read and always work.
- Optional parameters are shown in *italics*. Every tool answers with JSON, except `screenshot` (a PNG image) and
  `read_file` (the file's text).

### Reading the scene

| Tool | Parameters | What it does | Permission |
|---|---|---|---|
| `get_scene` | — | The open scene: entity tree (ids, names, components, inactive entities, folders), the scene file, unsaved changes, the selection and the Play mode state | — |
| `get_entity` | `entity` | One entity: local position / rotation / scale and world position, parent, folder, children, and every component with its property values in the format `set_property` takes | — |
| `find_entities` | *`name`*, *`component`*, *`tag`* | Entities whose name contains a text, that have a component type and/or that have a tag (up to 200) | — |
| `list_component_types` | — | Every component type that can be added (engine and game scripts), whether it is a script, and its properties with their types | — |
| `get_scene_settings` | — | Environment (ambient light, sky, shadow distance) and physics (gravity, steps per second) settings | — |
| `get_console` | *`count`* (1-500, default 30), *`level`* (`Info` / `Warning` / `Error`) | The latest Console messages, with the file and line for script errors | — |
| `screenshot` | *`view`* (`scene` default, or `game`) | A PNG of the Scene view (editor camera) or the Game view (the scene's main camera). The view's tab must be shown in the editor | — |

### Entities

| Tool | Parameters | What it does | Permission |
|---|---|---|---|
| `create_entity` | *`name`*, *`shape`*, *`parent`*, *`position`*, *`rotation`*, *`scale`*, *`physics`* | Creates `empty` (default), `cube`, `sphere`, `plane` (shapes come with a collider; `physics: true` adds a RigidBody), `camera`, `directional_light` or `point_light`. Returns its id | Edit the scene |
| `delete_entity` | `entity` | Deletes the entity and its children | Edit the scene |
| `duplicate_entity` | `entity` | Copies the entity with its children and components next to the original | Edit the scene |
| `rename_entity` | `entity`, `name` | Renames the entity | Edit the scene |
| `set_tag` | `entity`, `tag` | Sets the entity's tag (empty = Untagged) | Edit the scene |
| `set_active` | `entity`, `active` | Activates or deactivates the entity | Edit the scene |
| `set_transform` | `entity`, *`position`*, *`rotation`*, *`scale`*, *`world`* | Changes only the given values; local to the parent by default, `world: true` for world position and rotation | Edit the scene |
| `move_entity` | `entity` and one of *`parent`* (`""` = top level), *`before`*, *`after`*, *`folder`* | Moves the entity in the Hierarchy, keeping its place in the world: under a parent, just before or after another entity on its level, or into a Hierarchy folder at the top level | Edit the scene |
| `select` | *`entity`* (empty clears), *`focus`* (default true) | Selects the entity in the editor (shown in the Inspector) and moves the Scene view camera to it | — |

### Components and scene settings

| Tool | Parameters | What it does | Permission |
|---|---|---|---|
| `add_component` | `entity`, `component`, *`properties`* | Adds a component (e.g. `Light`, `RigidBody` or a script class), optionally setting properties at once: `{"range": 10}` | Edit the scene |
| `remove_component` | `entity`, `component` | Removes the component | Edit the scene |
| `set_property` | `entity`, `component`, `property`, `value` | Sets one property, e.g. `MeshRenderer` / `color` / `[1, 0, 0, 1]` | Edit the scene |
| `set_scene_setting` | `section` (`environment` / `physics`), `property`, `value` | Sets one scene-wide setting, e.g. `physics` / `gravity` / `[0, -20, 0]` | Edit the scene |

### Assets and scripts

| Tool | Parameters | What it does | Permission |
|---|---|---|---|
| `list_assets` | *`type`* (`Scene`, `Script`, `Model`, `Texture`, `Material`, `Other`), *`search`* | Files in the Assets folder with their type and asset reference | — |
| `read_file` | `path` | The text of a `.cs`, `.lmat`, `.lscene`, `.json`, `.txt` or `.md` file (up to 256 KB) | — |
| `write_script` | `path`, `code` | Creates or replaces a C# script, waits for the scripts to compile (up to 90 s) and returns the result with every error and warning (file, line, message) | Change files |
| `create_material` | `path`, *`color`*, *`metallic`*, *`roughness`*, *`texture`* | Creates a material (`.lmat`) and returns its reference | Change files |
| `assign_asset` | `entity`, `asset` | Applies a material, texture or model to the entity's Mesh Renderer, or adds the behaviour of a script asset to the entity | Edit the scene |
| `instantiate_model` | `asset`, *`parent`*, *`position`* | Adds a model (`.gltf`, `.glb`, `.obj`, `.stl`) to the scene as a new entity | Edit the scene |

### Editor

| Tool | Parameters | What it does | Permission |
|---|---|---|---|
| `set_camera` | *`position`*, *`lookAt`* | Points the Scene view camera, e.g. before a screenshot | — |
| `undo` | *`steps`* (1-50, default 1) | Undoes the latest steps in the editor (the assistant's or the user's) and lists them | Edit the scene |
| `redo` | *`steps`* (1-50, default 1) | Redoes undone steps | Edit the scene |
| `save_scene` | *`path`* | Saves the scene to its file, or to a new path (e.g. `Scenes/Level.lscene`). Not while playing | Change files |
| `open_scene` | `path`, *`discardChanges`* | Opens another scene; refuses when the open scene has unsaved changes unless `discardChanges` is true | — |
| `play_mode` | `action` (`play`, `stop`, `pause`, `step`) | Runs the game in the editor. Changes made while playing are discarded when it stops | Run Play mode |

While Play mode runs, scene tools change the playing copy of the scene; their answer then includes a note that the
change is discarded when Play stops.

## Example

> Build a small room: a floor, four walls and a point light in the middle. Then add a script that makes the light
> pulse, and show me a screenshot.

The assistant creates the entities with `create_entity`, places them with `set_transform`, writes the script with
`write_script` (fixing it if the compiler reports errors), adds it with `assign_asset` and checks the result with
`screenshot`.
