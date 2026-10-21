using Lapis.Scripting;

namespace Lapis.Simulation;

/// <summary>
/// A running game: the scripts of a scene plus its physics, stepped together.
/// Used by the editor's Play mode and by the standalone player.
/// </summary>
public sealed class GameSimulation : IDisposable
{
    private readonly ScriptRunner _scripts;
    private readonly PhysicsWorld _physics;
    private bool _disposed;

    /// <summary>Starts simulating <paramref name="scene"/>; physics bodies are created immediately.</summary>
    public GameSimulation(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Scene = scene;
        _physics = new PhysicsWorld(scene);
        _scripts = new ScriptRunner(scene)
        {
            FixedDeltaTime = 1f / Math.Clamp(scene.Physics.StepsPerSecond, 10, 240),
        };
        _scripts.PhysicsStep += _physics.Step;
        Lapis.Physics.ActiveWorld = _physics;
    }

    /// <summary>The scene being simulated.</summary>
    public Scene Scene { get; }

    /// <summary>The physics simulation.</summary>
    public PhysicsWorld Physics => _physics;

    /// <summary>Seconds of game time run so far.</summary>
    public double GameTime => _scripts.GameTime;

    /// <summary>Advances scripts and physics by one frame of <paramref name="deltaTime"/> seconds.</summary>
    public void Tick(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _scripts.Tick(deltaTime);
    }

    /// <summary>Calls <c>OnDestroy</c> on the scripts and releases the physics engine.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _scripts.Shutdown();
        if (Lapis.Physics.ActiveWorld == _physics)
        {
            Lapis.Physics.ActiveWorld = null;
        }

        _physics.Dispose();
        _disposed = true;
    }
}
