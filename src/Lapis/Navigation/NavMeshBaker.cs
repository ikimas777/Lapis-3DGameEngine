using System.Diagnostics;
using System.Numerics;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Lapis.Rendering;

namespace Lapis.Navigation;

/// <summary>What a bake produced.</summary>
/// <param name="Data">The navigation mesh, or null when nothing in the scene is walkable.</param>
/// <param name="PolygonCount">Number of walkable polygons.</param>
/// <param name="TriangleCount">Number of collider triangles read from the scene.</param>
/// <param name="Seconds">How long the bake took.</param>
public sealed record NavMeshBakeResult(byte[]? Data, int PolygonCount, int TriangleCount, double Seconds);

/// <summary>
/// Builds a scene's navigation mesh with Recast (DotRecast): the walkable surfaces of its colliders, for an agent of
/// the size in <see cref="SceneNavigation"/>. Triggers, character controllers and colliders of entities with a
/// <see cref="RigidBody"/> (they move) are left out.
/// </summary>
public static class NavMeshBaker
{
    /// <summary>Most corners a navigation polygon has.</summary>
    internal const int MaxVertsPerPoly = 6;

    /// <summary>Polygon flag of walkable polygons (agents' query filters include it).</summary>
    internal const int WalkableFlag = 1;

    /// <summary>Bakes the scene's navigation mesh with its <see cref="Scene.Navigation"/> settings.</summary>
    public static NavMeshBakeResult Bake(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var stopwatch = Stopwatch.StartNew();
        var vertices = new List<float>();
        var triangles = new List<int>();
        CollectGeometry(scene, vertices, triangles);
        if (triangles.Count == 0)
        {
            return new NavMeshBakeResult(null, 0, 0, stopwatch.Elapsed.TotalSeconds);
        }

        var settings = scene.Navigation;
        var cellSize = Math.Clamp(settings.CellSize, 0.02f, 2f);
        var cellHeight = MathF.Max(cellSize * 0.5f, 0.01f);
        var geometry = new RcSampleInputGeomProvider(vertices.ToArray(), triangles.ToArray());
        var config = new RcConfig(
            RcPartition.WATERSHED,
            cellSize,
            cellHeight,
            Math.Clamp(settings.MaxSlope, 0f, 89f),
            MathF.Max(settings.AgentHeight, cellHeight),
            MathF.Max(settings.AgentRadius, 0f),
            MathF.Max(settings.MaxStepHeight, 0f),
            8,      // smallest region kept, in cells (squared)
            20,     // regions smaller than this merge into neighbours
            12f,    // longest polygon edge, in metres
            1.3f,   // how far polygon edges may stray from the walkable area's outline, in cells
            MaxVertsPerPoly,
            6f,     // detail mesh sample spacing, in cells
            1f,     // detail mesh height error, in cells
            true, true, true,
            new RcAreaModification(0x3f),
            true);
        var result = new RcBuilder().Build(geometry, new RcBuilderConfig(config, geometry.GetMeshBoundsMin(), geometry.GetMeshBoundsMax()), false);
        var mesh = result.Mesh;
        if (mesh is null || mesh.npolys == 0)
        {
            return new NavMeshBakeResult(null, 0, triangles.Count / 3, stopwatch.Elapsed.TotalSeconds);
        }

        for (var i = 0; i < mesh.npolys; i++)
        {
            mesh.flags[i] = WalkableFlag;
        }

        var detail = result.MeshDetail;
        var parameters = new DtNavMeshCreateParams
        {
            verts = mesh.verts,
            vertCount = mesh.nverts,
            polys = mesh.polys,
            polyAreas = mesh.areas,
            polyFlags = mesh.flags,
            polyCount = mesh.npolys,
            nvp = mesh.nvp,
            detailMeshes = detail?.meshes,
            detailVerts = detail?.verts,
            detailVertsCount = detail?.nverts ?? 0,
            detailTris = detail?.tris,
            detailTriCount = detail?.ntris ?? 0,
            walkableHeight = settings.AgentHeight,
            walkableRadius = settings.AgentRadius,
            walkableClimb = settings.MaxStepHeight,
            bmin = mesh.bmin,
            bmax = mesh.bmax,
            cs = cellSize,
            ch = cellHeight,
            buildBvTree = true,
        };

        var meshData = DtNavMeshBuilder.CreateNavMeshData(parameters);
        if (meshData is null)
        {
            return new NavMeshBakeResult(null, 0, triangles.Count / 3, stopwatch.Elapsed.TotalSeconds);
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            new DtMeshDataWriter().Write(writer, meshData, RcByteOrder.LITTLE_ENDIAN, false);
        }

        return new NavMeshBakeResult(stream.ToArray(), mesh.npolys, triangles.Count / 3, stopwatch.Elapsed.TotalSeconds);
    }

