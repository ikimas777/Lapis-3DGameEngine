using System.Collections.Concurrent;
using System.Numerics;
using JoltPhysicsSharp;
using Lapis.Diagnostics;

namespace Lapis.Simulation;

/// <summary>
/// Simulates the colliders and rigid bodies of a scene with the Jolt physics engine.
/// </summary>
/// <remarks>
/// Each step: bodies are created / removed / rebuilt to match the scene, static and kinematic bodies
/// follow their transforms, the simulation advances, dynamic bodies write their result back to their
/// transforms, and collision / trigger events are sent to behaviours. Jolt reports contacts from worker
/// threads, so they are queued and delivered on the calling thread after the step.
/// The class is split by topic into PhysicsWorld.Bodies.cs, .Shapes.cs, .Characters.cs and .Contacts.cs.
/// </remarks>
public sealed partial class PhysicsWorld : IDisposable
{
    private const int MaxBodies = 16384;
    private static readonly ObjectLayer NonMoving = new(0);
    private static readonly ObjectLayer Moving = new(1);
    private static readonly Lock InitLock = new();
    private static bool _joltInitialized;

    private readonly Scene _scene;
    private readonly PhysicsSystem _system;
    private readonly JobSystemThreadPool _jobs;
    private readonly ObjectLayerPairFilterTable _layerPairs;
    private readonly BroadPhaseLayerInterfaceTable _broadPhaseLayers;
    private readonly ObjectVsBroadPhaseLayerFilterTable _objectVsBroadPhase;
    private readonly Dictionary<Collider, BodyRecord> _byCollider = [];
    private readonly Dictionary<uint, BodyRecord> _byBodyId = [];
    private readonly ConcurrentQueue<ContactEvent> _contactEvents = new();
    private readonly Dictionary<(uint, uint), int> _contactCounts = [];
    private readonly List<Collider> _sceneColliders = [];
    private readonly HashSet<Collider> _seen = [];
    private readonly List<BodyRecord> _toRemove = [];
    private readonly HashSet<Entity> _warnedEntities = [];
    private readonly Dictionary<CharacterController, CharacterRecord> _characters = [];
    private readonly HashSet<CharacterController> _seenCharacters = [];
    private readonly CharacterBodyFilter _characterBodyFilter;
    private readonly AllShapesFilter _characterShapeFilter = new();
    private bool _disposed;

    /// <summary>Creates the simulation and a body for every collider in <paramref name="scene"/>.</summary>
    public PhysicsWorld(Scene scene)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        EnsureJoltInitialized();

        // Two layers: non-moving bodies never collide with each other, moving bodies collide with everything.
        _layerPairs = new ObjectLayerPairFilterTable(2);
        _layerPairs.EnableCollision(NonMoving, Moving);
        _layerPairs.EnableCollision(Moving, Moving);

        _broadPhaseLayers = new BroadPhaseLayerInterfaceTable(2, 2);
        _broadPhaseLayers.MapObjectToBroadPhaseLayer(NonMoving, new BroadPhaseLayer(0));
        _broadPhaseLayers.MapObjectToBroadPhaseLayer(Moving, new BroadPhaseLayer(1));
        _objectVsBroadPhase = new ObjectVsBroadPhaseLayerFilterTable(_broadPhaseLayers, 2, _layerPairs, 2);

        _system = new PhysicsSystem(new PhysicsSystemSettings
        {
            MaxBodies = MaxBodies,
            MaxBodyPairs = MaxBodies * 4,
            MaxContactConstraints = MaxBodies * 4,
            ObjectLayerPairFilter = _layerPairs,
            BroadPhaseLayerInterface = _broadPhaseLayers,
            ObjectVsBroadPhaseLayerFilter = _objectVsBroadPhase,
        })
        {
            Gravity = scene.Physics.Gravity,
        };

        _jobs = new JobSystemThreadPool();
        _characterBodyFilter = new CharacterBodyFilter(this);
        _system.OnContactAdded += OnContactAdded;
        _system.OnContactRemoved += OnContactRemoved;

