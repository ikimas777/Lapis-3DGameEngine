# Lapis

Lapis is a small 3D game engine with a visual editor, written entirely in C#.
Build scenes in the editor, write gameplay in C# scripts, and export your game as a standalone Windows app.

> **Status:** early development (milestone M8 — assets). See [PLAN.md](PLAN.md) for the roadmap and architecture.

```csharp
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

## Requirements

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- GPU with OpenGL 4.5 support

## Building

```bash
dotnet build Lapis.slnx
```

```bash
dotnet test --solution Lapis.slnx
```

## Editor executable

Build a standalone `Lapis.Editor.exe` (includes .NET, runs on any 64-bit Windows 10/11 PC):

```powershell
.\scripts\publish-editor.ps1
```

The file is written to `publish\Lapis.Editor\Lapis.Editor.exe`. Add `-Shortcut` to also put a
"Lapis Editor" shortcut on the desktop. The executable has no console window; its messages are shown in the
Console panel and written to `%LOCALAPPDATA%\Lapis\editor.log`.

## Running

```bash
dotnet run --project src/Lapis.Editor
```

To open the sample project with its scripts:

```bash
dotnet run --project src/Lapis.Editor -- --project samples/Sandbox
```

![Lapis editor](docs/images/editor.png)

### Scripting

Scripts are C# classes in a project's `Assets` folder. The editor compiles them itself and reloads them
while it runs whenever a file is saved — see the [scripting guide](docs/scripting.md).

### Play mode

Press **Play** (Ctrl+P) in the middle of the menu bar to run the game in the Game view. In the sample
project, W A S D / arrow keys move the blue ball and the box spins. **Pause** (Ctrl+Shift+P) freezes the
game and **Step** (Ctrl+Alt+P) advances it by one frame. The menu bar turns blue while playing: changes
made during play are thrown away on **Stop**, and the scene returns to exactly how it was.

![Play mode](docs/images/play-mode.png)

### Assets

Models (glTF, GLB, OBJ, STL), textures and materials live in the project's `Assets` folder and are managed
in the **Assets** panel: drag files in from Explorer, then drag assets into the scene or onto Inspector fields.
See the [assets guide](docs/assets.md).

### Physics

Colliders and rigid bodies are simulated with [Jolt](https://github.com/jrouwe/JoltPhysics) while the game
runs. In the sample, the blue ball (a kinematic body you steer) pushes the crates, and the red ball bounces.
See the [physics section of the scripting guide](docs/scripting.md#physics).

![Physics](docs/images/physics.png)

### Scene view controls

| Input | Action |
|---|---|
| Left click | Select the object under the cursor |
| Right mouse + drag | Look around |
| Right mouse + W A S D / Q E | Fly (hold Shift to go faster) |
| Alt + left mouse + drag | Orbit around the focus point |
| Middle mouse + drag | Pan |
| Mouse wheel | Zoom |
| F | Focus the selected entity |

### Editor shortcuts

| Keys | Action |
|---|---|
| Ctrl+N | New scene |
| Ctrl+O | Open scene |
| Ctrl+S / Ctrl+Shift+S | Save / Save As |
| Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) | Undo / Redo |
| Ctrl+D | Duplicate the selected entity |
| Ctrl+P / Ctrl+Shift+P / Ctrl+Alt+P | Play or stop / Pause / Step |
| Delete | Delete the selected entity |
| W / E / R (Scene view) | Move / Rotate / Scale tool |
| Ctrl while dragging the gizmo | Toggle snapping |

Drag entities in the Hierarchy to re-parent them, or drop an entity onto an entity / component field in the Inspector to assign it.

### Command-line options

`--help` lists them all:

- `--project samples/Sandbox` opens a game project (by default the last opened project is reopened)
- `--scene path/to/file.lscene` opens a scene file
- `--select Box` selects an entity at startup
- `--play` starts in Play mode
- `--save-scene file.lscene` saves the opened scene
- `--capture shot.png` renders a few frames with the default layout, saves a screenshot and exits

The scene file format is described in [docs/scene-format.md](docs/scene-format.md).
The editor stores its window layout in `%LOCALAPPDATA%\Lapis\editor-layout.ini`; use View > Reset Layout to restore the default.

## Repository layout

| Path | Contents |
|---|---|
| `src/Lapis` | Engine library (`Lapis.dll`) — the API game scripts use |
| `src/Lapis.Editor` | The editor application |
| `src/Lapis.Player` | Runtime that runs exported games |
| `samples/Sandbox` | Sample game project |
| `tests/Lapis.Tests` | Unit tests |

## License

[MIT](LICENSE)
