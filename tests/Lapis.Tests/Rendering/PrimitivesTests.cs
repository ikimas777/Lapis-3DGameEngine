using Lapis.Rendering;

namespace Lapis.Tests.Rendering;

public class PrimitivesTests
{
    public static TheoryData<string> Shapes => ["cube", "plane", "sphere"];

    [Fact]
    public void Cube_HasFourVerticesAndTwoTrianglesPerFace()
    {
        var cube = Primitives.CreateCube();

        Assert.Equal(24, cube.Vertices.Length);
        Assert.Equal(12, cube.TriangleCount);
    }

    [Fact]
    public void Cube_VerticesLieOnItsSurface()
    {
        var cube = Primitives.CreateCube(2f);

        foreach (var vertex in cube.Vertices)
        {
            var p = Vector3.Abs(vertex.Position);
            Assert.Equal(1f, MathF.Max(p.X, MathF.Max(p.Y, p.Z)), 5);
        }
    }

    [Fact]
    public void Sphere_VerticesLieOnRadius()
    {
        var sphere = Primitives.CreateSphere(radius: 2f, segments: 12, rings: 6);

        Assert.Equal(13 * 7, sphere.Vertices.Length);
        Assert.All(sphere.Vertices, vertex => Assert.Equal(2f, vertex.Position.Length(), 4));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Normals_AreUnitLength(string shape)
    {
        Assert.All(Create(shape).Vertices, vertex => Assert.Equal(1f, vertex.Normal.Length(), 4));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Triangles_AreCounterClockwiseFromOutside(string shape)
    {
        var mesh = Create(shape);

        for (var i = 0; i < mesh.Indices.Length; i += 3)
        {
            var a = mesh.Vertices[mesh.Indices[i]];
            var b = mesh.Vertices[mesh.Indices[i + 1]];
            var c = mesh.Vertices[mesh.Indices[i + 2]];

            var faceNormal = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (faceNormal.LengthSquared() < 1e-12f)
            {
                continue; // degenerate triangle at a sphere pole
            }

            var vertexNormal = a.Normal + b.Normal + c.Normal;
            Assert.True(Vector3.Dot(faceNormal, vertexNormal) > 0f, $"{shape}: triangle {i / 3} faces inwards.");
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Indices_AreInRange(string shape)
    {
        var mesh = Create(shape);

        Assert.Equal(0, mesh.Indices.Length % 3);
        Assert.All(mesh.Indices, index => Assert.InRange(index, 0u, (uint)mesh.Vertices.Length - 1));
    }

    private static MeshData Create(string shape) => shape switch
    {
        "cube" => Primitives.CreateCube(),
        "plane" => Primitives.CreatePlane(),
        "sphere" => Primitives.CreateSphere(),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };
}
