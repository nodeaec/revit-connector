using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NodeAec.Connector.Config;
using NodeAec.Connector.Cryptography;
using NodeAec.Connector.Hardware;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;

namespace NodeAec.Connector.Client;

/// <summary>
/// HTTP client for the official Node.aec API.
/// Syncs the Master Entitlements Lease, activates one-off keys,
/// performs periodic renewal (heartbeat), and deactivates seats.
/// </summary>
public class ConnectorApiClient
{
    /// <summary>
    /// Explicit per-call timeout (M6). The <see cref="HttpClient"/> default is 100 s —
    /// an "Atualizar" click could hang the UI for minutes on a bad network.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Fixed message for when the Machine ID cannot be derived: without it no licensing
    /// call can address this machine (fail-closed).
    /// </summary>
    private const string MachineIdUnavailableMessage =
        "Não foi possível identificar esta máquina (MachineGuid do Windows indisponível). Contate o suporte Node.aec.";

    /// <summary>
    /// Single per-process HTTP client (M6): TCP+TLS handshake and DNS resolution once per
    /// Revit session instead of once per user action — socket churn is
    /// particularly expensive on net48/Revit 2023-2024 (HTTP.sys + DNS caching). Safe to
    /// share across concurrent calls as long as headers (Authorization,
    /// etc.) stay on each request's <see cref="HttpRequestMessage"/>, as already done.
    /// Never disposed by consumers.
    /// </summary>
    private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>
    /// Creates the API client. Without <paramref name="httpClient"/>, uses the per-process
    /// <see cref="SharedHttpClient"/> (shared and never disposed by
    /// this instance); an injected client remains the caller's responsibility.
    /// </summary>
    /// <param name="baseUrl">API base; when null uses <c>ConnectorConfig.ApiBaseUrl</c>.</param>
    /// <param name="httpClient">Alternate HTTP client (tests/mocks); not owned nor disposed here.</param>
    public ConnectorApiClient(string? baseUrl = null, HttpClient? httpClient = null)
    {
        _baseUrl = (baseUrl ?? ConnectorConfig.ApiBaseUrl).TrimEnd('/');
        _httpClient = httpClient ?? SharedHttpClient;
    }

    /// <summary>
    /// Resolves this machine's Machine ID or returns the ready-made licensing failure.
    /// </summary>
    /// <param name="machineId">Canonical identifier when the return is <c>true</c>.</param>
    /// <param name="failure">Ready-made failure to return when the return is <c>false</c>.</param>
    /// <returns><c>true</c> when the Machine ID was resolved.</returns>
    private static bool TryResolveMachineId(out string machineId, out SyncResult? failure)
    {
        if (HardwareId.TryGetMachineId(out machineId, out string? reason))
        {
            failure = null;
            return true;
        }

        Diagnostics.ConnectorLog.Write("WARN", $"Machine ID indisponível: {reason}.");
        failure = SyncResult.Failed(MachineIdUnavailableMessage);
        return false;
    }

