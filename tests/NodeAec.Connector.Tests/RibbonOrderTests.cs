using System;
using System.IO;
using System.Linq;
using NodeAec.Connector.Storage;
using NodeAec.Connector.UI;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Order of the panels on the Node.aec tab: Connector first, then the user's saved order,
/// then new panels alphabetically; and the saved order file.
/// </summary>
public class RibbonOrderTests : IDisposable
{
    private const string Pinned = "Node.aec";
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "naec-ribbon-" + Guid.NewGuid().ToString("N"));

    public RibbonOrderTests()
    {
        Directory.CreateDirectory(_tempDir);
        LeaseStorage.SetCustomBasePath(_tempDir);
    }

    public void Dispose()
    {
        LeaseStorage.SetCustomBasePath(null);
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static string[] Arranged(string[] current, string[]? saved) =>
        RibbonOrder.Arrange(current, saved, Pinned).Select(i => current[i]).ToArray();

    [Fact]
    public void Arrange_NoSavedOrder_PinnedFirstThenAlphabetical()
    {
        var result = Arranged(new[] { "Zeta", "Quant", Pinned, "Ábaco", "beta" }, null);

        Assert.Equal(new[] { Pinned, "Ábaco", "beta", "Quant", "Zeta" }, result);
    }

    [Fact]
    public void Arrange_SavedOrder_WinsOverAlphabetical_NewPanelsGoAfterAlphabetically()
    {
        var result = Arranged(new[] { "Alfa", Pinned, "Novo B", "Zeta", "Novo A", "Quant" }, new[] { "Zeta", "Quant", "Alfa" });

        Assert.Equal(new[] { Pinned, "Zeta", "Quant", "Alfa", "Novo A", "Novo B" }, result);
    }

    [Fact]
    public void Arrange_SavedOrderNeverMovesThePinnedPanel()
    {
        var result = Arranged(new[] { "Zeta", Pinned, "Alfa" }, new[] { "Zeta", Pinned, "Alfa" });

        Assert.Equal(new[] { Pinned, "Zeta", "Alfa" }, result);
    }

    [Fact]
    public void Arrange_IgnoresSavedTitlesNotOnTheTab_AndMatchesCaseInsensitively()
    {
        var result = Arranged(new[] { "alfa", "Zeta" }, new[] { "Removido", "ZETA", "Alfa" });

        Assert.Equal(new[] { "Zeta", "alfa" }, result);
    }

    [Fact]
    public void Arrange_ReturnsEveryIndexOnce_WithDuplicateTitles()
    {
        var current = new[] { "B", "A", "B", Pinned };

        var order = RibbonOrder.Arrange(current, new[] { "B" }, Pinned);

        Assert.Equal(new[] { 3, 0, 1, 2 }, order.ToArray());
    }

    [Fact]
    public void UserOrder_DropsPinnedBlanksAndRepeats()
    {
        var saved = RibbonOrder.UserOrder(new[] { "Zeta", Pinned, "", "Alfa", "zeta" }, Pinned);

        Assert.Equal(new[] { "Zeta", "Alfa" }, saved);
    }

    [Fact]
    public void SameSequenceAndAllKnown_IgnoreCase()
    {
        Assert.True(RibbonOrder.SameSequence(new[] { "a", "B" }, new[] { "A", "b" }));
        Assert.False(RibbonOrder.SameSequence(new[] { "a", "B" }, new[] { "B", "a" }));
        Assert.True(RibbonOrder.AllKnown(new[] { "a" }, new[] { "A", "b" }));
        Assert.False(RibbonOrder.AllKnown(new[] { "c" }, new[] { "A", "b" }));
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        Assert.Null(RibbonOrder.Load());

        Assert.True(RibbonOrder.Save(new[] { "Zeta", "Ábaco" }));
        Assert.True(RibbonOrder.Save(new[] { "Quant", "Zeta" }));

        Assert.Equal(new[] { "Quant", "Zeta" }, RibbonOrder.Load());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"panels\": \"x\"}")]
    public void Load_InvalidFile_ReturnsNull(string content)
    {
        File.WriteAllText(RibbonOrder.FilePath(), content);

        Assert.Null(RibbonOrder.Load());
    }

    [Fact]
    public void Load_SkipsNonStringEntries()
    {
        File.WriteAllText(RibbonOrder.FilePath(), "{\"panels\": [\"A\", 5, \"\", \"B\"]}");

        Assert.Equal(new[] { "A", "B" }, RibbonOrder.Load());
    }
}
