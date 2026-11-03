namespace Lapis.Tests.Common;

/// <summary>
/// A unique folder under the system's temporary folder for one test class, deleted (with retries, as Windows can
/// hold a file for a moment after it is closed) when disposed.
/// </summary>
public sealed class TempFolder : IDisposable
{
    /// <param name="name">Group name, e.g. "lapis-asset-tests"; a unique sub-folder is made inside it.</param>
    /// <param name="create">False to leave creating the folder to the test (e.g. when it checks that it is made).</param>
    public TempFolder(string name, bool create = true)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name, Guid.NewGuid().ToString("N"));
        if (create)
        {
            Directory.CreateDirectory(Path);
        }
    }

    /// <summary>Full path of the folder.</summary>
    public string Path { get; }

    public void Dispose()
    {
        for (var attempt = 0; Directory.Exists(Path); attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }
}
