using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using NodeAec.Connector.Models;
using NodeAec.Connector.Updates;

namespace NodeAec.Connector.Plugins;

/// <summary>Validation of platform product slugs read from disk or used in file names.</summary>
internal static class ProductSlugs
{
    /// <summary>Longest slug accepted.</summary>
    private const int MaxLength = 100;

    /// <summary>
    /// True for lower-case letters and digits in groups separated by single hyphens
    /// (<c>revit-sample-plugin</c>). Anything else (paths, spaces, upper case) is refused.
    /// </summary>
    /// <param name="slug">Candidate slug.</param>
    /// <returns><c>true</c> when the slug is well formed.</returns>
    internal static bool IsValid(string? slug)
    {
        if (string.IsNullOrEmpty(slug) || slug!.Length > MaxLength || slug[0] == '-' || slug[slug.Length - 1] == '-')
        {
            return false;
        }

        for (int i = 0; i < slug.Length; i++)
        {
            char c = slug[i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || (c == '-' && slug[i - 1] != '-');
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// One Autodesk bundle found in an <c>ApplicationPlugins</c> folder. Bundles built from the
/// Node.aec template (0.4.0 and later) carry <c>NodeAec.product.json</c>, which gives
/// <see cref="ProductSlug"/> and <see cref="Version"/>; older bundles only have the
/// <c>PackageContents.xml</c> name and version.
/// </summary>
internal sealed class InstalledBundle
{
    /// <summary>Product slug from <c>NodeAec.product.json</c>; <c>null</c> for bundles without it.</summary>
    public string? ProductSlug { get; set; }

    /// <summary>Installed version (from <c>NodeAec.product.json</c>, else <c>PackageContents.xml</c> <c>AppVersion</c>).</summary>
    public string? Version { get; set; }

    /// <summary><c>PackageContents.xml</c> <c>Name</c>, used only to recognize older Node.aec bundles.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Full path of the <c>.bundle</c> folder.</summary>
    public string BundlePath { get; set; } = string.Empty;
}

/// <summary>Whether an owned plugin is installed on this computer.</summary>
internal enum InstallState
{
    /// <summary>The installed bundles were not read yet.</summary>
    NotScanned,

    /// <summary>A bundle with this product's slug is installed.</summary>
    Installed,

    /// <summary>No bundle of this product is installed.</summary>
    NotInstalled,

    /// <summary>
    /// A bundle with the product's name but no slug file is installed (built before template
    /// 0.4.0): it is probably this product, so no "Instalar" is offered, and its version is
    /// not trusted for updates.
    /// </summary>
    Unknown,
}

/// <summary>What the plugin row offers.</summary>
internal enum PluginAction
{
    /// <summary>No button.</summary>
    None,

    /// <summary>A newer version is published: "Atualizar".</summary>
    Update,

    /// <summary>Owned but not installed here: "Instalar".</summary>
    Install,
}

/// <summary>
/// One row of the plugin list: a product the account owns, a Node.aec bundle installed here
/// without a license on this account, or both.
/// </summary>
internal sealed class PluginRow
{
    /// <summary>The account's license for the product; <c>null</c> when installed but not owned.</summary>
    public EntitlementItem? Entitlement { get; set; }

    /// <summary>The installed bundle; <c>null</c> when not installed or not scanned.</summary>
    public InstalledBundle? Installed { get; set; }

    /// <summary>Install state on this computer.</summary>
    public InstallState State { get; set; }

    /// <summary>Product slug.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>true</c> when the account owns the product.</summary>
    public bool IsOwned => Entitlement != null;
}

/// <summary>
/// Pure logic behind the plugin list: reading the bundle metadata files, matching what the
/// account owns with what is installed, and deciding each row's detail text and action.
/// No WPF, Revit or file-system access (see <see cref="InstalledBundleScanner"/>).
/// </summary>
internal static class PluginInventory
{
    /// <summary>File a Node.aec bundle carries to name its product and version.</summary>
    internal const string ProductFileName = "NodeAec.product.json";

    /// <summary>
    /// Parses <c>NodeAec.product.json</c> (<c>{"productSlug": "...", "version": "..."}</c>).
    /// Returns <c>false</c> for malformed JSON, an invalid slug, or a version that is not
    /// semantic. Never throws.
    /// </summary>
    /// <param name="json">File contents.</param>
    /// <param name="slug">Parsed slug.</param>
    /// <param name="version">Parsed version.</param>
    /// <returns><c>true</c> when both values are valid.</returns>
    internal static bool TryParseProductFile(string? json, out string slug, out string version)
    {
        slug = string.Empty;
        version = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json!);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("productSlug", out var slugElem) || slugElem.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("version", out var versionElem) || versionElem.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            string s = slugElem.GetString()!.Trim();
            string v = versionElem.GetString()!.Trim();
            if (!ProductSlugs.IsValid(s) || !SemanticVersion.TryParse(v, out _))
            {
                return false;
            }

            slug = s;
            version = v;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads <c>Name</c>, <c>AppVersion</c> and <c>Author</c> from the root of an Autodesk
    /// <c>PackageContents.xml</c>. DTDs are refused. Returns <c>false</c> when the document
    /// is malformed or its root is not <c>ApplicationPackage</c>. Never throws.
    /// </summary>
    /// <param name="xml">File contents.</param>
    /// <param name="name">Package name (may be empty).</param>
    /// <param name="appVersion">Package version (may be empty).</param>
    /// <param name="author">Package author (may be empty).</param>
    /// <returns><c>true</c> when the root element was read.</returns>
    internal static bool TryParsePackageContents(string? xml, out string name, out string appVersion, out string author)
    {
        name = string.Empty;
        appVersion = string.Empty;
        author = string.Empty;
        if (string.IsNullOrWhiteSpace(xml))
        {
            return false;
        }

        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml!), settings);
            if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "ApplicationPackage")
            {
                return false;
            }

            name = (reader.GetAttribute("Name") ?? string.Empty).Trim();
            appVersion = (reader.GetAttribute("AppVersion") ?? string.Empty).Trim();
            author = (reader.GetAttribute("Author") ?? string.Empty).Trim();
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Matches the products the account owns with the bundles installed here, by slug.
    /// Every owned product gets a row (Installed, NotInstalled, Unknown, or NotScanned when
    /// <paramref name="installed"/> is <c>null</c>). Installed bundles with a slug the account
    /// does not own get a row of their own; <paramref name="hiddenSlug"/> (the Connector) never
    /// does. When the same slug is installed twice (machine and user folders), the higher
    /// version wins.
    /// </summary>
    /// <param name="owned">Entitlements to show, in token order (nulls are skipped).</param>
    /// <param name="installed">Bundles found on disk; <c>null</c> when not scanned yet.</param>
    /// <param name="hiddenSlug">Slug never listed as an installed-only row.</param>
    /// <returns>Owned rows in token order, then installed-only rows by name.</returns>
    internal static IReadOnlyList<PluginRow> Build(
        IEnumerable<EntitlementItem> owned,
        IReadOnlyList<InstalledBundle>? installed,
        string hiddenSlug)
    {
        var bySlug = new Dictionary<string, InstalledBundle>(StringComparer.OrdinalIgnoreCase);
        var withoutSlug = new List<InstalledBundle>();
        foreach (var bundle in installed ?? Array.Empty<InstalledBundle>())
        {
            if (bundle == null)
            {
                continue;
            }

            if (string.IsNullOrEmpty(bundle.ProductSlug))
            {
                withoutSlug.Add(bundle);
                continue;
            }

            if (!bySlug.TryGetValue(bundle.ProductSlug!, out var existing) ||
                SemanticVersion.Compare(bundle.Version, existing.Version) > 0)
            {
                bySlug[bundle.ProductSlug!] = bundle;
            }
        }

        var rows = new List<PluginRow>();
        var ownedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in owned)
        {
            if (item == null)
            {
                continue;
            }

            ownedSlugs.Add(item.Slug ?? string.Empty);
            string name = string.IsNullOrWhiteSpace(item.Name) ? item.Slug ?? string.Empty : item.Name;
            var row = new PluginRow { Entitlement = item, Slug = item.Slug ?? string.Empty, Name = name };

            if (installed == null)
            {
                row.State = InstallState.NotScanned;
            }
            else if (bySlug.TryGetValue(row.Slug, out var bundle))
            {
                row.State = InstallState.Installed;
                row.Installed = bundle;
            }
            else if (withoutSlug.Any(b => SameName(b.DisplayName, name)))
            {
                row.State = InstallState.Unknown;
            }
            else
            {
                row.State = InstallState.NotInstalled;
            }

            rows.Add(row);
        }

        rows.AddRange(bySlug.Values
            .Where(b => !ownedSlugs.Contains(b.ProductSlug!) &&
                        !string.Equals(b.ProductSlug, hiddenSlug, StringComparison.OrdinalIgnoreCase))
            .Select(b => new PluginRow
            {
                Installed = b,
                State = InstallState.Installed,
                Slug = b.ProductSlug!,
                Name = string.IsNullOrWhiteSpace(b.DisplayName) ? b.ProductSlug! : b.DisplayName!,
            })
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase));

        return rows;
    }

