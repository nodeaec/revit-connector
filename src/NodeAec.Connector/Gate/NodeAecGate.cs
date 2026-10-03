using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NodeAec.Connector.Cryptography;
using NodeAec.Connector.Hardware;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;

namespace NodeAec.Connector.Gate;

/// <summary>
/// Canonical offline validation micro-SDK for partner plugins and internal tools
/// of the Node.aec ecosystem. Zero network calls: reads the local master lease (DPAPI) and checks
/// the Ed25519 signature against the cached JWKS before trusting any claim.
/// </summary>
public static class NodeAecGate
{
    /// <summary>Clock tolerance (seconds) accepted for the `iat` claim to be in the future.</summary>
    private const long ClockSkewToleranceSeconds = 300;
    /// <summary>
    /// Immutable result of <see cref="Validate(string)"/>: only the
    /// <see cref="Success"/>/<see cref="Failure"/> factories build it and no property changes
    /// afterwards — a consumer cannot rewrite a gate result.
    /// </summary>
    public class GateResult
    {
        private GateResult(bool isLicensed, string? licenseType, string? licenseKey, string? productName, DateTimeOffset? expiresAt, string message)
        {
            IsLicensed = isLicensed;
            LicenseType = licenseType;
            LicenseKey = licenseKey;
            ProductName = productName;
            ExpiresAt = expiresAt;
            Message = message;
        }

        public bool IsLicensed { get; }
        public string? LicenseType { get; }
        public string? LicenseKey { get; }
        public string? ProductName { get; }
        public DateTimeOffset? ExpiresAt { get; }
        public string Message { get; }

        public static GateResult Success(string type, string? key, string? name, DateTimeOffset? expiresAt, string message = "Licença ativa e verificada.")
        {
            return new GateResult(true, type, key, name, expiresAt, message);
        }

        public static GateResult Failure(string message)
        {
            return new GateResult(false, null, null, null, null, message);
        }
    }

