# Lapis — project plan

Lapis is a small 3D game engine with a visual editor, written entirely in C#.
Games are assembled as scenes in the editor and programmed with C# scripts (`Behaviour`).

> Status: **M0 — setup**. This is a living document; it is updated at the end of every milestone.

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
- Scripting API: `GetComponent<T>()`, `AddComponent<T>()`, `Destroy()`, `Instantiate()`, `Scene.Find<T>()`.
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

Estimates are in **work sessions** (one session ≈ one afternoon: Claude writes code, you run and test it).

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

**MVP total: about 17–26 sessions.**

After the MVP: prefabs, audio, animation, in-game UI, particles, editor profiler, Linux / macOS.

---

## 6. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Hot reload leaks (old `Game.dll` cannot unload because something still references it) | the editor never keeps script types outside the scene; unload is verified with a `WeakReference` test; fallback: full reload when entering Play |
| Garbage collector stutter | no allocations in the loop, object pools, `Span<T>`; `SustainedLowLatency` GC mode during play |
| Native libraries (Jolt, cimgui) | NuGet packages ship prebuilt binaries; Windows x64 only for now |
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
| 4 | Target platforms? | **Windows only** for now. |
| 5 | Engine name? | **Lapis** (namespace `Lapis`). |
| 6 | API naming style? | Agreed: lowercase `entity` / `transform` shortcuts in scripts, PascalCase for everything else. |
| 7 | Coordinate system? | **Right-handed**, Y up, −Z forward. |
| 8 | Physics backend? | **Jolt** (JoltPhysicsSharp). |
| 9 | Model formats? | **glTF, GLB, OBJ, STL.** |
| 10 | First game genre? | None specific — build **general-purpose tools** first. |
| 11 | Prefabs and audio in the MVP? | **No**, they can wait until after the MVP. |
| 12 | Who commits? | **Claude commits** completed steps. |
| 13 | Branching? | **One branch per milestone**. |
| 14 | Remote and CI? | **Add GitHub Actions** (build + tests). |
| 15 | License? | **Open source, MIT.** |
| 16 | Language? | **English everywhere**, including documentation. |
| 17 | References to other engines? | **None.** Lapis must not reference or imitate another engine in its code, API names or documentation. |

### Open items
- No `origin` remote is configured yet; CI starts running once the repository is pushed to GitHub.
- Copyright holder name in `LICENSE` is currently "Lapis contributors" — change it if you want your name there.
