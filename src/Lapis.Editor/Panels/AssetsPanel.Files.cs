using Hexa.NET.ImGui;
using Lapis.Assets;

namespace Lapis.Editor.Panels;

/// <summary>Assets panel: helpers for reading folders and naming files.</summary>
internal sealed unsafe partial class AssetsPanel
{
    private static string[] SafeDirectories(string folder)
    {
        try
        {
            return [.. Directory.GetDirectories(folder).Where(d => !Path.GetFileName(d).StartsWith('.')).Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string[] SafeFiles(string folder)
    {
        try
        {
            return [.. Directory.GetFiles(folder)
                .Where(f => !f.EndsWith(AssetDatabase.MetaExtension, StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name}{i}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static bool IsInside(string root, string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width)
        {
            return text;
        }

        for (var length = text.Length - 1; length > 1; length--)
        {
            var candidate = string.Concat(text.AsSpan(0, length), "...");
            if (ImGui.CalcTextSize(candidate).X <= width)
            {
                return candidate;
            }
        }

        return text[..1];
    }
}
