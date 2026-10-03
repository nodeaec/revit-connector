using NodeAec.Connector.Models;

namespace NodeAec.Connector.Diagnostics;

/// <summary>
/// Pure lease-heartbeat logging decision (M7/P13): translates a <see cref="SyncResult"/>
/// into the local log WARN line, or <c>null</c> when there is nothing to record. All branches
/// are <b>fixed text</b>: <see cref="SyncResult.Message"/> may contain raw server text
/// (unmapped path), which the log sanitization rule forbids — the UI is what
/// displays it.
/// </summary>
internal static class HeartbeatLog
{
    /// <summary>
    /// Builds the heartbeat WARN line, or <c>null</c> when the lease renewed with a
    /// verified signature and a fresh JWKS (nothing to report).
    /// </summary>
    /// <param name="result">Result of <c>ValidateHeartbeatAsync</c>.</param>
    /// <returns>Message ready for <c>ConnectorLog.Write("WARN", …)</c>, or <c>null</c>.</returns>
    internal static string? WarningMessage(SyncResult result)
    {
        if (!result.Success)
        {
            // Stable category (P13): the result message does not enter the log.
            return "Heartbeat de lease falhou (falha de sincronização).";
        }

        if (!result.KeysVerified)
        {
            // M1: fixed, sanitized text (never a server message) — the lease renewed
            // without a key to check the signature against; the gate denies until the JWKS returns.
            return "Heartbeat renovou o lease sem verificar a assinatura (JWKS indisponível).";
        }

        if (!result.JwksRefreshed)
        {
            return "Heartbeat validou o lease, mas o cache JWKS não pôde ser renovado.";
        }

        return null;
    }
}
