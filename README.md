# Lapis

Lapis is a small 3D game engine with a visual editor, written entirely in C#.
Build scenes in the editor, write gameplay in C# scripts, and export your game as a standalone app for Windows or Linux.

> **Status:** early development — the editor, C# scripting, physics, assets and export work on Windows and Linux.

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

While the repository is private, these links need a signed-in account: use the [GitHub CLI](https://cli.github.com)
instead (`gh auth login` once), for example on Linux:

```bash
gh release download --repo ikimas777/Lapis-3DGameEngine --pattern Lapis-Linux-x64.tar.gz && tar -xzf Lapis-Linux-x64.tar.gz && ./Lapis/Lapis.Editor
```

More options are under [Download](#download).

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
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

## Requirements

- Windows 10/11 x64, or 64-bit Linux (X11 or Wayland with XWayland)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- GPU with OpenGL 4.5 support

## Building

```bash
dotnet build Lapis.slnx
```

```bash
dotnet test --solution Lapis.slnx
```

## Download

Ready-to-run builds for both systems are on the [Releases](../../releases) page. They include .NET, so
nothing else needs to be installed:

- **Windows:** `Lapis-Windows-x64.zip` — unzip and run `Lapis.Editor.exe`.
- **Linux:** `Lapis-Linux-x64.tar.gz` — extract (`tar -xzf Lapis-Linux-x64.tar.gz`) and run `Lapis/Lapis.Editor`.
  Optional: install `zenity` (or `kdialog`) for the folder chooser and `code` (VS Code) for editing scripts.

Keep the `Player` folder next to the editor: exported games are built from it.

### Updates

The project launcher checks GitHub for a newer release when it opens (this can be turned off in the update
window). When one is found, the **Lapis Update** window shows what is new and downloads the package for your
system — `Lapis-Windows-x64.zip` on Windows, `Lapis-Linux-x64.tar.gz` on Linux — into your Downloads folder.
Close Lapis and unpack it to update; projects are not affected. The **Check for updates** link under the version
number opens the window at any time. While the repository is private, the check reports that no release can be
found.

## Editor executable (Windows)

A ready-to-run editor is committed in the repository root: double-click **`Lapis.Editor.exe`**
(it includes .NET and runs on any 64-bit Windows 10/11 PC). Keep the `Player` folder next to it: exported games
are built from `Player\Lapis.Player.exe`.

To rebuild it after changing the code:

```powershell
.\scripts\publish-editor.ps1 -RepoRoot
```

Without `-RepoRoot` the build only goes to `publish\Lapis.Editor`. Add `-Shortcut` to also put a
"Lapis Editor" shortcut on the desktop. The executable has no console window; its messages are shown in the
Console panel and written to `%LOCALAPPDATA%\Lapis\editor.log`.

On Linux, build the editor with:

```bash
./scripts/publish-editor.sh
```

The result is `publish/Lapis.Editor-linux/Lapis.Editor` (messages go to `~/.local/share/Lapis/editor.log`).

### Publishing a release

Pushing a version tag builds Windows and Linux packages on GitHub Actions and attaches them to a new release:

```bash
git tag v0.2.0
```

```bash
git push origin v0.2.0
```

## Running

```bash
dotnet run --project src/Lapis.Editor
```

The editor starts with the project launcher: pick a recent project, open a project folder, or create a new
project from a starter template. **Project > Close Project** returns to it.

![Project launcher](docs/images/launcher.png)

To skip the launcher and open the sample project directly:

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

### Console

The **Console** panel shows messages from the editor and from `Log.Info` / `Log.Warning` / `Log.Error` in scripts.

- Drag over a message's text to select words or letters (double-click selects a word) and press **Ctrl+C** to
  copy them.
- Click a message's time or the empty space after it to select the whole message (Ctrl+click adds one,
  Shift+click selects a range, Ctrl+A selects all) and press **Ctrl+C**, or right-click for **Copy**,
  **Copy Message Text Only** and **Copy All**.
- **Clear on Play** (next to **Clear**) empties the Console every time Play starts; the choice is remembered.
- Double-click a script error to open the file at that line.

### Assets

Models (glTF, GLB, OBJ, STL), textures and materials live in the project's `Assets` folder and are managed
in the **Assets** panel: drag files in from Explorer (or your Linux file manager), then drag assets into the scene or onto Inspector fields.
See the [assets guide](docs/assets.md).

### Exporting

**File > Export Game...** builds a standalone executable of the open project for the system the editor runs on
(a Windows `.exe`, or a Linux executable)
without the editor or .NET. See the [export guide](docs/export.md).

### Physics

Colliders and rigid bodies are simulated with [Jolt](https://github.com/jrouwe/JoltPhysics) while the game
runs. In the sample, the blue ball (a kinematic body you steer) pushes the crates, and the red ball bounces.
See the [physics section of the scripting guide](docs/scripting.md#physics).

![Physics](docs/images/physics.png)

### Scene view controls

| Input | Action |
|---|---|
| Left click | Select the object under the cursor |
| W A S D / Q E | Fly (hold Shift to go faster) — no mouse button needed |
| Right mouse + drag | Look around (flying keeps working while you hold it) |
| Alt + left mouse + drag | Orbit around the focus point |
| Middle mouse + drag | Pan |
| Mouse wheel | Zoom |
| F | Focus the selected entity |

**Options > Keybindings** lists every shortcut and lets you choose how flying works: with W A S D / Q E at any
time (the default), or only while holding the right mouse button — then W / E / R also pick the
Move / Rotate / Scale tools.

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
| 1 / 2 / 3 (Scene view) | Move / Rotate / Scale tool |
| Ctrl while dragging the gizmo | Toggle snapping |

Drag entities in the Hierarchy to re-parent them, or drop an entity onto an entity / component field in the Inspector to assign it.

### Hierarchy folders

The folder button next to the Hierarchy's search box (or **New Folder** in its right-click menu) adds a folder
for organising the scene. Folders only exist in the editor: they have no position and do not change the game.

- Drag entities or other folders onto a folder to move them in; drag them to the empty space below the list to
  take them out. An entity with a parent moves to the top level (keeping its place in the world) when it is
  put in a folder. Right-click an entity > **Move to Folder** does the same from a menu.
- Double-click a folder (or select it and press F2) to rename it.
- **Delete** (or Del) removes only the folder: what was inside moves up one level. **Delete Folder and
  Contents** also deletes everything inside. Both can be undone.

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
