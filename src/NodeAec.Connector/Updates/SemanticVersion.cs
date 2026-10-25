using System;
using System.Globalization;

namespace NodeAec.Connector.Updates;

/// <summary>
/// Lightweight, zero-dependency semantic version parser and comparer (<c>major.minor.patch[-prerelease]</c>).
/// Accepts an optional <c>v</c> prefix. Releases sort above their pre-release builds
/// (e.g. <c>0.4.0-beta</c> &lt; <c>0.4.0</c>). Never throws on malformed input.
/// </summary>
internal sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    /// <summary>Gets the major version component.</summary>
    public int Major { get; }

    /// <summary>Gets the minor version component.</summary>
    public int Minor { get; }

    /// <summary>Gets the patch version component.</summary>
    public int Patch { get; }

    /// <summary>Gets the optional pre-release identifier, or <c>null</c> if this is a final release.</summary>
    public string? Prerelease { get; }

    /// <summary>Gets the original text from which this version was created or parsed.</summary>
    public string OriginalText { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticVersion"/> class.
    /// </summary>
    /// <param name="major">Major version number (>= 0).</param>
    /// <param name="minor">Minor version number (>= 0).</param>
    /// <param name="patch">Patch version number (>= 0).</param>
    /// <param name="prerelease">Optional pre-release string.</param>
    /// <param name="originalText">Optional original unparsed string.</param>
    public SemanticVersion(int major, int minor, int patch, string? prerelease = null, string? originalText = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = string.IsNullOrWhiteSpace(prerelease) ? null : prerelease.Trim();
        OriginalText = originalText ?? ToString();
    }

    /// <summary>
    /// Attempts to parse a semantic version string.
    /// Returns <c>false</c> on any syntax error or out-of-range value without throwing.
    /// Strictly rejects path traversal characters, spaces, and signs.
    /// </summary>
    /// <param name="input">Version string to parse (e.g. "0.4.0", "v1.2.3", "0.4.0-preview.1").</param>
    /// <param name="version">Parsed semantic version if successful; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if valid; otherwise <c>false</c>.</returns>
    public static bool TryParse(string? input, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string trimmed = input!.Trim();
        string raw = trimmed;

        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(1).Trim();
        }

        if (trimmed.Length == 0)
        {
            return false;
        }

        string mainPart;
        string? prereleasePart = null;

        int dashIndex = trimmed.IndexOf('-');
        if (dashIndex >= 0)
        {
            mainPart = trimmed.Substring(0, dashIndex);
            prereleasePart = trimmed.Substring(dashIndex + 1);
            if (string.IsNullOrWhiteSpace(prereleasePart) ||
                prereleasePart.StartsWith(".") ||
                prereleasePart.EndsWith("."))
            {
                return false;
            }

            string[] preSegments = prereleasePart.Split('.');
            foreach (string seg in preSegments)
            {
                if (seg.Length == 0)
                {
                    return false;
                }

                for (int i = 0; i < seg.Length; i++)
                {
                    char c = seg[i];
                    bool valid = (c >= '0' && c <= '9') ||
                                 (c >= 'a' && c <= 'z') ||
                                 (c >= 'A' && c <= 'Z') ||
                                 c == '-';
                    if (!valid)
                    {
                        return false;
                    }
                }
            }
        }
        else
        {
            mainPart = trimmed;
        }

        string[] parts = mainPart.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        int major;
        int minor;
        int patch;
        try
        {
            major = int.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture);
            minor = int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture);
            patch = int.Parse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture);
        }
        catch
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, prereleasePart, raw);
        return true;
    }

    /// <summary>
    /// Compares two version strings safely.
    /// Returns &lt; 0 if a &lt; b, 0 if equal, &gt; 0 if a &gt; b.
    /// Returns <c>null</c> if either string cannot be parsed as a valid semantic version.
    /// </summary>
    /// <param name="a">First version string.</param>
    /// <param name="b">Second version string.</param>
    /// <returns>Comparison result or null if parsing fails.</returns>
    public static int? Compare(string? a, string? b)
    {
        if (!TryParse(a, out var vA) || !TryParse(b, out var vB))
        {
            return null;
        }

        return vA!.CompareTo(vB!);
    }

    /// <summary>
    /// Compares this version to another semantic version.
    /// </summary>
    /// <param name="other">The other semantic version.</param>
    /// <returns>A signed number indicating relative values.</returns>
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        if (ReferenceEquals(this, other)) return 0;

        int majorCmp = Major.CompareTo(other.Major);
        if (majorCmp != 0) return majorCmp;

        int minorCmp = Minor.CompareTo(other.Minor);
        if (minorCmp != 0) return minorCmp;

        int patchCmp = Patch.CompareTo(other.Patch);
        if (patchCmp != 0) return patchCmp;

        // SemVer 2.0: A normal release has higher precedence than a pre-release version.
        if (Prerelease is null && other.Prerelease is null) return 0;
        if (Prerelease is null) return 1;
        if (other.Prerelease is null) return -1;

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string a, string b)
    {
        string[] partsA = a.Split('.');
        string[] partsB = b.Split('.');
        int minLen = Math.Min(partsA.Length, partsB.Length);

        for (int i = 0; i < minLen; i++)
        {
            string pA = partsA[i];
            string pB = partsB[i];

            bool isNumA = int.TryParse(pA, out int numA);
            bool isNumB = int.TryParse(pB, out int numB);

            if (isNumA && isNumB)
            {
                int numCmp = numA.CompareTo(numB);
                if (numCmp != 0) return numCmp;
            }
            else if (isNumA)
            {
                // Numeric identifiers always have lower precedence than non-numeric.
                return -1;
            }
            else if (isNumB)
            {
                return 1;
            }
            else
            {
                int strCmp = string.Compare(pA, pB, StringComparison.OrdinalIgnoreCase);
                if (strCmp != 0) return strCmp;
            }
        }

        return partsA.Length.CompareTo(partsB.Length);
    }

    /// <summary>
    /// Determines whether this version is equal to another version.
    /// </summary>
    /// <param name="other">The other semantic version.</param>
    /// <returns><c>true</c> if equal; otherwise <c>false</c>.</returns>
    public bool Equals(SemanticVersion? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return CompareTo(other) == 0;
    }

    /// <summary>
    /// Determines whether this version is equal to the specified object.
    /// </summary>
    public override bool Equals(object? obj) => Equals(obj as SemanticVersion);

    /// <summary>
    /// Returns the hash code for this version.
    /// </summary>
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (Major * 397) ^ (Minor * 31) ^ Patch;
            if (Prerelease != null)
            {
                hash ^= StringComparer.OrdinalIgnoreCase.GetHashCode(Prerelease);
            }
            return hash;
        }
    }

    /// <summary>
    /// Returns the canonical string representation of this semantic version.
    /// </summary>
    public override string ToString()
    {
        return Prerelease == null
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{Prerelease}";
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(SemanticVersion? left, SemanticVersion? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

    /// <summary>Less-than operator.</summary>
    public static bool operator <(SemanticVersion? left, SemanticVersion? right) => left is not null && right is not null && left.CompareTo(right) < 0;

    /// <summary>Less-than-or-equal operator.</summary>
    public static bool operator <=(SemanticVersion? left, SemanticVersion? right) => left is null || (right is not null && left.CompareTo(right) <= 0);

    /// <summary>Greater-than operator.</summary>
    public static bool operator >(SemanticVersion? left, SemanticVersion? right) => left is not null && (right is null || left.CompareTo(right) > 0);

    /// <summary>Greater-than-or-equal operator.</summary>
    public static bool operator >=(SemanticVersion? left, SemanticVersion? right) => left is null ? right is null : (right is null || left.CompareTo(right) >= 0);
}
