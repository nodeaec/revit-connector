using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using NodeAec.Connector.Config;
using NodeAec.Connector.Storage;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace NodeAec.Connector.Cryptography;

/// <summary>
/// Verifies the Ed25519 (RFC 8032) signature of lease tokens issued by the Node.aec platform
/// before any claim is trusted. The only candidate key is the anchor compiled into the
/// add-in (<see cref="ConnectorConfig.DefaultLicensePublicKeySpkiBase64"/>), replaceable
/// by operations via <c>NODEAEC_LICENSE_PUBLIC_KEY_SPKI</c>.
/// The cached JWKS (<c>%APPDATA%\NodeAec\license-jwks.json</c>, refreshed by
/// <c>GET /license/jwks</c>) is no longer a trust source: it serves <c>kid</c> discovery
/// and rotation signaling, and an off-anchor key never verifies.
/// Always fails closed: with no usable anchor or an invalid signature, the lease
/// is not accepted.
/// </summary>
public static class LeaseSignatureVerifier
{
    /// <summary>Signature algorithm accepted on leases (EdDSA / Ed25519).</summary>
    public const string AcceptedAlgorithm = "EdDSA";

    /// <summary>Raw Ed25519 key size in bytes.</summary>
    private const int RawKeySize = 32;

    /// <summary>Ed25519 signature size in bytes.</summary>
    private const int SignatureSize = 64;

    /// <summary>Fixed DER prefix of an Ed25519 SubjectPublicKeyInfo (RFC 8410).</summary>
    private static readonly byte[] SpkiEd25519Prefix =
    {
        0x30, 0x2a, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x03, 0x21, 0x00,
    };

    /// <summary>
    /// Guarantees a single rotation warning per process — verification runs on every plugin
    /// command and the JWKS keeps the old key after a rotation.
    /// </summary>
    private static bool _rotationSignalLogged;

    /// <summary>
    /// Granular signature-verification outcome. Lets the caller distinguish
    /// "a key is available and the signature does not check out" (firm rejection) from "there is
    /// no key to check against" (unavailability — the caller propagates the
    /// "unverified" state instead of alleging tampering).
    /// </summary>
    public enum VerificationOutcome
    {
        /// <summary>The Ed25519 signature checks out against a candidate key.</summary>
        Verified,

        /// <summary>No candidate key exists (no cached JWKS and no pinned anchor).</summary>
        NoKeysAvailable,

        /// <summary>Malformed token, unaccepted algorithm, or signature not confirmed by the available keys.</summary>
        Rejected,
    }

    /// <summary>
    /// Verifies the Ed25519 signature of a lease JWT in <c>header.payload.signature</c> form.
    /// </summary>
    /// <param name="jwt">Raw JWT token.</param>
    /// <param name="reason">Human-readable failure reason when the return is <c>false</c> (for logging; never displayed internally).</param>
    /// <returns><c>true</c> only when the header is EdDSA and the signature checks out against a candidate key.</returns>
    public static bool TryVerify(string? jwt, out string? reason)
    {
        return Evaluate(jwt, out reason) == VerificationOutcome.Verified;
    }

