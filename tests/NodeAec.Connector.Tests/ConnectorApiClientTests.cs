using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NodeAec.Connector.Client;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;
using NodeAec.Connector.Updates;
using Org.BouncyCastle.Crypto.Parameters;
using Xunit;

namespace NodeAec.Connector.Tests;

public class ConnectorApiClientTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IDisposable _testPin;

    public ConnectorApiClientTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecApiTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LeaseStorage.SetCustomBasePath(_tempDir);
        // H4: a verificação usa a âncora compilada; os testes injetam a chave RFC 8032 como pin.
        _testPin = TestHelpers.WithTestLicensePin();
    }

    public void Dispose()
    {
        _testPin.Dispose();
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
    public async Task MidDependentCalls_WithoutMachineId_FailClosedBeforeHttp()
    {
        // MachineGuid ilegível ⇒ nenhuma chamada pode sair para a API: sem Machine ID não
        // há como ativar, sincronizar, validar ou desativar assento nesta máquina.
        using var machineGuidScope = TestHelpers.WithMachineGuid(null);
        var handler = new MockHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP não deveria ser chamado sem Machine ID."));
        using var httpClient = new HttpClient(handler);
        var client = new ConnectorApiClient("https://api.test", httpClient);

        var sync = await client.SyncMasterEntitlementsAsync("valid-user-jwt");
        var activate = await client.ActivateKeyAsync("NAEC-KEY1-KEY2-KEY3-KEY4");
        var heartbeat = await client.ValidateHeartbeatAsync("hdr.payload.sig");
        bool deactivated = await client.DeactivateLicenseAsync("NAEC-KEY1-KEY2-KEY3-KEY4");

        Assert.False(sync.Success);
        Assert.False(activate.Success);
        Assert.False(heartbeat.Success);
        Assert.False(deactivated);
        Assert.Contains("identificar esta máquina", sync.Message);
        Assert.Contains("identificar esta máquina", activate.Message);
        Assert.Contains("identificar esta máquina", heartbeat.Message);
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
        string forgedLease = CreateForgedMasterLease(TestHelpers.CurrentMachineId());
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
            TestHelpers.CurrentMachineId(),
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
        string forgedRenewal = CreateForgedMasterLease(TestHelpers.CurrentMachineId());
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

    [Fact]
    public void ParseApiErrorMessage_ConnectorOutdated_FormatsFriendlyMessageWithData()
    {
        string errorJson = JsonSerializer.Serialize(new
        {
            error = true,
            status = 426,
            code = "CONNECTOR_OUTDATED",
            message = "Connector is outdated",
            data = new
            {
                currentVersion = "0.3.0",
                minimumVersion = "0.4.0",
                latestVersion = "0.4.0"
            }
        });

        string message = ConnectorApiClient.ParseApiErrorMessage(errorJson, 426);

        Assert.Equal("Esta versão do Node.aec Connector (0.3.0) não é mais aceita pela plataforma. Atualize para a versão 0.4.0 ou mais recente em Atualizações.", message);
    }

    [Fact]
    public void ParseApiErrorMessage_ConnectorOutdated_WithoutData_FallsBackToServerMessage()
    {
        string errorJson = JsonSerializer.Serialize(new
        {
            error = true,
            status = 426,
            code = "CONNECTOR_OUTDATED",
            message = "Versão desatualizada pelo servidor."
        });

        string message = ConnectorApiClient.ParseApiErrorMessage(errorJson, 426);

        Assert.Equal("Versão desatualizada pelo servidor.", message);
    }

    [Fact]
    public async Task SyncMasterEntitlementsAsync_ConnectorOutdated_ReturnsOutdatedResult()
    {
        var errorResponse = new
        {
            error = true,
            status = 426,
            code = "CONNECTOR_OUTDATED",
            message = "Server outdated message",
            data = new
            {
                currentVersion = "0.3.0",
                minimumVersion = "0.4.0",
                latestVersion = "0.5.0"
            }
        };

        var handler = new MockHttpMessageHandler(req =>
            new HttpResponseMessage((HttpStatusCode)426)
            {
                Content = new StringContent(JsonSerializer.Serialize(errorResponse))
            });

        using var httpClient = new HttpClient(handler);
        var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.SyncMasterEntitlementsAsync("user-token");

        Assert.False(result.Success);
        Assert.True(result.IsOutdated);
        Assert.Equal("CONNECTOR_OUTDATED", result.ErrorCode);
        Assert.Equal("0.4.0", result.MinimumVersion);
        Assert.Equal("0.5.0", result.LatestVersion);
        Assert.Contains("Esta versão do Node.aec Connector (0.3.0) não é mais aceita", result.Message);

        var status = UpdateStorage.LoadUpdateStatus();
        Assert.NotNull(status);
        Assert.Equal("0.5.0", status!.LatestVersion);
        Assert.Equal("0.4.0", status.MinimumVersion);
    }

    [Fact]
    public async Task ActivateKeyAsync_ConnectorOutdated_SavesUpdateStatus()
    {
        var errorResponse = new
        {
            error = true,
            status = 426,
            code = "CONNECTOR_OUTDATED",
            message = "Update required",
            data = new
            {
                currentVersion = "0.3.0",
                minimumVersion = "0.4.0",
                latestVersion = "0.6.0"
            }
        };

        var handler = new MockHttpMessageHandler(req =>
            new HttpResponseMessage((HttpStatusCode)426)
            {
                Content = new StringContent(JsonSerializer.Serialize(errorResponse))
            });

        using var httpClient = new HttpClient(handler);
        var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.ActivateKeyAsync("NAEC-1234-5678-9012-3456");

        Assert.False(result.Success);
        Assert.True(result.IsOutdated);
        Assert.Equal("0.6.0", result.LatestVersion);
        Assert.Equal("0.4.0", result.MinimumVersion);

        var status = UpdateStorage.LoadUpdateStatus();
        Assert.NotNull(status);
        Assert.Equal("0.6.0", status!.LatestVersion);
        Assert.Equal("0.4.0", status.MinimumVersion);
    }

    [Fact]
    public async Task ValidateHeartbeatAsync_ConnectorOutdated_SavesUpdateStatus()
    {
        var errorResponse = new
        {
            error = true,
            status = 426,
            code = "CONNECTOR_OUTDATED",
            message = "Update required",
            data = new
            {
                currentVersion = "0.3.0",
                minimumVersion = "0.4.0",
                latestVersion = "0.7.0"
            }
        };

        var handler = new MockHttpMessageHandler(req =>
            new HttpResponseMessage((HttpStatusCode)426)
            {
                Content = new StringContent(JsonSerializer.Serialize(errorResponse))
            });

        using var httpClient = new HttpClient(handler);
        var client = new ConnectorApiClient("https://api.test", httpClient);

        string sampleJwt = TestHelpers.CreateMasterLeaseJwt(
            "usr_1",
            TestHelpers.CurrentMachineId(),
            DateTimeOffset.UtcNow.AddDays(30),
            new List<EntitlementItem> { new EntitlementItem { Slug = "revit-automator", Status = "active" } });

        var result = await client.ValidateHeartbeatAsync(sampleJwt);

        Assert.False(result.Success);
        Assert.True(result.IsOutdated);
        Assert.Equal("0.7.0", result.LatestVersion);
        Assert.Equal("0.4.0", result.MinimumVersion);

        var status = UpdateStorage.LoadUpdateStatus();
        Assert.NotNull(status);
        Assert.Equal("0.7.0", status!.LatestVersion);
        Assert.Equal("0.4.0", status.MinimumVersion);
    }

    [Fact]
    public async Task SyncMasterEntitlementsAsync_WithConnectorUpdate_SavesUpdateBadge()
    {
        string validLease = TestHelpers.CreateMasterLeaseJwt(
            "usr_1",
            TestHelpers.CurrentMachineId(),
            DateTimeOffset.UtcNow.AddDays(30),
            new List<EntitlementItem> { new EntitlementItem { Slug = "revit-automator", Status = "active" } });

        var responseBody = new
        {
            success = true,
            leaseToken = validLease,
            grantedCount = 1,
            totalCount = 1,
            connectorUpdate = new
            {
                latestVersion = "0.4.0",
                minimumVersion = "0.3.0"
            }
        };

        using var httpClient = new HttpClient(HandlerServing(responseBody));
        var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.SyncMasterEntitlementsAsync("valid-user-jwt");

        Assert.True(result.Success);
        var badge = UpdateStorage.LoadUpdateStatus();
        Assert.NotNull(badge);
        Assert.Equal("0.4.0", badge!.LatestVersion);
        Assert.Equal("0.3.0", badge.MinimumVersion);
    }

    [Fact]
    public async Task ValidateHeartbeatAsync_WithConnectorUpdate_SavesUpdateBadge()
    {
        string validLease = TestHelpers.CreateMasterLeaseJwt(
            "usr_1",
            TestHelpers.CurrentMachineId(),
            DateTimeOffset.UtcNow.AddDays(30),
            new List<EntitlementItem> { new EntitlementItem { Slug = "revit-automator", Status = "active" } });

        var responseBody = new
        {
            success = true,
            valid = true,
            scope = "master-lease",
            leaseToken = validLease,
            connectorUpdate = new
            {
                latestVersion = "0.4.0",
                minimumVersion = "0.3.0"
            }
        };

        using var httpClient = new HttpClient(HandlerServing(responseBody));
        var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.ValidateHeartbeatAsync(validLease);

        Assert.True(result.Success);
        var badge = UpdateStorage.LoadUpdateStatus();
        Assert.NotNull(badge);
        Assert.Equal("0.4.0", badge!.LatestVersion);
        Assert.Equal("0.3.0", badge.MinimumVersion);
    }

    [Fact]
    public async Task ActivateKeyAsync_WithConnectorUpdate_SavesUpdateBadge()
    {
        string singleProductLease = TestHelpers.CreateSignedJwt(
            new
            {
                iss = "node-aec",
                sub = "lic_1",
                mid = TestHelpers.CurrentMachineId(),
                scope = "product-lease",
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()
            },
            new Ed25519PrivateKeyParameters(TestHelpers.Rfc8032TestSeed, 0));

        var responseBody = new
        {
            success = true,
            leaseToken = singleProductLease,
            connectorUpdate = new
            {
                latestVersion = "0.5.0",
                minimumVersion = null as string
            }
        };

        using var httpClient = new HttpClient(HandlerServing(responseBody));
        var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.ActivateKeyAsync("NAEC-KEY1-KEY2-KEY3-KEY4");

        Assert.True(result.Success);
        var badge = UpdateStorage.LoadUpdateStatus();
        Assert.NotNull(badge);
        Assert.Equal("0.5.0", badge!.LatestVersion);
        Assert.Null(badge.MinimumVersion);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_Success_ReturnsParsedInfo()
    {
        var payload = new
        {
            slug = "nodeaec-connector",
            version = "0.4.0",
            releasedAt = "2026-11-02T12:00:00.000Z",
            notes = "Notas da versao",
            minimumVersion = "0.3.0",
            file = new
            {
                versionPublicId = "ver_123",
                filePublicId = "fil_456",
                name = "NodeAecConnector-0.4.0-Setup.exe",
                sizeBytes = 1000,
                sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
            }
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("/products/nodeaec-connector/latest", req.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Get, req.Method);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload))
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new ConnectorApiClient("https://api.test", httpClient);

        var release = await client.GetLatestReleaseAsync();

        Assert.NotNull(release);
        Assert.Equal("nodeaec-connector", release!.Slug);
        Assert.Equal("0.4.0", release.Version);
        Assert.Equal("ver_123", release.File.VersionPublicId);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_404_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = new HttpClient(handler);
        var client = new ConnectorApiClient("https://api.test", httpClient);

        var release = await client.GetLatestReleaseAsync();

        Assert.Null(release);
    }

    [Fact]
    public async Task GetDownloadUrlAsync_SuccessHttps_ReturnsUrl()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("/products/nodeaec-connector/versions/v1/files/f1/download", req.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("user-jwt", req.Headers.Authorization.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    downloadUrl = "https://downloads.nodeaec.com.br/files/setup.exe"
                }))
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new ConnectorApiClient("https://api.test", httpClient);

        string? url = await client.GetDownloadUrlAsync("nodeaec-connector", "v1", "f1", "user-jwt");

        Assert.Equal("https://downloads.nodeaec.com.br/files/setup.exe", url);
    }

    [Fact]
    public async Task GetDownloadUrlAsync_NonHttps_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                downloadUrl = "http://insecure.nodeaec.com.br/setup.exe"
            }))
        });

        using var httpClient = new HttpClient(handler);
        var client = new ConnectorApiClient("https://api.test", httpClient);

        string? url = await client.GetDownloadUrlAsync("nodeaec-connector", "v1", "f1", "user-jwt");

        Assert.Null(url);
    }

    [Fact]
    public async Task DownloadFileAsync_Success_StreamsAndReportsProgress()
    {
        byte[] content = Encoding.UTF8.GetBytes("downloaded file content");
        var handler = new MockHttpMessageHandler(req =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            };
            res.Content.Headers.ContentLength = content.Length;
            return res;
        });

        using var httpClient = new HttpClient(handler);
        var client = new ConnectorApiClient("https://api.test", httpClient);

        string dest = Path.Combine(_tempDir, "downloaded-setup.exe");
        double lastReported = 0;
        var progress = new Progress<double>(p => lastReported = p);

        bool success = await client.DownloadFileAsync("https://downloads.test/setup.exe", dest, content.Length, progress);

        Assert.True(success);
        Assert.True(File.Exists(dest));
        Assert.Equal(content, File.ReadAllBytes(dest));
        Assert.True(lastReported > 0);
    }

    [Fact]
    public async Task DownloadFileAsync_OversizeBody_AbortsImmediatelyAndDeletesTempFile()
    {
        byte[] content = new byte[2000];
        new Random(42).NextBytes(content);

        var handler = new MockHttpMessageHandler(req =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            };
            res.Content.Headers.ContentLength = content.Length;
            return res;
        });

        using var httpClient = new HttpClient(handler);
        var client = new ConnectorApiClient("https://api.test", httpClient);

        string dest = Path.Combine(_tempDir, "oversize-setup.exe");

        bool success = await client.DownloadFileAsync("https://downloads.test/setup.exe", dest, expectedSizeBytes: 500);

        Assert.False(success);
        Assert.False(File.Exists(dest));
        var remainingFiles = Directory.GetFiles(_tempDir, "*oversize*");
        Assert.Empty(remainingFiles);
    }

    [Fact]
    public async Task DownloadFileAsync_NonHttps_ReturnsFalse()
    {
        var client = new ConnectorApiClient("https://api.test");
        string dest = Path.Combine(_tempDir, "insecure.exe");

        bool success = await client.DownloadFileAsync("http://insecure.test/setup.exe", dest, 100);

        Assert.False(success);
        Assert.False(File.Exists(dest));
    }
}
