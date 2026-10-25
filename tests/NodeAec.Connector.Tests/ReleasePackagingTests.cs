using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Regras do pacote .bundle que o build não verifica: o manifesto copiado para cada
/// <c>Contents\&lt;grupo&gt;\</c>, e os identificadores que <c>release.ps1</c> e
/// <c>installer.iss</c> repetem.
/// </summary>
public class ReleasePackagingTests
{
    private static readonly string Root = FindRepositoryRoot();

    /// <summary>
    /// O mesmo .addin vai para as três pastas do bundle, então o Assembly tem de ser
    /// relativo ao manifesto. Um XML inválido (por exemplo "--" num comentário) faz o
    /// Revit ignorar o add-in sem aviso claro.
    /// </summary>
    [Fact]
    public void AddinManifest_IsValidXmlWithRelativeAssembly()
    {
        var doc = XDocument.Load(Path.Combine(Root, "src/NodeAec.Connector/NodeAec.Connector.addin"));
        var addIn = doc.Root!.Element("AddIn")!;

        Assert.Equal("NodeAec.Connector.dll", addIn.Element("Assembly")!.Value);
        Assert.Equal("NodeAec.Connector.App", addIn.Element("FullClassName")!.Value);
        Assert.Equal("4B8E1A2C-9F3D-4E5A-8B7C-1D2E3F4A5B6C", addIn.Element("AddInId")!.Value);
    }

    /// <summary>
    /// ProductCode e UpgradeCodes identificam o pacote para o carregador da Autodesk; o zip
    /// (release.ps1) e o instalador (installer.iss) geram o PackageContents.xml separadamente.
    /// </summary>
    [Fact]
    public void BundleCodes_MatchBetweenReleaseScriptAndInstaller()
    {
        string release = File.ReadAllText(Path.Combine(Root, "scripts/release.ps1"));
        string installer = File.ReadAllText(Path.Combine(Root, "scripts/installer.iss"));

        string productCode = Regex.Match(release, @"\$ProductCode = ""(\{[0-9A-F-]{36}\})""").Groups[1].Value;
        string upgradeCode = Regex.Match(release, @"\$UpgradeCode = ""(\{[0-9A-F-]{36}\})""").Groups[1].Value;

        Assert.NotEqual(string.Empty, productCode);
        Assert.NotEqual(string.Empty, upgradeCode);
        Assert.Contains($"BundleProductCode = '{productCode}'", installer);
        Assert.Contains($"BundleUpgradeCode = '{upgradeCode}'", installer);
    }

    /// <summary>O ISCC lê o script como ANSI: um caractere fora do ASCII sai corrompido no instalador.</summary>
    [Fact]
    public void InstallerScript_IsPlainAscii()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(Root, "scripts/installer.iss"));

        Assert.DoesNotContain(bytes, b => b > 0x7F);
    }

    /// <summary>Cada ano do Revit aponta para o build do seu grupo de compatibilidade.</summary>
    [Fact]
    public void Installer_MapsEveryYearToItsGroupBuild()
    {
        string installer = File.ReadAllText(Path.Combine(Root, "scripts/installer.iss"));

        foreach (string group in new[] { "2023-2024", "2025-2026", "2027" })
        {
            Assert.Contains($@"Source: ""{{#PayloadStage}}\Contents\{group}\*""", installer);
            Assert.Contains($"Check: GroupSelected('{group}')", installer);
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NodeAec.Connector.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Raiz do repositório (NodeAec.Connector.sln) não encontrada.");
    }
}
