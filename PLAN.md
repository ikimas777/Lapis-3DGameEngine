# Lapis — project plan

Lapis is a small 3D game engine with a visual editor, written entirely in C#.
Games are assembled as scenes in the editor and programmed with C# scripts (`Behaviour`).

> Status: **M12 — Linux support** (done). **All MVP milestones are complete**; next steps are listed under "After the MVP". This is a living document; it is updated at the end of every milestone.

---

## 1. Goals and scope

### In scope (MVP)
- Editor with dockable panels: **Hierarchy**, **Scene view / Game view**, **Inspector**, **Assets**, **Console**.
- Scene model built from **entities and components**.
- 3D renderer: directional and point lights, shadows, skybox, materials, glTF / GLB / OBJ / STL models.
- **C# scripting** with a lifecycle: `Start`, `Update`, `PhysicsUpdate`, `PostUpdate`, `OnDestroy`.
- **Physics**: `RigidBody`, `BoxCollider`, `SphereCollider`, `CapsuleCollider`, `MeshCollider`, triggers, raycasts, collision callbacks.
- Move / rotate / scale gizmos, mouse picking, undo / redo.
- **Play / Pause / Step** mode that restores the scene when stopped.
- Scene and project files (JSON), exporting a game as a standalone `.exe`.

### Out of scope (by design)
- Terrain editor, configurable render pipelines, shader graph, visual scripting.
- Networking / multiplayer, VR, consoles.
- Lightmaps, global illumination.

### After the MVP (in priority order)
Prefabs → audio → skeletal animation → in-game UI (buttons, text) → particles → Linux / macOS.

---

## 2. Technology stack

| Area | Library | Notes |
|---|---|---|
| Runtime | **.NET 10 (LTS)**, C# 14 | |
| Window, input, OpenGL | **Silk.NET** (Windowing, Input, OpenGL) | OpenGL 4.5 Core with Direct State Access |
| Editor UI | **Hexa.NET.ImGui** (docking) + **Hexa.NET.ImGuizmo** | own OpenGL backend for ImGui |
| Math | **System.Numerics** (`Vector3`, `Quaternion`, `Matrix4x4`) | SIMD; extra helpers via `MathUtil` and C# 14 extension members |
| Physics | **JoltPhysicsSharp** (native Jolt) | behind an `IPhysicsWorld` interface so the backend can be swapped |
| Models | **SharpGLTF** for glTF / GLB; own importers for OBJ and STL | |
| Textures | **StbImageSharp** | PNG, JPG, TGA, HDR |
| Serialization | **System.Text.Json** (source generators) | scenes, materials, settings |
| Audio (later) | Silk.NET.OpenAL or miniaudio | |
| Tests | **xUnit v3** | math, serialization, scene logic — no GPU required |
| CI | **GitHub Actions** | build + tests on every push and pull request |

---

## 3. Repository layout

```
GameEngineC#/
├─ Lapis.slnx                      # solution (XML format)
├─ Directory.Build.props           # shared settings: net10.0, nullable, warnings as errors
├─ Directory.Packages.props        # central NuGet package versions
├─ global.json                     # pins the .NET SDK
├─ .editorconfig                   # code style
├─ .github/workflows/build.yml     # CI
├─ PLAN.md  README.md  LICENSE
│
├─ src/
│  ├─ Lapis/                       # Lapis.dll — the only library game scripts reference
│  │  ├─ Core/                     # Application, Time, Input, Log, MathUtil, game loop
│  │  ├─ Scene/                    # Scene, Entity, Component, Transform, Behaviour
│  │  ├─ Components/               # Camera, Light, MeshRenderer, RigidBody, colliders
│  │  ├─ Rendering/                # GL wrappers, Shader, Mesh, Texture, Material, Renderer, shadows
│  │  ├─ Physics/                  # IPhysicsWorld, JoltPhysicsWorld, raycasts, collision events
│  │  ├─ Assets/                   # AssetDatabase, GUIDs, importers (glTF, OBJ, STL, images)
│  │  ├─ Serialization/            # JSON for scenes and components
│  │  └─ Scripting/                # loading Game.dll, hot reload, field reflection
│  │
│  ├─ Lapis.Editor/                # Lapis.Editor.exe
│  │  ├─ Panels/                   # Hierarchy, Inspector, SceneView, GameView, Assets, Console
│  │  ├─ PropertyDrawers/          # float, Vector3, Color, enum, entity / asset references
│  │  ├─ Gizmos/                   # transform gizmos, collider debug draw, grid
│  │  ├─ Commands/                 # undo / redo (command pattern)
│  │  ├─ Project/                  # opening projects, compiling scripts, exporting games
│  │  └─ Theme/                    # dark theme, icons, fonts
│  │
│  └─ Lapis.Player/                # Lapis.Player.exe — runs an exported game without the editor
│
├─ samples/
│  └─ Sandbox/                     # sample game project (scenes + scripts, e.g. PlayerController)
│
├─ tests/
│  └─ Lapis.Tests/
│
└─ docs/                           # scripting API reference, file formats
```

