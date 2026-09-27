using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;
using Xunit;

namespace NodeAec.Connector.Tests;

public class LeaseStorageTests : IDisposable
{
    private readonly string _tempDir;

    public LeaseStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecTests_" + Guid.NewGuid().ToString("N"));
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
    public void ClearAll_RemovesBothMasterLeaseAndSession()
    {
        // M7 — primitivo do logout: apaga lease E sessão. Escreve arquivos crus (sem
        // DPAPI) porque o caminho testado é a limpeza, não a persistência criptográfica.
        File.WriteAllText(LeaseStorage.GetLeaseFilePath(), "dummy-lease");
        File.WriteAllText(LeaseStorage.GetSessionFilePath(), "dummy-session");
        Assert.True(File.Exists(LeaseStorage.GetLeaseFilePath()));
        Assert.True(File.Exists(LeaseStorage.GetSessionFilePath()));

        LeaseStorage.ClearAll();

        Assert.False(File.Exists(LeaseStorage.GetLeaseFilePath()));
        Assert.False(File.Exists(LeaseStorage.GetSessionFilePath()));
    }

    [Fact]
    public void ParseUserSessionClaims_ExtractsIdentityFromUserToken()
    {
        string userToken = TestHelpers.CreateUserSessionJwt("x1lwHOhSSguYA", "arquiteta@escritorio.com.br", "Maria");

        var claims = LeaseStorage.ParseUserSessionClaims(userToken);

        Assert.NotNull(claims);
        Assert.Equal("x1lwHOhSSguYA", claims.Id);
        Assert.Equal("arquiteta@escritorio.com.br", claims.Email);
        Assert.Equal("Maria", claims.Name);
    }

    [Fact]
    public void ParseUserSessionClaims_ReturnsNullWithoutIdentityClaims()
    {
        // O lease mestre não tem email/nome: não deve ser tratado como sessão de usuário.
        var exp = DateTimeOffset.UtcNow.AddDays(30);
        string leaseJwt = TestHelpers.CreateMasterLeaseJwt("usr_123", "machine_abc", exp, new List<EntitlementItem>());

        Assert.Null(LeaseStorage.ParseUserSessionClaims(leaseJwt));
        Assert.Null(LeaseStorage.ParseUserSessionClaims(null));
        Assert.Null(LeaseStorage.ParseUserSessionClaims("not-a-jwt"));
    }

    [Fact]
    public void ParseJwtPayload_ExtractsClaimsSuccessfully()
    {
        var exp = DateTimeOffset.UtcNow.AddDays(30);
        var ents = new List<EntitlementItem>
        {
            new EntitlementItem
            {
                Slug = "revit-automator",
                Name = "Revit Automator",
                Type = "perpetual",
                Status = "active"
            }
        };

        string jwt = TestHelpers.CreateMasterLeaseJwt("usr_123", "machine_abc", exp, ents);
        var payload = LeaseStorage.ParseJwtPayload(jwt);

        Assert.NotNull(payload);
        Assert.Equal("usr_123", payload.Sub);
        Assert.Equal("machine_abc", payload.Mid);
        Assert.Equal("master-lease", payload.Scope);
        Assert.Single(payload.Entitlements);
        Assert.Equal("revit-automator", payload.Entitlements[0].Slug);
        Assert.False(payload.IsExpired);
    }

    [Fact]
    public void WriteAllBytesAtomic_ConcurrentWriters_LeaveCompletePayloadAndNoTempResidue()
    {
        // Escritores concorrentes (heartbeat + janelas) por vários rounds: o arquivo final
        // deve ser SEMPRE um payload completo — nunca truncado ou misturado — e não pode
        // sobrar temporário no diretório.
        string path = Path.Combine(_tempDir, "concurrent.lease");
        var payloads = new List<byte[]>();
        for (int i = 0; i < 8; i++)
        {
            var payload = new byte[1024 * (i + 1)]; // tamanhos distintos expõem truncamento
            for (int j = 0; j < payload.Length; j++) payload[j] = (byte)(i + 1);
            payloads.Add(payload);
        }

        System.Threading.Tasks.Parallel.For(0, payloads.Count, i =>
        {
            for (int round = 0; round < 25; round++)
            {
                LeaseStorage.WriteAllBytesAtomic(path, payloads[i]);
            }
        });

        byte[] final = File.ReadAllBytes(path);
        Assert.Contains(payloads, p => p.SequenceEqual(final));
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    [Fact]
    public void SaveMasterLease_WithBlankToken_ReturnsFalse()
    {
        Assert.False(LeaseStorage.SaveMasterLease(""));
        Assert.False(LeaseStorage.SaveMasterLease("   "));
        Assert.Null(LeaseStorage.LoadMasterLease());
    }
}
