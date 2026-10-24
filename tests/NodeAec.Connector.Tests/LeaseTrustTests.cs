using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NodeAec.Connector.Cryptography;
using NodeAec.Connector.Diagnostics;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;
using NodeAec.Licensing.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// O Connector verifica leases e calcula o Machine ID com o NodeAec.Licensing.Lite, o mesmo
/// código dos plugins parceiros. Estes testes cobrem o que é do Connector: usar as âncoras
/// do Lite (não uma cópia), o aviso de rotação via JWKS e o Machine ID.
/// A verificação em si é testada no repositório do Lite.
/// </summary>
public class LeaseTrustTests : IDisposable
{
    private readonly string _tempDir;

    public LeaseTrustTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecLeaseTrustTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LeaseStorage.SetCustomBasePath(_tempDir);
        LeaseTrust.ResetRotationSignalForTests();
    }

    public void Dispose()
    {
        LeaseTrust.ResetRotationSignalForTests();
        LeaseStorage.SetCustomBasePath(null);
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Ed25519Signing_MatchesRfc8032KnownAnswerVector()
    {
        // Vetor TEST 1 da RFC 8032: garante que os leases de teste são assinados corretamente.
        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(TestHelpers.Rfc8032TestSeed, 0));
        signer.BlockUpdate(Array.Empty<byte>(), 0, 0);

        byte[] expected = Convert.FromHexString(
            "E5564300C360AC729086E2CC806E828A84877F1EB8E5D974D873E065224901555FB8821590A33BACC61E39701CF9B46BD25BF5F0595BBE24655141438E7A100B");
        Assert.Equal(expected, signer.GenerateSignature());
        Assert.Equal(
            TestHelpers.Rfc8032TestPublicKey,
            new Ed25519PrivateKeyParameters(TestHelpers.Rfc8032TestSeed, 0).GeneratePublicKey().GetEncoded());
    }

    [Fact]
    public void Evaluate_InProduction_UsesLiteAnchorsAndRejectsTestSignedLease()
    {
        // Sem troca de verificador: a âncora é a do Lite. O JWKS em cache tem a chave de teste
        // e a assinatura confere com ela, mas o cache não é fonte de confiança.
        TestHelpers.InstallTestSigningKey();

        var outcome = LeaseTrust.Evaluate(CreateToken(), out string? reason);

        Assert.Equal(LeaseSignatureVerifier.VerificationOutcome.Rejected, outcome);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void Evaluate_InProduction_DelegatesToLite()
    {
        string jwt = CreateToken();

        var expected = LeaseSignatureVerifier.Evaluate(jwt, out string? expectedReason);
        var actual = LeaseTrust.Evaluate(jwt, out string? actualReason);

        Assert.Equal(expected, actual);
        Assert.Equal(expectedReason, actualReason);
    }

    [Fact]
    public void Evaluate_WithTestPin_VerifiesOnlyTheTestKey()
    {
        using var pin = TestHelpers.WithTestLicensePin();
        string forged = TestHelpers.CreateSignedJwt(
            new { iss = "node-aec", scope = "master-lease" },
            new Ed25519PrivateKeyParameters(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("attacker-seed")), 0));

        Assert.Equal(LeaseSignatureVerifier.VerificationOutcome.Verified, LeaseTrust.Evaluate(CreateToken(), out _));
        Assert.Equal(LeaseSignatureVerifier.VerificationOutcome.Rejected, LeaseTrust.Evaluate(forged, out _));
    }

    [Fact]
    public void Evaluate_JwksWithKeyOutsideLiteAnchors_LogsRotationOnce()
    {
        // A chave de teste não é âncora do Lite: para o Connector é uma chave nova publicada.
        TestHelpers.InstallTestSigningKey();

        LeaseTrust.Evaluate(CreateToken(), out _);
        LeaseTrust.Evaluate(CreateToken(), out _);

        string log = ReadShared(ConnectorLog.GetLogFilePath());
        Assert.Contains("possível rotação", log);
        Assert.Equal(1, CountOccurrences(log, "possível rotação"));
    }

    [Fact]
    public void Evaluate_JwksWithOnlyTrustedKeys_DoesNotLogRotation()
    {
        using var pin = TestHelpers.WithTestLicensePin();
        TestHelpers.InstallTestSigningKey();

        LeaseTrust.Evaluate(CreateToken(), out _);

        string path = ConnectorLog.GetLogFilePath();
        Assert.True(!File.Exists(path) || !ReadShared(path).Contains("possível rotação"));
    }

    [Fact]
    public void TryGetMachineId_InProduction_ReturnsLiteSha256Hex()
    {
        Assert.True(LeaseTrust.TryGetMachineId(out string machineId, out string? reason), reason);
        Assert.Equal(64, machineId.Length);
        Assert.Matches("^[0-9a-f]{64}$", machineId);
    }

    [Fact]
    public void TryGetMachineId_WithoutGuid_FailsClosed()
    {
        using var noGuid = TestHelpers.WithMachineGuid(null);

        Assert.False(LeaseTrust.TryGetMachineId(out string machineId, out string? reason));
        Assert.Equal(string.Empty, machineId);
        Assert.Contains("MachineGuid", reason);
    }

    private static string CreateToken()
    {
        return TestHelpers.CreateMasterLeaseJwt(
            "usr_1",
            new string('m', 64),
            DateTimeOffset.UtcNow.AddDays(10),
            new List<EntitlementItem> { new EntitlementItem { Slug = "revit-automator", Status = "active" } });
    }

    /// <summary>Reads the log while <see cref="ConnectorLog"/> keeps its writer open.</summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
