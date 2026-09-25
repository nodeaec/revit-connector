using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NodeAec.Connector.Client;
using NodeAec.Connector.Hardware;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;
using Org.BouncyCastle.Crypto.Parameters;
using Xunit;

namespace NodeAec.Connector.Tests;

public class ConnectorApiClientTests : IDisposable
{
    private readonly string _tempDir;

    public ConnectorApiClientTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecApiTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LeaseStorage.SetCustomBasePath(_tempDir);
    }

    public void Dispose()
    {
        LeaseStorage.SetCustomBasePath(null);
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task SyncMasterEntitlementsAsync_WithoutToken_FailsFast()
    {
        var client = new ConnectorApiClient();

        var result = await client.SyncMasterEntitlementsAsync(string.Empty);

        Assert.False(result.Success);
        Assert.Contains("ausente", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyncMasterEntitlementsAsync_ApiError_UsesStableCodeCopy()
    {
        // Formato real do middleware de erro da API: { error: true, status, type, code, message }.
        var errorResponse = new
        {
            error = true,
            status = 403,
            type = "Forbidden",
            code = "ACTIVATION_LIMIT_REACHED",
            message = "Seat limit reached."
        };

        var handler = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(JsonSerializer.Serialize(errorResponse))
            });

        using var httpClient = new HttpClient(handler);
        var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.SyncMasterEntitlementsAsync("valid-user-jwt");

        Assert.False(result.Success);
        Assert.Contains("Limite de assentos simultâneos atingido", result.Message);
    }

    // ---- M1: verificação de assinatura + scope + mid ANTES de SaveMasterLease ----

    [Fact]
    public async Task SyncMasterEntitlementsAsync_TamperedLease_RejectedBeforeSaving()
    {
        // O JWKS servido traz a chave de teste; o lease vem assinado por chave forjada.
        string forgedLease = CreateForgedMasterLease(HardwareId.GetMachineId());
        using var httpClient = new HttpClient(HandlerServing(new { success = true, leaseToken = forgedLease }));
        var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.SyncMasterEntitlementsAsync("valid-user-jwt");

        // A recusa acontece ANTES de gravar: a mensagem é de verificação (não de falha de
        // disco) e nenhum lease toca o disco — o lease anterior, se houver, permaneceria.
        Assert.False(result.Success);
        Assert.Contains("verificação de segurança", result.Message);
        Assert.Null(LeaseStorage.LoadMasterLease());
    }

    [Fact]
    public async Task SyncMasterEntitlementsAsync_WrongMachineId_RejectedBeforeSaving()
    {
        // Assinatura e scope válidos, mas emitido para outra máquina: mid divergente.
        string leaseForAnotherMachine = TestHelpers.CreateMasterLeaseJwt(
            "usr_1",
            new string('x', 64),
            DateTimeOffset.UtcNow.AddDays(30),
            new List<EntitlementItem> { new EntitlementItem { Slug = "revit-automator", Status = "active" } });

        using var httpClient = new HttpClient(HandlerServing(new { success = true, leaseToken = leaseForAnotherMachine }));
        var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.SyncMasterEntitlementsAsync("valid-user-jwt");

        Assert.False(result.Success);
        Assert.Contains("verificação de segurança", result.Message);
        Assert.Null(LeaseStorage.LoadMasterLease());
    }

    [Fact]
    public async Task SyncMasterEntitlementsAsync_NonMasterScope_RejectedBeforeSaving()
    {
        // Assinatura e mid válidos, mas scope de produto — não é um master lease.
        string pluginScopedLease = TestHelpers.CreateMasterLeaseJwt(
            "usr_1",
            HardwareId.GetMachineId(),
            DateTimeOffset.UtcNow.AddDays(30),
            new List<EntitlementItem> { new EntitlementItem { Slug = "revit-automator", Status = "active" } },
            scope: "plugin-license");

        using var httpClient = new HttpClient(HandlerServing(new { success = true, leaseToken = pluginScopedLease }));
        var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.SyncMasterEntitlementsAsync("valid-user-jwt");

        Assert.False(result.Success);
        Assert.Contains("verificação de segurança", result.Message);
        Assert.Null(LeaseStorage.LoadMasterLease());
    }

    [Fact]
    public async Task ValidateHeartbeatAsync_TamperedRenewal_RejectedBeforeSaving()
    {
        string forgedRenewal = CreateForgedMasterLease(HardwareId.GetMachineId());
        using var httpClient = new HttpClient(HandlerServing(
            new { success = true, valid = true, scope = "master-lease", leaseToken = forgedRenewal }));
        var client = new ConnectorApiClient("https://api.test", httpClient);

        // Token de entrada via parâmetro: o arrange não grava nada em disco.
        var result = await client.ValidateHeartbeatAsync("lease-existente.nao-gravado.token");

        Assert.False(result.Success);
        Assert.Contains("verificação de segurança", result.Message);
        Assert.Null(LeaseStorage.LoadMasterLease());
    }

    [Fact]
    public async Task ActivateKeyAsync_OnActivationLimitReached_ReturnsFriendlyMessage()
    {
        var errorResponse = new
        {
            error = true,
            status = 403,
            type = "Forbidden",
            code = "ACTIVATION_LIMIT_REACHED",
            message = "Seat limit reached."
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("/license/activate", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(JsonSerializer.Serialize(errorResponse))
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.ActivateKeyAsync("NAEC-KEY1-KEY2-KEY3-KEY4");

        Assert.False(result.Success);
        Assert.Contains("Limite de assentos simultâneos atingido", result.Message);
    }

    /// <summary>
    /// Handler que serve o JWKS real da chave de teste em <c>/license/jwks</c> e responde
    /// <paramref name="apiResponse"/> em qualquer outro endpoint. Com
    /// <paramref name="jwksAvailable"/> = <c>false</c>, o refresh do JWKS falha (500).
    /// </summary>
    private static MockHttpMessageHandler HandlerServing(object apiResponse, bool jwksAvailable = true)
    {
        return new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/license/jwks")
            {
                return jwksAvailable
                    ? JwksResponse()
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("jwks indisponível")
                    };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(apiResponse))
            };
        });
    }

    /// <summary>
    /// Monta um lease master com estrutura perfeita, mas assinado por uma chave FORJADA
    /// (não publicada no JWKS): a assinatura jamais poderá conferir.
    /// </summary>
    private static string CreateForgedMasterLease(string mid, string scope = "master-lease")
    {
        byte[] attackerSeed = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes("attacker-seed"));

        return TestHelpers.CreateSignedJwt(
            new
            {
                iss = "node-aec",
                scope,
                mid,
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
            },
            new Ed25519PrivateKeyParameters(attackerSeed, 0));
    }

    private static HttpResponseMessage JwksResponse()
    {
        string x = TestHelpers.EncodeBase64Url(TestHelpers.Rfc8032TestPublicKey);
        string jwks = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new { kty = "OKP", crv = "Ed25519", x, kid = TestHelpers.TestKeyId, use = "sig", alg = "EdDSA" }
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jwks)
        };
    }
}