    /// <summary>
    /// Evaluates the lease signature, distinguishing confirmation, key unavailability, and
    /// rejection. It is the basis for deciding whether to persist a lease received from the API
    /// before any claim is trusted.
    /// </summary>
    /// <param name="jwt">Raw JWT token in <c>header.payload.signature</c> form.</param>
    /// <param name="reason">Human-readable outcome reason (for logging; never displayed internally).</param>
    /// <returns>Verification outcome — see <see cref="VerificationOutcome"/>.</returns>
    public static VerificationOutcome Evaluate(string? jwt, out string? reason)
    {
        reason = null;

        if (string.IsNullOrWhiteSpace(jwt))
        {
            reason = "token ausente";
            return VerificationOutcome.Rejected;
        }

        string[] parts = jwt.Trim().Split('.');
        if (parts.Length != 3)
        {
            reason = "estrutura JWT inválida";
            return VerificationOutcome.Rejected;
        }

        if (!TryReadHeader(parts[0], out string? algorithm, out reason))
        {
            return VerificationOutcome.Rejected;
        }

        if (!string.Equals(algorithm, AcceptedAlgorithm, StringComparison.Ordinal))
        {
            reason = $"algoritmo não suportado ({algorithm})";
            return VerificationOutcome.Rejected;
        }

        byte[]? signature = TryFromBase64Url(parts[2]);
        if (signature == null || signature.Length != SignatureSize)
        {
            reason = "assinatura em formato inválido";
            return VerificationOutcome.Rejected;
        }

        // The verification key is the anchor, not the JWKS: with no usable anchor (build without
        // pin or an invalid override) verification fails closed. The cache only serves to discover
        // the kid and to signal rotation.
        var cached = SigningKeyStore.LoadVerificationKeys();
        var candidates = OrderCandidates(cached, out string? anchorReason);
        if (candidates.Count == 0)
        {
            reason = anchorReason ?? "âncora pública de verificação indisponível";
            return VerificationOutcome.NoKeysAvailable;
        }

        byte[] data = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        foreach (var candidate in candidates)
        {
            if (VerifySignature(data, signature, candidate.RawKey))
            {
                reason = null;
                return VerificationOutcome.Verified;
            }
        }

        reason = "assinatura não corresponde à âncora de verificação do Connector";
        return VerificationOutcome.Rejected;
    }

    /// <summary>
    /// Resolves the effective verification anchor: the operations override
    /// (<c>NODEAEC_LICENSE_PUBLIC_KEY_SPKI</c>) when set and valid; otherwise the key
    /// compiled into the add-in. A set-but-invalid override is fail-closed — it never falls
    /// back silently to the compiled-in key.
    /// </summary>
    /// <param name="rawKey">Raw Ed25519 key (32 bytes) when the return is <c>true</c>.</param>
    /// <param name="reason">Human-readable reason when the return is <c>false</c> (for logging).</param>
    /// <returns><c>true</c> when a usable anchor exists.</returns>
    private static bool TryGetAnchorKey(out byte[] rawKey, out string? reason)
    {
        string? overrideSpki = ConnectorConfig.LicensePublicKeySpkiOverride;
        if (!string.IsNullOrWhiteSpace(overrideSpki))
        {
            if (TryDecodeSpkiBase64(overrideSpki, out rawKey, out reason))
            {
                return true;
            }

            reason = $"âncora NODEAEC_LICENSE_PUBLIC_KEY_SPKI inválida ({reason})";
            return false;
        }

        return TryDecodeSpkiBase64(ConnectorConfig.DefaultLicensePublicKeySpkiBase64, out rawKey, out reason);
    }

    /// <summary>
    /// Decodes an Ed25519 SPKI public key (standard base64) into the 32 raw curve bytes.
    /// </summary>
    /// <param name="spkiBase64">SPKI key in base64 (44 DER bytes total).</param>
    /// <param name="rawKey">32-byte raw key when the return is <c>true</c>.</param>
    /// <param name="reason">Human-readable failure reason when the return is <c>false</c>.</param>
    /// <returns><c>true</c> when the SPKI key has the expected Ed25519 shape.</returns>
    public static bool TryDecodeSpkiBase64(string? spkiBase64, out byte[] rawKey, out string? reason)
    {
        rawKey = Array.Empty<byte>();

        byte[]? der = null;
        try
        {
            der = string.IsNullOrWhiteSpace(spkiBase64) ? null : Convert.FromBase64String(spkiBase64.Trim());
        }
        catch (FormatException)
        {
            reason = "chave SPKI não é base64 válido";
            return false;
        }

        if (der == null)
        {
            reason = "chave SPKI vazia";
            return false;
        }

        if (der.Length != SpkiEd25519Prefix.Length + RawKeySize)
        {
            reason = "chave SPKI com tamanho inesperado";
            return false;
        }

        for (int i = 0; i < SpkiEd25519Prefix.Length; i++)
        {
            if (der[i] != SpkiEd25519Prefix[i])
            {
                reason = "chave SPKI não é Ed25519";
                return false;
            }
        }

        // Explicit copy instead of a range slice (`der[i..]`), which requires System.Index/
        // System.Range — types missing on .NET Framework 4.8 (Revit 2023/2024).
        rawKey = new byte[RawKeySize];
        Array.Copy(der, SpkiEd25519Prefix.Length, rawKey, 0, RawKeySize);
        reason = null;
        return true;
    }

