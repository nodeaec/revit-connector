using NodeAec.Connector.Updates;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Unit tests for the update decision: installed version against the latest published one.
/// </summary>
public class UpdateDecisionsTests
{
    [Theory]
    [InlineData("0.4.0", "0.4.0")]
    [InlineData("0.5.0", "0.4.0")]
    [InlineData("v1.0.0", "1.0.0")]
    public void Evaluate_SameOrNewerInstalled_IsUpToDate(string current, string latest)
    {
        Assert.Equal(UpdateDecision.UpToDate, UpdateDecisions.Evaluate(current, latest));
    }

    [Theory]
    [InlineData("0.3.0", "0.4.0")]
    [InlineData("0.2.0", "1.0.0")]
    [InlineData("0.4.0-beta", "0.4.0")]
    public void Evaluate_NewerPublished_IsAvailable(string current, string latest)
    {
        Assert.Equal(UpdateDecision.Available, UpdateDecisions.Evaluate(current, latest));
    }

    [Theory]
    [InlineData(null, "0.4.0")]
    [InlineData("invalid", "0.4.0")]
    [InlineData("0.3.0", null)]
    [InlineData("0.3.0", "bad-ver")]
    public void Evaluate_UnparseableVersion_IsUnknown(string? current, string? latest)
    {
        Assert.Equal(UpdateDecision.Unknown, UpdateDecisions.Evaluate(current, latest));
    }
}
