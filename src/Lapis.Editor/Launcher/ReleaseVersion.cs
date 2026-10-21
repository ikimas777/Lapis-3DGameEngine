using System.Globalization;

namespace Lapis.Editor.Launcher;

/// <summary>How finished a release is. Also used as the update channel: a channel gets releases of its stage and later stages.</summary>
internal enum ReleaseStage
{
    /// <summary>Features are still being built and may change (<c>-alpha.N</c>).</summary>
    Alpha,

    /// <summary>Features are done; bugs are being fixed (<c>-beta.N</c>).</summary>
    Beta,

    /// <summary>A finished release, without a suffix.</summary>
    Stable,
}

/// <summary>
/// A Lapis version: <c>X.Y.Z</c> (major update, general update, hotfix) with an optional stage,
/// e.g. <c>0.7.0-alpha.1</c> &lt; <c>0.7.0-alpha.2</c> &lt; <c>0.7.0-beta.1</c> &lt; <c>0.7.0</c>.
/// </summary>
internal readonly record struct ReleaseVersion(int Major, int Minor, int Patch, ReleaseStage Stage = ReleaseStage.Stable, int StageNumber = 0)
    : IComparable<ReleaseVersion>
{
    /// <summary>The version of the running editor (from the informational version, which keeps the stage).</summary>
    public static ReleaseVersion Current { get; } =
        Parse(EngineInfo.InformationalVersion) ?? new ReleaseVersion(EngineInfo.Version.Major, EngineInfo.Version.Minor, Math.Max(EngineInfo.Version.Build, 0));

    /// <summary>
    /// Reads <c>1.2.3</c>, <c>v1.2.3</c>, <c>1.2</c>, <c>1.2.3-beta.2</c>, <c>1.2.3-alpha</c> (also <c>-a.1</c> / <c>-b.1</c>);
    /// build metadata after <c>+</c> is ignored. Returns null for anything that is not a version.
    /// </summary>
    public static ReleaseVersion? Parse(string? text)
    {
        var value = text?.Trim().TrimStart('v', 'V').Split('+')[0];
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var dash = value.IndexOf('-', StringComparison.Ordinal);
        var core = dash < 0 ? value : value[..dash];
        if (!Version.TryParse(core, out var number))
        {
            return null;
        }

        var stage = ReleaseStage.Stable;
        var stageNumber = 0;
        if (dash >= 0)
        {
            var parts = value[(dash + 1)..].Split('.');
            stage = parts[0].ToLowerInvariant() switch
            {
                "beta" or "b" or "rc" => ReleaseStage.Beta,
                _ => ReleaseStage.Alpha, // "alpha", "a" and unknown labels count as the earliest stage
            };
            if (parts.Length > 1)
            {
                int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out stageNumber);
            }
        }

        return new ReleaseVersion(number.Major, number.Minor, Math.Max(number.Build, 0), stage, stageNumber);
    }

    public int CompareTo(ReleaseVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result == 0) result = Minor.CompareTo(other.Minor);
        if (result == 0) result = Patch.CompareTo(other.Patch);
        if (result == 0) result = Stage.CompareTo(other.Stage);
        if (result == 0) result = StageNumber.CompareTo(other.StageNumber);
        return result;
    }

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    /// <summary>"0.7.0", "0.7.0-beta.2" or "0.7.0-alpha".</summary>
    public override string ToString()
    {
        var core = string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
        if (Stage == ReleaseStage.Stable)
        {
            return core;
        }

        var label = Stage == ReleaseStage.Beta ? "beta" : "alpha";
        return StageNumber > 0 ? string.Create(CultureInfo.InvariantCulture, $"{core}-{label}.{StageNumber}") : $"{core}-{label}";
    }
}
