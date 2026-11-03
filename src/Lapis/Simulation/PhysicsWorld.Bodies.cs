using System.Numerics;
using JoltPhysicsSharp;

namespace Lapis.Simulation;
/// <summary>Physics: keeping one body per collider in step with the scene, and the bodies' bookkeeping.</summary>
public sealed partial class PhysicsWorld
{
    private void SyncBodies(float deltaTime)
    {
        CollectColliders();

        _seen.Clear();
        _seenCharacters.Clear();
        foreach (var collider in _sceneColliders)
        {
            if (collider is CharacterController controller)
            {
                _seenCharacters.Add(controller);
                SyncCharacter(controller);
                continue;
            }

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

        foreach (var character in _characters.Values.Where(c => !_seenCharacters.Contains(c.Controller)).ToList())
        {
            DestroyCharacter(character);
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
                CylinderCollider cylinder => new Vector4(cylinder.Radius, cylinder.Height, 0f, 0f),
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

    private sealed class BodyRecord(Collider collider, RigidBody? rigidBody, Body? body, BodyID id, MotionType motion, ShapeKey key)
    {
        public Collider Collider { get; } = collider;

        public RigidBody? RigidBody { get; } = rigidBody;

        /// <summary>The Jolt body, or null for the inner body of a character (owned by the character).</summary>
        public Body? Body { get; } = body;

        public BodyID Id { get; } = id;

        public MotionType Motion { get; } = motion;

        public ShapeKey Key { get; } = key;

        public Vector3 LastPosition { get; set; }

        public Quaternion LastRotation { get; set; }
    }
}
