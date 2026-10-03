using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NodeAec.Connector.Diagnostics;

namespace NodeAec.Connector.Storage;

/// <summary>
/// Local cache of the Node.aec platform public JWKS (<c>GET /license/jwks</c>).
/// The Connector refreshes the cache on every lease sync/validation, and the gate uses its
/// content only for <c>kid</c> discovery and rotation diagnostics — the key that
/// verifies signatures is the anchor compiled into the add-in (H4), not this file.
/// The JWKS is public material, so it is written as plain text (no DPAPI).
/// </summary>
public static class SigningKeyStore
{
    /// <summary>Cache file name for the JWKS in the Connector base directory.</summary>
    public const string JwksFileName = "license-jwks.json";

    /// <summary>Returns the absolute path of the JWKS cache file.</summary>
    public static string GetJwksFilePath() => Path.Combine(LeaseStorage.GetBaseDirectory(), JwksFileName);

    /// <summary>
    /// Reads the cached JWKS, or <c>null</c> when no valid cache exists yet.
    /// </summary>
    public static string? LoadCachedJwks()
    {
        try
        {
            string path = GetJwksFilePath();
            if (!File.Exists(path)) return null;

            string json = File.ReadAllText(path);
            return HasUsableKey(json) ? json : null;
        }
        catch (Exception ex)
        {
            ConnectorLog.Write("WARN", $"Falha ao ler cache do JWKS: {ex.GetType().Name}.");
            return null;
        }
    }

    /// <summary>
    /// Atomically persists a JWKS document. Returns <c>false</c> (and does not write)
    /// when the document contains no usable OKP/Ed25519 key.
    /// </summary>
    /// <param name="jwksJson">JWKS document returned by the API.</param>
    public static bool SaveCachedJwks(string? jwksJson)
    {
        if (!HasUsableKey(jwksJson))
        {
            return false;
        }

        try
        {
            LeaseStorage.WriteAllBytesAtomic(GetJwksFilePath(), System.Text.Encoding.UTF8.GetBytes(jwksJson!));
            return true;
        }
        catch (Exception ex)
        {
            ConnectorLog.Write("ERROR", $"Falha ao gravar cache do JWKS: {ex.GetType().Name}.");
            return false;
        }
    }

    /// <summary>
    /// Fetches <c>GET /license/jwks</c> from the server and refreshes the local cache.
    /// Best-effort: network/server failures are logged and return
    /// <c>false</c> without breaking an in-flight lease sync.
    /// </summary>
    /// <param name="baseUrl">Node.aec API base URL.</param>
    /// <param name="httpClient">HTTP client to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<bool> RefreshAsync(
        string baseUrl,
        HttpClient httpClient,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient
                .GetAsync($"{baseUrl.TrimEnd('/')}/license/jwks", cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                ConnectorLog.Write("WARN", $"Atualização do JWKS recusada pela API ({(int)response.StatusCode}).");
                return false;
            }

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!SaveCachedJwks(json))
            {
                ConnectorLog.Write("WARN", "Atualização do JWKS ignorada: documento sem chave utilizável.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            ConnectorLog.Write("WARN", $"Falha ao atualizar JWKS: {ex.GetType().Name}.");
            return false;
        }
    }

    /// <summary>
    /// Returns the Ed25519 public keys (kid + 32 raw bytes) present in the cache.
    /// Malformed entries are skipped individually.
    /// </summary>
    public static IReadOnlyList<(string? Kid, byte[] RawKey)> LoadVerificationKeys()
    {
        var keys = new List<(string? Kid, byte[] RawKey)>();
        string? json = LoadCachedJwks();
        if (json == null) return keys;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("keys", out var keyArray) ||
                keyArray.ValueKind != JsonValueKind.Array)
            {
                return keys;
            }

            foreach (var jwk in keyArray.EnumerateArray())
            {
                if (jwk.ValueKind != JsonValueKind.Object) continue;
                if (!string.Equals(GetString(jwk, "kty"), "OKP", StringComparison.Ordinal)) continue;
                if (!string.Equals(GetString(jwk, "crv"), "Ed25519", StringComparison.Ordinal)) continue;

                string? x = GetString(jwk, "x");
                byte[]? raw = TryFromBase64Url(x);
                if (raw == null || raw.Length != 32) continue;

                keys.Add((GetString(jwk, "kid"), raw));
            }
        }
        catch (JsonException ex)
        {
            ConnectorLog.Write("WARN", $"Cache do JWKS corrompido: {ex.GetType().Name}.");
        }

        return keys;
    }

    /// <summary>Checks whether the document holds at least one valid OKP/Ed25519 key.</summary>
    private static bool HasUsableKey(string? jwksJson)
    {
        if (string.IsNullOrWhiteSpace(jwksJson)) return false;

        try
        {
            using var doc = JsonDocument.Parse(jwksJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("keys", out var keyArray) ||
                keyArray.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var jwk in keyArray.EnumerateArray())
            {
                if (jwk.ValueKind != JsonValueKind.Object) continue;
                if (!string.Equals(GetString(jwk, "kty"), "OKP", StringComparison.Ordinal)) continue;
                if (!string.Equals(GetString(jwk, "crv"), "Ed25519", StringComparison.Ordinal)) continue;
                if (TryFromBase64Url(GetString(jwk, "x")) is byte[] raw && raw.Length == 32) return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Reads a JWK string property, or <c>null</c>.</summary>
    private static string? GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>Converts base64url to bytes, or <c>null</c> when invalid.</summary>
    private static byte[]? TryFromBase64Url(string? input)
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
}
