using System.Collections.Concurrent;
using System.Numerics;
using JoltPhysicsSharp;

namespace Lapis.Simulation;

/// <summary>
/// Simulates the colliders and rigid bodies of a scene with the Jolt physics engine.
/// </summary>
/// <remarks>
/// Each step: bodies are created / removed / rebuilt to match the scene, static and kinematic bodies
/// follow their transforms, the simulation advances, dynamic bodies write their result back to their
/// transforms, and collision / trigger events are sent to behaviours. Jolt reports contacts from worker
/// threads, so they are queued and delivered on the calling thread after the step.
/// </remarks>
public sealed class PhysicsWorld : IDisposable
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

        _system.Gravity = _scene.Physics.Gravity;
        SyncBodies(deltaTime);

        var error = _system.Update(deltaTime, 1, _jobs);
        if (error != PhysicsUpdateError.None)
        {
            Log.Warning($"Physics step reported {error}; the scene may have too many touching bodies.");
        }

        WriteBackDynamicBodies();
        DispatchContactEvents();
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
        var normal = record.Body.GetWorldSpaceSurfaceNormal(new SubShapeID(result.subShapeID2), point);
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

    // ---------------------------------------------------------------- scene -> bodies

    private void SyncBodies(float deltaTime)
    {
        CollectColliders();

        _seen.Clear();
        foreach (var collider in _sceneColliders)
        {
            _seen.Add(collider);
            var rigidBody = collider.entity.GetComponent<RigidBody>();
            var key = ShapeKey.Of(collider, rigidBody);

            if (_byCollider.TryGetValue(collider, out var record))
            {
                if (record.Key != key)
                {
                    DestroyBody(record); // changed in the Inspector while playing: rebuild
                    record = CreateBody(collider, rigidBody, key);
                }
            }
            else
            {
                record = CreateBody(collider, rigidBody, key);
            }

            if (record is not null)
            {
                FollowTransform(record, deltaTime);
            }
        }

        _toRemove.Clear();
        foreach (var record in _byCollider.Values)
        {
            if (!_seen.Contains(record.Collider))
            {
                _toRemove.Add(record);
            }
        }

        foreach (var record in _toRemove)
        {
            DestroyBody(record);
        }
    }

    private void CollectColliders()
    {
        _sceneColliders.Clear();
        foreach (var root in _scene.Roots)
        {
            CollectColliders(root);
        }
    }

    private void CollectColliders(Transform transform)
    {
        var entity = transform.entity;
        if (!entity.IsActive)
        {
            return;
        }

        Collider? first = null;
        foreach (var component in entity.Components)
        {
            if (component is not Collider collider)
            {
                continue;
            }

            if (first is null)
            {
                first = collider;
            }
            else if (_warnedEntities.Add(entity))
            {
                Log.Warning($"'{entity.Name}' has more than one collider; only the first ({first.GetType().Name}) is used.");
            }
        }

        if (first is not null)
        {
            _sceneColliders.Add(first);
        }
        else if (entity.GetComponent<RigidBody>() is not null && _warnedEntities.Add(entity))
        {
            Log.Warning($"'{entity.Name}' has a RigidBody but no collider, so physics ignores it.");
        }

        foreach (var child in transform.Children)
        {
            CollectColliders(child);
        }
    }

    private BodyRecord? CreateBody(Collider collider, RigidBody? rigidBody, ShapeKey key)
    {
        var transform = collider.transform;
        var motion = rigidBody?.Type switch
        {
            RigidBodyType.Dynamic => MotionType.Dynamic,
            RigidBodyType.Kinematic => MotionType.Kinematic,
            // A trigger without a RigidBody is kinematic so it can detect static objects as well.
            _ => collider.IsTrigger ? MotionType.Kinematic : MotionType.Static,
        };

        Shape? shape;
        try
        {
            shape = CreateShape(collider, transform.LossyScale, motion);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Log.Warning($"Could not create the collider of '{collider.entity.Name}': {exception.Message}");
            return null;
        }

        if (shape is null)
        {
            return null;
        }

        var position = transform.Position;
        var rotation = transform.Rotation;
        using var settings = new BodyCreationSettings(shape, position, rotation, motion, motion == MotionType.Static ? NonMoving : Moving)
        {
            Friction = collider.Friction,
            Restitution = collider.Bounciness,
            IsSensor = collider.IsTrigger,
            CollideKinematicVsNonDynamic = collider.IsTrigger,
        };

        if (rigidBody is not null)
        {
            settings.LinearDamping = rigidBody.LinearDamping;
            settings.AngularDamping = rigidBody.AngularDamping;
            settings.GravityFactor = rigidBody.UseGravity ? 1f : 0f;
            settings.MotionQuality = rigidBody.ContinuousCollision ? MotionQuality.LinearCast : MotionQuality.Discrete;
            settings.AllowedDOFs = AllowedDegreesOfFreedom(rigidBody);
            settings.LinearVelocity = rigidBody.PendingVelocity;

            if (motion == MotionType.Dynamic)
            {
                settings.OverrideMassProperties = OverrideMassProperties.CalculateInertia;
                var mass = settings.MassPropertiesOverride;
                mass.Mass = MathF.Max(rigidBody.Mass, 0.001f);
                settings.MassPropertiesOverride = mass;
            }
        }

        var body = _system.BodyInterface.CreateBody(settings);
        _system.BodyInterface.AddBody(body, motion == MotionType.Static ? Activation.DontActivate : Activation.Activate);

        var record = new BodyRecord(collider, rigidBody, body, body.ID, motion, key)
        {
            LastPosition = position,
            LastRotation = rotation,
        };

        _byCollider[collider] = record;
        _byBodyId[body.ID.ID] = record;
        if (rigidBody is not null)
        {
            rigidBody.World = this;
        }

        return record;
    }

    private void DestroyBody(BodyRecord record)
    {
        var id = record.Id;
        _system.BodyInterface.RemoveAndDestroyBody(id);
        _byCollider.Remove(record.Collider);
        _byBodyId.Remove(id.ID);
        if (record.RigidBody is { } rigidBody && rigidBody.World == this)
        {
            rigidBody.World = null;
        }

        // Forget contacts of the removed body so later begin / end events stay balanced.
        foreach (var pair in _contactCounts.Keys.Where(pair => pair.Item1 == id.ID || pair.Item2 == id.ID).ToList())
        {
            _contactCounts.Remove(pair);
        }
    }

    /// <summary>Moves bodies whose transforms were changed by scripts or the editor.</summary>
    private void FollowTransform(BodyRecord record, float deltaTime)
    {
        var transform = record.Collider.transform;
        var position = transform.Position;
        var rotation = transform.Rotation;
        var moved = Vector3.DistanceSquared(position, record.LastPosition) > 1e-10f ||
                    MathF.Abs(Quaternion.Dot(rotation, record.LastRotation)) < 0.9999999f;

        switch (record.Motion)
        {
            case MotionType.Kinematic when deltaTime > 0f:
                // Moving kinematic bodies get a velocity, so they push dynamic bodies instead of passing through.
                _system.BodyInterface.MoveKinematic(record.Id, position, rotation, deltaTime);
                break;
            case MotionType.Static when moved:
                _system.BodyInterface.SetPositionAndRotation(record.Id, position, rotation, Activation.DontActivate);
                break;
            case MotionType.Dynamic when moved:
                // A script or the editor teleported a dynamic body.
                _system.BodyInterface.SetPositionAndRotation(record.Id, position, rotation, Activation.Activate);
                break;
        }

        record.LastPosition = position;
        record.LastRotation = rotation;
    }

    private void WriteBackDynamicBodies()
    {
        foreach (var record in _byCollider.Values)
        {
            if (record.Motion != MotionType.Dynamic || !_system.BodyInterface.IsActive(record.Id))
            {
                continue;
            }

            var transform = record.Collider.transform;
            transform.Position = _system.BodyInterface.GetPosition(record.Id);
            transform.Rotation = _system.BodyInterface.GetRotation(record.Id);
            record.LastPosition = transform.Position;
            record.LastRotation = transform.Rotation;
        }
    }

    private static AllowedDOFs AllowedDegreesOfFreedom(RigidBody body)
    {
        var dofs = AllowedDOFs.All;
        if (body.FreezePositionX)
        {
            dofs &= ~AllowedDOFs.TranslationX;
        }

        if (body.FreezePositionY)
        {
            dofs &= ~AllowedDOFs.TranslationY;
        }

        if (body.FreezePositionZ)
        {
            dofs &= ~AllowedDOFs.TranslationZ;
        }

        if (body.FreezeRotationX)
        {
            dofs &= ~AllowedDOFs.RotationX;
        }

        if (body.FreezeRotationY)
        {
            dofs &= ~AllowedDOFs.RotationY;
        }

        if (body.FreezeRotationZ)
        {
            dofs &= ~AllowedDOFs.RotationZ;
        }

        // Jolt needs at least one free axis; a fully frozen body behaves like a static one anyway.
        return dofs == 0 ? AllowedDOFs.TranslationY : dofs;
    }

    // ---------------------------------------------------------------- shapes

    private Shape? CreateShape(Collider collider, Vector3 scale, MotionType motion)
    {
        scale = Vector3.Abs(scale);
        var maxScale = MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z));

        ShapeSettings settings;
        switch (collider)
        {
            case BoxCollider box:
            {
                var half = Vector3.Max(Vector3.Abs(box.Size) * scale * 0.5f, new Vector3(0.005f));
                var convexRadius = MathF.Min(Foundation.DefaultConvexRadius, MathF.Min(half.X, MathF.Min(half.Y, half.Z)) * 0.5f);
                settings = new BoxShapeSettings(half, convexRadius);
                break;
            }

            case SphereCollider sphere:
                settings = new SphereShapeSettings(MathF.Max(sphere.Radius * maxScale, 0.005f));
                break;

            case CapsuleCollider capsule:
            {
                var radius = MathF.Max(capsule.Radius * MathF.Max(scale.X, scale.Z), 0.005f);
                var halfCylinder = MathF.Max(capsule.Height * scale.Y * 0.5f - radius, 0.005f);
                settings = new CapsuleShapeSettings(halfCylinder, radius);
                break;
            }

            case MeshCollider meshCollider:
                if (CreateMeshShapeSettings(meshCollider, scale, motion) is not { } meshSettings)
                {
                    return null;
                }

                settings = meshSettings;
                break;

            default:
                Log.Warning($"Unsupported collider type {collider.GetType().Name} on '{collider.entity.Name}'.");
                return null;
        }

        using (settings)
        {
            if (collider.Center == Vector3.Zero)
            {
                return settings.Create();
            }

            var offset = collider.Center * scale;
            var identity = Quaternion.Identity;
            using var offsetSettings = new RotatedTranslatedShapeSettings(offset, identity, settings);
            return offsetSettings.Create();
        }
    }

    private unsafe ShapeSettings? CreateMeshShapeSettings(MeshCollider collider, Vector3 scale, MotionType motion)
    {
        var entity = collider.entity;
        if (entity.GetComponent<MeshRenderer>() is not { } renderer || BuiltInMeshes.GetData(renderer.MeshId) is not { } mesh)
        {
            Warn(entity, "a MeshCollider needs a MeshRenderer with a mesh");
            return null;
        }

        var meshOffset = renderer.MeshOffset; // the collider follows the mesh when its origin was moved

        if (motion == MotionType.Dynamic)
        {
            // Jolt cannot simulate concave triangle meshes as dynamic bodies; use the mesh bounds instead.
            Warn(entity, "a MeshCollider on a dynamic RigidBody is replaced by a box around the mesh; use a Box, Sphere or Capsule collider for moving objects");
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var vertex in mesh.Vertices)
            {
                min = Vector3.Min(min, (vertex.Position + meshOffset) * scale);
                max = Vector3.Max(max, (vertex.Position + meshOffset) * scale);
            }

            var half = Vector3.Max((max - min) * 0.5f, new Vector3(0.005f));
            var box = new BoxShapeSettings(half, MathF.Min(Foundation.DefaultConvexRadius, MathF.Min(half.X, MathF.Min(half.Y, half.Z)) * 0.5f));
            var boxCenter = (min + max) * 0.5f;
            if (boxCenter.LengthSquared() < 1e-8f)
            {
                return box;
            }

            // The box sits around the mesh, which is not centred on the origin (e.g. after Set Origin).
            using (box)
            {
                return new RotatedTranslatedShapeSettings(boxCenter, Quaternion.Identity, box);
            }
        }

        var triangles = new Triangle[mesh.TriangleCount];
        for (var i = 0; i < triangles.Length; i++)
        {
            var a = (mesh.Vertices[mesh.Indices[i * 3]].Position + meshOffset) * scale;
            var b = (mesh.Vertices[mesh.Indices[i * 3 + 1]].Position + meshOffset) * scale;
            var c = (mesh.Vertices[mesh.Indices[i * 3 + 2]].Position + meshOffset) * scale;
            triangles[i] = new Triangle(a, b, c, 0);
        }

        fixed (Triangle* data = triangles)
        {
            return new MeshShapeSettings(data, triangles.Length);
        }
    }

    private void Warn(Entity entity, string message)
    {
        if (_warnedEntities.Add(entity))
        {
            Log.Warning($"'{entity.Name}': {message}.");
        }
    }

    // ---------------------------------------------------------------- contacts

    private void OnContactAdded(PhysicsSystem system, in Body body1, in Body body2, in ContactManifold manifold, ref ContactSettings settings)
    {
        var point = manifold.PointCount > 0 ? manifold.GetWorldSpaceContactPointOn1(0) : body1.Position;
        _contactEvents.Enqueue(new ContactEvent(true, body1.ID.ID, body2.ID.ID, point, manifold.WorldSpaceNormal));
    }

    private void OnContactRemoved(PhysicsSystem system, ref SubShapeIDPair pair) =>
        _contactEvents.Enqueue(new ContactEvent(false, pair.Body1ID.ID, pair.Body2ID.ID, Vector3.Zero, Vector3.Zero));

    private void DispatchContactEvents()
    {
        while (_contactEvents.TryDequeue(out var contact))
        {
            var key = contact.Body1 < contact.Body2 ? (contact.Body1, contact.Body2) : (contact.Body2, contact.Body1);
            _contactCounts.TryGetValue(key, out var count);

            if (contact.Added)
            {
                _contactCounts[key] = count + 1;
                if (count == 0)
                {
                    SendBegin(contact);
                }
            }
            else if (count > 0)
            {
                if (count == 1)
                {
                    _contactCounts.Remove(key);
                    SendEnd(contact);
                }
                else
                {
                    _contactCounts[key] = count - 1;
                }
            }
        }
    }

    private void SendBegin(ContactEvent contact)
    {
        if (!_byBodyId.TryGetValue(contact.Body1, out var first) || !_byBodyId.TryGetValue(contact.Body2, out var second))
        {
            return;
        }

        if (first.Collider.IsTrigger || second.Collider.IsTrigger)
        {
            Notify(first.Collider, b => b.OnTriggerBegin(second.Collider), nameof(Behaviour.OnTriggerBegin));
            Notify(second.Collider, b => b.OnTriggerBegin(first.Collider), nameof(Behaviour.OnTriggerBegin));
            return;
        }

        // Jolt's normal points from the first body to the second.
        Notify(first.Collider, b => b.OnCollisionBegin(new Collision(second.Collider, contact.Point, -contact.Normal)), nameof(Behaviour.OnCollisionBegin));
        Notify(second.Collider, b => b.OnCollisionBegin(new Collision(first.Collider, contact.Point, contact.Normal)), nameof(Behaviour.OnCollisionBegin));
    }

    private void SendEnd(ContactEvent contact)
    {
        if (!_byBodyId.TryGetValue(contact.Body1, out var first) || !_byBodyId.TryGetValue(contact.Body2, out var second))
        {
            return;
        }

        var trigger = first.Collider.IsTrigger || second.Collider.IsTrigger;
        Notify(first.Collider, b => (trigger ? (Action<Collider>)b.OnTriggerEnd : b.OnCollisionEnd)(second.Collider), trigger ? nameof(Behaviour.OnTriggerEnd) : nameof(Behaviour.OnCollisionEnd));
        Notify(second.Collider, b => (trigger ? (Action<Collider>)b.OnTriggerEnd : b.OnCollisionEnd)(first.Collider), trigger ? nameof(Behaviour.OnTriggerEnd) : nameof(Behaviour.OnCollisionEnd));
    }

    private static void Notify(Collider collider, Action<Behaviour> call, string method)
    {
        if (!collider.IsAttached)
        {
            return;
        }

        foreach (var component in collider.entity.Components.ToList())
        {
            if (component is not Behaviour { HasStarted: true, IsActiveAndEnabled: true } behaviour)
            {
                continue;
            }

            try
            {
                call(behaviour);
            }
            catch (Exception exception)
            {
                behaviour.Enabled = false;
                Log.Error(exception, $"{behaviour.GetType().Name}.{method} on '{collider.entity.Name}' threw an exception; the script has been disabled.");
            }
        }
    }

    private readonly record struct ContactEvent(bool Added, uint Body1, uint Body2, Vector3 Point, Vector3 Normal);

    /// <summary>Everything that requires rebuilding a body when it changes.</summary>
    private readonly record struct ShapeKey(
        Type ColliderType, Vector4 Shape, Vector3 Center, Vector3 Scale, bool Trigger, float Friction, float Bounciness,
        RigidBodyType? BodyType, Vector4 BodySettings, int Flags, string? Mesh)
    {
        public static ShapeKey Of(Collider collider, RigidBody? body)
        {
            var shape = collider switch
            {
                BoxCollider box => new Vector4(box.Size, 0f),
                SphereCollider sphere => new Vector4(sphere.Radius, 0f, 0f, 0f),
                CapsuleCollider capsule => new Vector4(capsule.Radius, capsule.Height, 0f, 0f),
                _ => Vector4.Zero,
            };

            var flags = 0;
            if (body is not null)
            {
                bool[] bits = [body.UseGravity, body.ContinuousCollision, body.FreezePositionX, body.FreezePositionY, body.FreezePositionZ,
                    body.FreezeRotationX, body.FreezeRotationY, body.FreezeRotationZ];
                for (var i = 0; i < bits.Length; i++)
                {
                    flags |= bits[i] ? 1 << i : 0;
                }
            }

            return new ShapeKey(
                collider.GetType(),
                shape,
                collider.Center,
                RoundScale(collider.transform.LossyScale),
                collider.IsTrigger,
                collider.Friction,
                collider.Bounciness,
                body?.Type,
                body is null ? Vector4.Zero : new Vector4(body.Mass, body.LinearDamping, body.AngularDamping, 0f),
                flags,
                collider is MeshCollider && collider.entity.GetComponent<MeshRenderer>() is { } renderer ? $"{renderer.MeshId}@{renderer.MeshOffset}" : null);
        }

        // Tiny float noise from transform math must not trigger a rebuild every step.
        private static Vector3 RoundScale(Vector3 scale) =>
            new(MathF.Round(scale.X, 4), MathF.Round(scale.Y, 4), MathF.Round(scale.Z, 4));
    }

    private sealed class BodyRecord(Collider collider, RigidBody? rigidBody, Body body, BodyID id, MotionType motion, ShapeKey key)
    {
        public Collider Collider { get; } = collider;

        public RigidBody? RigidBody { get; } = rigidBody;

        public Body Body { get; } = body;

        public BodyID Id { get; } = id;

        public MotionType Motion { get; } = motion;

        public ShapeKey Key { get; } = key;

        public Vector3 LastPosition { get; set; }

        public Quaternion LastRotation { get; set; }
    }
}
