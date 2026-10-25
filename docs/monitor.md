# Lapis Monitor

Lapis Monitor shows what the editor (or an exported game) is doing while it runs: frame time, rendering, physics,
memory, garbage collection, errors and more, as live charts. It raises alerts when something looks wrong and
records every run, so you can look at it again later.

## Starting it

- **From the editor:** **Debug > Lapis Monitor**. The monitor opens and attaches to this editor.
- **On its own:** start `Lapis.Monitor.exe` (Linux: `./Lapis.Monitor`) from the folder with the editor, e.g. from
  a terminal. It waits until a Lapis editor (`Lapis.Editor`) or game runtime (`Lapis.Runtime`) is running and
  attaches by itself. When that program exits, the monitor saves the run and waits for the next one, so it can
  stay open while you restart the editor.
- **From a clone of the repository:** the editor in the repository root (`Lapis.Editor.exe`) is ready to run,
  but the monitor is not committed as a program; build it once from its sources:
  1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`dotnet --version` should
     print 10.x).
  2. Open a terminal in the repository folder.
  3. Run `dotnet build src/Lapis.Monitor`.
  4. Click **Debug > Lapis Monitor** in the editor (again). The editor finds the build in `src/Lapis.Monitor/bin`.

  If the monitor has not been built yet, Debug > Lapis Monitor writes these steps to the Console.
  `dotnet run --project src/Lapis.Monitor` builds and starts it in one go. After pulling changes to the monitor,
  build it again.

Command-line options (`--help` lists them):

- `--pid 1234` attaches to this process (any .NET program works, e.g. an exported game with a custom name)
- `--capture shot.png 10` saves a screenshot of the monitor after 10 seconds and exits

The process box at the top switches to another running program at any time.

## What it shows

| Tab | Charts |
|---|---|
| Overview | FPS, frame time, draw calls, entities, memory, GPU memory, disk space; the most important charts |
| Frame | Frame time (average and slowest frame), CPU time of update + draw, slow frames (> 50 ms), CPU usage |
| Rendering | Scene render time, draw calls, GPU memory of textures and meshes, texture / mesh uploads (cache misses) |
| Simulation | Script + physics time per frame, physics step time, steps per second, entities and bodies, script exceptions |
| Memory | Working set, managed heap, heap generations, Large Object Heap, allocation rate, fragmentation |
| Runtime | Garbage collections, time in GC, .NET exceptions, lock contention, thread pool, JIT |
| Editor | Logged warnings / errors, OpenGL errors, script compile time, reloads, old script versions stuck in memory, MCP calls, free disk space |
| All counters | Every counter with its last, average and maximum value in the shown time range |

**View** chooses the time range (1, 5 or 15 minutes, or everything); **Pause** stops the charts from scrolling.

## Alerts

The **Alerts** tab lists problems as they happen, for example:

- a frame that took over 100 ms (a hitch), or less than 30 FPS for 5 seconds
- errors in the log, OpenGL errors, exceptions thrown by scripts
- old script code that stayed in memory after a reload (a leak)
- textures or meshes uploaded again and again (cache thrashing)
- a managed heap that keeps growing, long garbage collections, full (gen 2) collections
- less than 2 GB of free disk space

The first five seconds after attaching are not checked for slow frames, since the editor is usually still loading.

## Recorded sessions

Every run is saved in its own folder (Debug > Open Monitor Sessions Folder, or **Sessions folder** in the monitor):

- Windows: `%LOCALAPPDATA%\Lapis\Monitor\Sessions`
- Linux: `~/.local/share/Lapis/Monitor/Sessions`

Each folder holds `counters.csv` (every value, once per second), `alerts.log` and `summary.json` (average and
maximum of every counter). The **Sessions** tab opens a recorded run with the same charts. The newest 40 runs are
kept, up to 300 MB in total.

## For engine developers

The engine publishes its counters through a .NET event source named `Lapis`
(`src/Lapis/Diagnostics/EngineMetrics.cs`). It does nothing until a tool listens, so it costs nothing in normal use.
Any .NET diagnostics tool can read it, for example:

```
dotnet-counters monitor -n Lapis.Editor --counters Lapis,System.Runtime
```
