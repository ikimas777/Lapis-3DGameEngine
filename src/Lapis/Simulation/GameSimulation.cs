using System.Numerics;
using Lapis.Audio;
using Lapis.Navigation;
using Lapis.Scripting;

namespace Lapis.Simulation;

/// <summary>
/// A running game: a scene's scripts, physics, navigation, animation, UI, sound and screen effects, stepped together.
/// Used by the editor's Play mode and by the standalone runtime.
/// </summary>
/// <remarks>
/// <para>
/// The script API reaches the running game through statics (<see cref="Lapis.Physics"/>, <see cref="NavMesh"/>,
/// <see cref="Screen"/>, <see cref="Cursor"/>, <see cref="UI"/>, sound). A simulation sets all of them up when it
/// starts and clears them when it is disposed, in <see cref="BeginGameState"/> and <see cref="EndGameState"/>.
/// </para>
/// <para>
/// One frame (<see cref="Tick"/>): UI input (buttons are clicked) → scripts' <c>Start</c> and <c>Update</c>, then
/// fixed physics steps with <c>PhysicsUpdate</c>, then <c>PostUpdate</c> → navigation agents → animators → screen
/// fade → sound.
/// </para>
/// </remarks>
public sealed class GameSimulation : IDisposable
{
    private readonly ScriptRunner _scripts;
    private readonly PhysicsWorld _physics;
    private readonly NavigationWorld _navigation;
    private readonly List<Animator> _animators = [];
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
        _navigation = new NavigationWorld(scene);
        BeginGameState();
    }

    /// <summary>The scene being simulated.</summary>
    public Scene Scene { get; }

    /// <summary>The physics simulation.</summary>
    public PhysicsWorld Physics => _physics;

    /// <summary>Seconds of game time run so far.</summary>
    public double GameTime => _scripts.GameTime;

    /// <summary>Advances the game by one frame of <paramref name="deltaTime"/> seconds (see the class remarks for the order).</summary>
    public void Tick(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Buttons are clicked before the scripts' Update, so scripts see the result in the same frame.
        var pointer = Cursor.Locked ? (Vector2?)null : Input.MousePosition * Screen.PixelsPerPoint;
        UI.Update(Scene, new Vector2(Screen.Width, Screen.Height), pointer,
            Input.GetMouseButtonDown(MouseButton.Left), Input.GetMouseButtonUp(MouseButton.Left));

        _scripts.Tick(deltaTime);
        _navigation.Step(deltaTime);
        AdvanceAnimators(Scene, deltaTime, _animators);
        Screen.Advance(deltaTime);
        AudioSystem.Update(Scene);
    }

    /// <summary>Moves every active <see cref="Animator"/> of the scene on by <paramref name="deltaTime"/> seconds.</summary>
    public static void AdvanceAnimators(Scene scene, float deltaTime) => AdvanceAnimators(scene, deltaTime, []);

    /// <summary>Calls <c>OnDestroy</c> on the scripts and releases the physics engine.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _scripts.Shutdown();
        EndGameState();
        _physics.Dispose();
        _disposed = true;
    }

    /// <summary>Moves every active <see cref="Animator"/> of the scene on by <paramref name="deltaTime"/> seconds.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="deltaTime">Seconds of game time.</param>
    /// <param name="buffer">A list to reuse for collecting the animators (cleared first).</param>
    internal static void AdvanceAnimators(Scene scene, float deltaTime, List<Animator> buffer)
    {
        buffer.Clear();
        scene.CollectComponents(buffer);
        foreach (var animator in buffer)
        {
            if (animator.entity.IsActiveInHierarchy)
            {
                animator.Advance(deltaTime);
            }
        }
    }

    /// <summary>Points the script API at this game and starts it from a clean state.</summary>
    private void BeginGameState()
    {
        Lapis.Physics.ActiveWorld = _physics;
        NavMesh.Active = _navigation;
        Screen.Reset(); // every run starts with a clear picture
        Cursor.Reset();
        UI.Reset();
        AudioSystem.BeginGame();
    }

    /// <summary>Stops sounds and screen effects and detaches the script API from this game.</summary>
    private void EndGameState()
    {
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
    }
}