    /// <summary>
    /// Builds the verification candidates: **only the effective anchor** (H4). The cached JWKS
    /// is no longer a trust source — off-anchor keys raise a rotation warning
    /// (once per process) and never verify.
    /// </summary>
    /// <param name="cached">Cached JWKS keys, used only for diagnostics/rotation.</param>
    /// <param name="anchorReason">Human-readable reason when no usable anchor exists.</param>
    private static List<PublicKeyCandidate> OrderCandidates(
        IReadOnlyList<(string? Kid, byte[] RawKey)> cached,
        out string? anchorReason)
    {
        var ordered = new List<PublicKeyCandidate>();

        if (!TryGetAnchorKey(out byte[] anchor, out anchorReason))
        {
            return ordered;
        }

        SignalJwksOutsideAnchor(cached, anchor);
        ordered.Add(new PublicKeyCandidate(null, anchor));
        return ordered;
    }

    /// <summary>
    /// Logs (once per process) when the cached JWKS carries a key other than the compiled-in
    /// anchor — a rotation signal: the installed Connector needs a release that trusts
    /// the new key.
    /// </summary>
    private static void SignalJwksOutsideAnchor(IReadOnlyList<(string? Kid, byte[] RawKey)> cached, byte[] anchor)
    {
        if (_rotationSignalLogged)
        {
            return;
        }

        foreach (var candidate in cached)
        {
            if (!KeysEqual(candidate.RawKey, anchor))
            {
                _rotationSignalLogged = true;
                Diagnostics.ConnectorLog.Write(
                    "WARN",
                    $"JWKS contém chave fora da âncora compilada (kid {candidate.Kid ?? "sem kid"}): possível rotação — atualize o Connector.");
                return;
            }
        }
    }

    /// <summary>Compares two raw keys byte by byte (avoids Span, missing on net48).</summary>
    private static bool KeysEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Decodes the JWT header and extracts <c>alg</c>. <c>kid</c> is not read: under H4 the only
    /// verification key is the compiled-in anchor, so the header takes no part in trust.
    /// </summary>
    private static bool TryReadHeader(string headerB64, out string? algorithm, out string? reason)
    {
        algorithm = null;

        byte[]? headerBytes = TryFromBase64Url(headerB64);
        if (headerBytes == null)
        {
            reason = "header JWT inválido";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(headerBytes));
            var root = doc.RootElement;
            algorithm = root.TryGetProperty("alg", out var alg) && alg.ValueKind == JsonValueKind.String
                ? alg.GetString()
                : null;
        }
        catch (JsonException)
        {
            reason = "header JWT não é JSON válido";
            return false;
        }

        if (string.IsNullOrWhiteSpace(algorithm))
        {
            reason = "header JWT sem algoritmo";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>Runs the Ed25519 check with the given raw key.</summary>
    private static bool VerifySignature(byte[] data, byte[] signature, byte[] rawKey)
    {
        try
        {
            var signer = new Ed25519Signer();
            signer.Init(false, new Ed25519PublicKeyParameters(rawKey, 0));
            signer.BlockUpdate(data, 0, data.Length);
            return signer.VerifySignature(signature);
        }
        catch (Exception)
        {
            // A malformed key must never take validation down: it simply does not check out.
            return false;
        }
    }

    /// <summary>Converts base64url (padded or not) to bytes, or <c>null</c> when invalid.</summary>
    private static byte[]? TryFromBase64Url(string input)
    {
        if (string.IsNullOrEmpty(input)) return null;

        string base64 = input.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
            case 1: return null;
        }

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>(kid, raw key) pair used in verification-key selection.</summary>
    public readonly record struct PublicKeyCandidate(string? Kid, byte[] RawKey);
}
