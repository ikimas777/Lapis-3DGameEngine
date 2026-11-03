using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;

namespace Lapis.Navigation;

/// <summary>
/// The navigation of a running game: the scene's navigation mesh and the agents walking on it (DotRecast's crowd,
/// which steers agents around each other). Stepped once per frame by the game.
/// </summary>
internal sealed class NavigationWorld
{
    private const int MaxPathPolygons = 256;
    private static readonly RcVec3f SearchExtents = new(2f, 4f, 2f);

    private readonly Scene _scene;
    private readonly DtNavMesh? _mesh;
    private readonly DtNavMeshQuery? _query;
    private readonly DtCrowd? _crowd;
    private readonly DtQueryDefaultFilter _filter = new();
    private readonly Dictionary<NavMeshAgent, DtCrowdAgent> _agents = [];
    private readonly Dictionary<NavMeshAgent, Vector3> _written = [];
    private readonly List<NavMeshAgent> _agentBuffer = [];
    private bool _warnedNoMesh;

    public NavigationWorld(Scene scene)
    {
        _scene = scene;
        _mesh = NavMeshBaker.Load(scene.Navigation.Data);
        if (_mesh is null)
        {
            return;
        }

        _query = new DtNavMeshQuery(_mesh);
        _crowd = new DtCrowd(new DtCrowdConfig(MathF.Max(scene.Navigation.AgentRadius, 0.6f) * 2f), _mesh);
    }

    public bool HasMesh => _mesh is not null;

    /// <summary>Moves every agent on by <paramref name="deltaTime"/> seconds.</summary>
    public void Step(float deltaTime)
    {
        var agents = _agentBuffer;
        agents.Clear();
        _scene.CollectComponents(agents);
        agents.RemoveAll(agent => !agent.entity.IsActiveInHierarchy);

        // Agents that were removed or switched off leave the crowd.
        foreach (var gone in _agents.Keys.Where(agent => !agents.Contains(agent)).ToList())
        {
            _crowd?.RemoveAgent(_agents[gone]);
            _agents.Remove(gone);
            _written.Remove(gone);
            gone.IsOnNavMesh = false;
        }

        if (_crowd is null || _query is null)
        {
            if (agents.Count > 0 && !_warnedNoMesh)
            {
                Log.Warning("The scene has NavMeshAgents but no navigation mesh: bake one in the Navigation tab.");
                _warnedNoMesh = true;
            }

            return;
        }

        foreach (var agent in agents)
        {
            Prepare(agent);
        }

        _crowd.Update(deltaTime, null);

        foreach (var agent in agents)
        {
            if (_agents.TryGetValue(agent, out var crowdAgent))
            {
                Apply(agent, crowdAgent, deltaTime);
            }
        }
    }

    public bool SamplePosition(Vector3 point, float maxDistance, out Vector3 position)
    {
        position = point;
        if (_query is null)
        {
            return false;
        }

        var extents = new RcVec3f(maxDistance, maxDistance + 2f, maxDistance);
        if (_query.FindNearestPoly(ToRc(point), extents, _filter, out var polygon, out var nearest, out _).Failed() || polygon == 0)
        {
            return false;
        }

        position = ToVector(nearest);
        return true;
    }

    public bool CalculatePath(Vector3 from, Vector3 to, List<Vector3> corners)
    {
        if (_query is null
            || _query.FindNearestPoly(ToRc(from), SearchExtents, _filter, out var startPolygon, out var start, out _).Failed() || startPolygon == 0
            || _query.FindNearestPoly(ToRc(to), SearchExtents, _filter, out var endPolygon, out var end, out _).Failed() || endPolygon == 0)
        {
            return false;
        }

        Span<long> path = stackalloc long[MaxPathPolygons];
        if (_query.FindPath(startPolygon, endPolygon, start, end, _filter, path, out var count, MaxPathPolygons).Failed() || count == 0)
        {
            return false;
        }

        // A partial path ends at the last polygon reached: aim for the point of it closest to the target.
        if (path[count - 1] != endPolygon)
        {
            _query.ClosestPointOnPoly(path[count - 1], end, out end, out _);
        }

        Span<DtStraightPath> straight = stackalloc DtStraightPath[MaxPathPolygons];
        if (_query.FindStraightPath(start, end, path, count, straight, out var cornerCount, MaxPathPolygons, 0).Failed())
        {
            return false;
        }

        for (var i = 0; i < cornerCount; i++)
        {
            corners.Add(ToVector(straight[i].pos));
        }

        return corners.Count > 0;
    }

