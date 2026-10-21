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
    public static string EditorExecutable => Path.Combine(InstallFolder, OperatingSystem.IsWindows() ? "Lapis.Editor.exe" : "Lapis.Editor");

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
            return ReplaceFiles(FindPackageRoot(unpacked), InstallFolder);
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
    internal static int ReplaceFiles(string source, string target)
    {
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                File.Move(destination, FreeOldName(destination));
            }

            File.Move(file, destination);
            if (!OperatingSystem.IsWindows() && Path.GetExtension(destination).Length == 0)
            {
                // Linux executables (Lapis.Editor, Player/Lapis.Player) must stay runnable.
                File.SetUnixFileMode(destination, File.GetUnixFileMode(destination)
                    | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            count++;
        }

        return count;
    }

    /// <summary>Deletes <c>.old</c> files left by an earlier update (call when the editor starts).</summary>
    public static void DeleteLeftovers(string? folder = null)
    {
        folder ??= InstallFolder;
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*" + OldSuffix + "*", SearchOption.TopDirectoryOnly)
                     .Concat(Directory.Exists(Path.Combine(folder, "Player"))
                         ? Directory.EnumerateFiles(Path.Combine(folder, "Player"), "*" + OldSuffix + "*")
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
