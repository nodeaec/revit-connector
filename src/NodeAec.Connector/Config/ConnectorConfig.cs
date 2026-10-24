using System;

namespace NodeAec.Connector.Config;

/// <summary>
/// Global configuration for Node.aec platform endpoints, version, and public keys.
/// Environment-overridable endpoints only accept <c>https://</c> — plaintext transport
/// would silently downgrade licensing, the trust-anchor fetch (JWKS), and browser
/// login (M2). Local development without TLS requires the explicit opt-in
/// <c>NODEAEC_ALLOW_INSECURE_DEV=1</c>.
/// </summary>
public static class ConnectorConfig
{
    /// <summary>
    /// Connector version sent to the API (<c>connectorVersion</c>, <c>clientVersion</c>,
    /// User-Agent) and shown in the window titles. Must equal <c>&lt;Version&gt;</c> in
    /// <c>NodeAec.Connector.csproj</c>; a unit test fails when they drift.
    /// </summary>
    public const string Version = "0.2.0";

    /// <summary>
    /// Host platform sent to the API (<c>platform</c>). Reflects the Revit year this
    /// assembly was compiled for: Directory.Build.props defines REVIT2023..REVIT2027 from
    /// <c>-p:RevitYear</c>. Builds without the define (e.g. headless tests or consumers
    /// recompiling this file) fall back to 2026.
    /// </summary>
#if REVIT2023
    public const string PlatformDescription = "Windows / Revit 2023";
#elif REVIT2024
    public const string PlatformDescription = "Windows / Revit 2024";
#elif REVIT2025
    public const string PlatformDescription = "Windows / Revit 2025";
#elif REVIT2027
    public const string PlatformDescription = "Windows / Revit 2027";
#else
    public const string PlatformDescription = "Windows / Revit 2026";
#endif

    /// <summary>Explicit opt-in that allows <c>http://</c> on configured endpoints.</summary>
    private const string AllowInsecureVariable = "NODEAEC_ALLOW_INSECURE_DEV";

    private static string? _apiBaseUrl;
    private static string? _webAuthUrl;
    private static string? _catalogUrl;

    /// <summary>
    /// Node.aec REST API base URL (<c>NODEAEC_API_URL</c>).
    /// Lazily resolved so that an insecure configuration throws
    /// <see cref="InvalidOperationException"/> with a clear message at the point of use, not an
    /// obscure <see cref="TypeInitializationException"/> at type load.
    /// </summary>
    /// <exception cref="InvalidOperationException">The variable defines a non-<c>https://</c> endpoint without the development opt-in.</exception>
    public static string ApiBaseUrl
    {
        get => _apiBaseUrl ??= ReadEndpointUrl("NODEAEC_API_URL", "https://api.nodeaec.com.br");
        set => _apiBaseUrl = value;
    }

    /// <summary>
    /// Desktop SSO login page URL (OAuth loopback) (<c>NODEAEC_AUTH_URL</c>).
    /// Same HTTPS policy as <see cref="ApiBaseUrl"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The variable defines a non-<c>https://</c> endpoint without the development opt-in.</exception>
    public static string WebAuthUrl
    {
        get => _webAuthUrl ??= ReadEndpointUrl("NODEAEC_AUTH_URL", "https://nodeaec.com.br/auth/desktop");
        set => _webAuthUrl = value;
    }

    /// <summary>
    /// Product catalog page URL (<c>NODEAEC_CATALOG_URL</c>).
    /// Same HTTPS policy as <see cref="ApiBaseUrl"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The variable defines a non-<c>https://</c> endpoint without the development opt-in.</exception>
    public static string CatalogUrl
    {
        get => _catalogUrl ??= ReadEndpointUrl("NODEAEC_CATALOG_URL", "https://nodeaec.com.br/products");
        set => _catalogUrl = value;
    }

    /// <summary>
    /// Reads the environment variable and delegates HTTPS validation to
    /// <see cref="ValidateEndpointUrl(string, string?, bool, string)"/>.
    /// </summary>
    /// <param name="variable">Environment variable name for the endpoint.</param>
    /// <param name="fallback">Default value when the variable is not set.</param>
    /// <returns>The configured https URL or the default.</returns>
    /// <exception cref="InvalidOperationException">Configured endpoint is not https without the opt-in.</exception>
    private static string ReadEndpointUrl(string variable, string fallback)
    {
        string? raw = Environment.GetEnvironmentVariable(variable);
        bool allowInsecure = string.Equals(
            Environment.GetEnvironmentVariable(AllowInsecureVariable),
            "1",
            StringComparison.Ordinal);

        return ValidateEndpointUrl(variable, raw, allowInsecure, fallback);
    }

    /// <summary>
    /// Validates a configurable endpoint (M2): empty falls back to the default; <c>https://</c>
    /// is always accepted; <c>http://</c> only with <paramref name="allowInsecure"/>; anything else
    /// (unknown scheme or malformed URL) is rejected. Rejection logs one fixed line
    /// (variable name, never the value — it could contain credentials) and throws
    /// with clear remediation guidance.
    /// </summary>
    /// <param name="variable">Variable name (for the error message).</param>
    /// <param name="rawValue">Raw configured value; null/empty uses <paramref name="fallback"/>.</param>
    /// <param name="allowInsecure">The <c>NODEAEC_ALLOW_INSECURE_DEV=1</c> opt-in.</param>
    /// <param name="fallback">Safe default value.</param>
    /// <returns>Validated endpoint (trimmed).</returns>
    /// <exception cref="InvalidOperationException">The value is not an acceptable endpoint.</exception>
    internal static string ValidateEndpointUrl(string variable, string? rawValue, bool allowInsecure, string fallback)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        string trimmed = rawValue!.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttps
                || (allowInsecure && uri.Scheme == Uri.UriSchemeHttp)))
        {
            return trimmed;
        }

        // Fixed, sanitized log: variable name only — the value may contain userinfo/credentials.
        Diagnostics.ConnectorLog.Write(
            "ERROR",
            $"{variable}: endpoint rejeitado (exige https://; http:// requer NODEAEC_ALLOW_INSECURE_DEV=1).");

        throw new InvalidOperationException(
            $"{variable} precisa apontar para uma URL https://. " +
            "Para desenvolvimento local sem TLS defina NODEAEC_ALLOW_INSECURE_DEV=1.");
    }
}
