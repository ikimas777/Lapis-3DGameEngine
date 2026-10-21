using System.Text.Json;

namespace Lapis.Editor;

/// <summary>Per-user editor preferences, stored in <c>%LOCALAPPDATA%\Lapis\editor-settings.json</c>.</summary>
internal sealed class EditorSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Project file opened last; reopened when the editor starts without arguments.</summary>
    public string? LastProject { get; set; }

    /// <summary>Scene opened last in <see cref="LastProject"/>.</summary>
    public string? LastScene { get; set; }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), EngineInfo.Name, "editor-settings.json");

    public static EditorSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new EditorSettings()
                : new EditorSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warning($"Editor settings could not be read and were reset: {exception.Message}");
            return new EditorSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Editor settings could not be saved: {exception.Message}");
        }
    }
}
