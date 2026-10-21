using Lapis.Simulation;
using Lapis.Serialization;

namespace Lapis.Editor;

/// <summary>Whether the game is running in the editor.</summary>
internal enum PlayState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>
/// Runs the game inside the editor. Play starts the scripts on a copy of the scene; Stop throws the
/// copy away, so the edited scene is exactly as it was before.
/// </summary>
internal sealed class PlayMode(EditorContext context) : IDisposable
{
    /// <summary>Time advanced by one <see cref="Step"/>.</summary>
    public const float StepDeltaTime = 1f / 60f;

    private GameSimulation? _game;

    public PlayState State { get; private set; } = PlayState.Stopped;

    public bool IsActive => State != PlayState.Stopped;

    /// <summary>Raised when Play is pressed, before the game is set up (not when resuming from pause).</summary>
    public event Action? Starting;

    /// <summary>Raised after play mode starts.</summary>
    public event Action? Started;

    /// <summary>Raised after play mode stops and the edited scene is back.</summary>
    public event Action? Stopped;

    /// <summary>Starts the game, or resumes it when paused.</summary>
    public void Play()
    {
        if (State == PlayState.Paused)
        {
            State = PlayState.Playing;
            return;
        }

        if (State == PlayState.Playing)
        {
            return;
        }

        Starting?.Invoke();

        // A deep copy through the scene format, so the game cannot change the edited scene.
        var playScene = SceneSerializer.FromJson(SceneSerializer.ToJson(context.Scene));
        context.EnterPlay(playScene);
        _game = new GameSimulation(playScene);
        Input.Reset();
        State = PlayState.Playing;
        Log.Info("Play mode started.");
        Started?.Invoke();
    }

    /// <summary>Stops the game and restores the edited scene.</summary>
    public void Stop()
    {
        if (State == PlayState.Stopped)
        {
            return;
        }

        _game?.Dispose();
        _game = null;
        context.ExitPlay();
        Input.Reset();
        State = PlayState.Stopped;
        Log.Info("Play mode stopped; the scene is back to how it was before Play.");
        Stopped?.Invoke();
    }

    /// <summary>Starts or stops the game.</summary>
    public void TogglePlay()
    {
        if (IsActive)
        {
            Stop();
        }
        else
        {
            Play();
        }
    }

    /// <summary>Pauses a running game, or resumes a paused one.</summary>
    public void TogglePause()
    {
        State = State switch
        {
            PlayState.Playing => PlayState.Paused,
            PlayState.Paused => PlayState.Playing,
            _ => State,
        };
    }

    /// <summary>Advances a paused game by one frame (starting and pausing it first if needed).</summary>
    public void Step()
    {
        if (State == PlayState.Stopped)
        {
            Play();
        }

        State = PlayState.Paused;
        _game?.Tick(StepDeltaTime);
    }

    /// <summary>Stops the game if it is running.</summary>
    public void Dispose() => Stop();

    /// <summary>Advances the game by one frame when it is playing. Call once per editor frame.</summary>
    public void Update(float deltaTime)
    {
        if (State == PlayState.Playing)
        {
            _game?.Tick(deltaTime);
        }
    }
}
