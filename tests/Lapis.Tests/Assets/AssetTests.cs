using System.Text;
using Lapis.Assets;
using Lapis.Assets.Importers;

namespace Lapis.Tests.Assets;

public sealed class AssetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lapis-asset-tests", Guid.NewGuid().ToString("N"));

    public AssetTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Refresh_CreatesMetaFiles_AndKeepsIdsStable()
    {
        Write("Models/Box.obj", "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        var database = new AssetDatabase(_root);
        var asset = Assert.Single(database.Assets);

        Assert.True(File.Exists(asset.FullPath + AssetDatabase.MetaExtension));
        Assert.Equal(AssetType.Model, asset.Type);

        var reopened = new AssetDatabase(_root);
        Assert.Equal(asset.Id, Assert.Single(reopened.Assets).Id);
    }

    [Fact]
    public void Move_KeepsTheId()
    {
        Write("a.png", "x");
        var database = new AssetDatabase(_root);
        var asset = Assert.Single(database.Assets);

        var moved = database.Move(asset, Path.Combine(_root, "Textures", "b.png"));

        Assert.Equal(asset.Id, moved.Id);
        Assert.False(File.Exists(Path.Combine(_root, "a.png.lmeta")));
        Assert.Equal(moved, database.FindByPath(Path.Combine(_root, "Textures", "b.png")));
    }

    [Fact]
    public void CopiedFileWithMeta_GetsItsOwnId()
    {
        Write("a.lmat", "{}");
        var first = Assert.Single(new AssetDatabase(_root).Assets);
        File.Copy(first.FullPath, Path.Combine(_root, "copy.lmat"));
        File.Copy(first.FullPath + AssetDatabase.MetaExtension, Path.Combine(_root, "copy.lmat" + AssetDatabase.MetaExtension));

        var database = new AssetDatabase(_root);

        Assert.Equal(2, database.Assets.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void OrphanMeta_IsRemoved()
    {
        Write("gone.png" + AssetDatabase.MetaExtension, "{ \"guid\": \"" + Guid.NewGuid() + "\" }");

        var database = new AssetDatabase(_root);

        Assert.Empty(database.Assets);
        Assert.False(File.Exists(Path.Combine(_root, "gone.png" + AssetDatabase.MetaExtension)));
    }

    [Theory]
    [InlineData("asset:0f8fad5bd9cb469fa16570867728950e", false)]
    [InlineData("asset:0f8fad5bd9cb469fa16570867728950e#2", true)]
    public void References_RoundTrip(string reference, bool hasFragment)
    {
        Assert.True(AssetDatabase.TryParseReference(reference, out var id, out var fragment));
        Assert.Equal(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), id);
        Assert.Equal(hasFragment, fragment is not null);
        Assert.StartsWith(AssetDatabase.ReferenceOf(id), reference, StringComparison.Ordinal);
        Assert.False(AssetDatabase.TryParseReference(BuiltInMeshes.Cube, out _, out _));
    }

    [Fact]
    public void Obj_TriangulatesPolygons_ResolvesNegativeIndices_AndReadsMaterial()
    {
        Write("Textures/stone.png", "not really an image");
        Write("Models/quad.mtl", "newmtl Stone\nKd 0.5 0.25 1\nmap_Kd ../Textures/stone.png\n");
        var path = Write("Models/quad.obj", """
            mtllib quad.mtl
            v 0 0 0
            v 1 0 0
            v 1 0 -1
            v 0 0 -1
            vt 0 0
            vt 1 0
            vt 1 1
            vt 0 1
            usemtl Stone
            f -4/1 -3/2 -2/3 -1/4
            """);
        var database = new AssetDatabase(_root);

        var model = ObjImporter.Import(path, database);

        Assert.Equal(2, model.Mesh.TriangleCount);
        Assert.Equal(4, model.Mesh.Vertices.Length);
        Assert.Equal(new Color(0.5f, 0.25f, 1f), model.BaseColor);
        Assert.Equal(database.FindByPath(Path.Combine(_root, "Textures", "stone.png"))!.Reference, model.TextureId);
        Assert.All(model.Mesh.Vertices, v => Assert.True(v.Normal.Y > 0.99f, "Computed normals should face up."));
        Assert.Equal(1f, model.Mesh.Vertices[0].TexCoord.Y, 5); // OBJ v=0 is the bottom row
    }

    [Fact]
    public void Obj_BadIndex_ReportsLine()
    {
        var path = Write("bad.obj", "v 0 0 0\nf 1 2 3\n");

        var exception = Assert.Throws<InvalidDataException>(() => ObjImporter.Import(path, null));

        Assert.Contains("Line 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Stl_Ascii_IsConvertedToYUp()
    {
        var path = Write("tri.stl", """
            solid tri
              facet normal 0 0 1
                outer loop
                  vertex 0 0 0
                  vertex 1 0 0
                  vertex 0 1 0
                endloop
              endfacet
            endsolid tri
            """);

        var model = StlImporter.Import(path);

        Assert.Equal(1, model.Mesh.TriangleCount);
        Assert.Equal(new Vector3(0, 0, -1), model.Mesh.Vertices[2].Position); // STL +Y becomes -Z
        Assert.True(model.Mesh.Vertices[0].Normal.Y > 0.99f); // STL +Z (up) becomes +Y
    }

    [Fact]
    public void Stl_Binary_IsRead()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(new byte[80]);
            writer.Write(2u);
            for (var i = 0; i < 2; i++)
            {
                writer.Write(new float[12].SelectMany(BitConverter.GetBytes).ToArray()[..12]);
                foreach (var v in new[] { 0f, 0f, i, 1f, 0f, i, 0f, 1f, i })
                {
                    writer.Write(v);
                }

                writer.Write((ushort)0);
            }
        }

        var path = Path.Combine(_root, "two.stl");
        File.WriteAllBytes(path, stream.ToArray());

        Assert.Equal(2, StlImporter.Import(path).Mesh.TriangleCount);
    }

    [Fact]
    public void Gltf_AppliesNodeTransform_AndComputesMissingNormals()
    {
        var positions = new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, -1f };
        var buffer = Convert.ToBase64String(positions.SelectMany(BitConverter.GetBytes).ToArray());
        var path = Write("tri.gltf", $$"""
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "mesh": 0, "translation": [ 0, 2, 0 ] } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 } } ] } ],
              "buffers": [ { "uri": "data:application/octet-stream;base64,{{buffer}}", "byteLength": 36 } ],
              "bufferViews": [ { "buffer": 0, "byteOffset": 0, "byteLength": 36 } ],
              "accessors": [ { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [ 0, 0, -1 ], "max": [ 1, 0, 0 ] } ]
            }
            """);

        var model = GltfImporter.Import(path, null);

        Assert.Equal(1, model.Mesh.TriangleCount);
        Assert.Equal(new Vector3(1, 2, 0), model.Mesh.Vertices[1].Position);
        Assert.True(model.Mesh.Vertices[0].Normal.Y > 0.99f);
    }

    [Fact]
    public void Material_RoundTrips()
    {
        var path = Path.Combine(_root, "m.lmat");
        new Material { Color = Color.Red, TextureId = "asset:0f8fad5bd9cb469fa16570867728950e", Metallic = 1, Roughness = 0.2f }.Save(path);

        var loaded = Material.Load(path);

        Assert.Equal(Color.Red, loaded.Color);
        Assert.Equal("asset:0f8fad5bd9cb469fa16570867728950e", loaded.TextureId);
        Assert.Equal(1f, loaded.Metallic);
        Assert.Equal(0.2f, loaded.Roughness);
    }

    [Fact]
    public void ModelAsset_CanBeUsedByMeshCollider()
    {
        Write("Models/floor.obj", "v -5 0 5\nv 5 0 5\nv 5 0 -5\nv -5 0 -5\nf 1 2 3 4\n");
        var database = new AssetDatabase(_root);
        var previous = AssetDatabase.Active;
        AssetDatabase.Active = database;
        try
        {
            var data = BuiltInMeshes.GetData(Assert.Single(database.Assets).Reference);

            Assert.NotNull(data);
            Assert.Equal(2, data.TriangleCount);
        }
        finally
        {
            AssetDatabase.Active = previous;
        }
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
