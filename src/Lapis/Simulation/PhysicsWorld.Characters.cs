using System.Numerics;
using JoltPhysicsSharp;

namespace Lapis.Simulation;
/// <summary>Physics: character controllers, simulated with Jolt's CharacterVirtual.</summary>
public sealed partial class PhysicsWorld
{
    /// <summary>Creates the character of a controller, rebuilds it after its shape changed, or follows a teleport.</summary>
    private void SyncCharacter(CharacterController controller)
    {
        var key = CharacterKey.Of(controller);
        if (_characters.TryGetValue(controller, out var record) && record.Key != key)
        {
            DestroyCharacter(record); // changed in the Inspector while playing: rebuild
            record = null;
        }

        if (record is null)
        {
            if (controller.entity.GetComponent<RigidBody>() is not null)
            {
                Warn(controller.entity, "a CharacterController moves on its own, so its RigidBody is ignored");
            }

            CreateCharacter(controller, key);
            return;
        }

        var position = controller.transform.Position;
        if (Vector3.DistanceSquared(position, record.LastPosition) > 1e-10f)
        {
            // Moved by a script (Teleport) or the editor: jump there.
            record.Character.Position = position;
            record.Character.LinearVelocity = Vector3.Zero;
            record.LastPosition = position;
        }
    }

    private void CreateCharacter(CharacterController controller, CharacterKey key)
    {
        var radius = MathF.Max(controller.Radius, 0.05f);
        var height = MathF.Max(controller.Height, radius * 2f + 0.01f);
        var center = controller.Center;

        // Kept with the character: Jolt uses them for as long as it lives.
        var shape = new CapsuleShape(height * 0.5f - radius, radius);
        var innerShape = new CapsuleShape(height * 0.5f - radius, radius);

        // Contacts below the centre of the bottom sphere hold the character up.
        var bottom = center.Y - height * 0.5f + radius;
        var settings = new CharacterVirtualSettings
        {
            Shape = shape,
            ShapeOffset = center, // the capsule's middle relative to the feet
            Up = Vector3.UnitY,
            MaxSlopeAngle = Math.Clamp(controller.MaxSlope, 0f, 89f) * MathUtil.Deg2Rad,
            SupportingVolume = new Plane(Vector3.UnitY, -bottom),
            Mass = MathF.Max(controller.Mass, 1f),
            MaxStrength = MathF.Max(controller.Mass, 1f) * 15f,
            InnerBodyShape = innerShape,
            InnerBodyLayer = Moving,
        };

        var position = controller.transform.Position;
        var character = new CharacterVirtual(settings, position, Quaternion.Identity, 0, _system);
        var record = new CharacterRecord(controller, character, key, [shape, innerShape]) { LastPosition = position };
        _characters[controller] = record;

        // The inner body is what triggers and raycasts see.
        _byBodyId[character.InnerBodyID.ID] = new BodyRecord(controller, null, null, character.InnerBodyID, MotionType.Kinematic, default);
        controller.PendingMotion = Vector3.Zero;
        controller.VerticalSpeed = 0f;
    }

    private void DestroyCharacter(CharacterRecord record)
    {
        var innerId = record.Character.InnerBodyID.ID;
        _byBodyId.Remove(innerId);
        foreach (var pair in _contactCounts.Keys.Where(pair => pair.Item1 == innerId || pair.Item2 == innerId).ToList())
        {
            _contactCounts.Remove(pair);
        }

        record.Character.Dispose();
        foreach (var shape in record.Shapes)
        {
            shape.Dispose();
        }

        _characters.Remove(record.Controller);
        record.Controller.IsGrounded = false;
        record.Controller.Velocity = Vector3.Zero;
    }

