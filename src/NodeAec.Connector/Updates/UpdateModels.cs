using System;
using System.Text.Json;

namespace NodeAec.Connector.Updates;

/// <summary>
/// Status of the installed version against the latest published release.
/// </summary>
internal enum UpdateDecision
{
    /// <summary>The installed version is equal to or newer than the latest available release.</summary>
    UpToDate,

    /// <summary>A newer version is published and available for download.</summary>
    Available,

    /// <summary>Version information is missing or could not be compared.</summary>
    Unknown,
}

/// <summary>
/// Pure decisions for connector updates.
/// </summary>
internal static class UpdateDecisions
{
    /// <summary>
    /// Compares the installed version with the latest published one. Never throws.
    /// </summary>
    /// <param name="currentVersion">Installed version string.</param>
    /// <param name="latestVersion">Latest published version string.</param>
    /// <returns><see cref="UpdateDecision.Unknown"/> when either version does not parse.</returns>
    public static UpdateDecision Evaluate(string? currentVersion, string? latestVersion)
    {
        if (!SemanticVersion.TryParse(currentVersion, out var current) || current is null ||
            !SemanticVersion.TryParse(latestVersion, out var latest) || latest is null)
        {
            return UpdateDecision.Unknown;
        }

        return latest > current ? UpdateDecision.Available : UpdateDecision.UpToDate;
    }
}

/// <summary>
/// File descriptor inside the latest release metadata.
/// </summary>
internal sealed class ReleaseFileInfo
{
    /// <summary>Gets the public ID of the product version.</summary>
    public string VersionPublicId { get; }

    /// <summary>Gets the public ID of the release file.</summary>
    public string FilePublicId { get; }

    /// <summary>Gets the original file name published by the server.</summary>
    public string Name { get; }

    /// <summary>Gets the expected file size in bytes.</summary>
    public long SizeBytes { get; }

    /// <summary>Gets the expected SHA-256 hash hex string.</summary>
    public string Sha256 { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReleaseFileInfo"/> class.
    /// </summary>
    /// <param name="versionPublicId">Public ID of the version.</param>
    /// <param name="filePublicId">Public ID of the file.</param>
    /// <param name="name">Original file name.</param>
    /// <param name="sizeBytes">File size in bytes.</param>
    /// <param name="sha256">SHA-256 hex string.</param>
    public ReleaseFileInfo(string versionPublicId, string filePublicId, string name, long sizeBytes, string sha256)
    {
        VersionPublicId = versionPublicId;
        FilePublicId = filePublicId;
        Name = name;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
    }
}

/// <summary>
/// Contract payload returned by <c>GET /products/:slug/latest</c>.
/// </summary>
internal sealed class LatestReleaseInfo
{
    /// <summary>Gets the product slug identifier.</summary>
    public string Slug { get; }

    /// <summary>Gets the latest release version string.</summary>
    public string Version { get; }

    /// <summary>Gets the optional publication timestamp.</summary>
    public DateTimeOffset? ReleasedAt { get; }

    /// <summary>Gets the optional release notes in markdown or plain text.</summary>
    public string? Notes { get; }

    /// <summary>Gets the primary installer file descriptor.</summary>
    public ReleaseFileInfo File { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LatestReleaseInfo"/> class.
    /// </summary>
    /// <param name="slug">Product slug.</param>
    /// <param name="version">Release version.</param>
    /// <param name="releasedAt">Release timestamp.</param>
    /// <param name="notes">Release notes.</param>
    /// <param name="file">File descriptor.</param>
    public LatestReleaseInfo(
        string slug,
        string version,
        DateTimeOffset? releasedAt,
        string? notes,
        ReleaseFileInfo file)
    {
        Slug = slug;
        Version = version;
        ReleasedAt = releasedAt;
        Notes = notes;
        File = file;
    }

    /// <summary>
    /// Parses and strictly validates release JSON from the server contract.
    /// Returns <c>false</c> if any required property is missing, malformed, or if the SHA-256 is invalid.
    /// Never throws.
    /// </summary>
    /// <param name="json">Raw JSON response.</param>
    /// <param name="info">Parsed release info if successful; otherwise null.</param>
    /// <returns><c>true</c> if valid; otherwise <c>false</c>.</returns>
    public static bool TryParse(string? json, out LatestReleaseInfo? info)
    {
        info = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json!);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("slug", out var slugProp) || slugProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            string slug = slugProp.GetString()!;
            if (string.IsNullOrWhiteSpace(slug)) return false;

            if (!root.TryGetProperty("version", out var verProp) || verProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            string version = verProp.GetString()!;
            if (!SemanticVersion.TryParse(version, out _))
            {
                return false;
            }

            DateTimeOffset? releasedAt = null;
            if (root.TryGetProperty("releasedAt", out var relProp) && relProp.ValueKind == JsonValueKind.String)
            {
                if (DateTimeOffset.TryParse(relProp.GetString(), out var parsedDate))
                {
                    releasedAt = parsedDate;
                }
            }

            string? notes = null;
            if (root.TryGetProperty("notes", out var notesProp) && notesProp.ValueKind == JsonValueKind.String)
            {
                notes = notesProp.GetString();
            }

            if (!filePropExists(root, out var fileProp))
            {
                return false;
            }

            if (!fileProp.TryGetProperty("versionPublicId", out var vpidProp) || vpidProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            string versionPublicId = vpidProp.GetString()!;
            if (string.IsNullOrWhiteSpace(versionPublicId)) return false;

            if (!fileProp.TryGetProperty("filePublicId", out var fpidProp) || fpidProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            string filePublicId = fpidProp.GetString()!;
            if (string.IsNullOrWhiteSpace(filePublicId)) return false;

            string fileName = string.Empty;
            if (fileProp.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
            {
                fileName = nameProp.GetString() ?? string.Empty;
            }

            if (!fileProp.TryGetProperty("sizeBytes", out var sizeProp) || !sizeProp.TryGetInt64(out long sizeBytes) || sizeBytes <= 0)
            {
                return false;
            }

            if (!fileProp.TryGetProperty("sha256", out var shaProp) || shaProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            string sha256 = shaProp.GetString()!.Trim();
            if (!IsValidSha256(sha256))
            {
                return false;
            }

            var fileInfo = new ReleaseFileInfo(versionPublicId, filePublicId, fileName, sizeBytes, sha256);
            info = new LatestReleaseInfo(slug, version, releasedAt, notes, fileInfo);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool filePropExists(JsonElement root, out JsonElement fileProp)
    {
        return root.TryGetProperty("file", out fileProp) && fileProp.ValueKind == JsonValueKind.Object;
    }

    /// <summary>
    /// Checks whether a string is a valid 64-character lowercase/uppercase hex string.
    /// </summary>
    /// <param name="sha">String to validate.</param>
    /// <returns><c>true</c> if valid hex SHA-256; otherwise <c>false</c>.</returns>
    public static bool IsValidSha256(string? sha)
    {
        if (sha is null || sha.Length != 64)
        {
            return false;
        }

        for (int i = 0; i < sha.Length; i++)
        {
            char c = sha[i];
            bool isHex = (c >= '0' && c <= '9') ||
                         (c >= 'a' && c <= 'f') ||
                         (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }

        return true;
    }
}
