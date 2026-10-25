using System;
using System.IO;
using System.Text;
using System.Text.Json;
using NodeAec.Connector.Storage;

namespace NodeAec.Connector.Updates;

/// <summary>
/// Manages persistence of update metadata (%APPDATA%\NodeAec\update-status.json).
/// Plain JSON, not secret. Uses atomic writes via <see cref="LeaseStorage.WriteAllBytesAtomic"/>.
/// Never throws on missing or corrupt files.
/// </summary>
internal static class UpdateStorage
{
    private const string UpdateStatusFileName = "update-status.json";

    /// <summary>
    /// Gets the absolute path to <c>update-status.json</c>.
    /// </summary>
    public static string GetUpdateStatusFilePath() =>
        Path.Combine(LeaseStorage.GetBaseDirectory(), UpdateStatusFileName);

    /// <summary>
    /// Saves the latest and minimum version info from a sync, activate, or heartbeat response.
    /// Performs an atomic write. Never throws.
    /// </summary>
    /// <param name="latestVersion">Latest published version on the platform.</param>
    /// <param name="minimumVersion">Minimum version required by the platform (may be null).</param>
    /// <returns><c>true</c> if written successfully; otherwise <c>false</c>.</returns>
    public static bool SaveUpdateStatus(string? latestVersion, string? minimumVersion)
    {
        try
        {
            var data = new UpdateStatusData(latestVersion, minimumVersion, DateTimeOffset.UtcNow);
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            LeaseStorage.WriteAllBytesAtomic(GetUpdateStatusFilePath(), bytes);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("WARN", $"Falha ao gravar status de atualizações: {ex.GetType().Name}.");
            return false;
        }
    }

    /// <summary>
    /// Loads the stored update status. Returns <c>null</c> if the file does not exist,
    /// is empty, or contains corrupt JSON. Never throws.
    /// </summary>
    public static UpdateStatusData? LoadUpdateStatus()
    {
        string path = GetUpdateStatusFilePath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return null;

            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string? latest = null;
            if ((root.TryGetProperty("latestVersion", out var latElem) || root.TryGetProperty("LatestVersion", out latElem)) &&
                latElem.ValueKind == JsonValueKind.String)
            {
                latest = latElem.GetString();
            }

            string? minimum = null;
            if ((root.TryGetProperty("minimumVersion", out var minElem) || root.TryGetProperty("MinimumVersion", out minElem)) &&
                minElem.ValueKind == JsonValueKind.String)
            {
                minimum = minElem.GetString();
            }

            DateTimeOffset? updatedAt = null;
            if ((root.TryGetProperty("updatedAt", out var upElem) || root.TryGetProperty("UpdatedAt", out upElem)) &&
                upElem.ValueKind == JsonValueKind.String)
            {
                if (DateTimeOffset.TryParse(upElem.GetString(), out var dt))
                {
                    updatedAt = dt;
                }
            }

            return new UpdateStatusData(latest, minimum, updatedAt);
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("WARN", $"Falha ao ler status de atualizações: {ex.GetType().Name}.");
            return null;
        }
    }
}