    /// <summary>
    /// Moves every character by what its scripts asked for plus gravity, sliding along walls, walking up steps and
    /// staying on the floor, then writes the result to its transform.
    /// </summary>
    private void UpdateCharacters(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        var gravity = _system.Gravity;
        foreach (var record in _characters.Values)
        {
            var controller = record.Controller;
            var character = record.Character;

            if (controller.UseGravity)
            {
                if (controller.IsGrounded && controller.VerticalSpeed < 0f)
                {
                    controller.VerticalSpeed = 0f; // resting on the ground
                }

                controller.VerticalSpeed += gravity.Y * deltaTime;
            }
            else
            {
                controller.VerticalSpeed = 0f;
            }

            var motion = controller.PendingMotion;
            controller.PendingMotion = Vector3.Zero;
            if (controller.UseGravity)
            {
                motion.Y = 0f; // up and down come from gravity and jumps
            }

            var wanted = motion / deltaTime + Vector3.UnitY * controller.VerticalSpeed;
            character.LinearVelocity = wanted;

            var stepHeight = MathF.Max(controller.StepHeight, 0f);
            var update = new ExtendedUpdateSettings
            {
                // Only stick to the floor while not jumping, or a jump would be pulled straight back down.
                StickToFloorStepDown = controller.VerticalSpeed > 0f ? Vector3.Zero : new Vector3(0f, -MathF.Max(stepHeight, 0.1f), 0f),
                WalkStairsStepUp = new Vector3(0f, stepHeight, 0f),
                WalkStairsMinStepForward = 0.02f,
                WalkStairsStepForwardTest = 0.15f,
                WalkStairsCosAngleForwardContact = MathF.Cos(75f * MathUtil.Deg2Rad),
                WalkStairsStepDownExtra = Vector3.Zero,
            };

            var before = character.Position;
            character.ExtendedUpdate(deltaTime, update, Moving, _system, _characterBodyFilter, _characterShapeFilter);

            var after = character.Position;
            controller.transform.Position = after;
            record.LastPosition = after;
            controller.Velocity = (after - before) / deltaTime;
            controller.IsGrounded = character.GroundState == GroundState.OnGround;

            // Hitting a ceiling ends the rise of a jump.
            if (controller.VerticalSpeed > 0f && controller.Velocity.Y < controller.VerticalSpeed * 0.5f)
            {
                controller.VerticalSpeed = MathF.Max(controller.Velocity.Y, 0f);
            }
        }
    }

    /// <summary>Surface normal of a character's capsule at a point on it (for raycast hits).</summary>
    private static Vector3 CapsuleNormal(CharacterController controller, Vector3 point)
    {
        var radius = MathF.Max(controller.Radius, 0.05f);
        var halfSegment = MathF.Max(controller.Height * 0.5f - radius, 0f);
        var center = controller.transform.Position + controller.Center;
        var onAxis = center with { Y = Math.Clamp(point.Y, center.Y - halfSegment, center.Y + halfSegment) };
        var outward = point - onAxis;
        return outward.LengthSquared() > 1e-10f ? Vector3.Normalize(outward) : Vector3.UnitY;
    }

    /// <summary>Everything that requires rebuilding a character when it changes.</summary>
    private readonly record struct CharacterKey(float Height, float Radius, Vector3 Center, float MaxSlope, float Mass)
    {
        public static CharacterKey Of(CharacterController controller) =>
            new(controller.Height, controller.Radius, controller.Center, controller.MaxSlope, controller.Mass);
    }

    /// <summary>What characters collide with: every body except triggers, which they pass through.</summary>
    private sealed class CharacterBodyFilter(PhysicsWorld world) : BodyFilter
    {
        protected override bool ShouldCollide(BodyID bodyID) => !IsTrigger(bodyID);

        protected override bool ShouldCollideLocked(Body body) => !IsTrigger(body.ID);

        private bool IsTrigger(BodyID id) => world._byBodyId.TryGetValue(id.ID, out var record) && record.Collider.IsTrigger;
    }

    /// <summary>Accepts every part of every shape (Jolt needs a filter object even when nothing is filtered).</summary>
    private sealed class AllShapesFilter : ShapeFilter;

    private sealed class CharacterRecord(CharacterController controller, CharacterVirtual character, CharacterKey key, Shape[] shapes)
    {
        public CharacterController Controller { get; } = controller;

        public CharacterVirtual Character { get; } = character;

        public CharacterKey Key { get; } = key;

        /// <summary>The character's shapes, released after the character.</summary>
        public Shape[] Shapes { get; } = shapes;

        public Vector3 LastPosition { get; set; }
    }
}
