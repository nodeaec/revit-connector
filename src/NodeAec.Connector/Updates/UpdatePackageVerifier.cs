using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NodeAec.Connector.Updates;

/// <summary>
/// Verifies downloaded update installer packages against the size and SHA-256 published by the platform,
/// and builds our deterministic target file names.
/// </summary>
internal static class UpdatePackageVerifier
{
    /// <summary>
    /// Generates our canonical installer file name: <c>NodeAecConnector-&lt;version&gt;-Setup.exe</c>.
    /// Strictly validates that <paramref name="version"/> is a valid semantic version to prevent
    /// path traversal or arbitrary server naming.
    /// </summary>
    /// <param name="version">Semantic version string.</param>
    /// <returns>Deterministic setup executable name.</returns>
    /// <exception cref="ArgumentException">Thrown when the version string is not valid semver.</exception>
    public static string GetTargetFileName(string version)
    {
        if (!SemanticVersion.TryParse(version, out var parsed) || parsed == null)
        {
            throw new ArgumentException($"Versão semântica inválida para o instalador: '{version}'.", nameof(version));
        }

        return $"NodeAecConnector-{parsed}-Setup.exe";
    }

    /// <summary>
    /// Gets the dedicated staging directory for downloaded updates (%TEMP%\NodeAec\updates).
    /// </summary>
    /// <returns>The updates staging directory path.</returns>
    public static string GetTargetDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "NodeAec", "updates");
    }

    /// <summary>
    /// Resolves the absolute path to save the installer for the specified version.
    /// Strictly verifies that the resolved path does not escape the target directory.
    /// </summary>
    /// <param name="version">Semantic version string.</param>
    /// <returns>Absolute target file path.</returns>
    /// <exception cref="ArgumentException">Thrown when version is invalid semver.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the path escapes the target directory.</exception>
    public static string GetTargetFilePath(string version)
    {
        string targetDir = Path.GetFullPath(GetTargetDirectory());
        string fileName = GetTargetFileName(version);
        string combined = Path.GetFullPath(Path.Combine(targetDir, fileName));

        string dirWithSep = targetDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(dirWithSep, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetDirectoryName(combined), targetDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Target file path '{combined}' escapes target directory '{targetDir}'.");
        }

        return combined;
    }

    /// <summary>
    /// Verifies the downloaded file against expected file size and expected SHA-256 hash.
    /// Returns <c>true</c> if and only if both the size and the SHA-256 match.
    /// Never throws.
    /// </summary>
    /// <param name="filePath">Path to the downloaded file.</param>
    /// <param name="expectedSizeBytes">Expected file size in bytes (> 0).</param>
    /// <param name="expectedSha256">Expected SHA-256 hex string (64 characters, case-insensitive).</param>
    /// <returns><c>true</c> if valid; otherwise <c>false</c>.</returns>
    public static bool VerifyFile(string filePath, long expectedSizeBytes, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        if (expectedSizeBytes <= 0 || !LatestReleaseInfo.IsValidSha256(expectedSha256))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(filePath);
            if (info.Length != expectedSizeBytes)
            {
                return false;
            }

            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hashBytes = sha.ComputeHash(stream);

            string actualHex = ToHexStringLower(hashBytes);
            return string.Equals(actualHex, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string ToHexStringLower(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        for (int i = 0; i < bytes.Length; i++)
        {
            sb.Append(bytes[i].ToString("x2"));
        }
        return sb.ToString();
    }
}
