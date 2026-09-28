using System;
using System.Globalization;
using System.Reflection;

namespace WebDisplay.Services;

/// <summary>One assembly-derived version for display, update checks, and requests.</summary>
public static class AppVersion
{
    public static string Current { get; } = ReadCurrent();
    public static string Display => "v" + Current;

    private static string ReadCurrent()
    {
        Assembly assembly = typeof(AppVersion).Assembly;
        return Normalize(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
            ?? Normalize(assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version)
            ?? Normalize(assembly.GetName().Version?.ToString())
            ?? "unknown";
    }

    internal static string? Normalize(string? value)
    {
        if (SemanticVersion.TryParse(value, out SemanticVersion semantic)) return semantic.Normalized;
        // Assembly/file versions can have a fourth build/revision component.
        // Product releases use the three-component version, never source metadata.
        string? core = value?.Split('+', 2)[0];
        if (Version.TryParse(core, out Version? version) && version.Major >= 0 && version.Minor >= 0)
            return string.Join(".", version.Major.ToString(CultureInfo.InvariantCulture),
                version.Minor.ToString(CultureInfo.InvariantCulture), Math.Max(0, version.Build).ToString(CultureInfo.InvariantCulture));
        return null;
    }
}

/// <summary>Bounded SemVer parsing; build metadata does not affect precedence.</summary>
internal sealed class SemanticVersion : IComparable<SemanticVersion>
{
    private readonly int _major, _minor, _patch;
    private readonly string[] _prerelease;
    internal string Normalized { get; }
    internal bool IsPrerelease => _prerelease.Length != 0;

    private SemanticVersion(int major, int minor, int patch, string prerelease)
    {
        _major = major; _minor = minor; _patch = patch;
        _prerelease = prerelease.Length == 0 ? Array.Empty<string>() : prerelease.Split('.');
        Normalized = string.Join(".", major.ToString(CultureInfo.InvariantCulture),
            minor.ToString(CultureInfo.InvariantCulture), patch.ToString(CultureInfo.InvariantCulture))
            + (prerelease.Length == 0 ? string.Empty : "-" + prerelease);
    }

    internal static bool TryParse(string? value, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrEmpty(value) || value.Length > 128 || value != value.Trim()) return false;
        if (value[0] is 'v' or 'V') value = value[1..];
        string[] metadata = value.Split('+');
        if (metadata.Length > 2 || (metadata.Length == 2 && !ValidIdentifiers(metadata[1], false))) return false;
        string core = metadata[0];
        string prerelease = string.Empty;
        int dash = core.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = core[(dash + 1)..];
            core = core[..dash];
            if (!ValidIdentifiers(prerelease, true)) return false;
        }
        string[] numbers = core.Split('.');
        if (numbers.Length != 3 || !TryNumber(numbers[0], out int major)
            || !TryNumber(numbers[1], out int minor) || !TryNumber(numbers[2], out int patch)) return false;
        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    private static bool TryNumber(string value, out int number)
    {
        number = 0;
        return IsDigits(value) && (value.Length == 1 || value[0] != '0')
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static bool IsDigits(string value)
    {
        if (value.Length == 0) return false;
        foreach (char character in value) if (character is < '0' or > '9') return false;
        return true;
    }

    private static bool ValidIdentifiers(string value, bool prerelease)
    {
        foreach (string part in value.Split('.'))
        {
            if (part.Length == 0 || (prerelease && part.Length > 1 && part[0] == '0' && IsDigits(part))) return false;
            foreach (char character in part)
                if (character is not (>= '0' and <= '9') and not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and not '-')
                    return false;
        }
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        int result = _major.CompareTo(other._major);
        if (result == 0) result = _minor.CompareTo(other._minor);
        if (result == 0) result = _patch.CompareTo(other._patch);
        if (result != 0) return result;
        if (!IsPrerelease) return other.IsPrerelease ? 1 : 0;
        if (!other.IsPrerelease) return -1;
        for (int index = 0; index < Math.Min(_prerelease.Length, other._prerelease.Length); index++)
        {
            string left = _prerelease[index], right = other._prerelease[index];
            bool leftNumber = IsDigits(left), rightNumber = IsDigits(right);
            if (leftNumber && rightNumber)
                result = left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
            else if (leftNumber != rightNumber)
                result = leftNumber ? -1 : 1;
            else result = string.CompareOrdinal(left, right);
            if (result != 0) return result;
        }
        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }
}
