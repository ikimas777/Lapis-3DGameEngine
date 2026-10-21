# Exporting a game

**File > Export Game...** turns the open project into a standalone Windows game that runs without the
editor and without installing .NET.

1. Choose the output folder (default: `Builds/<project name>` inside the project), the name of the `.exe`
   and the scene the game starts with.
2. Click **Export**. Unsaved changes to the open scene are saved first, and scripts are compiled.
3. When it finishes, use **Run Game** or **Open Folder**. Give the whole folder to players (e.g. as a `.zip`).

```
MyGame/
├─ MyGame.exe        the game (the Lapis player, renamed)
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

## Where the player comes from

The published editor (`scripts/publish-editor.ps1`) includes `Player/Lapis.Player.exe` next to
`Lapis.Editor.exe`. When the editor runs from source with `dotnet run`, the first export builds the player into
`artifacts/player` (this needs the .NET SDK and takes about a minute; later exports reuse it).
