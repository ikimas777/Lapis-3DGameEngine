using Lapis.Audio;
using Lapis.Scripting;

namespace Lapis.Simulation;

/// <summary>
/// A running game: the scripts of a scene plus its physics, stepped together.
/// Used by the editor's Play mode and by the standalone runtime.
/// </summary>
public sealed class GameSimulation : IDisposable
{
    private readonly ScriptRunner _scripts;
    private readonly PhysicsWorld _physics;
    private readonly Navigation.NavigationWorld _navigation;
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
        _navigation = new Navigation.NavigationWorld(scene);
        NavMesh.Active = _navigation;
        Screen.Reset(); // every run starts with a clear picture
        Cursor.Reset();
        UI.Reset();
        AudioSystem.BeginGame();
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

        // Buttons are clicked before the scripts' Update, so scripts see the result in the same frame.
        var pointer = Cursor.Locked ? (System.Numerics.Vector2?)null : Input.MousePosition * Screen.PixelsPerPoint;
        UI.Update(Scene, new System.Numerics.Vector2(Screen.Width, Screen.Height), pointer,
            Input.GetMouseButtonDown(MouseButton.Left), Input.GetMouseButtonUp(MouseButton.Left));

        _scripts.Tick(deltaTime);
        _navigation.Step(deltaTime);
        AdvanceAnimators(Scene, deltaTime);
        Screen.Advance(deltaTime);
        AudioSystem.Update(Scene);
    }

    /// <summary>Moves every active <see cref="Animator"/> of the scene on by <paramref name="deltaTime"/> seconds.</summary>
    public static void AdvanceAnimators(Scene scene, float deltaTime)
    {
        foreach (var entity in scene.AllEntities)
        {
            if (entity.GetComponent<Animator>() is { } animator && entity.IsActiveInHierarchy)
            {
                animator.Advance(deltaTime);
            }
        }
    }

    /// <summary>Calls <c>OnDestroy</c> on the scripts and releases the physics engine.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _scripts.Shutdown();
        Screen.Reset();
        Cursor.Reset();
        UI.Reset();
        AudioSystem.EndGame();
        if (NavMesh.Active == _navigation)
        {
            NavMesh.Active = null;
        }

        if (Lapis.Physics.ActiveWorld == _physics)
        {
            Lapis.Physics.ActiveWorld = null;
        }

        _physics.Dispose();
        _disposed = true;
    }
}
