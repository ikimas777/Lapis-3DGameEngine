using System.Numerics;
using JoltPhysicsSharp;

namespace Lapis.Simulation;
/// <summary>Physics: collision and trigger events, queued from Jolt's threads and sent to behaviours after each step.</summary>
public sealed partial class PhysicsWorld
{
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
}