    /// <summary>
    /// Builds the per-process <see cref="HttpClient"/> with an explicit timeout and a
    /// <c>User-Agent</c> identifying the add-in version (eases server-side diagnostics).
    /// </summary>
    private static HttpClient CreateSharedHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler())
        {
            Timeout = RequestTimeout,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"NodeAec.Connector/{ConnectorConfig.Version}");
        return client;
    }

    /// <summary>
    /// Syncs all active licenses of the authenticated user to the current machine,
    /// fetching the signed Master Entitlements Lease and persisting it via DPAPI.
    /// </summary>
    public async Task<SyncResult> SyncMasterEntitlementsAsync(string userToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userToken))
        {
            return SyncResult.Failed("Token de autenticação do usuário ausente.");
        }

        if (!TryResolveMachineId(out string machineId, out SyncResult? machineIdFailure))
        {
            return machineIdFailure!;
        }

        string deviceName = Environment.MachineName;

        var requestPayload = new
        {
            machineId,
            deviceName,
            platform = ConnectorConfig.PlatformDescription,
            connectorVersion = ConnectorConfig.Version,
        };

        var requestJson = JsonSerializer.Serialize(requestPayload);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/account/entitlements/lease")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken.Trim());

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string errorMsg = ParseApiErrorMessage(responseBody, (int)response.StatusCode);
                return SyncResult.Failed(errorMsg);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            string? leaseToken = root.TryGetProperty("leaseToken", out var lt) ? lt.GetString() : null;
            if (string.IsNullOrWhiteSpace(leaseToken))
            {
                return SyncResult.Failed("Resposta da API não continha o token de concessão (leaseToken).");
            }

            int granted = root.TryGetProperty("grantedCount", out var gc) ? gc.GetInt32() : 0;
            int total = root.TryGetProperty("totalCount", out var tc) ? tc.GetInt32() : 0;

            DateTimeOffset? expiresAt = null;
            if (root.TryGetProperty("expiresAt", out var expElem) && expElem.GetString() is string expStr)
            {
                if (DateTimeOffset.TryParse(expStr, out var parsedExp)) expiresAt = parsedExp;
            }

            var entitlements = ParseEntitlements(root) ?? new List<EntitlementItem>();

            // Refreshes the public-key cache (JWKS) for offline lease verification.
            // The return is NOT discarded: it is propagated to the caller in `JwksRefreshed`.
            bool jwksRefreshed = await SigningKeyStore.RefreshAsync(_baseUrl, _httpClient, cancellationToken).ConfigureAwait(false);

            // M1: signature + scope + mid verified BEFORE persisting. A lease that fails
            // verification never touches disk (the previous lease stays intact);
            // with no key available, it is saved with an "unverified" warning to the UI, instead
            // of reporting a success the gate would later opaquely reject.
            LeaseVerdict verdict = VerifyLeaseBeforeSave(leaseToken, out string? verdictReason);
            if (verdict == LeaseVerdict.Rejected)
            {
                Diagnostics.ConnectorLog.Write("WARN", $"Lease recebido recusado antes de salvar: {verdictReason}.");
                return SyncResult.Failed("A licença recebida não passou na verificação de segurança e não foi salva. Atualize novamente; se o problema persistir, contate o suporte Node.aec.");
            }

            // Saves the master token to DPAPI-protected disk (failure = fail-closed, no plaintext)
            if (!LeaseStorage.SaveMasterLease(leaseToken))
            {
                return SyncResult.Failed("Suas licenças foram recebidas, mas não puderam ser salvas neste computador. Verifique as permissões do usuário e tente novamente.");
            }

            SyncResult synced = SyncResult.Succeeded(leaseToken, entitlements, expiresAt, granted, total);
            synced.KeysVerified = verdict == LeaseVerdict.Verified;
            synced.JwksRefreshed = jwksRefreshed;
            return synced;
        }
        catch (Exception ex)
        {
            // L13: only the error type becomes user text — `ex.Message` may leak
            // internal details (paths, TLS, addresses).
            return SyncResult.Failed($"Erro de conexão com o servidor Node.aec ({ex.GetType().Name}).");
        }
    }

    /// <summary>
    /// Activates a one-off manual license key (NAEC-XXXX-...) for this machine.
    /// </summary>
    public async Task<SyncResult> ActivateKeyAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return SyncResult.Failed("Informe a chave de licença no formato NAEC-XXXX-...");
        }

        if (!TryResolveMachineId(out string machineId, out SyncResult? machineIdFailure))
        {
            return machineIdFailure!;
        }

        string deviceName = Environment.MachineName;

        var requestPayload = new
        {
            licenseKey = licenseKey.Trim().ToUpperInvariant(),
            machineId,
            deviceName,
            platform = ConnectorConfig.PlatformDescription,
            clientVersion = ConnectorConfig.Version,
        };

        var requestJson = JsonSerializer.Serialize(requestPayload);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/license/activate", content, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string errorMsg = ParseApiErrorMessage(responseBody, (int)response.StatusCode);
                return SyncResult.Failed(errorMsg);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            string? leaseToken = root.TryGetProperty("leaseToken", out var lt) ? lt.GetString() : null;
            if (string.IsNullOrWhiteSpace(leaseToken))
            {
                return SyncResult.Failed("Resposta da API não continha o token de concessão.");
            }

            // NOTE: the lease issued by /license/activate is single-product (no
            // `entitlements` claim) and must NEVER replace the local Master Entitlements Lease. Writing it to
            // `entitlements.lease` would wipe the other grants and the gate would start denying
            // everything. Activation only takes effect once the account resyncs the master lease
            // (flow handled by the window after this return).
            // The activation lease is never persisted here (single product), but the JWKS
            // refresh is propagated in `JwksRefreshed`: without a cached key, the next master
            // sync would have no way to verify the signature.
            bool jwksRefreshed = await SigningKeyStore.RefreshAsync(_baseUrl, _httpClient, cancellationToken).ConfigureAwait(false);

            var payload = LeaseStorage.ParseJwtPayload(leaseToken);

            SyncResult activated = SyncResult.Succeeded(
                leaseToken,
                new List<EntitlementItem>(),
                payload?.ExpiresAt,
                0,
                0,
                "Chave ativada com sucesso!");
            activated.JwksRefreshed = jwksRefreshed;
            return activated;
        }
        catch (Exception ex)
        {
            return SyncResult.Failed($"Falha ao ativar chave ({ex.GetType().Name}).");
        }
    }

    /// <summary>
    /// Validates the current lease token against the API and issues a renewed lease (heartbeat).
    /// </summary>
    public async Task<SyncResult> ValidateHeartbeatAsync(string? leaseToken = null, CancellationToken cancellationToken = default)
    {
        string? token = leaseToken ?? LeaseStorage.LoadMasterLease();
        if (string.IsNullOrWhiteSpace(token))
        {
            return SyncResult.Failed("Nenhum lease token encontrado para validar.");
        }

        if (!TryResolveMachineId(out string machineId, out SyncResult? machineIdFailure))
        {
            return machineIdFailure!;
        }

        var requestPayload = new { machineId };
        var requestJson = JsonSerializer.Serialize(requestPayload);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/license/validate")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        // Verification-key state to propagate in the result. With no renewed lease,
        // nothing is persisted or updated, so both stay in the healthy state.
        bool keysVerified = true;
        bool jwksRefreshed = true;

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return SyncResult.Failed(ParseApiErrorMessage(responseBody, (int)response.StatusCode));
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // The API answers `valid: false` when the grant is no longer valid on this machine.
            if (root.TryGetProperty("valid", out var validElem) &&
                validElem.ValueKind == JsonValueKind.False)
            {
                return SyncResult.Failed("A plataforma informou que esta concessão não está mais válida neste computador. Entre com sua conta para renovar.");
            }

            string? renewedToken = root.TryGetProperty("leaseToken", out var lt) ? lt.GetString() : null;
            if (!string.IsNullOrWhiteSpace(renewedToken))
            {
                jwksRefreshed = await SigningKeyStore.RefreshAsync(_baseUrl, _httpClient, cancellationToken).ConfigureAwait(false);

                // M1 (same sync policy): signature + scope + mid verified BEFORE
                // overwriting the local lease; rejection preserves the previous lease intact.
                LeaseVerdict verdict = VerifyLeaseBeforeSave(renewedToken, out string? verdictReason);
                if (verdict == LeaseVerdict.Rejected)
                {
                    Diagnostics.ConnectorLog.Write("WARN", $"Lease renovado recusado antes de salvar: {verdictReason}.");
                    return SyncResult.Failed("A licença renovada não passou na verificação de segurança e não foi salva. Atualize novamente; se o problema persistir, contate o suporte Node.aec.");
                }

                if (!LeaseStorage.SaveMasterLease(renewedToken))
                {
                    return SyncResult.Failed("A licença foi renovada, mas não puderam ser salvas neste computador. Verifique as permissões do usuário.");
                }

                token = renewedToken;
                keysVerified = verdict == LeaseVerdict.Verified;
            }

            // The response may carry fresher granular statuses than the local token
            // (e.g. `seat_released`); when present, it takes priority over the payload.
            var entitlements = ParseEntitlements(root) ?? LeaseStorage.ParseJwtPayload(token)?.Entitlements
                ?? new List<EntitlementItem>();
            int activeCount = entitlements.FindAll(e => e.IsActive()).Count;
            var expiresAt = LeaseStorage.ParseJwtPayload(token)?.ExpiresAt;

            SyncResult renewed = SyncResult.Succeeded(
                token,
                entitlements,
                expiresAt,
                activeCount,
                entitlements.Count,
                "Validação concluída com sucesso.");
            renewed.KeysVerified = keysVerified;
            renewed.JwksRefreshed = jwksRefreshed;
            return renewed;
        }
        catch (Exception ex)
        {
            return SyncResult.Failed($"Falha de rede ao validar ({ex.GetType().Name}).");
        }
    }

    /// <summary>
    /// Deactivates a seat bound to this machine.
    /// </summary>
    public async Task<bool> DeactivateLicenseAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        if (!TryResolveMachineId(out string machineId, out _))
        {
            return false;
        }

        var requestPayload = new { licenseKey = licenseKey.Trim(), machineId };
        var requestJson = JsonSerializer.Serialize(requestPayload);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/license/deactivate", content, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Pre-persist verdict for a lease received from the API <b>before</b> persisting it.
    /// </summary>
    private enum LeaseVerdict
    {
        /// <summary>Signature, scope, and mid check out: may be saved as verified.</summary>
        Verified,

        /// <summary>No key available to check the signature: saves only with an "unverified" warning.</summary>
        Unverifiable,

        /// <summary>Failed verification: must never touch disk (fail-closed).</summary>
        Rejected,
    }

    /// <summary>
    /// Verifies a lease received from the API BEFORE persisting it: Ed25519 signature (with the
    /// available key), <c>scope</c> <c>master-lease</c>, and hardware binding
    /// (<c>mid</c>) to this machine. No step trusts the earlier ones: any failure
    /// blocks the write and keeps the previous lease intact.
    /// </summary>
    /// <param name="leaseToken">Lease JWT returned by the API.</param>
    /// <param name="reason">Human-readable reason for logging when the outcome is <see cref="LeaseVerdict.Rejected"/> (never shown to the user).</param>
    /// <returns>
    /// <see cref="LeaseVerdict.Verified"/> (all checks out), <see cref="LeaseVerdict.Unverifiable"/>
    /// (no key available — persist only with a warning), or <see cref="LeaseVerdict.Rejected"/>.
    /// </returns>
    private static LeaseVerdict VerifyLeaseBeforeSave(string leaseToken, out string? reason)
    {
        // 1. Signature first: without it no claim deserves trust. A missing key
        //    is not rejection — it is inability to verify (the caller propagates "unverified").
        LeaseSignatureVerifier.VerificationOutcome outcome = LeaseSignatureVerifier.Evaluate(leaseToken, out reason);
        if (outcome == LeaseSignatureVerifier.VerificationOutcome.Rejected)
        {
            return LeaseVerdict.Rejected;
        }

        // 2. Structural claims: verifiable even without a key (no crypto involved).
        //    Fixed reasons for the log — never echo claims of unknown origin.
        var payload = LeaseStorage.ParseJwtPayload(leaseToken);
        if (payload == null)
        {
            reason = "payload do lease ilegível";
            return LeaseVerdict.Rejected;
        }

        if (!string.Equals(payload.Scope, "master-lease", StringComparison.OrdinalIgnoreCase))
        {
            reason = "scope do lease não é master-lease";
            return LeaseVerdict.Rejected;
        }

        if (!HardwareId.TryGetMachineId(out string currentMachineId, out string? machineIdReason))
        {
            reason = $"Machine ID indisponível: {machineIdReason}";
            return LeaseVerdict.Rejected;
        }

        if (!string.Equals(payload.Mid, currentMachineId, StringComparison.OrdinalIgnoreCase))
        {
            reason = "mid do lease divergente desta máquina";
            return LeaseVerdict.Rejected;
        }

        return outcome == LeaseSignatureVerifier.VerificationOutcome.Verified
            ? LeaseVerdict.Verified
            : LeaseVerdict.Unverifiable;
    }

    /// <summary>
    /// Converts the response <c>entitlements</c> array when present, or returns
    /// <c>null</c> so the caller falls back to the local lease payload.
    /// </summary>
    private static List<EntitlementItem>? ParseEntitlements(JsonElement root)
    {
        if (!root.TryGetProperty("entitlements", out var entArray) ||
            entArray.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var entitlements = new List<EntitlementItem>();
        foreach (var item in entArray.EnumerateArray())
        {
            var ent = JsonSerializer.Deserialize<EntitlementItem>(item.GetRawText());
            if (ent != null) entitlements.Add(ent);
        }

        return entitlements;
    }

    /// <summary>
    /// Maps the API error response — real format
    /// <c>{ error: true, status, type, code, message }</c> — to a friendly
    /// user-language message. Priority: mapped stable code → server message
    /// → raw code → HTTP status. Never throws: a non-JSON body falls through to the status fallback.
    /// </summary>
    /// <param name="responseBody">JSON (or text) body of the error response.</param>
    /// <param name="statusCode">HTTP status of the response.</param>
    private static string ParseApiErrorMessage(string responseBody, int statusCode)
    {
        string? code = null;
        string? serverMessage = null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("code", out var codeElem) &&
                    codeElem.ValueKind == JsonValueKind.String)
                {
                    code = codeElem.GetString();
                }

                // `error` is a boolean in the contract; only `message` carries readable text.
                if (doc.RootElement.TryGetProperty("message", out var msgElem) &&
                    msgElem.ValueKind == JsonValueKind.String)
                {
                    serverMessage = msgElem.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON body (proxy/gateway): fall through to the HTTP status.
        }

        string? mapped = code switch
        {
            "BAD_REQUEST" => "Os dados enviados foram recusados. Revise e tente novamente.",
            "UNAUTHORIZED" => "Sua sessão expirou. Entre com sua conta novamente.",
            "FORBIDDEN" => "Você não tem permissão para esta ação.",
            "NOT_FOUND" => "Serviço não encontrado. Verifique a conexão com a plataforma Node.aec.",
            "RATE_LIMITED" or "LICENSE_RATE_LIMITED" =>
                "Muitas tentativas em pouco tempo. Aguarde alguns minutos e tente novamente.",
            "ACCOUNT_INACTIVE" => "Sua conta não está ativa. Fale com o suporte do Node.aec.",
            "LICENSE_KEY_REQUIRED" => "Informe a chave de licença (formato NAEC-XXXX-XXXX-XXXX-XXXX).",
            "MACHINE_ID_REQUIRED" => "A identificação da máquina não foi enviada. Reinicie o Connector e tente novamente.",
            "INVALID_LICENSE_KEY_FORMAT" => "Formato de chave inválido. A chave deve seguir o formato NAEC-XXXX-XXXX-XXXX-XXXX.",
            "LICENSE_NOT_FOUND" => "Chave de licença não encontrada. Verifique a digitação.",
            "LICENSE_EXPIRED" => "Esta licença ou período de avaliação expirou.",
            "LICENSE_SUSPENDED" => "Esta licença foi suspensa administrativamente. Fale com o suporte do Node.aec.",
            "LICENSE_REVOKED" => "Esta licença foi cancelada ou reembolsada. Libere outra chave.",
            "LICENSE_INACTIVE" => "Esta licença não está ativa. Fale com o suporte do Node.aec.",
            "TRIAL_ALREADY_USED" => "O período de avaliação já foi usado neste computador. Contrate uma assinatura comercial.",
            "ACTIVATION_LIMIT_REACHED" => "Limite de assentos simultâneos atingido para esta licença. Desative o assento em outro computador ou pelo portal web.",
            "ACTIVATION_NOT_FOUND" => "Este computador ainda não está registrado nesta licença. Ative a chave primeiro.",
            "LEASE_TOKEN_REQUIRED" => "Nenhuma licença local encontrada. Clique em atualizar para baixar suas licenças.",
            "INVALID_LEASE_TOKEN" => "A licença local é inválida ou foi adulterada. Atualize suas licenças na internet.",
            "LEASE_TOKEN_EXPIRED" => "O prazo de tolerância offline expirou. Conecte-se à internet para sincronizar.",
            "MACHINE_MISMATCH" => "A licença local pertence a outro computador. Entre com sua conta para ativar este equipamento.",
            _ => null,
        };

        if (!string.IsNullOrWhiteSpace(mapped)) return mapped!;
        if (!string.IsNullOrWhiteSpace(serverMessage)) return serverMessage!;
        if (!string.IsNullOrWhiteSpace(code)) return $"O servidor recusou a solicitação ({code}).";
        return $"Servidor retornou código {statusCode}.";
    }

}
