using System;
using NodeAec.Connector.Storage;
using NodeAec.Licensing.Cryptography;
using NodeAec.Licensing.Hardware;

namespace NodeAec.Connector.Cryptography;

/// <summary>
/// The Connector's only route to the lease signature check and to the Machine ID. Both come
/// from the <c>NodeAec.Licensing.Lite</c> package, the same code every partner plugin runs, so
/// the Hub saves exactly the leases the plugin gates accept and binds them to the same
/// <c>mid</c>. The trust anchors live in Lite only; this repository compiles no key.
/// </summary>
internal static class LeaseTrust
{
    /// <summary>Shape of <see cref="LeaseSignatureVerifier.Evaluate(string?, out string?)"/>.</summary>
    internal delegate LeaseSignatureVerifier.VerificationOutcome SignatureCheck(string? jwt, out string? reason);

    /// <summary>
    /// Signature check. Lite's verifier in production. Replaceable by the test suite only
    /// (the add-in never assigns it), because Lite's anchors cannot be swapped from outside.
    /// </summary>
    internal static SignatureCheck VerifySignature { get; set; } = LeaseSignatureVerifier.Evaluate;

    /// <summary>
    /// Machine ID source: Lite's SHA-256 of the Windows <c>MachineGuid</c>, or <c>null</c> when
    /// it cannot be read. Replaceable by the test suite only.
    /// </summary>
    internal static Func<string?> MachineIdReader { get; set; } = ReadMachineIdFromLite;

    /// <summary>Anchor membership used for the rotation warning. Replaceable by the test suite only.</summary>
    internal static Func<byte[], bool> IsTrustedKey { get; set; } = LeaseSignatureVerifier.IsTrustedKey;

    /// <summary>One rotation warning per process: the check runs on every sync and the JWKS keeps old keys.</summary>
    private static bool _rotationSignalLogged;

    /// <summary>
    /// Evaluates the lease signature against Lite's compiled anchors.
    /// </summary>
    /// <param name="jwt">Raw lease JWT.</param>
    /// <param name="reason">Reason for the log when not verified (never shown to the user).</param>
    /// <returns>The outcome reported by Lite.</returns>
    public static LeaseSignatureVerifier.VerificationOutcome Evaluate(string? jwt, out string? reason)
    {
        SignalJwksOutsideAnchors();
        return VerifySignature(jwt, out reason);
    }

    /// <summary>
    /// Gets this machine's identifier (64 lowercase hex characters), the value leases carry in <c>mid</c>.
    /// </summary>
    /// <param name="machineId">The identifier when the return is <c>true</c>.</param>
    /// <param name="reason">Reason for the log when the return is <c>false</c>.</param>
    /// <returns><c>true</c> when the <c>MachineGuid</c> was read.</returns>
    public static bool TryGetMachineId(out string machineId, out string? reason)
    {
        string? id = MachineIdReader();
        if (id == null || id.Length == 0)
        {
            machineId = string.Empty;
            reason = "MachineGuid do Windows indisponível (registro ilegível ou sistema não-Windows)";
            return false;
        }

        machineId = id;
        reason = null;
        return true;
    }

    /// <summary>
    /// Logs once per process when the cached JWKS publishes a key that Lite does not trust:
    /// the platform is rotating keys and this Connector needs a release with a newer Lite.
    /// </summary>
    private static void SignalJwksOutsideAnchors()
    {
        if (_rotationSignalLogged)
        {
            return;
        }

        foreach (var (kid, rawKey) in SigningKeyStore.LoadVerificationKeys())
        {
            if (!IsTrustedKey(rawKey))
            {
                _rotationSignalLogged = true;
                Diagnostics.ConnectorLog.Write(
                    "WARN",
                    $"JWKS contém chave fora das âncoras do NodeAec.Licensing.Lite (kid {kid ?? "sem kid"}): possível rotação — atualize o Connector.");
                return;
            }
        }
    }

    /// <summary>Clears the once-per-process rotation flag; used by tests.</summary>
    internal static void ResetRotationSignalForTests() => _rotationSignalLogged = false;

    private static string? ReadMachineIdFromLite() =>
        HardwareId.TryGetMachineId(out string machineId, out _) ? machineId : null;
}