    /// <summary>Adds new agents to the crowd and passes on settings, teleports and destinations.</summary>
    private void Prepare(NavMeshAgent agent)
    {
        var position = agent.transform.Position;
        if (agent.WarpRequested is { } warp)
        {
            agent.WarpRequested = null;
            if (_agents.Remove(agent, out var old))
            {
                _crowd!.RemoveAgent(old);
            }

            position = warp;
            agent.transform.Position = warp;
        }

        if (!_agents.TryGetValue(agent, out var crowdAgent))
        {
            if (!SamplePosition(position, 2f, out var start))
            {
                agent.IsOnNavMesh = false;
                return;
            }

            crowdAgent = _crowd!.AddAgent(ToRc(start), Parameters(agent));
            _agents[agent] = crowdAgent;
            _written[agent] = position;
            if (agent.Destination is { } destination)
            {
                agent.RequestedDestination ??= destination; // keep walking after a warp
            }
        }
        else
        {
            _crowd!.UpdateAgentParameters(crowdAgent, Parameters(agent));

            // The body may have been moved by something else (a character controller, a script): follow it.
            if (Vector3.DistanceSquared(position, _written[agent]) > 1e-4f)
            {
                crowdAgent.npos = ToRc(position);
            }
        }

        agent.IsOnNavMesh = true;

        if (agent.ResetRequested)
        {
            agent.ResetRequested = false;
            agent.Destination = null;
            _crowd.ResetMoveTarget(crowdAgent);
        }

        if (agent.RequestedDestination is { } target)
        {
            agent.RequestedDestination = null;
            if (_query!.FindNearestPoly(ToRc(target), SearchExtents, _filter, out var polygon, out var nearest, out _).Succeeded() && polygon != 0)
            {
                agent.Destination = ToVector(nearest);
                _crowd.RequestMoveTarget(crowdAgent, polygon, nearest);
            }
            else
            {
                Log.Warning($"NavMeshAgent '{agent.entity.Name}': the destination {target} is not near the navigation mesh.");
            }
        }

        if (agent.IsStopped)
        {
            _crowd.ResetMoveTarget(crowdAgent);
        }
        else if (agent.Destination is { } destination && crowdAgent.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE)
        {
            // Resumed after IsStopped: ask for the same destination again.
            if (_query!.FindNearestPoly(ToRc(destination), SearchExtents, _filter, out var polygon, out var nearest, out _).Succeeded() && polygon != 0)
            {
                _crowd.RequestMoveTarget(crowdAgent, polygon, nearest);
            }
        }
    }

