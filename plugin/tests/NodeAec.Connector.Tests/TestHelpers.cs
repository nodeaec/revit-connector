using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NodeAec.Connector.Hardware;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NodeAec.Connector.Tests;

public static class TestHelpers
{
    /// <summary>
    /// Semente Ed25519 do vetor de teste 1 da RFC 8032 (chave pública conhecida:
    /// d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a).
    /// Determinística: tokens de teste são sempre assinados com a mesma chave.
    /// </summary>
    public static readonly byte[] Rfc8032TestSeed = Convert.FromHexString(
        "9D61B19DEFFD5A60BA844AF492EC2CC44449C5697B326919703BAC031CAE7F60");

    /// <summary>Chave pública esperada da semente acima (vetor da RFC 8032).</summary>
    public static readonly byte[] Rfc8032TestPublicKey = Convert.FromHexString(
        "D75A980182B10AB7D54BFED3C964073A0EE172F3DAA62325AF021A68F707511A");

    /// <summary>Kid usado no JWKS de teste e no header dos tokens de teste.</summary>
    public const string TestKeyId = "node-aec-test-1";

    /// <summary>Prefixo DER de um SPKI Ed25519 (RFC 8410), usado para montar o pin de teste.</summary>
    private static readonly byte[] SpkiEd25519Prefix =
    {
        0x30, 0x2a, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x03, 0x21, 0x00,
    };

    /// <summary>
    /// Pin de teste (SPKI base64) da chave RFC 8032: é o valor injetado em
    /// <c>NODEAEC_LICENSE_PUBLIC_KEY_SPKI</c> pelos testes que verificam assinatura.
    /// </summary>
    public static string TestLicensePinSpkiBase64 { get; } = CreateTestLicensePin();

    private static string CreateTestLicensePin()
    {
        byte[] der = new byte[SpkiEd25519Prefix.Length + Rfc8032TestPublicKey.Length];
        SpkiEd25519Prefix.CopyTo(der, 0);
        Rfc8032TestPublicKey.CopyTo(der, SpkiEd25519Prefix.Length);
        return Convert.ToBase64String(der);
    }

    private static Ed25519PrivateKeyParameters TestPrivateKey => new(Rfc8032TestSeed, 0);

    /// <summary>
    /// Gera um Master Entitlements Lease JWT <b>realmente assinado</b> com a chave de teste
    /// (EdDSA + kid), no mesmo formato emitido pela API Node.aec.
    /// </summary>
    public static string CreateMasterLeaseJwt(
        string sub,
        string mid,
        DateTimeOffset expiresAt,
        List<EntitlementItem> entitlements,
        string scope = "master-lease",
        long? iatOverride = null)
    {
        return CreateSignedJwt(
            new
            {
                iss = "node-aec",
                aud = new[] { "node-aec-desktop", "node-aec-plugin" },
                sub,
                mid,
                scope,
                iat = iatOverride ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = expiresAt.ToUnixTimeSeconds(),
                entitlements
            },
            TestPrivateKey);
    }

    /// <summary>
    /// Monta um JWT assinado por uma chave Ed25519 arbitrária (para testes de rejeição
    /// de chave errada) com header/kid informados.
    /// </summary>
    public static string CreateSignedJwt(object payload, Ed25519PrivateKeyParameters privateKey, string? kid = TestKeyId)
    {
        var header = new Dictionary<string, object> { ["alg"] = "EdDSA", ["typ"] = "JWT" };
        if (kid != null) header["kid"] = kid;

        string headerBase64 = ToBase64Url(JsonSerializer.Serialize(header));
        string payloadBase64 = ToBase64Url(JsonSerializer.Serialize(payload));

        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        byte[] message = Encoding.ASCII.GetBytes($"{headerBase64}.{payloadBase64}");
        signer.BlockUpdate(message, 0, message.Length);
        byte[] signature = signer.GenerateSignature();

        return $"{headerBase64}.{payloadBase64}.{EncodeBase64Url(signature)}";
    }

