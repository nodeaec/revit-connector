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
/// Verifica a assinatura Ed25519 (RFC 8032) de tokens de lease emitidos pela plataforma Node.aec
/// antes de qualquer claim ser confiável. A única chave candidata é a âncora compilada no
/// add-in (<see cref="ConnectorConfig.DefaultLicensePublicKeySpkiBase64"/>), substituível
/// pela operação via <c>NODEAEC_LICENSE_PUBLIC_KEY_SPKI</c>.
/// O JWKS em cache (<c>%APPDATA%\NodeAec\license-jwks.json</c>, atualizado por
/// <c>GET /license/jwks</c>) deixa de ser fonte de confiança: serve para descoberta de
/// <c>kid</c> e para sinalizar rotação, e uma chave fora da âncora nunca verifica.
/// Falha sempre em modo fechado: sem âncora utilizável ou com assinatura inválida, o lease
/// não é aceito.
/// </summary>
public static class LeaseSignatureVerifier
{
    /// <summary>Algoritmo de assinatura aceito nos leases (EdDSA / Ed25519).</summary>
    public const string AcceptedAlgorithm = "EdDSA";

    /// <summary>Tamanho da chave bruta Ed25519 em bytes.</summary>
    private const int RawKeySize = 32;

    /// <summary>Tamanho da assinatura Ed25519 em bytes.</summary>
    private const int SignatureSize = 64;

    /// <summary>Prefixo DER fixo de um SubjectPublicKeyInfo Ed25519 (RFC 8410).</summary>
    private static readonly byte[] SpkiEd25519Prefix =
    {
        0x30, 0x2a, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x03, 0x21, 0x00,
    };

    /// <summary>
    /// Garante um único aviso de rotação por processo — a verificação roda a cada comando de
    /// plugin e o JWKS continua com a chave antiga depois de uma rotação.
    /// </summary>
    private static bool _rotationSignalLogged;

    /// <summary>
    /// Desfecho granular da verificação de assinatura. Permite ao chamador distinguir
    /// "há chave disponível e a assinatura não confere" (rejeição firme) de "não existe
    /// nenhuma chave com que verificar" (indisponibilidade — o chamador propaga o estado
    /// de "não verificado" em vez de acusar adulteração).
    /// </summary>
    public enum VerificationOutcome
    {
        /// <summary>A assinatura Ed25519 confere com uma chave candidata.</summary>
        Verified,

        /// <summary>Nenhuma chave candidata existe (sem JWKS em cache e sem âncora fixa).</summary>
        NoKeysAvailable,

        /// <summary>Token malformado, algoritmo não aceito ou assinatura não confirmada pelas chaves disponíveis.</summary>
        Rejected,
    }

    /// <summary>
    /// Verifica a assinatura Ed25519 de um JWT de lease no formato <c>header.payload.signature</c>.
    /// </summary>
    /// <param name="jwt">Token JWT bruto.</param>
    /// <param name="reason">Motivo legível da falha quando o retorno é <c>false</c> (para log; nunca exibir internamente).</param>
    /// <returns><c>true</c> somente quando o header é EdDSA e a assinatura confere com uma chave candidata.</returns>
    public static bool TryVerify(string? jwt, out string? reason)
    {
        return Evaluate(jwt, out reason) == VerificationOutcome.Verified;
    }

    /// <summary>
    /// Avalia a assinatura do lease distinguindo confirmação, indisponibilidade de chave e
    /// rejeição. É a base da decisão de persistir (ou não) um lease recebido da API antes
    /// de qualquer claim ser confiável.
    /// </summary>
    /// <param name="jwt">Token JWT bruto no formato <c>header.payload.signature</c>.</param>
    /// <param name="reason">Motivo legível do desfecho (para log; nunca exibir internamente).</param>
    /// <returns>Desfecho da verificação — ver <see cref="VerificationOutcome"/>.</returns>
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

        // A chave de verificação é a âncora, não o JWKS: sem âncora utilizável (build sem pin
        // ou override inválido) a verificação falha fechada. O cache só serve para descobrir
        // o kid e para sinalizar rotação.
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
    /// Resolve a âncora efetiva de verificação: o override de operação
    /// (<c>NODEAEC_LICENSE_PUBLIC_KEY_SPKI</c>) quando definido e válido; senão a chave
    /// compilada no add-in. Override definido e inválido é falha fechada — nunca cai
    /// silenciosamente para a chave compilada.
    /// </summary>
    /// <param name="rawKey">Chave bruta Ed25519 (32 bytes) quando o retorno é <c>true</c>.</param>
    /// <param name="reason">Motivo legível quando o retorno é <c>false</c> (para log).</param>
    /// <returns><c>true</c> quando existe âncora utilizável.</returns>
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
    /// Decodifica uma chave pública SPKI (base64 padrão) de Ed25519 para os 32 bytes brutos da curva.
    /// </summary>
    /// <param name="spkiBase64">Chave SPKI em base64 (44 bytes DER no total).</param>
    /// <param name="rawKey">Chave bruta de 32 bytes quando o retorno é <c>true</c>.</param>
    /// <param name="reason">Motivo legível da falha quando o retorno é <c>false</c>.</param>
    /// <returns><c>true</c> quando a chave SPKI tem o formato Ed25519 esperado.</returns>
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

        // Cópia explícita em vez de fatia com range (`der[i..]`), que exige System.Index/
        // System.Range — tipos ausentes no .NET Framework 4.8 (Revit 2023/2024).
        rawKey = new byte[RawKeySize];
        Array.Copy(der, SpkiEd25519Prefix.Length, rawKey, 0, RawKeySize);
        reason = null;
        return true;
    }

    /// <summary>
    /// Monta os candidatos de verificação: **apenas a âncora efetiva** (H4). O JWKS em cache
    /// deixa de ser fonte de confiança — chaves fora da âncora geram um aviso de rotação
    /// (uma vez por processo) e jamais verificam.
    /// </summary>
    /// <param name="cached">Chaves do JWKS em cache, usadas só para diagnóstico/rotação.</param>
    /// <param name="anchorReason">Motivo legível quando não há âncora utilizável.</param>
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
    /// Registra (uma vez por processo) quando o JWKS em cache traz chave diferente da âncora
    /// compilada — sinal de rotação: o Connector instalado precisa de uma release que confie
    /// na nova chave.
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

    /// <summary>Compara duas chaves brutas byte a byte (evita Span, ausente no net48).</summary>
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
    /// Decodifica o header JWT e extrai <c>alg</c>. O <c>kid</c> não é lido: sob H4 a única
    /// chave de verificação é a âncora compilada, então o header não participa da confiança.
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

    /// <summary>Executa a verificação Ed25519 com a chave bruta informada.</summary>
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
            // Chave malformada nunca deve derrubar a validação: apenas não confere.
            return false;
        }
    }

    /// <summary>Converte base64url (com ou sem padding) em bytes, ou <c>null</c> se inválido.</summary>
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

    /// <summary>Par (kid, chave bruta) usado na seleção de chaves de verificação.</summary>
    public readonly record struct PublicKeyCandidate(string? Kid, byte[] RawKey);
}
