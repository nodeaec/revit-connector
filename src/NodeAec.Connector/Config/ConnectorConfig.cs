using System;

namespace NodeAec.Connector.Config;

/// <summary>
/// Configuração global de endpoints, versão e chaves públicas da plataforma Node.aec.
/// Endpoints configuráveis por variável de ambiente só aceitam <c>https://</c> — transporte
/// em claro rebaixaria silenciosamente a licença, a busca da âncora de confiança (JWKS) e o
/// login no navegador (M2). Desenvolvimento local sem TLS exige o opt-in explícito
/// <c>NODEAEC_ALLOW_INSECURE_DEV=1</c>.
/// </summary>
public static class ConnectorConfig
{
    public const string Version = "0.1.2";

    /// <summary>
    /// Plataforma do host enviada à API (<c>platform</c>). Reflete o ano do Revit em que
    /// este assembly foi compilado: o Directory.Build.props define REVIT2023..REVIT2027 a
    /// partir de <c>-p:RevitYear</c>. Compilações sem o define (ex.: testes headless ou
    /// consumidores que recompilam este arquivo) caem no padrão 2026.
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

    /// <summary>Opt-in explícito que autoriza <c>http://</c> em endpoints configurados.</summary>
    private const string AllowInsecureVariable = "NODEAEC_ALLOW_INSECURE_DEV";

    private static string? _apiBaseUrl;
    private static string? _webAuthUrl;
    private static string? _catalogUrl;

    /// <summary>
    /// URL base da API REST do Node.aec (<c>NODEAEC_API_URL</c>).
    /// Resolução preguiçosa para que uma configuração insegura lance
    /// <see cref="InvalidOperationException"/> com mensagem clara no ponto de uso, e não um
    /// <see cref="TypeInitializationException"/> obscuro na carga do tipo.
    /// </summary>
    /// <exception cref="InvalidOperationException">A variável define um endpoint que não é <c>https://</c> sem o opt-in de desenvolvimento.</exception>
    public static string ApiBaseUrl
    {
        get => _apiBaseUrl ??= ReadEndpointUrl("NODEAEC_API_URL", "https://api.nodeaec.com.br");
        set => _apiBaseUrl = value;
    }

    /// <summary>
    /// URL da página de login SSO para desktop (OAuth loopback) (<c>NODEAEC_AUTH_URL</c>).
    /// Mesma política HTTPS de <see cref="ApiBaseUrl"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A variável define um endpoint que não é <c>https://</c> sem o opt-in de desenvolvimento.</exception>
    public static string WebAuthUrl
    {
        get => _webAuthUrl ??= ReadEndpointUrl("NODEAEC_AUTH_URL", "https://nodeaec.com.br/auth/desktop");
        set => _webAuthUrl = value;
    }

    /// <summary>
    /// URL da página de catálogo de produtos (<c>NODEAEC_CATALOG_URL</c>).
    /// Mesma política HTTPS de <see cref="ApiBaseUrl"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A variável define um endpoint que não é <c>https://</c> sem o opt-in de desenvolvimento.</exception>
    public static string CatalogUrl
    {
        get => _catalogUrl ??= ReadEndpointUrl("NODEAEC_CATALOG_URL", "https://nodeaec.com.br/products");
        set => _catalogUrl = value;
    }

    /// <summary>
    /// Chave pública SPKI (Ed25519, base64) compilada no add-in: o trust root da
    /// verificação offline de leases. A chave privada correspondente nunca sai do servidor
    /// e nenhuma outra chave é aceita, mesmo que apareça no JWKS em cache.
    /// </summary>
    public const string DefaultLicensePublicKeySpkiBase64 =
        "MCowBQYDK2VwAyEArMYcaZMAlBeimfR6twrHZndEWOSaIHlSURYFhTjalMg=";

    /// <summary>
    /// Substituição de operação da âncora compilada, definida em
    /// <c>NODEAEC_LICENSE_PUBLIC_KEY_SPKI</c> (base64 SPKI Ed25519).
    /// Quando presente tem precedência sobre <see cref="DefaultLicensePublicKeySpkiBase64"/>;
    /// quando inválida a verificação falha fechada — nunca cai silenciosamente para a
    /// chave compilada. Nunca há chave privada neste repositório.
    /// </summary>
    public static string? LicensePublicKeySpkiOverride =>
        Environment.GetEnvironmentVariable("NODEAEC_LICENSE_PUBLIC_KEY_SPKI");

    /// <summary>
    /// Lê a variável de ambiente e delega a validação HTTPS a
    /// <see cref="ValidateEndpointUrl(string, string?, bool, string)"/>.
    /// </summary>
    /// <param name="variable">Nome da variável de ambiente do endpoint.</param>
    /// <param name="fallback">Valor padrão quando a variável não está definida.</param>
    /// <returns>A URL https configurada ou o padrão.</returns>
    /// <exception cref="InvalidOperationException">Endpoint configurado não é https sem o opt-in.</exception>
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
    /// Valida um endpoint configurável (M2): vazio cai no padrão; <c>https://</c> é sempre
    /// aceito; <c>http://</c> só com <paramref name="allowInsecure"/>; qualquer outra coisa
    /// (esquema desconhecido ou URL malformada) é rejeitada. A rejeição registra uma linha
    /// fixa no log (nome da variável, nunca o valor — poderia conter credenciais) e lança
    /// com instrução clara de correção.
    /// </summary>
    /// <param name="variable">Nome da variável (para a mensagem de erro).</param>
    /// <param name="rawValue">Valor bruto configurado; nulo/vazio usa <paramref name="fallback"/>.</param>
    /// <param name="allowInsecure">Opt-in <c>NODEAEC_ALLOW_INSECURE_DEV=1</c>.</param>
    /// <param name="fallback">Valor padrão seguro.</param>
    /// <returns>Endpoint validado (sem espaços nas bordas).</returns>
    /// <exception cref="InvalidOperationException">O valor não é um endpoint aceitável.</exception>
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

        // Log fixo e sanitizado: só o nome da variável — o valor pode conter userinfo/credenciais.
        Diagnostics.ConnectorLog.Write(
            "ERROR",
            $"{variable}: endpoint rejeitado (exige https://; http:// requer NODEAEC_ALLOW_INSECURE_DEV=1).");

        throw new InvalidOperationException(
            $"{variable} precisa apontar para uma URL https://. " +
            "Para desenvolvimento local sem TLS defina NODEAEC_ALLOW_INSECURE_DEV=1.");
    }
}