    /// <summary>
    /// The small second line of a row and its button. <paramref name="latest"/> is the
    /// product's newest published release, or <c>null</c> when unknown (not checked yet,
    /// offline, or not published).
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="latest">Latest published release, if known.</param>
    /// <returns>Detail text (empty for none) and the action to offer.</returns>
    internal static (string Detail, PluginAction Action) Describe(PluginRow row, LatestReleaseInfo? latest)
    {
        switch (row.State)
        {
            case InstallState.Installed:
                string installed = $"Instalado: {row.Installed?.Version}";
                if (row.IsOwned && latest != null &&
                    UpdateDecisions.Evaluate(row.Installed?.Version, latest.Version) == UpdateDecision.Available)
                {
                    return ($"{installed} · Nova versão {latest.Version}", PluginAction.Update);
                }

                return (installed, PluginAction.None);

            case InstallState.NotInstalled:
                return ("Não instalado neste computador", latest != null ? PluginAction.Install : PluginAction.None);

            default:
                return (string.Empty, PluginAction.None);
        }
    }

    /// <summary>
    /// Count of owned, installed rows whose product has a newer published release.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="latestBySlug">Known latest releases by slug.</param>
    /// <returns>Number of rows that would show "Atualizar".</returns>
    internal static int UpdatesAvailable(IEnumerable<PluginRow> rows, IReadOnlyDictionary<string, LatestReleaseInfo?> latestBySlug)
    {
        return rows.Count(r => latestBySlug.TryGetValue(r.Slug, out var latest) && Describe(r, latest).Action == PluginAction.Update);
    }

    private static bool SameName(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        return string.Compare(a!.Trim(), b!.Trim(), CultureInfo.InvariantCulture,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;
    }
}
