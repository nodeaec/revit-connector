using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NodeAec.Connector.Config;
using NodeAec.Connector.Cryptography;
using NodeAec.Connector.Gate;
using NodeAec.Connector.Storage;
using Org.BouncyCastle.Crypto.Parameters;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Teste de paridade Connector-vs-Lite (onda C1, via doc + paridade, sem delegação
/// ativa).
///
/// Proveniência do lado Lite: `revit-licensing-lite` 1.0.0-preview.1,
/// `src/NodeAec.Licensing.Lite/Gate.cs` (decisão + taxonomia) e
/// `src/NodeAec.Licensing.Lite/Cryptography/LeaseSignatureVerifier.cs:28-31`
/// (âncora `TrustedAnchors[0]`). O namespace do Lite está em normalização por outro
/// agente, então este arquivo NÃO referencia o assembly Lite: as expectativas do
/// lado Lite são literais copiados verbatim, cada um com a origem citada. Quando o
/// namespace congelar e o Lite sair no NuGet, estender para comparação direta (ver
/// `docs/lite-adoption.md`, seção "Plano de troca").
///
/// Deliberadamente livre de DPAPI (regra do AGENTS.md: `dotnet test` fecha 100% em
/// qualquer sessão, SSH ou interativa). Os casos de paridade que exigem um lease
/// instalado (`mid`, `exp`, `slug`, `granted`/`status`, token adulterado no Gate)
/// ficam diferidos para a fase de delegação; a lista completa está em
/// `docs/lite-adoption.md`.
/// </summary>
public class NodeAecGateVsLiteTests : IDisposable
{
    /// <summary>
    /// Âncoras compiladas precisam coincidir, senão Hub e plugin decidem diferente
    /// sobre o mesmo lease. Literal do Lite 1.0.0-preview.1
    /// (`Cryptography/LeaseSignatureVerifier.cs:28-31`); se o Lite rodar a chave,
    /// este teste quebra de propósito para forçar a atualização do par.
    /// </summary>
    private const string LitePreview1Anchor =
        "MCowBQYDK2VwAyEArMYcaZMAlBeimfR6twrHZndEWOSaIHlSURYFhTjalMg=";

    private readonly string _tempDir;
    private readonly IDisposable _testPin;

    public NodeAecGateVsLiteTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecVsLiteTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LeaseStorage.SetCustomBasePath(_tempDir);
        // Âncora de teste (RFC 8032) como override, igual aos testes do verificador:
        // exercita o caminho Hub com override, que a delegação precisa preservar.
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
    public void LiteAnchor_MatchesConnectorCompiledAnchor()
    {
        Assert.Equal(LitePreview1Anchor, ConnectorConfig.DefaultLicensePublicKeySpkiBase64);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_NullOrWhitespaceSlug_MatchesLiteMessage(string? slug)
    {
        // Lite `Gate.cs:31-34`.
        var result = NodeAecGate.Validate(slug!);

        Assert.False(result.IsLicensed);
        Assert.Equal("Slug do produto não informado para validação.", result.Message);
    }

    [Fact]
    public void Validate_WithoutLease_MatchesLiteMessage()
    {
        // Lite `Gate.cs:36-40`. Diretório temporário vazio: sem DPAPI no caminho.
        var result = NodeAecGate.Validate("revit-automator");

        Assert.False(result.IsLicensed);
        Assert.Equal(
            "Nenhuma credencial do Node.aec encontrada nesta estação. Abra o Node.aec Connector na Ribbon para entrar com sua conta ou ativar sua licença.",
            result.Message);
    }

    [Fact]
    public void GateResult_Success_MapsAllFieldsLikeLiteSnapshot()
    {
        // Paridade com `Snapshot.Success(type, key, name, expiresAt)` do Lite
        // (`Gate.cs:232-235`): o wrapper compatível precisa repassar campo a campo.
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);

        var result = NodeAecGate.GateResult.Success("perpetual", "NAEC-1", "Revit Automator", expiresAt);

        Assert.True(result.IsLicensed);
        Assert.Equal("perpetual", result.LicenseType);
        Assert.Equal("NAEC-1", result.LicenseKey);
        Assert.Equal("Revit Automator", result.ProductName);
        Assert.Equal(expiresAt, result.ExpiresAt);
        Assert.Equal("Licença ativa e verificada.", result.Message);
    }

    [Fact]
    public void GateResult_Failure_NullsAllClaimsLikeLiteSnapshot()
    {
        // Paridade com `Snapshot.Failure(message)` do Lite (`Gate.cs:237-240`).
        const string message = "A licença de 'X' está com status 'suspended'.";

        var result = NodeAecGate.GateResult.Failure(message);

        Assert.False(result.IsLicensed);
        Assert.Null(result.LicenseType);
        Assert.Null(result.LicenseKey);
        Assert.Null(result.ProductName);
        Assert.Null(result.ExpiresAt);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public void OpenConnector_WithoutHub_DoesNotThrow()
    {
        // Paridade com `Gate.OpenConnector()` do Lite (`Gate.cs:179-194`): no-op
        // silencioso quando o Hub não está no AppDomain (a suíte de testes não
        // carrega `ConnectorWindow`).
        var ex = Record.Exception(() => NodeAecGate.OpenConnector());

        Assert.Null(ex);
    }

    [Fact]
    public void Signature_ValidTestToken_VerifiesLikeLite()
    {
        // Mesmo algoritmo e mesma âncora de teste: o caminho Ed25519 precisa
        // aceitar o que o Lite aceitaria sob a mesma âncora.
        string jwt = TestHelpers.CreateSignedJwt(
            new { iss = "node-aec", sub = "usr_parity" },
            new Ed25519PrivateKeyParameters(TestHelpers.Rfc8032TestSeed, 0));

        Assert.True(LeaseSignatureVerifier.TryVerify(jwt, out string? reason), reason);
    }

    [Fact]
    public void Signature_TamperedPayload_RejectsLikeLite()
    {
        string jwt = TestHelpers.CreateSignedJwt(
            new { iss = "node-aec", sub = "usr_parity" },
            new Ed25519PrivateKeyParameters(TestHelpers.Rfc8032TestSeed, 0));

        string[] parts = jwt.Split('.');
        string payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(ToStandardBase64(parts[1])));
        string tampered = $"{parts[0]}.{ToBase64Url(payloadJson.Replace("usr_parity", "usr_tamper"))}.{parts[2]}";

        Assert.False(LeaseSignatureVerifier.TryVerify(tampered, out string? reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void Signature_WrongKey_RejectsLikeLite()
    {
        byte[] attackerSeed = SHA256Of("parity-attacker");
        string jwt = TestHelpers.CreateSignedJwt(
            new { iss = "node-aec", sub = "usr_parity" },
            new Ed25519PrivateKeyParameters(attackerSeed, 0));

        Assert.False(LeaseSignatureVerifier.TryVerify(jwt, out string? reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void Signature_AlgorithmConfusion_RejectsLikeLite()
    {
        string header = ToBase64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        string payload = ToBase64Url("{\"iss\":\"node-aec\"}");
        string jwt = $"{header}.{payload}.c2lnbmF0dXJl";

        Assert.False(LeaseSignatureVerifier.TryVerify(jwt, out string? reason));
        Assert.Contains("algoritmo", reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("apenas-uma-parte")]
    [InlineData("a.b.c.d")]
    public void Signature_MalformedInput_RejectsLikeLite(string? jwt)
    {
        Assert.False(LeaseSignatureVerifier.TryVerify(jwt, out string? reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void Signature_InvalidPinOverride_FailsClosed_HubOnlyDivergence()
    {
        // Divergência intencional documentada: o Hub (operação) aceita override via
        // `NODEAEC_LICENSE_PUBLIC_KEY_SPKI` e falha fechado quando inválido; o Lite
        // é âncora-só e ignora a variável. A delegação precisa preservar este ramo.
        using var badPin = TestHelpers.WithLicensePin("não-é-base64!!");
        string jwt = TestHelpers.CreateSignedJwt(
            new { iss = "node-aec", sub = "usr_parity" },
            new Ed25519PrivateKeyParameters(TestHelpers.Rfc8032TestSeed, 0));

        var outcome = LeaseSignatureVerifier.Evaluate(jwt, out string? reason);

        Assert.Equal(LeaseSignatureVerifier.VerificationOutcome.NoKeysAvailable, outcome);
        Assert.Contains("âncora", reason);
    }

    [Fact]
    public void Signature_CompiledAnchor_RejectsTestKeyWithoutOverride()
    {
        // Sem override vale a âncora compilada (produção), que não é a chave de
        // teste: mesmo um token bem assinado pela chave de teste é rejeitado.
        using var noOverride = TestHelpers.WithLicensePin(null);
        string jwt = TestHelpers.CreateSignedJwt(
            new { iss = "node-aec", sub = "usr_parity" },
            new Ed25519PrivateKeyParameters(TestHelpers.Rfc8032TestSeed, 0));

        Assert.False(LeaseSignatureVerifier.TryVerify(jwt, out string? reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    private static byte[] SHA256Of(string input)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(input));
    }

    private static string ToStandardBase64(string base64Url)
    {
        string base64 = base64Url.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        return base64;
    }

    private static string ToBase64Url(string input)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(input))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
