using System;
using System.IO;
using System.Text.Json;
using NodeAec.Connector.Gate;
using NodeAec.Connector.Storage;
using Xunit;

namespace NodeAec.Connector.Tests;

public class NodeAecGateTests : IDisposable
{
    private readonly string _tempDir;

    public NodeAecGateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecGateTests_" + Guid.NewGuid().ToString("N"));
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
    public void Validate_WithoutLease_ReturnsUnlicensedWithConnectorPrompt()
    {
        var result = NodeAecGate.Validate("revit-automator");

        Assert.False(result.IsLicensed);
        Assert.Contains("Connector", result.Message);
    }

    [Fact]
    public void Validate_WithEmptySlug_ReturnsFailure()
    {
        var result = NodeAecGate.Validate("");

        Assert.False(result.IsLicensed);
    }

    [Theory]
    [InlineData("\"node-aec-plugin\"", true)]
    [InlineData("[\"node-aec-desktop\",\"node-aec-plugin\"]", true)]
    [InlineData("[\"node-aec-desktop\"]", true)]
    [InlineData("[\"outro-produto\"]", false)]
    [InlineData("[]", false)]
    [InlineData("\"node-aec-api\"", false)]
    [InlineData("null", false)]
    public void HasPlatformAudience_AcceptsOnlyPlatformValues(string audJson, bool expected)
    {
        JsonElement aud = JsonSerializer.Deserialize<JsonElement>(audJson);

        Assert.Equal(expected, NodeAecGate.HasPlatformAudience(aud));
    }

    [Fact]
    public void HasPlatformAudience_MissingClaim_Denies()
    {
        Assert.False(NodeAecGate.HasPlatformAudience(null));
    }
}