**Why one `Lapis` library instead of many?** Game scripts reference a single assembly, and folders
plus namespaces are enough to keep it organised. If an area grows large it can be split out later.

### Game project layout (created by the editor)

```
MyGame/
├─ MyGame.lapis                    # project file (JSON): name, engine version, start scene
├─ MyGame.csproj                   # generated — scripts compile into Game.dll
├─ Assets/
│  ├─ Scenes/Main.lscene
│  ├─ Scripts/PlayerController.cs
│  ├─ Models/  Textures/  Materials/
│  └─ *.lmeta                      # GUID for every asset (stable references)
├─ Settings/                       # input axes, physics (gravity, layers), graphics
├─ .lapis/                         # cache and temporary files — ignored by git
└─ Builds/                         # exported games — ignored by git
```

---

## 4. Architecture

### 4.1 Layers

```
┌──────────────────────────────────────────────────────────────┐
│ Lapis.Editor   ImGui panels · gizmos · undo/redo · export    │
├──────────────────────────────────────────────────────────────┤
│ Game.dll       user scripts (PlayerController, ...)          │  ← loaded in an AssemblyLoadContext
├──────────────────────────────────────────────────────────────┤
│ Lapis.dll      Scene · Components · Rendering · Physics ·    │
│                Assets · Input · Time · Serialization         │
├──────────────────────────────────────────────────────────────┤
│ Silk.NET (OpenGL, window, input) · Jolt (native) · SharpGLTF │
└──────────────────────────────────────────────────────────────┘
```

`Lapis.Player` uses the same `Lapis.dll` + `Game.dll`, just without the editor.

### 4.2 Scene model

- `Scene` → list of root `Entity` objects.
- `Entity`: name, active flag, a `Transform` (always present), a list of `Component`s.
- `Transform`: local position / rotation / scale, parent, children, cached world matrix (dirty flag).
- `Component` → `Behaviour` (has `Enabled` and lifecycle methods) → user scripts.
- Scripting API: `GetComponent<T>()`, `AddComponent<T>()`, `Destroy()`, `Scene.Find<T>()`; `Instantiate()` (cloning) arrives together with prefabs.
- Inside scripts, `entity` and `transform` are lowercase shortcuts; all other members use PascalCase
  (`Position`, `Rotation`, `DeltaTime`).

The engine deliberately uses an object model rather than a pure ECS: it is simpler to edit and
to script. Performance comes from flat per-type component lists inside the engine (for example,
all `MeshRenderer`s in one list for rendering).

### 4.3 Frame loop

```
1. Input.Poll()
2. Time.Update()                          DeltaTime, Time, FrameCount
3. Start()        — for newly added behaviours
4. Update()       — all enabled behaviours
5. Physics with a fixed-step accumulator (50 Hz by default)
      while (accumulator >= fixedDelta):
         PhysicsUpdate()
         RigidBody ← Transform           (kinematic bodies)
         physics.Step(fixedDelta)
         Transform ← RigidBody           (dynamic bodies)
         OnCollisionBegin / Stay / End, OnTriggerBegin / End
6. PostUpdate()
7. Remove entities marked with Destroy()
8. Render: culling → shadow pass → opaque → skybox → transparent
9. (Editor) ImGui on top
```

Outside Play mode the editor only runs steps 1, 8 and 9.

### 4.4 Rendering

