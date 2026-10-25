using System;
using NodeAec.Connector.Updates;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Unit tests for parsing the GET /products/:slug/latest server contract payload.
/// </summary>
public class LatestReleaseInfoTests
{
    private const string ValidJson = @"{
      ""slug"": ""nodeaec-connector"",
      ""version"": ""0.4.0"",
      ""releasedAt"": ""2026-11-02T12:00:00.000Z"",
      ""notes"": ""Melhorias na interface e atualizações automáticas."",
      ""minimumVersion"": ""0.3.0"",
      ""file"": {
        ""versionPublicId"": ""ver_abc123"",
        ""filePublicId"": ""fil_xyz789"",
        ""name"": ""NodeAecConnector-0.4.0-Setup.exe"",
        ""sizeBytes"": 12345678,
        ""sha256"": ""e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855""
      }
    }";

    [Fact]
    public void TryParse_ValidJson_ReturnsTrueAndPopulatesProperties()
    {
        bool success = LatestReleaseInfo.TryParse(ValidJson, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal("nodeaec-connector", info!.Slug);
        Assert.Equal("0.4.0", info.Version);
        Assert.NotNull(info.ReleasedAt);
        Assert.Equal("Melhorias na interface e atualizações automáticas.", info.Notes);
        Assert.Equal("0.3.0", info.MinimumVersion);

        Assert.NotNull(info.File);
        Assert.Equal("ver_abc123", info.File.VersionPublicId);
        Assert.Equal("fil_xyz789", info.File.FilePublicId);
        Assert.Equal("NodeAecConnector-0.4.0-Setup.exe", info.File.Name);
        Assert.Equal(12345678, info.File.SizeBytes);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", info.File.Sha256);
    }

    [Fact]
    public void TryParse_NullMinimumVersionAndNotes_ReturnsTrue()
    {
        string json = @"{
          ""slug"": ""nodeaec-connector"",
          ""version"": ""0.4.0"",
          ""minimumVersion"": null,
          ""file"": {
            ""versionPublicId"": ""ver_1"",
            ""filePublicId"": ""fil_1"",
            ""sizeBytes"": 100,
            ""sha256"": ""E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855""
          }
        }";

        bool success = LatestReleaseInfo.TryParse(json, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Null(info!.MinimumVersion);
        Assert.Null(info.Notes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData(@"{}")]
    [InlineData(@"{""slug"": """"}")]
    [InlineData(@"{""slug"": ""nodeaec-connector"", ""version"": ""invalid-semver""}")]
    [InlineData(@"{""slug"": ""nodeaec-connector"", ""version"": ""0.4.0""}")]
    [InlineData(@"{""slug"": ""nodeaec-connector"", ""version"": ""0.4.0"", ""file"": null}")]
    [InlineData(@"{""slug"": ""nodeaec-connector"", ""version"": ""0.4.0"", ""file"": { ""versionPublicId"": """", ""filePublicId"": ""f"", ""sizeBytes"": 10, ""sha256"": ""e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"" }}")]
    [InlineData(@"{""slug"": ""nodeaec-connector"", ""version"": ""0.4.0"", ""file"": { ""versionPublicId"": ""v"", ""filePublicId"": ""f"", ""sizeBytes"": 0, ""sha256"": ""e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"" }}")]
    [InlineData(@"{""slug"": ""nodeaec-connector"", ""version"": ""0.4.0"", ""file"": { ""versionPublicId"": ""v"", ""filePublicId"": ""f"", ""sizeBytes"": -5, ""sha256"": ""e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"" }}")]
    [InlineData(@"{""slug"": ""nodeaec-connector"", ""version"": ""0.4.0"", ""file"": { ""versionPublicId"": ""v"", ""filePublicId"": ""f"", ""sizeBytes"": 10, ""sha256"": ""short-sha"" }}")]
    [InlineData(@"{""slug"": ""nodeaec-connector"", ""version"": ""0.4.0"", ""file"": { ""versionPublicId"": ""v"", ""filePublicId"": ""f"", ""sizeBytes"": 10, ""sha256"": ""e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85g"" }}")]
    public void TryParse_MalformedOrMissingFields_ReturnsFalseWithoutThrowing(string? json)
    {
        bool success = LatestReleaseInfo.TryParse(json, out var info);

        Assert.False(success);
        Assert.Null(info);
    }
}
