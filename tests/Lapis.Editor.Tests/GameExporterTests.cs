using Lapis.Editor.Export;
using Lapis.Projects;
using Lapis.Serialization;

namespace Lapis.Editor.Tests;

[Collection(nameof(ScriptCompilerTests))] // compiles scripts like those tests
public sealed class GameExporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lapis-export-tests", Guid.NewGuid().ToString("N"));
    private readonly LapisProject _project;
    private readonly string _runtime;

    public GameExporterTests()
    {
        _project = LapisProject.Create(Path.Combine(_root, "MyGame"));
        SceneSerializer.Save(new Scene { Name = "Main" }, _project.StartScenePath!);
        File.WriteAllText(Path.Combine(_project.AssetsDirectory, "Scripts", "Spin.cs"), "public class Spin : Behaviour { }");
        File.WriteAllText(Path.Combine(_project.AssetsDirectory, "Scripts", "Spin.cs.lmeta"), "{}");
        Directory.CreateDirectory(Path.Combine(_project.AssetsDirectory, "Textures"));
        File.WriteAllText(Path.Combine(_project.AssetsDirectory, "Textures", "a.png"), "image");

        _runtime = Path.Combine(_root, "Lapis.Runtime.exe");
        File.WriteAllText(_runtime, "player");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Export_WritesExecutableScriptsManifestAndAssets()
    {
        var output = Path.Combine(_root, "Out");

        var result = GameExporter.Export(new ExportSettings(_project, output, _project.StartScene!, "My Game"), _runtime);

        // Windows: "My Game.exe"; Linux: "My Game"; macOS: the app "My Game.app".
        var program = OperatingSystem.IsMacOS()
            ? Path.Combine(output, "My Game.app", "Contents", "MacOS", "My Game")
            : Path.Combine(output, OperatingSystem.IsWindows() ? "My Game.exe" : "My Game");
        Assert.Equal(OperatingSystem.IsMacOS() ? Path.Combine(output, "My Game.app") : program, result.ExecutablePath);
        Assert.Equal("player", File.ReadAllText(program));
        var gameFolder = GameFolder(output, "My Game");
        Assert.True(new FileInfo(Path.Combine(gameFolder, "Game.dll")).Length > 0);

        var data = Path.Combine(gameFolder, "Data");
        var manifest = GameManifest.Load(data);
        Assert.Equal("MyGame", manifest.Name);
        Assert.Equal("Assets/Scenes/Main.lscene", manifest.StartScene);
        Assert.True(File.Exists(Path.Combine(data, "Assets", "Scenes", "Main.lscene")));
        Assert.True(File.Exists(Path.Combine(data, "Assets", "Textures", "a.png")));

        // Scripts are compiled into Game.dll, so their sources (and .lmeta files) are not shipped.
        Assert.False(File.Exists(Path.Combine(data, "Assets", "Scripts", "Spin.cs")));
        Assert.False(File.Exists(Path.Combine(data, "Assets", "Scripts", "Spin.cs.lmeta")));
    }

    [Fact]
    public void Export_Again_ReplacesOldData()
    {
        var output = Path.Combine(_root, "Out");
        var settings = new ExportSettings(_project, output, _project.StartScene!, "Game");
        GameExporter.Export(settings, _runtime);
        File.Delete(Path.Combine(_project.AssetsDirectory, "Textures", "a.png"));

        GameExporter.Export(settings, _runtime);

        Assert.False(File.Exists(Path.Combine(GameFolder(output, "Game"), "Data", "Assets", "Textures", "a.png")));
    }

    // The folder with Game.dll and Data: the output folder, or Contents/Resources of the app on macOS.
    private static string GameFolder(string output, string name) =>
        OperatingSystem.IsMacOS() ? Path.Combine(output, name + ".app", "Contents", "Resources") : output;

    [Fact]
    public void Export_WithScriptErrors_Fails()
    {
        File.WriteAllText(Path.Combine(_project.AssetsDirectory, "Scripts", "Broken.cs"), "public class Broken : Behaviour { void X() { nope(); } }");

        var exception = Assert.Throws<ExportException>(() =>
            GameExporter.Export(new ExportSettings(_project, Path.Combine(_root, "Out"), _project.StartScene!, "Game"), _runtime));

        Assert.Contains("error", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Export_IntoAssetsFolder_IsRefused()
    {
        Assert.Throws<ExportException>(() =>
            GameExporter.Export(new ExportSettings(_project, Path.Combine(_project.AssetsDirectory, "Build"), _project.StartScene!, "Game"), _runtime));
    }

    [Fact]
    public void Export_WithMissingScene_IsRefused()
    {
        Assert.Throws<ExportException>(() =>
            GameExporter.Export(new ExportSettings(_project, Path.Combine(_root, "Out"), "Assets/Scenes/Nope.lscene", "Game"), _runtime));
    }
}
