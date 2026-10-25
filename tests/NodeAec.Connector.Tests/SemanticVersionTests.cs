using System;
using NodeAec.Connector.Updates;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Unit tests for semantic version parsing, formatting, and comparison.
/// </summary>
public class SemanticVersionTests
{
    [Theory]
    [InlineData("0.4.0", 0, 4, 0, null)]
    [InlineData("v0.4.0", 0, 4, 0, null)]
    [InlineData("V1.2.3", 1, 2, 3, null)]
    [InlineData("  10.20.30  ", 10, 20, 30, null)]
    [InlineData("0.4.0-preview", 0, 4, 0, "preview")]
    [InlineData("v0.4.0-beta.1", 0, 4, 0, "beta.1")]
    [InlineData("1.0.0-rc-2", 1, 0, 0, "rc-2")]
    public void TryParse_ValidInput_ParsesCorrectly(string input, int major, int minor, int patch, string? prerelease)
    {
        bool success = SemanticVersion.TryParse(input, out var version);

        Assert.True(success);
        Assert.NotNull(version);
        Assert.Equal(major, version!.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(prerelease, version.Prerelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("0.4")]
    [InlineData("0.4.0.0")]
    [InlineData("abc")]
    [InlineData("0.4.-1")]
    [InlineData("0.x.0")]
    [InlineData("v1.2.3-")]
    [InlineData("-1.0.0")]
    [InlineData("0.4.0-..\\..\\x")]
    [InlineData("0.4.0-a/b")]
    [InlineData("0.4.0-a b")]
    [InlineData("1. 2.3")]
    [InlineData("+1.2.3")]
    [InlineData("1.2.3-")]
    public void TryParse_InvalidInput_ReturnsFalseWithoutThrowing(string? input)
    {
        bool success = SemanticVersion.TryParse(input, out var version);

        Assert.False(success);
        Assert.Null(version);
    }

    [Fact]
    public void CompareTo_MajorDifference()
    {
        var v1 = new SemanticVersion(1, 0, 0);
        var v2 = new SemanticVersion(0, 9, 9);

        Assert.True(v1 > v2);
        Assert.True(v2 < v1);
    }

    [Fact]
    public void CompareTo_MinorDifference()
    {
        var v1 = new SemanticVersion(0, 4, 0);
        var v2 = new SemanticVersion(0, 3, 9);

        Assert.True(v1 > v2);
        Assert.True(v2 < v1);
    }

    [Fact]
    public void CompareTo_PatchDifference()
    {
        var v1 = new SemanticVersion(0, 3, 1);
        var v2 = new SemanticVersion(0, 3, 0);

        Assert.True(v1 > v2);
        Assert.True(v2 < v1);
    }

    [Fact]
    public void CompareTo_ReleaseHigherThanPrereleaseOfSameVersion()
    {
        var release = new SemanticVersion(0, 4, 0);
        var prerelease = new SemanticVersion(0, 4, 0, "beta");

        Assert.True(release > prerelease);
        Assert.True(prerelease < release);
    }

    [Fact]
    public void CompareTo_PrereleaseComparison()
    {
        var alpha = new SemanticVersion(0, 4, 0, "alpha");
        var beta = new SemanticVersion(0, 4, 0, "beta");
        var beta1 = new SemanticVersion(0, 4, 0, "beta.1");
        var beta2 = new SemanticVersion(0, 4, 0, "beta.2");

        Assert.True(alpha < beta);
        Assert.True(beta1 < beta2);
    }

    [Fact]
    public void Compare_StaticHelper()
    {
        Assert.Equal(-1, SemanticVersion.Compare("0.3.0", "0.4.0"));
        Assert.Equal(0, SemanticVersion.Compare("v0.4.0", "0.4.0"));
        Assert.Equal(1, SemanticVersion.Compare("0.4.0", "0.4.0-preview"));

        Assert.Null(SemanticVersion.Compare("invalid", "0.4.0"));
        Assert.Null(SemanticVersion.Compare(null, "0.4.0"));
    }

    [Fact]
    public void ToString_FormatsCorrectly()
    {
        Assert.Equal("0.4.0", new SemanticVersion(0, 4, 0).ToString());
        Assert.Equal("0.4.0-beta.1", new SemanticVersion(0, 4, 0, "beta.1").ToString());
    }
}
