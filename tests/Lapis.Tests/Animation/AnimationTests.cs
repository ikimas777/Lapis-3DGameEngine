using System.Numerics;
using System.Text.Json.Nodes;
using Lapis.Animation;
using Lapis.Assets.Importers;

namespace Lapis.Tests.Animation;

using Scene = Lapis.Scene;

[Collection(GlobalState.Collection)] // sets AssetDatabase.Active
public sealed class AnimationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "lapis-animation-tests", Guid.NewGuid().ToString("N"));

    public AnimationTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Import_ReadsTheSkeletonClipsAndRestPoseVertices()
    {
        var model = GltfImporter.Import(WriteArmModel(), null);

        var skeleton = Assert.IsType<ModelSkeleton>(model.Skeleton);
        Assert.Equal(["Root", "Arm", "Mesh"], skeleton.Nodes.Select(node => node.Name));
        Assert.Equal(0, skeleton.Nodes[1].Parent); // Arm is under Root
        var clip = Assert.Single(skeleton.Clips);
        Assert.Equal("Bend", clip.Name);
        Assert.Equal(1f, clip.Duration);
        Assert.NotNull(model.Mesh.Skin);
        VectorAssert.Equal(new Vector3(0f, 2f, 0f), model.Mesh.Vertices[2].Position);
    }

    [Fact]
    public void BoneMatrices_MoveVerticesWithTheirBones()
    {
        var model = GltfImporter.Import(WriteArmModel(), null);
        var skeleton = model.Skeleton!;
        var poses = new NodePose[skeleton.Nodes.Count];
        skeleton.RestPose(poses);
        var bones = new Matrix4x4[skeleton.BoneNodes.Count];

        skeleton.ComputeBoneMatrices(poses, bones);
        foreach (var bone in bones)
        {
            Assert.True(bone.IsIdentity || Matrix4x4.Identity.Equals(Round(bone)), "At rest every bone stays where it is.");
        }

        skeleton.Clips[0].Apply(poses, 1f); // the arm turned 90° around Z
        skeleton.ComputeBoneMatrices(poses, bones);
        VectorAssert.Equal(new Vector3(-1f, 1f, 0f), Skin(model, bones, 2));
        VectorAssert.Equal(new Vector3(0f, 2f, 0f), Skin(model, bones, 1));
        VectorAssert.Equal(Vector3.Zero, Skin(model, bones, 0)); // on the root, which does not move
    }

    [Fact]
    public void Channel_InterpolatesBetweenKeys_AndHoldsOutside()
    {
        var channel = new AnimationChannel(0, AnimationProperty.Translation, [0f, 2f], [Vector4.Zero, new Vector4(4f, 0f, 0f, 0f)]);
        var step = channel with { Step = true };

        Assert.Equal(2f, channel.Sample(1f).X);
        Assert.Equal(0f, step.Sample(1f).X);
        Assert.Equal(4f, channel.Sample(5f).X);
        Assert.Equal(0f, channel.Sample(-1f).X);
    }

    [Fact]
    public void Animator_PlaysLoopsFiresEventsAndStops()
    {
        var path = WriteArmModel();
        var assets = new Lapis.Assets.AssetDatabase(_folder);
        var previous = Lapis.Assets.AssetDatabase.Active;
        Lapis.Assets.AssetDatabase.Active = assets;
        try
        {
            var scene = new Scene();
            var entity = scene.CreateEntity("Arm");
            entity.AddComponent<MeshRenderer>().MeshId = assets.FindByPath(path)!.Reference;
            var animator = entity.AddComponent<Animator>();
            var events = 0;
            animator.AddEvent("Bend", 0.5f, () => events++);

            animator.Advance(0.25f); // starts "Bend" (the first clip) on the first frame
            Assert.Equal("Bend", animator.CurrentClip);
            Assert.NotNull(animator.BoneMatrices);
            animator.Advance(0.5f);
            Assert.Equal(1, events);
            animator.Advance(0.5f); // loops: 1.25 → 0.25
            Assert.Equal(0.25f, animator.Time, 3);
            animator.Advance(0.5f);
            Assert.Equal(2, events);

            animator.Loop = false;
            animator.Advance(2f);
            Assert.False(animator.IsPlaying);
            Assert.Equal(1f, animator.NormalizedTime, 3);
            Assert.False(animator.Play("Missing"));
        }
        finally
        {
            Lapis.Assets.AssetDatabase.Active = previous;
        }
    }

    private static Vector3 Skin(ImportedModel model, Matrix4x4[] bones, int vertex)
    {
        var skin = model.Mesh.Skin![vertex];
        var position = model.Mesh.Vertices[vertex].Position;
        var result = Vector3.Zero;
        for (var k = 0; k < 4; k++)
        {
            result += Vector3.Transform(position, bones[(int)skin.Bones[k]]) * skin.Weights[k];
        }

        return result;
    }

    private static Matrix4x4 Round(Matrix4x4 m)
    {
        static float R(float value) => MathF.Round(value, 4);
        return new Matrix4x4(R(m.M11), R(m.M12), R(m.M13), R(m.M14), R(m.M21), R(m.M22), R(m.M23), R(m.M24),
            R(m.M31), R(m.M32), R(m.M33), R(m.M34), R(m.M41), R(m.M42), R(m.M43), R(m.M44));
    }

    /// <summary>
    /// A glTF file with two bones (Root and Arm, one metre up), a triangle with one vertex on Root and two on Arm,
    /// and an animation "Bend" that turns the Arm 90° around Z in one second.
    /// </summary>
    private string WriteArmModel()
    {
        var buffer = new MemoryStream();
        var writer = new BinaryWriter(buffer);
        var views = new JsonArray();
        var accessors = new JsonArray();

        int Add(Action write, int count, string type, int componentType = 5126)
        {
            var offset = (int)buffer.Length;
            write();
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = (int)buffer.Length - offset });
            accessors.Add(new JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = componentType, ["count"] = count, ["type"] = type });
            return accessors.Count - 1;
        }

        void Floats(params float[] values)
        {
            foreach (var value in values)
            {
                writer.Write(value);
            }
        }

        var positions = Add(() => Floats(0, 0, 0, 1, 1, 0, 0, 2, 0), 3, "VEC3");
        ((JsonObject)accessors[positions]!)["min"] = new JsonArray(0, 0, 0);
        ((JsonObject)accessors[positions]!)["max"] = new JsonArray(1, 2, 0);
        var joints = Add(() =>
        {
            foreach (var joint in new ushort[] { 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 })
            {
                writer.Write(joint);
            }
        }, 3, "VEC4", 5123);
        var weights = Add(() => Floats(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0), 3, "VEC4");
        var inverseBind = Add(() => Floats(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -1, 0, 1), 2, "MAT4");
        var times = Add(() => Floats(0, 1), 2, "SCALAR");
        ((JsonObject)accessors[times]!)["min"] = new JsonArray(0);
        ((JsonObject)accessors[times]!)["max"] = new JsonArray(1);
        var half = MathF.Sqrt(0.5f);
        var rotations = Add(() => Floats(0, 0, 0, 1, 0, 0, half, half), 2, "VEC4");
        writer.Flush();

        var document = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0, 2) }),
            ["nodes"] = new JsonArray(
                new JsonObject { ["name"] = "Root", ["children"] = new JsonArray(1) },
                new JsonObject { ["name"] = "Arm", ["translation"] = new JsonArray(0, 1, 0) },
                new JsonObject { ["name"] = "Mesh", ["mesh"] = 0, ["skin"] = 0 }),
            ["meshes"] = new JsonArray(new JsonObject
            {
                ["primitives"] = new JsonArray(new JsonObject
                {
                    ["attributes"] = new JsonObject { ["POSITION"] = positions, ["JOINTS_0"] = joints, ["WEIGHTS_0"] = weights },
                }),
            }),
            ["skins"] = new JsonArray(new JsonObject { ["joints"] = new JsonArray(0, 1), ["inverseBindMatrices"] = inverseBind }),
            ["animations"] = new JsonArray(new JsonObject
            {
                ["name"] = "Bend",
                ["samplers"] = new JsonArray(new JsonObject { ["input"] = times, ["output"] = rotations }),
                ["channels"] = new JsonArray(new JsonObject { ["sampler"] = 0, ["target"] = new JsonObject { ["node"] = 1, ["path"] = "rotation" } }),
            }),
            ["buffers"] = new JsonArray(new JsonObject
            {
                ["byteLength"] = (int)buffer.Length,
                ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(buffer.ToArray()),
            }),
            ["bufferViews"] = views,
            ["accessors"] = accessors,
        };

        var path = Path.Combine(_folder, "Arm.gltf");
        File.WriteAllText(path, document.ToJsonString());
        return path;
    }
}