- OpenGL 4.5 Core, Direct State Access, one uniform buffer per camera and one for lights.
- Forward rendering with a **physically based** lighting model (metallic / roughness, as in glTF).
- Shadows: shadow map for the directional light (cascades later), PCF filtering.
- Scene view and Game view render into **framebuffers** shown as images inside ImGui panels.
- Mouse picking: a separate pass writes entity IDs into an `R32UI` texture; the editor reads the pixel under the cursor.
- Draw calls sorted by shader / material; instancing for repeated meshes later.
- Coordinate system: **right-handed, Y up, −Z forward**, 1 unit = 1 metre (matches OpenGL and glTF, no conversion needed).

### 4.5 C# scripting

- Each game project has a generated `.csproj` referencing `Lapis.dll` → `dotnet build` → `Game.dll`.
- The editor loads `Game.dll` in a **collectible `AssemblyLoadContext`**.
- **Hot reload**: a `.cs` change → background compile → serialize scene → unload old context →
  load new one → deserialize. Field values survive the reload.
- The Inspector shows fields through reflection: `public` fields or fields marked `[Serialize]`;
  `[HideInEditor]` hides a field, `[Range(0, 10)]` adds a slider, `[Tooltip("...")]` adds help text.
- Compile errors appear in the **Console**; clicking one opens the file at that line in VS Code.

Example script:

```csharp
using Lapis;

public class PlayerController : Behaviour
{
    [Range(0, 20)] public float speed = 5.0f;

    public override void Update()
    {
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        // -Z is forward, so "Vertical" (W = +1) maps to -z.
        Vector3 direction = Vector3.ClampLength(new Vector3(x, 0, -z), 1f);

        transform.Position += direction * speed * Time.DeltaTime;
    }
}
```

### 4.6 Physics

- `IPhysicsWorld` interface in the engine, `JoltPhysicsWorld` implementation.
- Components: `RigidBody` (mass, drag, gravity, kinematic, axis locks) and colliders with
  `IsTrigger` and a physics material (friction, bounciness).
- `Physics.Raycast`, `Physics.OverlapSphere`, collision layers (matrix in `Settings/`).
- Collider debug drawing in the Scene view.
- Later: character controller (Jolt `CharacterVirtual`), joints.

### 4.7 Assets

- Every file in `Assets/` gets a `.lmeta` sidecar with a **GUID**, so references in scenes survive renames and moves.
- Importers (glTF / GLB / OBJ / STL → meshes and materials, images → textures) write their output to `.lapis/cache/`.
- A `FileSystemWatcher` triggers automatic re-import when files change.

### 4.8 Editor

- Layout: Hierarchy on the left, Scene / Game in the centre, Inspector on the right,
  a bottom dock with Assets and Console tabs (Animation later).
- Toolbar: Move / Rotate / Scale, Local / World, snapping, Play / Pause / Step.
- **Undo / redo**: every editor change is an `IEditorCommand` (`Execute` / `Undo`); consecutive changes
  to the same value merge (dragging a slider = one undo step).
- Play mode: the scene is serialized to memory before playing and restored when stopped.

### 4.9 Conventions

- Everything in **English**: code, comments, documentation, commit messages.
- Lapis is designed independently: docs and code do not refer to or copy other engines.
- `nullable enable`, warnings as errors, file-scoped namespaces, `.editorconfig` enforced on build.
- No allocations in the hot game loop (verified with `dotnet-counters`).
- One commit = one logical change; every milestone ends in a working state.
- Git: one branch per milestone (`milestone/m0-setup`, `milestone/m1-renderer`, ...), merged into `main` when done.

---

## 5. Milestones

Estimates are in **work sessions** (one session ≈ one afternoon of writing code, then running and testing it).

