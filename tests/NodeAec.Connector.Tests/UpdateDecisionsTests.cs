using NodeAec.Connector.Updates;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Unit tests for update decision logic (UpToDate, Available, Required, Unknown).
/// </summary>
public class UpdateDecisionsTests
{
    [Theory]
    [InlineData("0.4.0", "0.4.0", "0.3.0", UpdateDecision.UpToDate)]
    [InlineData("0.5.0", "0.4.0", null, UpdateDecision.UpToDate)]
    [InlineData("v1.0.0", "1.0.0", "0.9.0", UpdateDecision.UpToDate)]
    [InlineData("0.3.0", "0.3.0", null, UpdateDecision.UpToDate)]
    internal void Evaluate_UpToDateCases(string current, string? latest, string? minimum, UpdateDecision expected)
    {
        var decision = UpdateDecisions.Evaluate(current, latest, minimum);
        Assert.Equal(expected, decision);
    }

    [Theory]
    [InlineData("0.3.0", "0.4.0", "0.3.0", UpdateDecision.Available)]
    [InlineData("0.3.0", "0.4.0", null, UpdateDecision.Available)]
    [InlineData("0.3.0", "0.4.0", "0.2.0", UpdateDecision.Available)]
    [InlineData("0.4.0-beta", "0.4.0", null, UpdateDecision.Available)]
    internal void Evaluate_AvailableCases(string current, string? latest, string? minimum, UpdateDecision expected)
    {
        var decision = UpdateDecisions.Evaluate(current, latest, minimum);
        Assert.Equal(expected, decision);
    }

    [Theory]
    [InlineData("0.2.0", "0.4.0", "0.3.0", UpdateDecision.Required)]
    [InlineData("0.1.0", "0.1.0", "0.2.0", UpdateDecision.Required)]
    [InlineData("0.3.0-preview", "0.4.0", "0.3.0", UpdateDecision.Required)]
    [InlineData("0.2.9", null, "0.3.0", UpdateDecision.Required)]
    internal void Evaluate_RequiredCases(string current, string? latest, string? minimum, UpdateDecision expected)
    {
        var decision = UpdateDecisions.Evaluate(current, latest, minimum);
        Assert.Equal(expected, decision);
    }

    [Theory]
    [InlineData(null, "0.4.0", "0.3.0", UpdateDecision.Unknown)]
    [InlineData("invalid", "0.4.0", "0.3.0", UpdateDecision.Unknown)]
    [InlineData("0.3.0", null, null, UpdateDecision.Unknown)]
    [InlineData("0.3.0", "bad-ver", null, UpdateDecision.Unknown)]
    internal void Evaluate_UnknownCases(string? current, string? latest, string? minimum, UpdateDecision expected)
    {
        var decision = UpdateDecisions.Evaluate(current, latest, minimum);
        Assert.Equal(expected, decision);
    }
}
