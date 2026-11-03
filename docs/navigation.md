# Navigation

Creatures with a **Nav Mesh Agent** find their own way through the map: around walls, through doorways, up stairs
and ramps, and out of each other's way. They walk on the scene's **navigation mesh**, a map of everywhere an agent
of a given size can stand, which you bake once in the editor. Navigation uses
[DotRecast](https://github.com/ikpil/DotRecast), the C# version of Recast & Detour.

## Baking the navigation mesh

Open the **Navigation** tab (next to the Inspector and Scene Settings; **View > Navigation** if it is closed),
set the agent's size and press **Bake**.

| Setting | Meaning |
|---|---|
| **Agent Radius** | How close to walls and edges agents walk, in metres. Gaps narrower than twice this are closed. |
| **Agent Height** | Places with a lower ceiling (under tables, low tunnels) are left out. |
| **Max Step Height** | The highest step or kerb agents walk up, in metres. Stairs need steps no higher than this. |
| **Max Slope** | The steepest ramp or hill agents climb, in degrees (0–60). Steeper ground is not walkable. |
| **Cell Size** | How finely the scene is measured. Smaller follows walls more closely but bakes slower; 0.1–0.2 suits rooms and corridors. |

What counts as the map:

- every **collider** of the scene (box, sphere, capsule, cylinder, mesh): floors, walls, stairs, ramps, furniture;
- except **triggers**, **Character Controllers**, and colliders of entities with a **Rigid Body** (they move, so
  they are not part of the map; agents push past them or wait for them).

The walkable area shows in the Scene view as light-blue outlines (**Show in Scene view** in the tab, or
**View > Show Navigation Mesh**). The mesh is saved in the scene file; **bake again after changing the map**.
**Clear** removes it. Baking and clearing can be undone.

## Agents

Add **Nav Mesh Agent** to a creature (Add Component) and give it a destination from a script.

| Setting | Meaning |
|---|---|
| **Speed**, **Acceleration** | Top speed (m/s) and how quickly it speeds up and slows down. |
| **Angular Speed** | How fast it turns to face where it walks (degrees per second), with **Update Rotation** on. |
| **Stopping Distance** | How close to the destination it stops. |
| **Radius**, **Height** | Its size, for keeping out of other agents' way. Keep the radius at most the baked Agent Radius. |
| **Avoid Other Agents** | Steer around other agents (off: walk straight through them). |

The agent moves the entity's transform along the navigation mesh. If the entity also has a **Character
Controller**, the agent walks it through the controller instead, so gravity, steps and collisions work as for the
player.

```csharp
using System.Numerics;
using Lapis;

/// <summary>Chases the player when it sees them within a range, otherwise wanders back home.</summary>
public class Chaser : Behaviour
{
    public Entity? player;

    [Range(1, 50)]
    public float sightRange = 15f;

    private NavMeshAgent? _agent;
    private Vector3 _home;

    public override void Start()
    {
        _agent = entity.GetComponent<NavMeshAgent>();
        _home = transform.Position;
    }

    public override void Update()
    {
        var target = player!.Transform.Position;
        if (Vector3.Distance(transform.Position, target) < sightRange)
        {
            _agent!.SetDestination(target); // every frame is fine: the path follows the player
        }
        else if (_agent!.Destination is null)
        {
            _agent.SetDestination(_home);
        }
    }
}
```

| API | Purpose |
|---|---|
| `SetDestination(point)` | Walk to a point (or as close as the navigation mesh gets). Returns false when the scene has no navigation mesh. |
| `ResetPath()`, `IsStopped` | Forget the destination; pause and resume walking. |
| `Warp(point)` | Jump to a point without walking (e.g. respawning). |
| `Destination`, `HasPath`, `PathPending`, `RemainingDistance` | Where it goes, whether it still walks there, whether the path is being found, and how far is left. |
| `Velocity`, `IsOnNavMesh` | How fast it moves (e.g. to choose Walk or Run in an Animator); whether it stands on the mesh. |
| `NavMesh.SamplePosition(point, maxDistance, out position)` | The nearest walkable point. |
| `NavMesh.CalculatePath(from, to, corners)` | The corners of a path, without moving anyone (e.g. to check that a place can be reached). |

Animate a walking creature from its speed:

```csharp
var speed = agent.Velocity.Length();
animator.Play(speed < 0.1f ? "Idle" : speed < 2.5f ? "Walk" : "Run");
```

## Notes

- Doors that open and close are not cut out of the mesh yet: bake with doors open (or without them), and stop
  agents with `IsStopped` or by blocking with a collider they bump into.
- Jumps between separate floors and ladders (off-mesh links) are not supported yet; connect floors with stairs or
  ramps no steeper than **Max Slope**.
- One agent size per scene: agents larger than the baked **Agent Radius** may brush walls.
