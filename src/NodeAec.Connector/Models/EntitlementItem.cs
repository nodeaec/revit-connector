using System;
using System.Globalization;
using System.Text.Json.Serialization;

namespace NodeAec.Connector.Models;

/// <summary>
/// Represents a single product grant/authorization inside the Master Entitlements Lease.
/// </summary>
public class EntitlementItem
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("licenseKey")]
    public string? LicenseKey { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "perpetual";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "active";

    /// <summary>
    /// Token <c>granted</c> claim: the issuer only includes granted grants, but the
    /// value is honored by <see cref="IsActive"/> (L6) — <c>false</c> denies even when the
    /// rest of the item is valid. Missing on the token → defaults to <c>true</c>.
    /// </summary>
    [JsonPropertyName("granted")]
    public bool Granted { get; set; } = true;

    [JsonPropertyName("expiresAt")]
    public string? ExpiresAtString { get; set; }

    /// <summary>
    /// Deterministically parsed expiration: invariant culture (a
    /// server-shaped date never depends on the machine's regional format) and
    /// <see cref="DateTimeStyles.AssumeUniversal"/> — with no explicit offset, the date counts as
    /// midnight UTC, not local midnight (the machine is not shifted forward/back on
    /// expiry by its own time zone). Invalid/out-of-range → <c>null</c>.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? ExpiresAt
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ExpiresAtString)) return null;
            if (DateTimeOffset.TryParse(ExpiresAtString, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)) return dt;
            return null;
        }
    }

    [JsonPropertyName("maxActivations")]
    public int? MaxActivations { get; set; }

    [JsonPropertyName("activeActivations")]
    public int? ActiveActivations { get; set; }

    /// <summary>
    /// Whether the grant is active and valid for immediate use.
    /// </summary>
    public bool IsActive()
    {
        // L6: the `granted` claim used to be deserialized and ignored. Honoring it here keeps the
        // model and the gate consistent: an explicitly ungranted grant is never
        // active (fail-closed should the issuer start emitting `granted: false`).
        if (!Granted)
        {
            return false;
        }

        if (!string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ExpiresAt.HasValue && ExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            return false;
        }

        return true;
    }
}