| # | Milestone | Done when | Sessions |
|---|---|---|---|
| **M0** | Setup | solution, projects, packages, CI, license; build and tests pass | 1 |
| **M1** | Window and renderer | Silk.NET window, camera, lit cube, shaders loaded from files | 1–2 |
| **M2** | Scene model | Entity / Transform / Component, hierarchy, `MeshRenderer`, `Camera`, `Light`, JSON save / load, tests | 1–2 |
| **M3** | Editor shell | docking layout, Scene view in a framebuffer, editor camera (fly / orbit), Hierarchy + Inspector (basic fields), Console | 2–3 |
| **M4** | Editor tools | gizmos, Local / World, mouse picking, undo / redo, creating primitives, saving / opening scenes | 2 |
| **M5** | C# scripting | game project, compiling to `Game.dll`, `Behaviour` lifecycle, `Input`, `Time`, fields in Inspector, hot reload, errors in Console | 2–3 |
| **M6** | Play mode | Play / Pause / Step, Game view, scene restore, `PlayerController` moves a cube | 1 |
| **M7** | Physics | RigidBody, four collider types, triggers, raycasts, collision callbacks, debug draw, physics settings | 2–3 |
| **M8** | Assets | Assets panel (icon grid), glTF / GLB / OBJ / STL and texture import, materials, GUID / `.lmeta`, drag & drop into scene and Inspector | 2–3 |
| **M9** | Visual quality | PCF shadows, skybox, PBR, MSAA, editor ground grid, polished dark theme | 2 |
| **M10** | Export | `File → Export`: `Lapis.Player.exe` + `Game.dll` + scenes + assets in one folder | 1–2 |
| **M11** | Project launcher | a start window shown before the editor: list of recent projects (name, path, last opened), open an existing project folder, create a new project from a template (name + location), remove from the list; the editor itself always opens with a project; `Lapis.Editor.exe` starts in the launcher | 1–2 |

**MVP total: about 18–28 sessions.**

Post-MVP milestones:

| # | Milestone | Content | Sessions |
|---|---|---|---|
| **M12** | Linux support | one code base for Windows and Linux: Linux folder chooser, file manager, Trash, VS Code and fonts; Linux player for exports; `scripts/publish-editor.sh`; CI on Windows and Ubuntu; GitHub Releases with Windows and Linux packages built from a version tag | 1–2 |

After the MVP: prefabs, audio, animation, in-game UI, particles, editor profiler, macOS.

---

## 6. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Hot reload leaks (old `Game.dll` cannot unload because something still references it) | the editor never keeps script types outside the scene; unload is verified with a `WeakReference` test; fallback: full reload when entering Play |
| Garbage collector stutter | no allocations in the loop, object pools, `Span<T>`; `SustainedLowLatency` GC mode during play |
| Native libraries (Jolt, cimgui, GLFW) | NuGet packages ship prebuilt binaries for Windows x64 and Linux x64 |
| Scope creep | the next milestone starts only when the current one is done; new ideas go to "After the MVP" |
| OpenGL context or driver errors | `GL_KHR_debug` validation in Debug builds, messages routed to the Console |

---

## 7. Project decisions (Q&A)

Answers given by the project owner on 2026-10-21.

| # | Question | Answer |
|---|---|---|
| 1 | Target .NET version? | **.NET 10 LTS** (installed). |
| 2 | IDE? | **VS Code** — the editor opens scripts there. |
| 3 | Does the GPU support OpenGL 4.5? | **Yes.** |
| 4 | Target platforms? | **Windows only** at first; **Linux** added in M12 (2026-10-21) from the same code base — no separate branch; per-system downloads on GitHub Releases. |
| 5 | Engine name? | **Lapis** (namespace `Lapis`). |
| 6 | API naming style? | Agreed: lowercase `entity` / `transform` shortcuts in scripts, PascalCase for everything else. |
| 7 | Coordinate system? | **Right-handed**, Y up, −Z forward. |
| 8 | Physics backend? | **Jolt** (JoltPhysicsSharp). |
| 9 | Model formats? | **glTF, GLB, OBJ, STL.** |
| 10 | First game genre? | None specific — build **general-purpose tools** first. |
| 11 | Prefabs and audio in the MVP? | **No**, they can wait until after the MVP. |
| 12 | Who commits? | Completed steps are **committed as they are finished**. |
| 13 | Branching? | **One branch per milestone**. |
| 14 | Remote and CI? | **Add GitHub Actions** (build + tests). |
| 15 | License? | **Open source, MIT.** |
| 16 | Language? | **English everywhere**, including documentation. |
| 17 | References to other engines? | **None.** Lapis must not reference or imitate another engine in its code, API names or documentation. |

---