    /// <summary>
    /// Instala o JWKS da chave de teste no diretório base ativo (onde o gate e o
    /// verificador procuram as chaves). Chamar no construtor dos testes que exercitam
    /// <c>NodeAecGate.Validate</c> ou <c>LeaseSignatureVerifier</c>.
    /// </summary>
    public static void InstallTestSigningKey(string? kid = TestKeyId)
    {
        string x = EncodeBase64Url(new Ed25519PrivateKeyParameters(Rfc8032TestSeed, 0).GeneratePublicKey().GetEncoded());
        string jwks = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new { kty = "OKP", crv = "Ed25519", x, kid, use = "sig", alg = "EdDSA" }
            }
        });

        Assert.True(SigningKeyStore.SaveCachedJwks(jwks));
    }

    /// <summary>Converte bytes em base64url sem padding (formato de segmentos JWT/JWK).</summary>
    public static string EncodeBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    /// <summary>
    /// Machine ID real do ambiente de teste. Falha explícita quando o MachineGuid não é
    /// legível — um teste que dependa da amarração de hardware não pode rodar às cegas.
    /// </summary>
    public static string CurrentMachineId()
    {
        if (!HardwareId.TryGetMachineId(out string machineId, out string? reason))
        {
            throw new InvalidOperationException($"MachineGuid indisponível no ambiente de teste: {reason}");
        }

        return machineId;
    }

    /// <summary>
    /// Substitui o leitor do MachineGuid durante o escopo e restaura leitor + cache ao final
    /// (use sempre com <c>using</c>). A suíte roda com paralelismo desabilitado, então o
    /// leitor global pode ser trocado sem risco de corrida.
    /// </summary>
    /// <param name="machineGuid">Valor devolvido pelo leitor; <c>null</c> simula GUID ilegível.</param>
    public static IDisposable WithMachineGuid(string? machineGuid)
    {
        Func<string?> previous = HardwareId.MachineGuidReader;
        HardwareId.MachineGuidReader = () => machineGuid;
        HardwareId.ResetCacheForTests();

        return new MachineGuidScope(previous);
    }

    /// <summary>
    /// Define o pin de teste (chave RFC 8032) como âncora durante o escopo e restaura o valor
    /// anterior ao final. Use sempre com <c>using</c>; o pin real é compilado no add-in.
    /// </summary>
    public static IDisposable WithTestLicensePin() => WithLicensePin(TestLicensePinSpkiBase64);

    /// <summary>
    /// Define a âncora de verificação (<c>NODEAEC_LICENSE_PUBLIC_KEY_SPKI</c>) durante o escopo
    /// e restaura o valor anterior ao final. <c>null</c> remove o override (volta ao pin
    /// compilado); string inválida exercita a falha fechada.
    /// </summary>
    public static IDisposable WithLicensePin(string? spkiBase64)
    {
        const string variable = "NODEAEC_LICENSE_PUBLIC_KEY_SPKI";
        string? previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, spkiBase64);

        return new LicensePinScope(previous);
    }

    private sealed class LicensePinScope : IDisposable
    {
        private readonly string? _previous;

        public LicensePinScope(string? previous) => _previous = previous;

        public void Dispose() =>
            Environment.SetEnvironmentVariable("NODEAEC_LICENSE_PUBLIC_KEY_SPKI", _previous);
    }

    private sealed class MachineGuidScope : IDisposable
    {
        private readonly Func<string?> _previous;

        public MachineGuidScope(Func<string?> previous) => _previous = previous;

        public void Dispose()
        {
            HardwareId.MachineGuidReader = _previous;
            HardwareId.ResetCacheForTests();
        }
    }

    /// <summary>
    /// Monta um JWT de sessão de usuário com as claims id/email/name emitidas
    /// pela plataforma (cf. createSessionToken na API), sem validar assinatura.
    /// </summary>
    public static string CreateUserSessionJwt(string id, string email, string name)
    {
        var header = new { alg = "RS256", typ = "JWT" };
        var payload = new { id, email, name };

        string headerBase64 = ToBase64Url(JsonSerializer.Serialize(header));
        string payloadBase64 = ToBase64Url(JsonSerializer.Serialize(payload));
        string dummySignature = ToBase64Url("dummy-rs256-signature-data-bytes");

        return $"{headerBase64}.{payloadBase64}.{dummySignature}";
    }

    private static string ToBase64Url(string input) => EncodeBase64Url(Encoding.UTF8.GetBytes(input));
}

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }
}