    /// <summary>Moves the entity to where the crowd put the agent, and stops it at its destination.</summary>
    private void Apply(NavMeshAgent agent, DtCrowdAgent crowdAgent, float deltaTime)
    {
        var position = ToVector(crowdAgent.npos);
        var velocity = ToVector(crowdAgent.vel);

        agent.PathPending = crowdAgent.targetState is DtMoveRequestState.DT_CROWDAGENT_TARGET_REQUESTING
            or DtMoveRequestState.DT_CROWDAGENT_TARGET_WAITING_FOR_QUEUE or DtMoveRequestState.DT_CROWDAGENT_TARGET_WAITING_FOR_PATH;
        agent.RemainingDistance = agent.Destination is { } destination ? PathLength(crowdAgent, position, destination) : 0f;

        if (agent.Destination is not null && !agent.PathPending && agent.RemainingDistance <= MathF.Max(agent.StoppingDistance, 0.05f))
        {
            agent.Destination = null; // arrived
            agent.RemainingDistance = 0f;
            _crowd!.ResetMoveTarget(crowdAgent);
            crowdAgent.vel = RcVec3f.Zero;
            crowdAgent.dvel = RcVec3f.Zero;
            velocity = Vector3.Zero;
        }

        if (agent.entity.GetComponent<CharacterController>() is { } controller)
        {
            // The controller walks the body (with gravity, steps and collisions); the agent follows it next frame.
            controller.Move(new Vector3(velocity.X, 0f, velocity.Z) * deltaTime);
            _written[agent] = agent.transform.Position;
        }
        else
        {
            agent.transform.Position = position;
            _written[agent] = position;
        }

        agent.Velocity = velocity;
        if (agent.UpdateRotation && velocity.X * velocity.X + velocity.Z * velocity.Z > 0.01f)
        {
            Turn(agent, velocity, deltaTime);
        }
    }

    private static void Turn(NavMeshAgent agent, Vector3 velocity, float deltaTime)
    {
        // Entities look along −Z, so the heading of a direction (x, z) is atan2(−x, −z).
        var target = MathF.Atan2(-velocity.X, -velocity.Z) * MathUtil.Rad2Deg;
        var current = MathUtil.ToEulerDegrees(agent.transform.Rotation).Y;
        var difference = (target - current + 540f) % 360f - 180f;
        var step = agent.AngularSpeed * deltaTime;
        var heading = MathF.Abs(difference) <= step ? target : current + MathF.Sign(difference) * step;
        agent.transform.Rotation = MathUtil.FromEulerDegrees(new Vector3(0f, heading, 0f));
    }

    private static float PathLength(DtCrowdAgent crowdAgent, Vector3 position, Vector3 destination)
    {
        if (crowdAgent.ncorners == 0)
        {
            return Horizontal(position, destination);
        }

        var length = 0f;
        var previous = position;
        for (var i = 0; i < crowdAgent.ncorners; i++)
        {
            var corner = ToVector(crowdAgent.corners[i].pos);
            length += Horizontal(previous, corner);
            previous = corner;
        }

        return length + Horizontal(previous, destination);
    }

    private static float Horizontal(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    private static DtCrowdAgentParams Parameters(NavMeshAgent agent)
    {
        var avoid = agent.AvoidOtherAgents;
        return new DtCrowdAgentParams
        {
            radius = MathF.Max(agent.Radius, 0.05f),
            height = MathF.Max(agent.Height, 0.2f),
            maxSpeed = MathF.Max(agent.Speed, 0f),
            maxAcceleration = MathF.Max(agent.Acceleration, 0.1f),
            collisionQueryRange = MathF.Max(agent.Radius, 0.05f) * 12f,
            pathOptimizationRange = MathF.Max(agent.Radius, 0.05f) * 30f,
            separationWeight = avoid ? 2f : 0f,
            updateFlags = DtCrowdAgentUpdateFlags.DT_CROWD_ANTICIPATE_TURNS | DtCrowdAgentUpdateFlags.DT_CROWD_OPTIMIZE_VIS | DtCrowdAgentUpdateFlags.DT_CROWD_OPTIMIZE_TOPO
                | (avoid ? DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE | DtCrowdAgentUpdateFlags.DT_CROWD_SEPARATION : 0),
            obstacleAvoidanceType = 3,
            queryFilterType = 0,
        };
    }

    private static RcVec3f ToRc(Vector3 value) => new(value.X, value.Y, value.Z);

    private static Vector3 ToVector(RcVec3f value) => new(value.X, value.Y, value.Z);
}