## 8. Progress log

| Milestone | Result |
|---|---|
| M0 | Solution, CI, license, plan. Merged into `main`. |
| M1 | OpenGL 4.5 window (Silk.NET), `Application` frame loop, `Time`, `Log`, GL debug output, `Shader` loaded from files, DSA `Mesh`, cube / plane / sphere primitives, lit preview scene with orbit camera, `--capture` screenshots. Verified on Intel UHD Graphics 620. |
| M2 | `Scene`, `Entity`, `Component`, `Transform` (hierarchy, cached world matrices, Euler helpers, `LookAt`), `Behaviour` lifecycle methods (executed from M5), `Camera`, `Light` (directional + up to 8 point lights), `MeshRenderer` with built-in meshes, `Color`, serialization attributes, JSON `.lscene` format with references and `MissingComponent` ([docs/scene-format.md](docs/scene-format.md)), `SceneRenderer`, sample scene in `samples/Scenes`. 65 tests. |
| M3 | Dear ImGui (Hexa.NET.ImGui 1.92) with an own Silk.NET input layer and OpenGL renderer (dynamic font atlas), dark theme with Segoe UI, docking layout (Hierarchy / Scene + Game / Inspector / Console) saved per user, `Framebuffer` render targets, editor camera (look + fly, orbit, pan, zoom, focus), Hierarchy with search, create / delete / re-parent by drag and drop, Inspector with name / active / transform and reflection-based component editing (numbers, vectors, colours, enums, strings, entity and component references), Add / remove component, Console with level filters and search, File menu with New / Open / Save / Save As. Known gaps: no undo yet (M4), no clipboard in text fields, no prompt for unsaved changes. |
| M4 | Undo / redo for every editor change (`UndoStack` with gesture merging, commands refer to entities by id so they survive delete + undo), transform gizmos (ImGuizmo: move / rotate / scale, local / world, snapping), Scene view toolbar and W / E / R shortcuts, mouse picking with an entity-id render pass (`EntityPicker`), duplicate (Ctrl+D), Edit menu, unsaved-changes tracking based on the history, scene fragments in `SceneSerializer` (`EntitiesToJson` / `EntitiesFromJson` with new ids). Standalone `Lapis.Editor.exe` via `scripts/publish-editor.ps1`. 88 tests (new `Lapis.Editor.Tests`). |
| M5 | Game projects (`.lapis` file, `Assets/`, generated `.csproj` + engine copy for VS Code), `samples/Sandbox` with `PlayerController` and `Rotator`, in-process Roslyn compilation (no SDK needed, ~50–150 ms per change after the first), collectible `AssemblyLoadContext` hot reload that keeps scene values, compile errors with file / line in the Console (double-click opens VS Code), "Edit Script" link in the Inspector, Project menu (new / open project, create script, recompile, open in VS Code), last project reopened on start, `Input` (keys, mouse, axes) and `ScriptRunner` (Start / Update / PhysicsUpdate / PostUpdate / OnDestroy, exceptions disable the script). Scripts start running in Play mode (M6). Guide: [docs/scripting.md](docs/scripting.md). 116 tests. |
| M6 | Play / Pause / Step (menu bar buttons, Ctrl+P / Ctrl+Shift+P / Ctrl+Alt+P, `--play`), the game runs on a JSON copy of the scene with its own undo history and is discarded on Stop (edited scene, history, selection and unsaved state restored), tinted menu bar while playing, Game view gets focus on Play and is the only panel that feeds keyboard / mouse to `Input`, game time in `Time` (stops when paused, one frame per Step, fixed step inside `PhysicsUpdate`), Play stops automatically when scripts are recompiled, saving is blocked while playing. 124 tests. |
| M7 | Jolt physics (JoltPhysicsSharp): `BoxCollider`, `SphereCollider`, `CapsuleCollider`, `MeshCollider` (static / kinematic), triggers, `RigidBody` (dynamic / kinematic, mass, damping, gravity, continuous collision, axis freezing, velocity / force / impulse / torque API), `OnCollisionBegin` / `End` and `OnTriggerBegin` / `End` delivered on the main thread, `Physics.Raycast`, scene physics settings (gravity, steps per second) saved with the scene and editable in the Inspector when nothing is selected, `GameSimulation` (scripts + physics) used by Play mode, bodies added / removed / rebuilt live while playing, collider outlines in the Scene view, Create > Physics items and automatic colliders on primitives. Sandbox demo with crates and a bouncing ball. 137 tests. Collision layers are not done yet (moved to after the MVP). |
| M8 | `AssetDatabase` with `.lmeta` GUID sidecars (references survive renames / moves, copies get new ids, orphans cleaned up), live sync with the disk, importers for glTF / GLB (SharpGLTF, node transforms, embedded images), OBJ (+ MTL colour and texture) and STL (binary / ASCII, Z-up → Y-up), textures (StbImageSharp, sRGB + mipmaps), `.lmat` materials, `MeshRenderer` material / texture slots, Assets panel (folder tree, icons and thumbnails, create folder / script / material / scene, rename, delete to Recycle Bin, move by drag, import, drop files from Explorer), asset Inspector (previews, model stats, material editing), drag & drop of assets into the Scene view, Hierarchy and Inspector slots, model assets usable by `MeshCollider`. Sandbox: checker material and an OBJ pyramid. Guide: [docs/assets.md](docs/assets.md). 150 tests. Import cache in `.lapis/cache` is not needed yet (models import in milliseconds) and is left for later. |
| M9 | Physically based shading (GGX / Smith / Schlick, metallic + roughness on `MeshRenderer` and materials, hemisphere ambient with reflected sky, ACES tone mapping), directional-light shadow map (2048², fitted to the view up to a configurable shadow distance, texel-snapped, normal-offset bias, 5×5 PCF, edge fade, per-renderer / per-light Cast Shadows), procedural skybox with sun disk (scene settings), 4× MSAA in the Scene and Game views, infinite editor ground grid with X / Z axes (Edit > Show Grid). Scene drops: the whole Scene view accepts assets, models land under the cursor, model files dropped from Explorer over the view go straight into the scene. Cascaded shadows, point-light shadows and image-based lighting are left for later. |
| M10 | File > Export Game: compiles scripts to `Game.dll`, copies assets (without `.cs`) to `Data/Assets`, writes `Data/game.json` and the player as `<Name>.exe`; background export with progress, Run Game / Open Folder; `--export` for command-line builds. `Lapis.Player`: loads the manifest, assets and scripts, runs `GameSimulation` and renders through the main camera with 4× MSAA, Alt+Enter fullscreen, `--scene` / `--fullscreen`, `log.txt`. The published editor ships the player in `Player/`; from source it is built on first export into `artifacts/player`. `InputBridge` moved into the engine and shared by editor and player. Verified: Sandbox exported from both the source and the published editor and run standalone. Guide: [docs/export.md](docs/export.md). 155 tests. Loading other scenes from scripts is not available yet. |
| M11 | Project launcher shown when the editor starts without `--project` / `--scene`: recent projects (name, folder, last opened, search, missing ones marked; right-click: open, show in Explorer, remove from list), New Project (name + location with a Windows folder browser, starter scene with camera, sun, ground and cube plus an example script), Open Project (folder browser, typed path, or drop a folder on the window). Project menu: Recent Projects, Close Project (asks to save unsaved changes, returns to the launcher). The last project is no longer reopened automatically. 166 tests. |
| M12 | Linux support from the same code base: `PlatformShell` (file manager via `xdg-open`, freedesktop Trash for deleted assets), folder chooser via zenity / kdialog, VS Code found on the Linux `PATH`, Noto / Ubuntu / DejaVu fonts, the player is built and copied without `.exe` and marked executable, `scripts/publish-editor.sh`, CI matrix (Windows + Ubuntu), `release.yml` publishing `Lapis-Windows-x64.zip` and `Lapis-Linux-x64.tar.gz` for `v*` tags. Options > Keybindings (fly mode toggle) added on main before M12. The Linux build is cross-compiled and passes CI on Ubuntu; not yet run by hand on a Linux desktop. 168 tests. |

### Open items
- The Linux build needs a first run on a real Linux machine (or the Ubuntu CI job) to confirm the editor and exported games start.
- Copyright holder name in `LICENSE` is currently "Lapis contributors" — change it if you want your name there.