    /// <summary>Reads a navigation mesh made by <see cref="Bake"/>; null when the data cannot be read.</summary>
    internal static DtNavMesh? Load(byte[]? data)
    {
        if (data is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var reader = new BinaryReader(new MemoryStream(data));
            var meshData = new DtMeshDataReader().Read(reader, MaxVertsPerPoly);
            var navMesh = new DtNavMesh();
            return navMesh.Init(meshData, MaxVertsPerPoly, 0).Succeeded() ? navMesh : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or IndexOutOfRangeException or ArgumentException)
        {
            Log.Warning($"The scene's navigation mesh could not be read ({exception.Message}); bake it again in the Navigation tab.");
            return null;
        }
    }

    /// <summary>World-space triangles of every collider that agents can stand on or bump into.</summary>
    internal static void CollectGeometry(Scene scene, List<float> vertices, List<int> triangles)
    {
        foreach (var entity in scene.AllEntities)
        {
            if (!entity.IsActiveInHierarchy || entity.GetComponent<RigidBody>() is not null)
            {
                continue; // objects with a rigid body move (by physics or scripts): they are not part of the map
            }

            foreach (var collider in entity.GetComponents<Collider>())
            {
                if (collider.IsTrigger || collider is CharacterController)
                {
                    continue;
                }

                var (mesh, local) = ShapeOf(collider);
                if (mesh is null)
                {
                    continue;
                }

                var toWorld = local * entity.Transform.LocalToWorld;
                var start = vertices.Count / 3;
                foreach (var vertex in mesh.Vertices)
                {
                    var world = Vector3.Transform(vertex.Position, toWorld);
                    vertices.Add(world.X);
                    vertices.Add(world.Y);
                    vertices.Add(world.Z);
                }

                // A mirrored transform flips the winding; Recast needs counter-clockwise triangles seen from above.
                var flip = toWorld.GetDeterminant() < 0f;
                for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
                {
                    triangles.Add(start + (int)mesh.Indices[i]);
                    triangles.Add(start + (int)mesh.Indices[flip ? i + 2 : i + 1]);
                    triangles.Add(start + (int)mesh.Indices[flip ? i + 1 : i + 2]);
                }
            }
        }
    }

    /// <summary>A collider's shape as a mesh, and the matrix that places it in the entity's space.</summary>
    private static (MeshData? Mesh, Matrix4x4 Local) ShapeOf(Collider collider)
    {
        var center = Matrix4x4.CreateTranslation(collider.Center);
        switch (collider)
        {
            case BoxCollider box:
                return (Primitives.CreateCube(), Matrix4x4.CreateScale(box.Size) * center);
            case SphereCollider sphere:
                return (Primitives.CreateSphere(sphere.Radius, 16, 8), center);
            case CapsuleCollider capsule:
                return (Primitives.CreateCapsule(capsule.Radius, capsule.Height, 16, 4), center);
            case CylinderCollider cylinder:
                return (Primitives.CreateCylinder(cylinder.Radius, cylinder.Height, 16), center);
            case MeshCollider when collider.entity.GetComponent<MeshRenderer>() is { } renderer:
                return (BuiltInMeshes.GetData(renderer.MeshId), Matrix4x4.CreateTranslation(renderer.MeshOffset));
            default:
                return (null, Matrix4x4.Identity);
        }
    }
}
