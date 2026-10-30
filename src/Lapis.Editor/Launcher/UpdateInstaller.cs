using System.Formats.Tar;
using System.IO.Compression;

namespace Lapis.Editor.Launcher;

/// <summary>
/// Installs a downloaded release over the running editor: the package is unpacked and every file in it replaces
/// the one next to the editor (like pulling a new version). Files the package does not contain, such as user
/// settings or projects, are left alone.
/// </summary>
/// <remarks>
/// A running program cannot be overwritten on Windows, but it can be renamed. Each replaced file is therefore
/// renamed to <c>name.old</c> first; the old copies are deleted the next time the editor starts
/// (<see cref="DeleteLeftovers"/>).
/// </remarks>
internal static class UpdateInstaller
{
    /// <summary>Suffix of replaced files that could not be deleted yet.</summary>
    public const string OldSuffix = ".old";

    /// <summary>The folder of the running editor executable.</summary>
    public static string InstallFolder => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    /// <summary>The editor executable inside <see cref="InstallFolder"/>.</summary>
    public static string EditorExecutable => Path.Combine(InstallFolder, HostPlatform.Executable("Lapis.Editor"));

    /// <summary>
    /// True when the editor runs as a published single-file program, which can be replaced. When it runs from
    /// source (<c>dotnet run</c>), updating means pulling the repository instead.
    /// </summary>
    public static bool CanInstallInPlace =>
        string.IsNullOrEmpty(typeof(UpdateInstaller).Assembly.Location) && File.Exists(EditorExecutable);

    /// <summary>Downloads the package, unpacks it and replaces the editor's files. Returns the number of files replaced.</summary>
    public static async Task<int> InstallAsync(ReleaseAsset asset, IProgress<float>? progress, CancellationToken cancellationToken = default)
    {
        var work = Path.Combine(Path.GetTempPath(), "lapis-update-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var package = await UpdateChecker.DownloadAsync(asset, work, progress, cancellationToken).ConfigureAwait(false);
            var unpacked = Path.Combine(work, "unpacked");
            await ExtractAsync(package, unpacked, cancellationToken).ConfigureAwait(false);
            if (HostPlatform.AppBundle is not { } bundle)
            {
                return ReplaceFiles(FindPackageRoot(unpacked), InstallFolder);
            }

            // macOS: the package holds Lapis.app next to Docs and README.txt, so everything is replaced in the folder
            // that holds the app. A running program may be deleted on macOS, so the old copies go at once, and the
            // changed app is signed again (Apple Silicon only runs signed programs).
            var oldFiles = new List<string>();
            var count = ReplaceFiles(FindPackageRoot(unpacked), Path.GetDirectoryName(bundle)!, oldFiles);
            foreach (var old in oldFiles)
            {
                File.Delete(old);
            }

            MacApp.SignAdHoc(bundle);
            return count;
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    /// <summary>Unpacks a <c>.zip</c> or <c>.tar.gz</c> package into <paramref name="destination"/>.</summary>
    internal static async Task ExtractAsync(string package, string destination, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destination);
        if (package.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await ZipFile.ExtractToDirectoryAsync(package, destination, overwriteFiles: true, cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var file = File.OpenRead(package);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzip, destination, overwriteFiles: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The packages hold one top folder (<c>Lapis/</c>); returns it, or the folder itself when there is none.</summary>
    internal static string FindPackageRoot(string unpacked)
    {
        var folders = Directory.GetDirectories(unpacked);
        return folders.Length == 1 && Directory.GetFiles(unpacked).Length == 0 ? folders[0] : unpacked;
    }

    /// <summary>
    /// Moves every file from <paramref name="source"/> to the same place under <paramref name="target"/>, renaming
    /// files that are already there to <c>.old</c> first. Returns the number of files written.
    /// </summary>
    /// <param name="source">The unpacked package.</param>
    /// <param name="target">The installation.</param>
    /// <param name="oldFiles">Receives the paths of the renamed <c>.old</c> copies.</param>
    internal static int ReplaceFiles(string source, string target, List<string>? oldFiles = null)
    {
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                var old = FreeOldName(destination);
                File.Move(destination, old);
                oldFiles?.Add(old);
            }

            File.Move(file, destination);
            if (!OperatingSystem.IsWindows() && IsProgram(destination))
            {
                // Linux and macOS programs (Lapis.Editor, Runtime/Lapis.Runtime...) must stay runnable.
                File.SetUnixFileMode(destination, File.GetUnixFileMode(destination)
                    | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            count++;
        }

        return count;
    }

    /// <summary>True for the programs in a Linux or macOS package: files without an extension and the Lapis programs.</summary>
    internal static bool IsProgram(string path) =>
        Path.GetFileName(path) is "Lapis.Editor" or "Lapis.Monitor" or "Lapis.Runtime" || Path.GetExtension(path).Length == 0;

    /// <summary>Deletes <c>.old</c> files left by an earlier update (call when the editor starts).</summary>
    public static void DeleteLeftovers(string? folder = null)
    {
        folder ??= InstallFolder;
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*" + OldSuffix + "*", SearchOption.TopDirectoryOnly)
                     .Concat(Directory.Exists(Path.Combine(folder, "Runtime"))
                         ? Directory.EnumerateFiles(Path.Combine(folder, "Runtime"), "*" + OldSuffix + "*")
                         : []))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Still in use (e.g. another editor window); removed on a later start.
            }
        }

        DeleteLegacyPlayerFolder(folder);
    }

    /// <summary>
    /// Versions before 0.6 kept the game runtime in <c>Player/Lapis.Player(.exe)</c>. After an update brought the new
    /// <c>Runtime</c> folder, the old one is removed if it holds nothing but that program.
    /// </summary>
    private static void DeleteLegacyPlayerFolder(string folder)
    {
        var legacy = Path.Combine(folder, "Player");
        if (!Directory.Exists(legacy) || !Directory.Exists(Path.Combine(folder, "Runtime")) ||
            Directory.EnumerateDirectories(legacy).Any() ||
            !Directory.EnumerateFiles(legacy).All(file => Path.GetFileName(file).StartsWith("Lapis.Player", StringComparison.Ordinal)))
        {
            return;
        }

        try
        {
            Directory.Delete(legacy, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Tried again on the next start.
        }
    }

    /// <summary>"Lapis.Editor.exe.old", or "Lapis.Editor.exe.old2"... when an older copy is still locked.</summary>
    private static string FreeOldName(string path)
    {
        var old = path + OldSuffix;
        for (var i = 2; File.Exists(old); i++)
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                old = $"{path}{OldSuffix}{i}";
            }
        }

        return old;
    }

    private static void TryDeleteDirectory(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Temporary files; the system cleans them up eventually.
        }
    }
}
