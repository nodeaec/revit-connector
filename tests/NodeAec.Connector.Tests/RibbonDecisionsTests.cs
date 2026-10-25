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
}
