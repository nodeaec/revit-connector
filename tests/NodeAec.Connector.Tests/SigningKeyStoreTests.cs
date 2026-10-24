using System;
using System.IO;
using NodeAec.Connector.Storage;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>Cache do JWKS: usado só para descobrir <c>kid</c> e sinalizar rotação, nunca para confiar.</summary>
public class SigningKeyStoreTests : IDisposable
{
    private readonly string _tempDir;

    public SigningKeyStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecJwksTests_" + Guid.NewGuid().ToString("N"));
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
    public void LoadVerificationKeys_IgnoresMalformedEntries()
    {
        string goodX = TestHelpers.EncodeBase64Url(TestHelpers.Rfc8032TestPublicKey);
        string jwks =
            "{\"keys\":[" +
            "{\"kty\":\"OKP\",\"crv\":\"Ed25519\",\"x\":\"curta\",\"kid\":\"ruim\"}," +
            "{\"kty\":\"RSA\",\"n\":\"abc\",\"kid\":\"rsa\"}," +
            $"{{\"kty\":\"OKP\",\"crv\":\"Ed25519\",\"x\":\"{goodX}\",\"kid\":\"boa\"}}" +
            "]}";
        Assert.True(SigningKeyStore.SaveCachedJwks(jwks));

        var keys = SigningKeyStore.LoadVerificationKeys();

        Assert.Single(keys);
        Assert.Equal("boa", keys[0].Kid);
        Assert.Equal(TestHelpers.Rfc8032TestPublicKey, keys[0].RawKey);
    }

    [Fact]
    public void SaveCachedJwks_RejectsDocumentWithoutUsableKey()
    {
        Assert.False(SigningKeyStore.SaveCachedJwks("{\"keys\":[]}"));
        Assert.False(SigningKeyStore.SaveCachedJwks("não é json"));
        Assert.False(SigningKeyStore.SaveCachedJwks(null));
        Assert.Null(SigningKeyStore.LoadCachedJwks());
    }
}
