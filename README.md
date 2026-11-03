# Lapis

Lapis is a small 3D game engine with a visual editor, written entirely in C#.
Build scenes in the editor, write gameplay in C# scripts, and export your game as a standalone app for Windows, Linux
or macOS.

> **Status:** 1.0.0, the first stable version, for Windows and Linux.

**Community:** join the Lapis server on Discord to ask questions, get help, share ideas and show what you make:
[discord.gg/K9vFUdfzw5](https://discord.gg/K9vFUdfzw5).

## What it can do

- **Editor** — Hierarchy, Inspector, Scene and Game views, undo for everything, Play mode that never changes the
  saved scene, rebindable shortcuts. See [the editor guide](docs/editor.md).
- **C# scripting** — scripts in the project's `Assets` folder are compiled and reloaded while the editor runs
  ([scripting guide](docs/scripting.md)).
- **Rendering** — physically based materials, a sky, directional, point and spot lights that all cast shadows,
  fog and screen fades ([lights, fog and fading](docs/scripting.md#lights-fog-and-fading)).
- **Physics** — colliders, rigid bodies and a character controller, simulated with [Jolt](https://github.com/jrouwe/JoltPhysics)
  ([physics](docs/scripting.md#physics)).
- **Assets** — glTF / GLB, OBJ and STL models, textures, materials and sounds, dragged in from the file manager
  ([assets guide](docs/assets.md)).
- **Prefabs** — ready-made objects placed many times and changed in one place ([prefab guide](docs/prefabs.md)).
- **Animation** — skeletons and animations from glTF models exported from Blender ([animation guide](docs/animation.md)).
- **Navigation** — baked navigation meshes and agents that find their way, using [DotRecast](https://github.com/ikpil/DotRecast)
  ([navigation guide](docs/navigation.md)).
- **UI** — canvases with images, text and buttons, anchored for any resolution ([UI guide](docs/ui.md)).
- **Sound** — WAV and OGG, 2D and 3D, through [OpenAL Soft](https://openal-soft.org/) ([sound](docs/scripting.md#sound)).
- **Export** — a standalone game without the editor or .NET ([export guide](docs/export.md)).
- **AI assistants** — a built-in [MCP](https://modelcontextprotocol.io/) server lets Claude Code, Claude Desktop or
  Codex build scenes and scripts while you watch ([MCP guide](docs/mcp.md)).
- **Lapis Monitor** — live performance charts and alerts of the editor or a game ([monitor guide](docs/monitor.md)).

All guides are listed in the [documentation index](docs/README.md).

## Quick start

Download the latest editor, unpack it and run it. Nothing else needs to be installed.

**Linux** (terminal):

```bash
curl -L https://github.com/ikimas777/Lapis-3DGameEngine/releases/latest/download/Lapis-Linux-x64.tar.gz | tar -xz && ./Lapis/Lapis.Editor
```

**Windows** (PowerShell):

```powershell
irm https://github.com/ikimas777/Lapis-3DGameEngine/releases/latest/download/Lapis-Windows-x64.zip -OutFile Lapis.zip; Expand-Archive Lapis.zip .; .\Lapis\Lapis.Editor.exe
```

**macOS** (Terminal; use `Lapis-macOS-x64.zip` on an Intel Mac). Mac builds are published for some versions only;
the newest one is v0.10.0:

```bash
curl -LO https://github.com/ikimas777/Lapis-3DGameEngine/releases/download/v0.10.0/Lapis-macOS-arm64.zip && ditto -x -k Lapis-macOS-arm64.zip . && open Lapis/Lapis.app
```

While the repository is private, these links need a signed-in account: use the [GitHub CLI](https://cli.github.com)
instead (`gh auth login` once), for example on Linux:

```bash
gh release download --repo ikimas777/Lapis-3DGameEngine --pattern Lapis-Linux-x64.tar.gz && tar -xzf Lapis-Linux-x64.tar.gz && ./Lapis/Lapis.Editor
```

The editor opens with the project launcher: create a project from a template, then attach a script like this one to
an entity and press **Play**:

```csharp
using System.Numerics;
using Lapis;

public class PlayerController : Behaviour
{
    public float speed = 5.0f;

    public override void Update()
    {
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        transform.Position += new Vector3(x, 0, -z) * speed * Time.DeltaTime;
    }
}
```

## Download

Ready-to-run builds are on the [Releases](../../releases) page. They include .NET, so nothing else needs to be
installed:

- **Windows:** `Lapis-Windows-x64.zip` — unzip and run `Lapis.Editor.exe`.
- **Linux:** `Lapis-Linux-x64.tar.gz` — extract (`tar -xzf Lapis-Linux-x64.tar.gz`) and run `Lapis/Lapis.Editor`.
- **macOS** (only some versions, currently v0.10.0): `Lapis-macOS-arm64.zip` (Apple Silicon) or
  `Lapis-macOS-x64.zip` (Intel) — unzip and open `Lapis/Lapis.app`. The app is not signed with an Apple developer
  certificate: the first time, right-click it > **Open** > **Open**.

The editor updates itself from the project launcher. The [editor guide](docs/editor.md#starting-the-editor) explains
what is in a package, updates, update channels and version numbers.

## Requirements

- Windows 10/11 x64, 64-bit Linux (X11 or Wayland with XWayland), or macOS 13+ (Apple Silicon or Intel)
- A GPU with OpenGL 4.5 support (on macOS: the system's OpenGL 4.1, which every supported Mac has)
- To build from source: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Building

```bash
dotnet build Lapis.slnx
```

```bash
dotnet test --solution Lapis.slnx
```

```bash
dotnet run --project src/Lapis.Editor
```

A ready-to-run Windows editor is committed in the repository root (`Lapis.Editor.exe`, with `Runtime\` and
`soft_oal.dll` next to it); `scripts/publish-editor.ps1 -RepoRoot` rebuilds it. How the code is organised is
described in [Architecture](docs/architecture.md).

## Repository layout

| Path | Contents |
|---|---|
| `src/Lapis` | Engine library (`Lapis.dll`) — the API game scripts use |
| `src/Lapis.Editor` | The editor application |
| `src/Lapis.Runtime` | Game runtime (the engine without the editor) that runs exported games |
| `src/Lapis.Monitor` | Lapis Monitor: live performance charts, alerts and recorded sessions |
| `samples/Sandbox` | Sample game project |
| `tests/Lapis.Tests` | Engine tests |
| `tests/Lapis.Editor.Tests` | Editor tests |
| `tests/Common` | Helpers shared by both test projects |
| `docs` | Guides (also shipped in every package as `Docs/`) |
| `scripts` | Publishing scripts and the files added to packages |

## Community

Questions, bug reports, ideas and screenshots of your games are welcome on the Lapis Discord server:
**[discord.gg/K9vFUdfzw5](https://discord.gg/K9vFUdfzw5)**. The project launcher links to it too (**Join the
Community**). Bugs can also be reported as GitHub issues.

## License

[MIT](LICENSE)
