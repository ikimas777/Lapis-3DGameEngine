# Exporting a game

**File > Export Game...** turns the open project into a standalone game for the system the editor runs on
(Windows, Linux or macOS) that runs without the
editor and without installing .NET.

1. Choose the output folder (default: `Builds/<project name>` inside the project), the name of the `.exe`
   and the scene the game starts with.
2. Click **Export**. Unsaved changes to the open scene are saved first, and scripts are compiled.
3. When it finishes, use **Run Game** or **Open Folder**. Give the whole folder to players (e.g. as a `.zip`).

```
MyGame/
├─ MyGame.exe        the game (the Lapis game runtime, renamed)
├─ Game.dll          your compiled scripts (+ Game.pdb for readable error messages)
├─ Data/
│  ├─ game.json      game name and start scene
│  └─ Assets/        scenes, models, textures, materials and their .lmeta files
└─ log.txt           written while the game runs; look here if something goes wrong
```

Script sources (`.cs`) are not copied. Exporting again replaces `Data`, `Game.dll` and the `.exe`;
other files you put in the folder are left alone.

## Running the game

- The game opens in a 1280 × 720 window; **Alt+Enter** switches to fullscreen and back.
- `MyGame.exe --fullscreen` starts in fullscreen; `--scene Assets/Scenes/Level2.lscene` starts with another scene.
- The game shows the scene through its main camera (the active Camera with the highest priority).

## From the command line

The editor can export without opening a window, e.g. for automated builds:

```bash
Lapis.Editor.exe --project path/to/MyGame --export path/to/output
```

This exports the project's start scene and returns exit code 1 when the export fails.

## Where the game runtime comes from

The published editor (`scripts/publish-editor.ps1`) includes `Runtime/Lapis.Runtime.exe` (the engine without the editor) next to
`Lapis.Editor.exe`. When the editor runs from source with `dotnet run`, the first export builds the runtime into
`artifacts/runtime` (this needs the .NET SDK and takes about a minute; later exports reuse it).

## Linux

The editor exports for the system it runs on. On Linux the game's executable has no extension (`MyGame`
instead of `MyGame.exe`) and is marked executable; give players the folder as a `.tar.gz` so the permission is
kept (`tar -czf MyGame.tar.gz MyGame`). To make a Windows build of a game, export it from the Windows editor,
and the other way round.

The Linux editor ships `Runtime/Lapis.Runtime`; build it with `scripts/publish-editor.sh`.

## macOS

On macOS the game is an app: `MyGame.app`, with the program in `Contents/MacOS`, the compiled scripts and the
`Data` folder in `Contents/Resources`, and an `Info.plist`. The editor signs it ad hoc (Apple Silicon Macs only
run signed programs). Give players the app as a `.zip` made in Finder (right-click > Compress) or with
`ditto -c -k --keepParent MyGame.app MyGame.zip`, which keeps the signature. Because it is not signed with an
Apple developer certificate, players open it the first time with right-click > **Open**. The game writes its
log to `~/Library/Logs/MyGame/log.txt`.

The Mac editor ships `Lapis.app/Contents/MacOS/Runtime/Lapis.Runtime`; build it with `scripts/publish-editor.sh`
on a Mac.