    /// <summary>
    /// Validates whether the product identified by <paramref name="productSlug"/> holds an
    /// active grant on this workstation. Runs locally with no network access:
    /// checks the lease Ed25519 signature and only then trusts the claims.
    /// </summary>
    public static GateResult Validate(string productSlug)
    {
        if (string.IsNullOrWhiteSpace(productSlug))
        {
            return GateResult.Failure("Slug do produto não informado para validação.");
        }

        string? jwtToken = LeaseStorage.LoadMasterLease();
        if (string.IsNullOrWhiteSpace(jwtToken))
        {
            return GateResult.Failure("Nenhuma credencial do Node.aec encontrada nesta estação. Abra o Node.aec Connector na Ribbon para entrar com sua conta ou ativar sua licença.");
        }

        try
        {
            var payload = LeaseStorage.ParseJwtPayload(jwtToken);
            if (payload == null)
            {
                return GateResult.Failure("Concessão corrompida ou estrutura inválida. Abra o Node.aec Connector para ressincronizar.");
            }

            // 0. Cryptographic verification (Ed25519 / RFC 8032): no claim above is worth
            // anything before the signature checks out. A tampered, forged, or
            // differently-keyed file is rejected here, fail-closed.
            if (!LeaseSignatureVerifier.TryVerify(jwtToken, out string? signatureReason))
            {
                Diagnostics.ConnectorLog.Write("WARN", $"Lease local rejeitado: {signatureReason}.");
                return GateResult.Failure("A licença local não passou na verificação de segurança. Conecte-se à internet e clique em atualizar no Node.aec Connector.");
            }

            // 0.1 Token contract: only master leases issued by the Node.aec platform.
            if (!string.Equals(payload.Iss, "node-aec", StringComparison.Ordinal))
            {
                return GateResult.Failure("Origem da licença local desconhecida. Conecte-se à internet e clique em atualizar no Node.aec Connector.");
            }

            if (!string.Equals(payload.Scope, "master-lease", StringComparison.OrdinalIgnoreCase))
            {
                return GateResult.Failure("A licença local está em formato não suportado. Conecte-se à internet e clique em atualizar no Node.aec Connector.");
            }

            // 0.1b Audience (RFC 7519): the issuer marks whom the token is intended for.
            // Missing or from another flow → does not validate at the plugin gate (fail-closed).
            if (!HasPlatformAudience(payload.Aud))
            {
                return GateResult.Failure("A licença local não foi emitida para este add-in. Conecte-se à internet e clique em atualizar no Node.aec Connector.");
            }

            // 0.2 Defense against a backdated clock: issuance in the future beyond the
            // 5-minute tolerance indicates a tampered date (accounts generated with iat > now + skew).
            if (payload.Iat > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ClockSkewToleranceSeconds)
            {
                return GateResult.Failure("A data da licença local é inválida. Confira a data e hora deste computador e tente novamente.");
            }

            // 1. Hardware binding validation (Machine ID). Without a readable MachineGuid
            //    there is no way to confirm the lease belongs to this machine: fail closed.
            if (!HardwareId.TryGetMachineId(out string currentMachineId, out string? machineIdReason))
            {
                Diagnostics.ConnectorLog.Write("WARN", $"Machine ID indisponível: {machineIdReason}.");
                return GateResult.Failure("Não foi possível identificar esta máquina (MachineGuid do Windows indisponível). Contate o suporte Node.aec.");
            }

            if (!string.Equals(payload.Mid, currentMachineId, StringComparison.OrdinalIgnoreCase))
            {
                return GateResult.Failure("A concessão de licenças foi emitida para outra estação de trabalho (Hardware ID divergente).");
            }

            // 2. Offline grace-period validation (30 days). Without a plausible `exp`,
            //    `IsExpired` is already true; the message distinguishes an unreadable deadline
            //    from an elapsed one so it never prints 01/01/1970 nor formats a null.
            if (payload.IsExpired)
            {
                return payload.ExpiresAt is { } exp
                    ? GateResult.Failure($"O prazo de tolerância offline expirou em {exp:dd/MM/yyyy}. Conecte-se à internet para sincronizar.")
                    : GateResult.Failure("O prazo da licença local não pôde ser lido. Conecte-se à internet e clique em atualizar no Node.aec Connector.");
            }

            // 3. Validation of the specific product in the grant list
            var item = payload.Entitlements?.FirstOrDefault(e =>
                string.Equals(e.Slug, productSlug.Trim(), StringComparison.OrdinalIgnoreCase));

            if (item == null)
            {
                return GateResult.Failure($"O produto '{productSlug}' não consta nas licenças ativas desta conta. Adquira ou ative no catálogo Node.aec.");
            }

            if (!item.IsActive())
            {
                if (string.Equals(item.Status, "seat_limit_reached", StringComparison.OrdinalIgnoreCase))
                {
                    return GateResult.Failure($"O limite de computadores simultâneos para '{item.Name}' foi atingido.");
                }

                if (item.ExpiresAt.HasValue && item.ExpiresAt.Value < DateTimeOffset.UtcNow)
                {
                    return GateResult.Failure($"A licença ou período de teste de '{item.Name}' expirou em {item.ExpiresAt.Value:dd/MM/yyyy}.");
                }

                return GateResult.Failure($"A licença de '{item.Name}' está com status '{item.Status}'.");
            }

            return GateResult.Success(item.Type, item.LicenseKey, item.Name, item.ExpiresAt);
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("ERROR", $"Erro inesperado na validação do gate: {ex.GetType().Name}.");
            return GateResult.Failure("Não foi possível verificar a licença local. Abra o Node.aec Connector para ressincronizar.");
        }
    }

    /// <summary>
    /// Checks whether the lease audience (<c>aud</c>) includes one of the platform
    /// audiences. Accepts a single string or an array (RFC 7519); a missing/foreign claim →
    /// deny. Known audiences cover desktop and plugin because the master-lease issuer
    /// signs for both consumers — refusing either would lock the whole gate.
    /// </summary>
    /// <param name="aud">Value of the <c>aud</c> claim as deserialized, or null when missing.</param>
    /// <returns><c>true</c> when the lease targets the Node.aec platform.</returns>
    internal static bool HasPlatformAudience(JsonElement? aud)
    {
        if (aud is not { } element)
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return IsPlatformAudience(element.GetString());
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in element.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String && IsPlatformAudience(entry.GetString()))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Compares an audience exactly against the known platform values.</summary>
    private static bool IsPlatformAudience(string? value) =>
        string.Equals(value, "node-aec-desktop", StringComparison.Ordinal) ||
        string.Equals(value, "node-aec-plugin", StringComparison.Ordinal);

    /// <summary>
    /// Invokes the Node.aec Connector management window if loaded in the AppDomain.
    /// </summary>
    public static void OpenConnector()
    {
        try
        {
            var uiType = Type.GetType("NodeAec.Connector.UI.ConnectorWindow, NodeAec.Connector");
            if (uiType != null)
            {
                var openMethod = uiType.GetMethod("Open", BindingFlags.Public | BindingFlags.Static);
                openMethod?.Invoke(null, null);
            }
        }
        catch
        {
            // Silent when the connector add-in is not in the same process
        }
    }
}
