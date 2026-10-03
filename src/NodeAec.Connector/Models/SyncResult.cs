using System;
using System.Collections.Generic;

namespace NodeAec.Connector.Models;

/// <summary>
/// Result of syncing or renewing the Master Entitlements Lease.
/// </summary>
public class SyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int GrantedCount { get; set; }
    public int TotalCount { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? LeaseToken { get; set; }
    public List<EntitlementItem> Entitlements { get; set; } = new();

    /// <summary>
    /// Whether the signature of the lease written to disk by this operation was confirmed
    /// with an available public key (cached JWKS or pinned anchor). Defaults to <c>true</c>
    /// when the operation wrote no new lease; persisting paths always set the
    /// real value — a lease saved with no key available leaves here with <c>false</c>.
    /// </summary>
    public bool KeysVerified { get; set; } = true;

    /// <summary>
    /// JWKS cache refresh outcome (<c>GET /license/jwks</c>) for this operation.
    /// <c>false</c> = the key refresh failed (verification may have used the previous
    /// cache). Defaults to <c>true</c> when no refresh was needed.
    /// </summary>
    public bool JwksRefreshed { get; set; } = true;

    /// <summary>
    /// Display-ready warning for when the operation degraded on verification keys (lease
    /// saved without a verified signature, or JWKS not refreshed).
    /// <c>null</c> = no degradation; the UI then shows the default success message.
    /// </summary>
    public string? VerificationWarning =>
        !KeysVerified
            ? "Suas licenças foram salvas, mas as chaves de verificação não puderam ser obtidas — os plugins podem continuar bloqueados até a próxima sincronização bem-sucedida."
            : !JwksRefreshed
                ? "Suas licenças foram atualizadas, mas a renovação das chaves de verificação falhou agora; as chaves já salvas continuam valendo."
                : null;

    public static SyncResult Succeeded(string leaseToken, List<EntitlementItem> entitlements, DateTimeOffset? expiresAt, int granted, int total, string message = "Licenças sincronizadas com sucesso.")
    {
        return new SyncResult
        {
            Success = true,
            Message = message,
            LeaseToken = leaseToken,
            Entitlements = entitlements,
            ExpiresAt = expiresAt,
            GrantedCount = granted,
            TotalCount = total,
        };
    }

    public static SyncResult Failed(string message)
    {
        return new SyncResult
        {
            Success = false,
            Message = message,
        };
    }
}
