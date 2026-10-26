using NodeAec.Connector.UI;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Idempotência da Ribbon: verificação de presença de botões no painel.
/// </summary>
public class RibbonDecisionsTests
{
    private const string ButtonName = "NodeAec_ManageConnector";

    [Fact]
    public void ContainsName_MatchesIgnoringCase()
    {
        Assert.True(RibbonDecisions.ContainsName(new[] { ButtonName }, "nodeaec_manageconnector"));
        Assert.False(RibbonDecisions.ContainsName(new[] { ButtonName }, "NodeAec_Other"));
    }

    [Fact]
    public void ContainsName_EmptyList_ReturnsFalse()
    {
        Assert.False(RibbonDecisions.ContainsName(System.Array.Empty<string>(), ButtonName));
    }

    [Theory]
    [InlineData("Node.aec", "Node.aec", true)]
    [InlineData("node.aec", "Node.aec", true)]
    [InlineData("NODE.AEC", "node.aec", true)]
    [InlineData(" Node.aec ", "Node.aec", true)]
    [InlineData("Node.aec", " Node.aec ", true)]
    [InlineData("OtherPlugin", "Node.aec", false)]
    [InlineData("Licenciamento", "Node.aec", false)]
    [InlineData("", "Node.aec", false)]
    [InlineData(null, "Node.aec", false)]
    [InlineData("Node.aec", "", false)]
    [InlineData("Node.aec", null, false)]
    public void IsNodeAecPanel_ValidatesCorrectly(string? title, string? pinned, bool expected)
    {
        Assert.Equal(expected, RibbonDecisions.IsNodeAecPanel(title, pinned));
    }
}
