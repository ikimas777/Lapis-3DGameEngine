# Architecture

How the parts of Lapis fit together and where each system lives in the source code. It is meant for people who
want to change the engine or the editor; making games needs only the other [guides](README.md).

## The programs

```
            ┌──────────────┐      ┌──────────────┐      ┌──────────────┐
            │ Lapis.Editor │      │ Lapis.Runtime│      │ Lapis.Monitor│
            │  (ImGui, MCP,│      │ (runs an     │      │ (live charts │
            │   Roslyn)    │      │  exported    │      │  of a running│
            │              │      │  game)       │      │  editor/game)│
            └──────┬───────┘      └──────┬───────┘      └──────┬───────┘
                   └─────────────┬───────┴─────────────────────┘
                          ┌──────┴───────┐
                          │    Lapis     │  engine library (Lapis.dll):
                          │              │  the API game scripts use
                          └──────────────┘
```

| Project | What it is |
|---|---|
| `src/Lapis` | The engine: scene model, rendering (OpenGL through Silk.NET), physics (Jolt), navigation (DotRecast), animation, UI, sound (OpenAL Soft), assets, serialization. Game scripts reference only this library. |
| `src/Lapis.Editor` | The editor: Dear ImGui panels, undo, Play mode, script compiling (Roslyn), the project launcher and updates, game export, the MCP server for AI assistants. |
| `src/Lapis.Runtime` | The game host that exported games are built from: opens a window, loads the game's scripts (`Game.dll`) and start scene from `Data/`, and runs them. |
| `src/Lapis.Monitor` | Lapis Monitor: reads the engine's `Lapis` EventSource counters of a running editor or game and draws charts, raises alerts and records sessions. |
| `tests/Lapis.Tests` | Engine tests (xUnit). |
| `tests/Lapis.Editor.Tests` | Editor tests: commands, edit rules, Play mode, MCP tools, export, updates. |
| `tests/Common` | Helpers shared by both test projects (linked into each, not a project of its own). |

## The engine (`src/Lapis`)

| Folder | Contents |
|---|---|
| `Core/` | `Application` (window and main loop), `Time`, `Log`, `Color`, `Rect`, `Screen` (size, fades), math helpers |
| `Scene/` | `Scene`, `Entity`, `Transform`, `Component`, `Behaviour` (the script base class), editor-only `SceneFolder` |
| `Components/` | `MeshRenderer`, `Camera`, `Light`, `Animator`, `AudioSource` / `AudioListener`, `NavMeshAgent` |
| `Physics/` | Colliders, `RigidBody`, `CharacterController`, the script API `Physics` (raycasts) |
| `Simulation/` | `GameSimulation` (one running game) and `PhysicsWorld*` — the only code that talks to Jolt |
| `Navigation/` | `NavMeshBaker` (bakes from colliders), `NavigationWorld` (paths and the agent crowd), the script API `NavMesh` |
| `Animation/` | `ModelSkeleton` (bones, bone matrices) and `AnimationClip` |
| `UI/` | `Canvas`, `RectTransform`, `Image`, `Text`, `Button`, layout and clicks |
| `Audio/` | `AudioSystem` (OpenAL Soft) and WAV / OGG decoding |
| `Rendering/` | `SceneRenderer` (lit pass, sky, fog, fades), shadow maps, meshes, textures, fonts, `UIRenderer`, picking, editor grid and lines |
| `Shaders/` | GLSL shaders, embedded in the library |
| `Assets/` | `AssetDatabase` (asset ids in `.lmeta` files), materials, model importers (glTF, OBJ, STL) |
| `Serialization/` | `SceneSerializer*` (scenes, prefabs, copied entities as JSON) and the rules for which members are saved and shown |
| `Scripting/` | `ScriptRunner` — calls the `Behaviour` methods (`Start`, `Update`, …) |
| `Projects/` | The `.lapis` project file and an exported game's manifest |
| `Input/`, `Platform/` | Keyboard, mouse and cursor state for scripts, fed by the window |
| `Diagnostics/` | `EngineMetrics`, the counters Lapis Monitor reads |

