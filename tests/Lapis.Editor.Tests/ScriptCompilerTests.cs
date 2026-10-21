using Lapis.Editor.Scripting;
using Lapis.Serialization;

namespace Lapis.Editor.Tests;

// Loading scripts changes the global script assembly, so these tests run one at a time.
[Collection(nameof(ScriptCompilerTests))]
public sealed class ScriptCompilerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "lapis-script-tests", Guid.NewGuid().ToString("N"));

    public ScriptCompilerTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        ComponentTypes.SetScriptAssembly(null);
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Compile_ValidScript_ProducesAssemblyAndTypeLocations()
    {
        var file = Write("Mover.cs", """
            public class Mover : Behaviour
            {
                public float speed = 2f;

                public override void Update()
                {
                    transform.Position += new Vector3(speed, 0, 0) * Time.DeltaTime;
                }
            }
            """);

        var result = ScriptCompiler.Compile([file]);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.AssemblyBytes);
        Assert.Equal(new ScriptLocation(file, 1), result.TypeLocations["Mover"]);
    }

    [Fact]
    public void Compile_Error_ReportsFileAndLine()
    {
        var file = Write("Broken.cs", """
            public class Broken : Behaviour
            {
                public override void Update()
                {
                    undefinedThing = 1;
                }
            }
            """);

        var result = ScriptCompiler.Compile([file]);

        Assert.False(result.Success);
        var error = Assert.Single(result.Diagnostics, d => d.IsError);
        Assert.Equal("CS0103", error.Id);
        Assert.Equal(file, error.FilePath);
        Assert.Equal(5, error.Line);
    }

    [Fact]
    public void LoadedScripts_AreFoundBySerializer_AndCanBeReplaced()
    {
        var file = Write("Health.cs", "public class Health : Behaviour { public int points = 10; }");
        var host = new ScriptHost();

        var first = ScriptCompiler.Compile([file]);
        ComponentTypes.SetScriptAssembly(host.Load(first.AssemblyBytes!, first.SymbolBytes));
        var scene = new Scene();
        var entity = scene.CreateEntity();
        entity.AddComponent(ComponentTypes.Resolve("Health")!);
        var json = SceneSerializer.ToJson(scene);

        // A new version adds a field; reloading keeps the saved value and uses the new default.
        File.WriteAllText(file, "public class Health : Behaviour { public int points = 10; public int armor = 3; }");
        var second = ScriptCompiler.Compile([file]);
        var newAssembly = host.Load(second.AssemblyBytes!, second.SymbolBytes);
        ComponentTypes.SetScriptAssembly(newAssembly);
        host.UnloadPrevious();

        var reloaded = SceneSerializer.FromJson(json).FindById(entity.Id)!.Components[1];
        Assert.Same(newAssembly, reloaded.GetType().Assembly);
        var armor = SerializedMember.For(reloaded.GetType()).Single(m => m.Key == "armor");
        Assert.Equal(3, armor.GetValue(reloaded));
    }

    [Fact]
    public void GlobalUsings_LetScriptsSkipUsingDirectives()
    {
        var file = Write("Usings.cs", """
            public class Usings : Behaviour
            {
                public List<Vector3> points = new();
                public int Count => points.Where(p => p.X > 0).Count();
            }
            """);

        Assert.True(ScriptCompiler.Compile([file]).Success);
    }

    [Fact]
    public void NewScript_ListsTheEngineAndDotNetNamespaces_AndCompilesWithoutWarnings()
    {
        var source = ScriptProjectFiles.NewScript("Enemy");

        Assert.StartsWith(
            "using System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing System.Numerics;\nusing Lapis;\n\n",
            source,
            StringComparison.Ordinal);

        var result = ScriptCompiler.Compile([Write("Enemy.cs", source)]);
        Assert.True(result.Success);
        Assert.DoesNotContain(result.Diagnostics, d => !d.IsError);
    }

    [Fact]
    public void EventMethodsWithWrongCaseOrNoOverride_GetAWarning()
    {
        var file = Write("Test.cs", """
            public class Test : Behaviour
            {
                public void start()
                {
                }

                public void OnTriggerBegin(Collider other)
                {
                }

                public override void Update()
                {
                }

                private void Helper()
                {
                }
            }
            """);

        var result = ScriptCompiler.Compile([file]);

        Assert.True(result.Success);
        var warnings = result.Diagnostics.Where(d => d.Id == ScriptCompiler.MisnamedEventId).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Contains("Declare it as 'public override void Start()'", warnings[0].Message, StringComparison.Ordinal);
        Assert.Equal((file, 3), (warnings[0].FilePath, warnings[0].Line));
        Assert.Contains("'public override void OnTriggerBegin(Collider other)'", warnings[1].Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Player", true)]
    [InlineData("_hidden", true)]
    [InlineData("Player2", true)]
    [InlineData("2Player", false)]
    [InlineData("my script", false)]
    [InlineData("class", false)]
    [InlineData("", false)]
    public void IsValidClassName(string name, bool expected)
    {
        Assert.Equal(expected, ScriptProjectFiles.IsValidClassName(name));
    }

    private string Write(string name, string source)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, source);
        return path;
    }
}
