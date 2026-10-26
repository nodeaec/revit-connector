using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NodeAec.Connector.Config;
using NodeAec.Connector.Models;
using NodeAec.Connector.Plugins;
using NodeAec.Connector.Updates;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Owned × installed plugins: the bundle metadata files, the matching by slug, each row's
/// detail and action, and the disk scan of <c>ApplicationPlugins</c> folders.
/// </summary>
public class PluginInventoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "naec-bundles-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    private static EntitlementItem Owned(string slug, string? name = null) => new() { Slug = slug, Name = name ?? slug };

    private static InstalledBundle Bundle(string? slug, string version, string? name = null) =>
        new() { ProductSlug = slug, Version = version, DisplayName = name ?? slug, BundlePath = @"C:\x\" + (slug ?? name) + ".bundle" };

    private static LatestReleaseInfo Release(string slug, string version) =>
        new(slug, version, null, null, new ReleaseFileInfo("v", "f", "setup.exe", 100, new string('a', 64)));

    // ---------- NodeAec.product.json ----------

    [Fact]
    public void TryParseProductFile_ValidFile_ReturnsSlugAndVersion()
    {
        Assert.True(PluginInventory.TryParseProductFile("{\"productSlug\": \"revit-sample-plugin\", \"version\": \"0.4.0\"}", out var slug, out var version));
        Assert.Equal("revit-sample-plugin", slug);
        Assert.Equal("0.4.0", version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"productSlug\": \"ok\"}")]
    [InlineData("{\"productSlug\": \"../evil\", \"version\": \"1.0.0\"}")]
    [InlineData("{\"productSlug\": \"Upper\", \"version\": \"1.0.0\"}")]
    [InlineData("{\"productSlug\": \"ok\", \"version\": \"latest\"}")]
    [InlineData("{\"productSlug\": 5, \"version\": \"1.0.0\"}")]
    public void TryParseProductFile_Invalid_ReturnsFalse(string? json)
    {
        Assert.False(PluginInventory.TryParseProductFile(json, out _, out _));
    }

    // ---------- PackageContents.xml ----------

    [Fact]
    public void TryParsePackageContents_ReadsRootAttributes()
    {
        const string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><ApplicationPackage SchemaVersion=\"1.0\" Name=\"Sample Plugin\" AppVersion=\"0.3.0\" Author=\"Node.aec\"><CompanyDetails Name=\"Node.aec\" /></ApplicationPackage>";

        Assert.True(PluginInventory.TryParsePackageContents(xml, out var name, out var version, out var author));
        Assert.Equal("Sample Plugin", name);
        Assert.Equal("0.3.0", version);
        Assert.Equal("Node.aec", author);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<Other />")]
    [InlineData("<ApplicationPackage")]
    [InlineData("<!DOCTYPE x [<!ENTITY e \"x\">]><ApplicationPackage Name=\"&e;\" />")]
    public void TryParsePackageContents_InvalidOrDtd_ReturnsFalse(string xml)
    {
        Assert.False(PluginInventory.TryParsePackageContents(xml, out _, out _, out _));
    }

    // ---------- Build: owned × installed ----------

    [Fact]
    public void Build_NotScanned_MarksOwnedRowsNotScanned()
    {
        var rows = PluginInventory.Build(new[] { Owned("a") }, installed: null, ConnectorConfig.ProductSlug);

        Assert.Equal(InstallState.NotScanned, Assert.Single(rows).State);
    }

    [Fact]
    public void Build_MatchesBySlug_AndAddsInstalledOnlyRowsAfterOwned()
    {
        var installed = new[]
        {
            Bundle("b-installed-only", "2.0.0", "Zeta"),
            Bundle("a", "1.0.0"),
            Bundle(ConnectorConfig.ProductSlug, "0.4.0"),
            Bundle("a-other-only", "1.0.0", "Alfa"),
        };

        var rows = PluginInventory.Build(new[] { Owned("a"), Owned("c") }, installed, ConnectorConfig.ProductSlug);

        Assert.Equal(new[] { "a", "c", "a-other-only", "b-installed-only" }, rows.Select(r => r.Slug).ToArray());
        Assert.Equal(InstallState.Installed, rows[0].State);
        Assert.Equal("1.0.0", rows[0].Installed!.Version);
        Assert.Equal(InstallState.NotInstalled, rows[1].State);
        Assert.False(rows[2].IsOwned);
        Assert.Equal("Alfa", rows[2].Name);
    }

    [Fact]
    public void Build_SameSlugTwice_KeepsTheHigherVersion()
    {
        var rows = PluginInventory.Build(new[] { Owned("a") }, new[] { Bundle("a", "1.0.0"), Bundle("a", "1.2.0"), Bundle("a", "1.1.0") }, ConnectorConfig.ProductSlug);

        Assert.Equal("1.2.0", rows[0].Installed!.Version);
    }

    [Fact]
    public void Build_OldBundleWithSameNameButNoSlug_IsUnknownNotNotInstalled()
    {
        var installed = new[] { Bundle(null, "0.2.0", "Exemplo de Integracao Revit") };

        var rows = PluginInventory.Build(new[] { Owned("exemplo", "Exemplo de Integração Revit") }, installed, ConnectorConfig.ProductSlug);

        Assert.Equal(InstallState.Unknown, rows[0].State);
        Assert.Single(rows);
    }

    [Fact]
    public void Build_SkipsNullEntries()
    {
        var rows = PluginInventory.Build(new[] { null!, Owned("a") }, new InstalledBundle[] { null! }, ConnectorConfig.ProductSlug);

        Assert.Single(rows);
    }

    // ---------- Describe: detail line and action ----------

    [Fact]
    public void Describe_InstalledWithNewerRelease_OffersUpdate()
    {
        var row = PluginInventory.Build(new[] { Owned("a") }, new[] { Bundle("a", "1.0.0") }, ConnectorConfig.ProductSlug)[0];

        Assert.Equal(("Instalado: 1.0.0 · Nova versão 1.1.0", PluginAction.Update), PluginInventory.Describe(row, Release("a", "1.1.0")));
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.9.0")]
    public void Describe_InstalledSameOrNewer_NoAction(string latest)
    {
        var row = PluginInventory.Build(new[] { Owned("a") }, new[] { Bundle("a", "1.0.0") }, ConnectorConfig.ProductSlug)[0];

        Assert.Equal(("Instalado: 1.0.0", PluginAction.None), PluginInventory.Describe(row, Release("a", latest)));
    }

    [Fact]
    public void Describe_NotInstalled_OffersInstallOnlyWhenAReleaseIsKnown()
    {
        var row = PluginInventory.Build(new[] { Owned("a") }, Array.Empty<InstalledBundle>(), ConnectorConfig.ProductSlug)[0];

        Assert.Equal(("Não instalado neste computador", PluginAction.Install), PluginInventory.Describe(row, Release("a", "1.0.0")));
        Assert.Equal(("Não instalado neste computador", PluginAction.None), PluginInventory.Describe(row, null));
    }

    [Fact]
    public void Describe_InstalledButNotOwned_NeverOffersUpdate()
    {
        var row = PluginInventory.Build(Array.Empty<EntitlementItem>(), new[] { Bundle("a", "1.0.0") }, ConnectorConfig.ProductSlug)[0];

        Assert.Equal(("Instalado: 1.0.0", PluginAction.None), PluginInventory.Describe(row, Release("a", "2.0.0")));
    }

    [Fact]
    public void Describe_UnknownOrNotScanned_ShowsNothing()
    {
        var unknown = new PluginRow { Entitlement = Owned("a"), Slug = "a", State = InstallState.Unknown };
        var notScanned = new PluginRow { Entitlement = Owned("a"), Slug = "a", State = InstallState.NotScanned };

        Assert.Equal((string.Empty, PluginAction.None), PluginInventory.Describe(unknown, Release("a", "9.0.0")));
        Assert.Equal((string.Empty, PluginAction.None), PluginInventory.Describe(notScanned, Release("a", "9.0.0")));
    }

    [Fact]
    public void UpdatesAvailable_CountsOnlyRowsOfferingUpdate()
    {
        var rows = PluginInventory.Build(
            new[] { Owned("a"), Owned("b"), Owned("c") },
            new[] { Bundle("a", "1.0.0"), Bundle("b", "1.0.0") },
            ConnectorConfig.ProductSlug);
        var latest = new Dictionary<string, LatestReleaseInfo?>
        {
            ["a"] = Release("a", "1.1.0"),
            ["b"] = Release("b", "1.0.0"),
            ["c"] = Release("c", "1.0.0"),
        };

        Assert.Equal(1, PluginInventory.UpdatesAvailable(rows, latest));
    }

    // ---------- Scan (temporary folders) ----------

    private void WriteBundle(string root, string folder, string? productJson, string? packageXml)
    {
        string dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        if (productJson != null) File.WriteAllText(Path.Combine(dir, PluginInventory.ProductFileName), productJson);
        if (packageXml != null) File.WriteAllText(Path.Combine(dir, "PackageContents.xml"), packageXml);
    }

    [Fact]
    public void Scan_ReadsProductFileAndFallsBackToPackageContents()
    {
        string machine = Path.Combine(_root, "machine");
        string user = Path.Combine(_root, "user");
        WriteBundle(machine, "New.bundle", "{\"productSlug\": \"novo\", \"version\": \"1.2.0\"}",
            "<ApplicationPackage Name=\"Novo\" AppVersion=\"9.9.9\" />");
        WriteBundle(user, "Old.bundle", null, "<ApplicationPackage Name=\"Antigo\" AppVersion=\"0.2.0\" />");
        WriteBundle(user, "Broken.bundle", "{oops", null);
        Directory.CreateDirectory(Path.Combine(user, "NotABundle"));

        var found = InstalledBundleScanner.Scan(new[] { machine, user, Path.Combine(_root, "missing") })
            .OrderBy(b => b.BundlePath).ToList();

        Assert.Equal(3, found.Count);
        var broken = found.Single(b => b.BundlePath.EndsWith("Broken.bundle"));
        var fresh = found.Single(b => b.BundlePath.EndsWith("New.bundle"));
        var old = found.Single(b => b.BundlePath.EndsWith("Old.bundle"));
        Assert.Null(broken.ProductSlug);
        Assert.Equal(("novo", "1.2.0", "Novo"), (fresh.ProductSlug, fresh.Version, fresh.DisplayName));
        Assert.Equal((null, "0.2.0", "Antigo"), (old.ProductSlug, old.Version, old.DisplayName));
    }
}