The renderer uses only OpenGL 4.1 features, so the same code runs on macOS; Windows and Linux ask for a 4.5 context.

### Scenes and components

A `Scene` owns its entities; every entity has a `Transform` and a list of components. The scene also keeps a list
of its components by type, so systems find theirs without walking the tree: `scene.CollectComponents<Animator>(list)`
returns every animator in the order they were added. Entities and components are saved with `SceneSerializer`;
which fields and properties are saved (and shown in the Inspector) is decided in one place, `SerializedMember`.

### One frame of a running game

`GameSimulation` runs a game, both in the editor's Play mode and in the runtime. When it starts it points the static
script API (`Physics`, `NavMesh`, `Screen`, `Cursor`, `UI`, sound) at itself, and it detaches them again when it
ends. Every frame (`GameSimulation.Tick`):

1. **UI input** — buttons under the mouse are hovered, pressed and clicked.
2. **Scripts** — `Start` for new scripts, then `Update`.
3. **Physics** — fixed steps of Jolt, each followed by the scripts' `PhysicsUpdate`; then `PostUpdate`.
4. **Navigation** — agents move along their paths and out of each other's way.
5. **Animators** — bone poses are advanced and blended.
6. **Screen fade** and **sound** (listener and 3D sources follow their entities).

After the tick the host draws the frame: shadow maps, the scene from the main camera, the UI and the fade.

## The editor (`src/Lapis.Editor`)

| Place | Contents |
|---|---|
| `EditorApplication*.cs` | The main loop, the dock layout, menus and shortcuts, popups, Play mode, export, the project launcher, MCP start-up |
| `EditorContext.cs` | Shared editor state: the open scene, its file, the selection, the undo stack, prefab mode |
| `EditPolicy.cs` | The one place that decides what may be changed (for example nothing inside a prefab copy); every panel and MCP tool asks it |
| `Commands/` | Undoable commands (`IEditorCommand`); every change to the scene goes through `UndoStack` |
| `Panels/` | Hierarchy, Inspector, Scene view, Game view, Scene Settings, Navigation, Assets, Console; large panels are split into partial files by topic |
| `Inspector/` | Component drawers: extra Inspector UI for one component type (scripts, Rect Transforms, colliders, animators) |
| `PropertyDrawers/` | `PropertyGrid`, which draws one field of any type |
| `SceneView/` | Editor camera, gizmos (colliders, lights, cameras, navigation mesh) and UI handles |
| `Scripting/` | Watching `Assets`, compiling scripts with Roslyn and loading them into a collectible context so they can be reloaded |
| `Mcp/` | The MCP server: tool declarations (`LapisMcpTools`) and their implementations (`McpEditorTools*`), run on the editor thread |
| `Launcher/` | The project launcher, update check and installer |
| `Export/` | `GameExporter`: copies the runtime, the compiled scripts and the project's data into a game folder |

**Play mode** saves the scene to JSON, plays a copy and throws the copy away on Stop, so nothing done while playing
reaches the saved scene. **Adding Inspector UI for a component** means writing a `ComponentDrawer` and listing it
in `ComponentDrawers.Create`, not changing `InspectorPanel`.

## Building, testing and publishing

```bash
dotnet build Lapis.slnx
dotnet test --solution Lapis.slnx
```

`scripts/publish-editor.ps1` (Windows) and `scripts/publish-editor.sh` (Linux, macOS) publish the editor, the runtime
and the monitor as single-file programs; OpenAL Soft stays a separate library because of its LGPL licence. The
GitHub workflows in `.github/workflows/` build and test pushes to `main` and milestone branches and pull requests on
Windows and Linux, and turn a `v*` tag into a release.
