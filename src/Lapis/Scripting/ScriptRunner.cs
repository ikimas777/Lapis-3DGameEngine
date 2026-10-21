namespace Lapis.Scripting;

/// <summary>
/// Runs the <see cref="Behaviour"/> lifecycle of a scene while the game is playing.
/// </summary>
/// <remarks>
/// Each <see cref="Tick"/>: <c>Start</c> for behaviours that have not started yet, <c>Update</c>,
/// zero or more <c>PhysicsUpdate</c> calls at <see cref="FixedDeltaTime"/> intervals, then <c>PostUpdate</c>.
/// A behaviour that throws an exception is logged and disabled, so one broken script cannot stop the game.
/// </remarks>
public sealed class ScriptRunner(Scene scene)
{
    /// <summary>At most this many physics steps run per frame, so a slow frame cannot snowball.</summary>
    public const int MaxPhysicsStepsPerFrame = 8;

    private readonly List<Behaviour> _behaviours = [];
    private double _accumulator;

    /// <summary>The scene being run.</summary>
    public Scene Scene { get; } = scene ?? throw new ArgumentNullException(nameof(scene));

    /// <summary>Seconds between <c>PhysicsUpdate</c> calls (50 Hz by default).</summary>
    public float FixedDeltaTime { get; set; } = 0.02f;

    /// <summary>Called once per physics step, after <c>PhysicsUpdate</c> (the physics engine hooks in here).</summary>
    public event Action<float>? PhysicsStep;

    /// <summary>Seconds of game time run so far.</summary>
    public double GameTime { get; private set; }

    /// <summary>Number of frames run so far.</summary>
    public long FrameCount { get; private set; }

    /// <summary>
    /// Advances the game by <paramref name="deltaTime"/> seconds. While the scripts run, <see cref="Time"/>
    /// reports this game's time rather than the host application's.
    /// </summary>
    public void Tick(float deltaTime)
    {
        deltaTime = Math.Clamp(deltaTime, 0f, Time.MaxDeltaTime);
        GameTime += deltaTime;
        FrameCount++;

        using (Time.Use(deltaTime, GameTime, FrameCount, FixedDeltaTime))
        {
            RunFrame(deltaTime);
        }
    }

    private void RunFrame(float deltaTime)
    {
        Collect();
        foreach (var behaviour in _behaviours)
        {
            if (!behaviour.HasStarted && behaviour.IsActiveAndEnabled)
            {
                behaviour.HasStarted = true;
                Invoke(behaviour, static b => b.Start(), nameof(Behaviour.Start));
            }
        }

        foreach (var behaviour in _behaviours)
        {
            if (behaviour.HasStarted && behaviour.IsActiveAndEnabled)
            {
                Invoke(behaviour, static b => b.Update(), nameof(Behaviour.Update));
            }
        }

        _accumulator += deltaTime;
        var steps = 0;
        // The small tolerance keeps float rounding (0.25 - 0.1 - 0.1 + 0.05 = 0.0999...) from skipping a step.
        var frameDelta = Time.SetDeltaTime(FixedDeltaTime); // PhysicsUpdate sees the fixed step
        while (_accumulator + 1e-6 >= FixedDeltaTime && steps < MaxPhysicsStepsPerFrame)
        {
            foreach (var behaviour in _behaviours)
            {
                if (behaviour.HasStarted && behaviour.IsActiveAndEnabled)
                {
                    Invoke(behaviour, static b => b.PhysicsUpdate(), nameof(Behaviour.PhysicsUpdate));
                }
            }

            PhysicsStep?.Invoke(FixedDeltaTime);
            _accumulator -= FixedDeltaTime;
            steps++;
        }

        Time.SetDeltaTime(frameDelta);
        if (steps == MaxPhysicsStepsPerFrame)
        {
            _accumulator = 0; // drop the backlog instead of trying to catch up forever
        }

        foreach (var behaviour in _behaviours)
        {
            if (behaviour.HasStarted && behaviour.IsActiveAndEnabled)
            {
                Invoke(behaviour, static b => b.PostUpdate(), nameof(Behaviour.PostUpdate));
            }
        }
    }

    /// <summary>Calls <c>OnDestroy</c> on every started behaviour, e.g. when play mode stops.</summary>
    public void Shutdown()
    {
        Collect();
        foreach (var behaviour in _behaviours)
        {
            behaviour.NotifyDestroyed();
        }
    }

    private void Collect()
    {
        _behaviours.Clear();
        foreach (var root in Scene.Roots)
        {
            Collect(root);
        }
    }

    private void Collect(Transform transform)
    {
        foreach (var component in transform.entity.Components)
        {
            if (component is Behaviour behaviour)
            {
                _behaviours.Add(behaviour);
            }
        }

        foreach (var child in transform.Children)
        {
            Collect(child);
        }
    }

    private static void Invoke(Behaviour behaviour, Action<Behaviour> call, string method)
    {
        // A behaviour destroyed earlier in this frame (by another script) is skipped.
        if (!behaviour.IsAttached)
        {
            return;
        }

        try
        {
            call(behaviour);
        }
        catch (Exception exception)
        {
            behaviour.Enabled = false;
            var where = behaviour.IsAttached ? $" on '{behaviour.entity.Name}'" : string.Empty;
            Log.Error(exception, $"{behaviour.GetType().Name}.{method}{where} threw an exception; the script has been disabled.");
        }
    }
}
