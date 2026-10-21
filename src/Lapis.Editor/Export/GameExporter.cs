using Lapis.Assets;
using Lapis.Editor.Scripting;
using Lapis.Projects;

namespace Lapis.Editor.Export;

/// <summary>What to export and where.</summary>
/// <param name="Project">The project to export.</param>
/// <param name="OutputFolder">Folder that receives the game; created when missing.</param>
/// <param name="StartScene">Project-relative path of the first scene, e.g. <c>Assets/Scenes/Main.lscene</c>.</param>
/// <param name="ExecutableName">File name of the game's executable, without an extension such as <c>.exe</c>.</param>
internal sealed record ExportSettings(LapisProject Project, string OutputFolder, string StartScene, string ExecutableName);

/// <summary>Result of a successful export.</summary>
internal sealed record ExportResult(string ExecutablePath, int AssetCount, long TotalBytes);

/// <summary>Thrown when a game cannot be exported (for example because scripts do not compile).</summary>
internal sealed class ExportException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// Builds a standalone copy of a game: the player executable, the compiled scripts and the assets.
/// See <see cref="GameManifest"/> for the resulting folder layout.
/// </summary>
internal static class GameExporter
{
    /// <summary>Exports the game. Safe to call from a background thread.</summary>
    /// <param name="settings">What to export.</param>
    /// <param name="runtimeExecutable">The Lapis runtime to copy as the game's executable.</param>
    /// <param name="progress">Receives short status messages.</param>
    /// <exception cref="ExportException">The game could not be exported; the message says why.</exception>
    public static ExportResult Export(ExportSettings settings, string runtimeExecutable, IProgress<string>? progress = null)
    {
        var project = settings.Project;
        var startScene = Path.Combine(project.Directory, settings.StartScene.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(startScene))
        {
            throw new ExportException($"The start scene '{settings.StartScene}' does not exist.");
        }

        if (!File.Exists(runtimeExecutable))
        {
            throw new ExportException($"The game runtime was not found at '{runtimeExecutable}'.");
        }

        if (string.IsNullOrWhiteSpace(settings.ExecutableName) || settings.ExecutableName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ExportException($"'{settings.ExecutableName}' is not a valid file name.");
        }

        var output = Path.GetFullPath(settings.OutputFolder);
        if (IsInside(project.AssetsDirectory, output))
        {
            throw new ExportException("The output folder must not be inside the Assets folder.");
        }

        progress?.Report("Compiling scripts...");
        var compilation = ScriptCompiler.Compile(ScriptProjectFiles.FindScripts(project));
        if (!compilation.Success)
        {
            var first = compilation.Diagnostics.First(d => d.IsError);
            throw new ExportException($"Scripts have {compilation.ErrorCount} error(s); fix them first. First error: {first}");
        }

        try
        {
            progress?.Report("Preparing the output folder...");
            Directory.CreateDirectory(output);
            var data = Path.Combine(output, GameManifest.DataFolderName);
            if (Directory.Exists(data))
            {
                Directory.Delete(data, recursive: true); // only our own data; anything else in the folder is left alone
            }

            progress?.Report("Copying the runtime...");
            // Same extension as the runtime: ".exe" on Windows, none on Linux.
            var executable = Path.Combine(output, settings.ExecutableName + Path.GetExtension(runtimeExecutable));
            File.Copy(runtimeExecutable, executable, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(executable, File.GetUnixFileMode(executable)
                    | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            File.WriteAllBytes(Path.Combine(output, GameManifest.ScriptsFileName), compilation.AssemblyBytes!);
            File.WriteAllBytes(Path.Combine(output, Path.ChangeExtension(GameManifest.ScriptsFileName, ".pdb")), compilation.SymbolBytes!);

            progress?.Report("Copying assets...");
            var assetCount = CopyAssets(project.AssetsDirectory, Path.Combine(data, "Assets"));

            new GameManifest(project.Name, settings.StartScene.Replace('\\', '/')).Save(data);

            var totalBytes = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
            progress?.Report("Done.");
            return new ExportResult(executable, assetCount, totalBytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ExportException($"Could not write the game to '{output}': {exception.Message}", exception);
        }
    }

    /// <summary>Copies every asset except scripts (they are compiled into Game.dll), keeping folders and .lmeta files.</summary>
    private static int CopyAssets(string source, string destination)
    {
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var assetFile = file.EndsWith(AssetDatabase.MetaExtension, StringComparison.OrdinalIgnoreCase)
                ? file[..^AssetDatabase.MetaExtension.Length]
                : file;
            if (AssetDatabase.TypeOf(assetFile) == AssetType.Script)
            {
                continue;
            }

            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            if (!ReferenceEquals(assetFile, file))
            {
                continue; // .lmeta files are not counted as assets
            }

            count++;
        }

        return count;
    }

    private static bool IsInside(string folder, string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
