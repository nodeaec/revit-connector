using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NodeAec.Connector.Updates;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Unit tests for target installer file naming and hash/size verification.
/// </summary>
public class UpdatePackageVerifierTests : IDisposable
{
    private readonly string _tempDir;

    public UpdatePackageVerifierTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecVerifierTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Theory]
    [InlineData("0.4.0", "NodeAecConnector-0.4.0-Setup.exe")]
    [InlineData("v1.2.3", "NodeAecConnector-1.2.3-Setup.exe")]
    [InlineData("0.4.0-beta.1", "NodeAecConnector-0.4.0-beta.1-Setup.exe")]
    public void GetTargetFileName_ValidSemver_ReturnsDeterministicName(string version, string expected)
    {
        string fileName = UpdatePackageVerifier.GetTargetFileName(version);
        Assert.Equal(expected, fileName);
    }

    [Theory]
    [InlineData("../../evil.exe")]
    [InlineData("malicious/path")]
    [InlineData("not-a-version")]
    [InlineData("")]
    [InlineData("0.4.0-..\\..\\x")]
    [InlineData("0.4.0-a/b")]
    [InlineData("0.4.0-a b")]
    [InlineData("1. 2.3")]
    [InlineData("+1.2.3")]
    [InlineData("1.2.3-")]
    public void GetTargetFileName_InvalidSemver_ThrowsArgumentException(string version)
    {
        Assert.Throws<ArgumentException>(() => UpdatePackageVerifier.GetTargetFileName(version));
    }

    [Theory]
    [InlineData("0.4.0")]
    [InlineData("v1.2.3")]
    [InlineData("0.4.0-beta.1")]
    [InlineData("10.20.30-preview.2")]
    public void GetTargetFilePath_ValidSemver_StaysInsideTargetDirectory(string version)
    {
        string targetDir = Path.GetFullPath(UpdatePackageVerifier.GetTargetDirectory());
        string filePath = UpdatePackageVerifier.GetTargetFilePath(version);

        Assert.StartsWith(targetDir, filePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(targetDir, Path.GetDirectoryName(filePath), ignoreCase: true);
    }

    [Theory]
    [InlineData("../../evil.exe")]
    [InlineData("0.4.0-..\\..\\x")]
    [InlineData("0.4.0-a/b")]
    public void GetTargetFilePath_InvalidSemver_ThrowsArgumentException(string version)
    {
        Assert.Throws<ArgumentException>(() => UpdatePackageVerifier.GetTargetFilePath(version));
    }

    [Fact]
    public void VerifyFile_MatchingHashAndSize_ReturnsTrue()
    {
        string content = "Sample Node.aec installer executable bytes for verification testing.";
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        string filePath = Path.Combine(_tempDir, "test-setup.exe");
        File.WriteAllBytes(filePath, bytes);

        string expectedSha256;
        using (var sha = SHA256.Create())
        {
            expectedSha256 = Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        }

        bool resultLower = UpdatePackageVerifier.VerifyFile(filePath, bytes.Length, expectedSha256);
        bool resultUpper = UpdatePackageVerifier.VerifyFile(filePath, bytes.Length, expectedSha256.ToUpperInvariant());

        Assert.True(resultLower);
        Assert.True(resultUpper);
    }

    [Fact]
    public void VerifyFile_MismatchedSize_ReturnsFalse()
    {
        string filePath = Path.Combine(_tempDir, "test-setup.exe");
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3, 4, 5 });

        bool result = UpdatePackageVerifier.VerifyFile(filePath, 9999, new string('a', 64));

        Assert.False(result);
    }

    [Fact]
    public void VerifyFile_MismatchedHash_ReturnsFalse()
    {
        string filePath = Path.Combine(_tempDir, "test-setup.exe");
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3, 4, 5 });

        bool result = UpdatePackageVerifier.VerifyFile(filePath, 5, new string('0', 64));

        Assert.False(result);
    }

    [Fact]
    public void VerifyFile_MissingFile_ReturnsFalse()
    {
        string nonExistent = Path.Combine(_tempDir, "does-not-exist.exe");

        bool result = UpdatePackageVerifier.VerifyFile(nonExistent, 100, new string('0', 64));

        Assert.False(result);
    }

    [Fact]
    public void VerifyFile_BadShaString_ReturnsFalseWithoutThrowing()
    {
        string filePath = Path.Combine(_tempDir, "test-setup.exe");
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3 });

        Assert.False(UpdatePackageVerifier.VerifyFile(filePath, 3, "short"));
        Assert.False(UpdatePackageVerifier.VerifyFile(filePath, 3, ""));
    }
}
