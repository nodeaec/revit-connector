using System;
using NodeAec.Connector.Config;

namespace NodeAec.Connector.Client;

/// <summary>
/// Builds public Node.aec Store product links from the grant slug.
/// Headless logic (no WPF or Revit API dependencies) to allow testing via 'dotnet test'.
/// </summary>
public static class ProductLinks
{
    /// <summary>
    /// Builds the product page URL (e.g. https://nodeaec.com.br/products/meu-plugin).
    /// Empty slugs return the catalog URL.
    /// </summary>
    public static string BuildProductUrl(string? slug)
    {
        string baseUrl = (ConnectorConfig.CatalogUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(slug))
        {
            return baseUrl;
        }

        string normalized = slug.Trim();
        return $"{baseUrl}/{Uri.EscapeDataString(normalized)}";
    }
}
