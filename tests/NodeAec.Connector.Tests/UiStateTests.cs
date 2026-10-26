using System;
using System.Collections.Generic;
using System.Linq;
using NodeAec.Connector.Config;
using NodeAec.Connector.Models;
using NodeAec.Connector.Plugins;
using NodeAec.Connector.UI;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Mapeamentos de estado da janela (M7): ramos de conta, status de licença
/// (legível / sem prazo / expirado / vigente) e lista de plugins
/// (logged-out / vazio / ativos-primeiro) — a lógica de decisão usada por
/// <c>ConnectorWindow.RenderUiFromStorage</c> e <c>ConnectorWindow.RenderPlugins</c>.
/// </summary>
public class UiStateTests
{
    // ---------- Conta (RenderUiFromStorage) ----------

    [Fact]
    public void Account_WithFullName_ShowsNameEmailAndLogout()
    {
        var (title, hint, showLogin, showLogout) = UiState.Account("Pablo", "pablo@nodeaec.com.br");

        Assert.Equal("Pablo", title);
        Assert.Equal("pablo@nodeaec.com.br", hint);
        Assert.False(showLogin);
        Assert.True(showLogout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Account_WithoutName_ShowsTheEmailAsTitle(string? name)
    {
        var (title, hint, showLogin, showLogout) = UiState.Account(name, "pablo@nodeaec.com.br");

        Assert.Equal("pablo@nodeaec.com.br", title);
        Assert.Equal(string.Empty, hint);
        Assert.False(showLogin);
        Assert.True(showLogout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Account_MissingEmail_ShowsLogin(string? email)
    {
        var (title, hint, showLogin, showLogout) = UiState.Account("Pablo", email);

        Assert.Equal("Entre com sua conta Node.aec", title);
        Assert.Equal("Para liberar seus plugins neste computador. O navegador abre para você entrar com segurança.", hint);
        Assert.True(showLogin);
        Assert.False(showLogout);
    }

    [Theory]
    [InlineData("Pablo Werlang", null, "PW")]
    [InlineData("pablo souza werlang", null, "PW")]
    [InlineData("Pablo", null, "P")]
    [InlineData(null, "ana@nodeaec.com.br", "A")]
    [InlineData("   ", "", "?")]
    public void Initials_UseFirstAndLastWordOrTheEmail(string? name, string? email, string expected)
    {
        Assert.Equal(expected, UiState.Initials(name, email));
    }

    // ---------- Status da licença (RenderUiFromStorage) ----------

    [Fact]
    public void LicenseStatus_NoLeaseToken_IsNeutralNote()
    {
        var (text, tone) = UiState.LicenseStatus(null, payload: null);

        Assert.Equal("Nenhuma licença encontrada neste computador ainda.", text);
        Assert.Equal(LicenseStatusTone.Neutral, tone);
    }

    [Fact]
    public void LicenseStatus_UnreadablePayload_WarnsWithRefreshHint()
    {
        var (text, tone) = UiState.LicenseStatus("token-adulterado", payload: null);

        Assert.Equal("Não conseguimos ler as licenças salvas. Clique em Atualizar licenças.", text);
        Assert.Equal(LicenseStatusTone.Warning, tone);
    }

    [Fact]
    public void LicenseStatus_MissingExpiry_WarnsWithoutPrintingEpoch()
    {
        // M5: exp fora da faixa/ausente vira null — o texto não pode conter 01/01/1970.
        var payload = new MasterLeasePayload { Exp = 0 };

        var (text, tone) = UiState.LicenseStatus("qualquer-token", payload);

        Assert.Equal("Não foi possível ler o prazo das licenças salvas. Clique em Atualizar licenças.", text);
        Assert.Equal(LicenseStatusTone.Warning, tone);
        Assert.DoesNotContain("1970", text);
    }

    [Fact]
    public void LicenseStatus_Expired_ShowsSinceDateWithWarning()
    {
        var payload = new MasterLeasePayload { Exp = DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeSeconds() };

        var (text, tone) = UiState.LicenseStatus("qualquer-token", payload);

        string expectedDate = payload.ExpiresAt!.Value.ToString("dd/MM/yyyy");
        Assert.Contains($"desatualizadas desde {expectedDate}", text);
        Assert.Equal(LicenseStatusTone.Warning, tone);
    }

    [Fact]
    public void LicenseStatus_Valid_ShowsUntilDateWithOkTone()
    {
        var payload = new MasterLeasePayload { Exp = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds() };

        var (text, tone) = UiState.LicenseStatus("qualquer-token", payload);

        string expectedDate = payload.ExpiresAt!.Value.ToString("dd/MM/yyyy");
        Assert.Contains($"atualizadas até {expectedDate}", text);
        Assert.Equal(LicenseStatusTone.Ok, tone);
    }

    // ---------- Lista de plugins (RenderPlugins) ----------

    [Fact]
    public void PluginsBranch_LoggedOut_WinsEvenWithEntitlements()
    {
        Assert.Equal(PluginsView.LoggedOut, UiState.PluginsBranch(isLoggedIn: false, entitlementCount: 3));
    }

    [Fact]
    public void PluginsBranch_LoggedInWithoutEntitlements_IsEmpty()
    {
        Assert.Equal(PluginsView.Empty, UiState.PluginsBranch(isLoggedIn: true, entitlementCount: 0));
    }

    [Fact]
    public void PluginsBranch_LoggedInWithEntitlements_IsList()
    {
        Assert.Equal(PluginsView.List, UiState.PluginsBranch(isLoggedIn: true, entitlementCount: 1));
    }

    // ---------- Linhas de plugin (RenderPluginRows) ----------

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static EntitlementItem Plugin(string slug, string? expires = null, string status = "active", string? name = null) =>
        new() { Slug = slug, Name = name ?? slug, Status = status, ExpiresAtString = expires };

    private static List<PluginRow> Rows(params EntitlementItem[] items) =>
        PluginInventory.Build(items, installed: null, ConnectorConfig.ProductSlug).ToList();

    private static PluginRow InstalledOnly(string slug) =>
        new() { Slug = slug, Name = slug, State = InstallState.Installed, Installed = new InstalledBundle { ProductSlug = slug, Version = "1.0.0" } };

    [Theory]
    [InlineData("2026-12-24T00:00:00Z", "active", "Liberado até 24/12/2026", PluginTone.Ok)]
    [InlineData(null, "active", "Liberado, sem data para expirar", PluginTone.Ok)]
    [InlineData("2026-10-31T12:00:00Z", "active", "Vence em 5 dias", PluginTone.Expiring)]
    [InlineData("2026-10-27T12:00:00Z", "active", "Vence amanhã", PluginTone.Expiring)]
    [InlineData("2026-10-26T12:00:00Z", "active", "Vence hoje", PluginTone.Expiring)]
    [InlineData("2026-10-01T00:00:00Z", "active", "Expirado em 01/10/2026", PluginTone.Problem)]
    [InlineData("2026-12-24T00:00:00Z", "cancelled", "Cancelado", PluginTone.Problem)]
    [InlineData(null, "suspended", "Suspenso", PluginTone.Problem)]
    [InlineData(null, "something-new", "Inativo", PluginTone.Problem)]
    internal void PluginStatus_MapsDatesAndStatuses(string? expires, string status, string text, PluginTone tone)
    {
        var (actualText, actualTone) = UiState.PluginStatus(Plugin("p", expires, status), Now);

        Assert.Equal(text, actualText);
        Assert.Equal(tone, actualTone);
    }

    [Fact]
    public void PluginStatus_NotGranted_IsBlocked()
    {
        var item = Plugin("p");
        item.Granted = false;

        Assert.Equal(("Não liberado", PluginTone.Problem), UiState.PluginStatus(item, Now));
    }

    [Fact]
    public void PluginsAttentionFirst_PutsBlockedThenExpiringThenOk_PreservingOrderInsideEachGroup()
    {
        var ok1 = Plugin("ok-1", "2027-01-01T00:00:00Z");
        var expiring = Plugin("vence", "2026-10-30T00:00:00Z");
        var ok2 = Plugin("ok-2");
        var cancelled = Plugin("cancelado", status: "cancelled");
        var expired = Plugin("vencido", "2020-01-01T00:00:00Z");

        var rows = Rows(ok1, expiring, ok2, cancelled, expired);
        rows.Insert(0, InstalledOnly("so-instalado"));
        var ordered = UiState.PluginsAttentionFirst(rows, Now);

        Assert.Equal(new[] { "cancelado", "vencido", "vence", "ok-1", "ok-2", "so-instalado" }, ordered.Select(e => e.Slug).ToArray());
    }

    [Fact]
    public void PluginsAttentionFirst_SkipsNullEntriesWithoutThrowing()
    {
        var ordered = UiState.PluginsAttentionFirst(new[] { null!, Rows(Plugin("ativo"))[0], null! }, Now);

        Assert.Single(ordered);
        Assert.Equal("ativo", ordered[0].Slug);
    }

    [Fact]
    public void PluginsSummary_TenPlugins_CountsUsableExpiringAndBlocked()
    {
        var items = Enumerable.Range(1, 8).Select(i => Plugin($"p{i}", "2027-06-01T00:00:00Z")).ToList();
        items.Add(Plugin("vence", "2026-10-30T00:00:00Z"));
        items.Add(Plugin("vencido", "2026-01-01T00:00:00Z"));

        Assert.Equal("10 plugins · 9 liberados · 1 vence em breve · 1 bloqueado", UiState.PluginsSummary(Rows(items.ToArray()), Now));
    }

    [Fact]
    public void PluginsSummary_OnePluginOk()
    {
        Assert.Equal("1 plugin · 1 liberado", UiState.PluginsSummary(Rows(Plugin("p")), Now));
    }

    [Fact]
    public void PluginsSummary_AllBlocked_HasNoLiberadosPart()
    {
        var items = new[] { Plugin("a", status: "revoked"), Plugin("b", status: "cancelled") };

        Assert.Equal("2 plugins · 2 bloqueados", UiState.PluginsSummary(Rows(items), Now));
    }

    [Fact]
    public void PluginsSummary_CountsUpdatesAndInstalledWithoutLicense()
    {
        var rows = Rows(Plugin("a"), Plugin("b"));
        rows.Add(InstalledOnly("c"));

        Assert.Equal("2 plugins · 2 liberados · 1 com atualização · 1 instalado sem licença",
            UiState.PluginsSummary(rows, Now, updatesAvailable: 1));
    }

    [Fact]
    public void RowStatus_InstalledWithoutLicense_IsNotOwned()
    {
        Assert.Equal(("Sem licença nesta conta", PluginTone.NotOwned), UiState.RowStatus(InstalledOnly("x"), Now));
    }

    [Theory]
    [InlineData("integracao", new[] { "exemplo" })]
    [InlineData("REVIT", new[] { "exemplo", "revit-tools" })]
    [InlineData("tools", new[] { "revit-tools" })]
    [InlineData("  ", new[] { "exemplo", "revit-tools", "quantitativos" })]
    [InlineData("nada", new string[0])]
    public void FilterPlugins_MatchesNameOrSlugIgnoringCaseAndAccents(string query, string[] expectedSlugs)
    {
        var items = Rows(
            Plugin("exemplo", name: "Exemplo de Integração Revit"),
            Plugin("revit-tools", name: "Ferramentas"),
            Plugin("quantitativos", name: "Quantitativos"));

        Assert.Equal(expectedSlugs, UiState.FilterPlugins(items, query).Select(e => e.Slug).ToArray());
    }

    [Fact]
    public void PluginsToShow_HidesTheConnectorItself_KeepingOrder()
    {
        var connector = new EntitlementItem { Slug = "NodeAec-Connector" };
        var first = new EntitlementItem { Slug = "plugin-a" };
        var second = new EntitlementItem { Slug = "plugin-b" };

        IReadOnlyList<EntitlementItem> shown = UiState.PluginsToShow(new[] { first, connector, second });

        Assert.Equal(new[] { "plugin-a", "plugin-b" }, shown.Select(e => e.Slug));
    }

    [Fact]
    public void PluginsToShow_OnlyTheConnector_LeavesTheListEmpty()
    {
        var shown = UiState.PluginsToShow(new[] { new EntitlementItem { Slug = ConnectorConfig.ProductSlug } });

        Assert.Empty(shown);
        Assert.Equal(PluginsView.Empty, UiState.PluginsBranch(true, shown.Count));
    }

    [Fact]
    public void PluginsToShow_Null_IsEmpty()
    {
        Assert.Empty(UiState.PluginsToShow(null));
    }
}