        SyncBodies(0f);
        _system.OptimizeBroadPhase();
    }

    /// <summary>Gravity of the simulation.</summary>
    public Vector3 Gravity => _system.Gravity;

    /// <summary>Number of bodies in the simulation.</summary>
    public int BodyCount => _byCollider.Count;

    /// <summary>Advances the simulation by <paramref name="deltaTime"/> seconds.</summary>
    public void Step(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var metricsStart = EngineMetrics.Start();
        _system.Gravity = _scene.Physics.Gravity;
        SyncBodies(deltaTime);
        UpdateCharacters(deltaTime);

        var error = _system.Update(deltaTime, 1, _jobs);
        if (error != PhysicsUpdateError.None)
        {
            Log.Warning($"Physics step reported {error}; the scene may have too many touching bodies.");
        }

        WriteBackDynamicBodies();
        DispatchContactEvents();
        EngineMetrics.EndPhysicsStep(metricsStart);
    }

    /// <summary>Casts a ray against all colliders, including triggers.</summary>
    internal bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit)
    {
        hit = default;
        if (direction.LengthSquared() < 1e-12f || maxDistance <= 0f)
        {
            return false;
        }

        var unit = Vector3.Normalize(direction);
        var ray = new Ray(origin, unit * maxDistance);
        var result = RayCastResult.Default;
        if (!_system.NarrowPhaseQuery.CastRay(ray, out result, null!, null!, null!) ||
            !_byBodyId.TryGetValue(result.BodyID.ID, out var record))
        {
            return false;
        }

        var distance = result.Fraction * maxDistance;
        var point = origin + unit * distance;
        var normal = record.Body?.GetWorldSpaceSurfaceNormal(new SubShapeID(result.subShapeID2), point)
            ?? CapsuleNormal((CharacterController)record.Collider, point);
        hit = new RaycastHit(record.Collider, point, normal, distance);
        return true;
    }

    internal Vector3 GetVelocity(RigidBody body) =>
        TryGetBody(body, out var record) ? _system.BodyInterface.GetLinearVelocity(record.Id) : Vector3.Zero;

    internal void SetVelocity(RigidBody body, Vector3 velocity)
    {
        if (TryGetBody(body, out var record))
        {
            _system.BodyInterface.SetLinearVelocity(record.Id, velocity);
            _system.BodyInterface.ActivateBody(record.Id);
        }
    }

    internal Vector3 GetAngularVelocity(RigidBody body) =>
        TryGetBody(body, out var record) ? _system.BodyInterface.GetAngularVelocity(record.Id) : Vector3.Zero;

    internal void SetAngularVelocity(RigidBody body, Vector3 velocity)
    {
        if (TryGetBody(body, out var record))
        {
            _system.BodyInterface.SetAngularVelocity(record.Id, velocity);
            _system.BodyInterface.ActivateBody(record.Id);
        }
    }

    internal void AddForce(RigidBody body, Vector3 force)
    {
        if (TryGetBody(body, out var record))
        {
            _system.BodyInterface.AddForce(record.Id, force);
            _system.BodyInterface.ActivateBody(record.Id);
        }
    }

    internal void AddImpulse(RigidBody body, Vector3 impulse)
    {
        if (TryGetBody(body, out var record))
        {
            _system.BodyInterface.AddImpulse(record.Id, impulse);
            _system.BodyInterface.ActivateBody(record.Id);
        }
    }

    internal void AddTorque(RigidBody body, Vector3 torque)
    {
        if (TryGetBody(body, out var record))
        {
            _system.BodyInterface.AddTorque(record.Id, torque);
            _system.BodyInterface.ActivateBody(record.Id);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _system.OnContactAdded -= OnContactAdded;
        _system.OnContactRemoved -= OnContactRemoved;

        foreach (var record in _byCollider.Values.ToList())
        {
            DestroyBody(record);
        }

        foreach (var character in _characters.Values.ToList())
        {
            DestroyCharacter(character);
        }

        _characterBodyFilter.Dispose();
        _characterShapeFilter.Dispose();
        _system.Dispose();
        _jobs.Dispose();
        _objectVsBroadPhase.Dispose();
        _broadPhaseLayers.Dispose();
        _layerPairs.Dispose();
        _disposed = true;
    }

    private static void EnsureJoltInitialized()
    {
        lock (InitLock)
        {
            if (_joltInitialized)
            {
                return;
            }

            if (!Foundation.Init(false))
            {
                throw new InvalidOperationException("The Jolt physics engine could not be initialised.");
            }

            _joltInitialized = true;
        }
    }

    private bool TryGetBody(RigidBody body, out BodyRecord record)
    {
        if (body.IsAttached && body.entity.GetComponent<Collider>() is { } collider && _byCollider.TryGetValue(collider, out var found))
        {
            record = found;
            return true;
        }

        record = null!;
        return false;
    }
}
