Lapis - a small 3D game engine with a visual editor, written in C#
==================================================================

What is in this folder
----------------------
  Lapis.Editor(.exe)      The editor. Start it to create or open a game project.
  Runtime/                The game runtime: the engine without the editor. File > Export Game
                          copies it next to your game as MyGame(.exe) together with your compiled
                          scripts and assets. Keep this folder next to the editor, or exporting
                          games will not work.
  Lapis.Monitor(.exe)     Live performance monitor: frame time, rendering, physics, memory,
                          errors and alerts. Start it from the editor (Debug > Lapis Monitor)
                          or on its own; it attaches to the running editor by itself.
  Docs/                   Guides: scripting.md, assets.md, export.md, mcp.md (AI assistants),
                          monitor.md, scene-format.md.
  README.txt              This file.
  LICENSE.txt             MIT licence.

Nothing has to be installed: .NET and all libraries are built into the programs.
A graphics card with OpenGL 4.5 is needed.

Getting started
---------------
  1. Start Lapis.Editor (on Linux: ./Lapis.Editor).
  2. In the launcher, click New Project, choose a name and a folder, and click Create.
  3. Scripts go in Assets/Scripts. Project > Create Script... makes one and opens it in VS Code.
  4. Drag a script onto an object in the Hierarchy to attach it, then press Play (Ctrl+P).

Useful shortcuts
----------------
  W A S D / Q E           Move the Scene view camera (Shift = faster)
  Right mouse + drag      Look around          Alt + left mouse   Orbit
  Middle mouse + drag     Pan                  Mouse wheel        Zoom
  F                       Focus the selected object
  1 / 2 / 3               Move / Rotate / Scale tool
  Ctrl+S                  Save the scene       Ctrl+Z / Ctrl+Y    Undo / Redo
  Ctrl+P                  Play / Stop          Ctrl+D             Duplicate
  Ctrl+click / Shift+click  Select several objects (Hierarchy, Scene view, Assets)
  Options > Keybindings lists them all and lets you change them.

Updates
-------
The launcher checks for new versions. "Update to x.y.z" replaces the programs, the
documentation and this file in this folder; your projects and settings are not touched.

Messages from the editor are also written to:
  Windows: %LOCALAPPDATA%\Lapis\editor.log
  Linux:   ~/.local/share/Lapis/editor.log
Lapis Monitor records every run in the Monitor\Sessions folder next to that log
(Debug > Open Monitor Sessions Folder).

Source code, releases and issues: https://github.com/ikimas777/Lapis-3DGameEngine
