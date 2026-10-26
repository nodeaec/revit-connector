using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NodeAec.Connector.Config;
using NodeAec.Connector.Models;
using NodeAec.Connector.Plugins;

namespace NodeAec.Connector.UI;

/// <summary>License status text tone; the window maps it to UiTheme brushes.</summary>
internal enum LicenseStatusTone
{
    /// <summary>No local lease — neutral (secondary) text.</summary>
    Neutral,

    /// <summary>Degraded/unreadable state — attention highlight.</summary>
    Warning,

    /// <summary>Valid lease within term — success color.</summary>
    Ok,
}

/// <summary>Tone of one plugin row's status label; the window maps it to UiTheme brushes.</summary>
internal enum PluginTone
{
    /// <summary>Blocked: expired, cancelled, suspended, or not granted.</summary>
    Problem,

    /// <summary>Active, but the license ends within <see cref="UiState.ExpiringSoonDays"/> days.</summary>
    Expiring,

    /// <summary>Active with time to spare, or with no end date.</summary>
    Ok,

    /// <summary>Installed on this computer, but not a product of this account.</summary>
    NotOwned,
}

/// <summary>Plugin list branch in <c>ConnectorWindow.RenderPlugins</c>.</summary>
internal enum PluginsView
{
    /// <summary>No local session — sign-in call to action.</summary>
    LoggedOut,

    /// <summary>Active session but no linked products — empty state + catalog.</summary>
    Empty,

    /// <summary>Active session with rows to show (owned or installed) — render them (attention first).</summary>
    List,
}

/// <summary>
/// Pure state → text/order/branch mappings used by
/// <c>ConnectorWindow.RenderUiFromStorage</c> and <c>ConnectorWindow.RenderPlugins</c> (M7).
/// No WPF/Revit dependencies: stays linked into the test project and covers the
/// "logged-out / unreadable lease / expired / empty / active-first" branches.
/// </summary>
internal static class UiState
{
    /// <summary>"Logged out" branch text of the plugin list.</summary>
    internal const string LoggedOutPluginsText = "Entre com sua conta para ver seus plugins aqui.";

    /// <summary>Empty-state text of the plugin list.</summary>
    internal const string NoPluginsText = "Nenhum plugin vinculado à sua conta ainda.";

    /// <summary>Text when the filter matches no plugin.</summary>
    internal const string NoFilterMatchText = "Nenhum plugin com esse nome.";

    /// <summary>A license ending within this many days shows "Vence em N dias".</summary>
    internal const int ExpiringSoonDays = 14;

    /// <summary>From this many plugins on, the list shows a filter box.</summary>
    internal const int FilterThreshold = 12;

    /// <summary>
    /// Maps the local session to the account row: signed in, the title is the user's name
    /// (or the email when there is no name) and the hint is the email; signed out, a
    /// sign-in call to action. A missing session or one without email counts as logged out.
    /// </summary>
    /// <param name="name">Session user name (optional).</param>
    /// <param name="email">Session email; blank/null ⇒ logged out.</param>
    /// <returns>Title, hint, and button visibility.</returns>
    internal static (string Title, string Hint, bool ShowLogin, bool ShowLogout) Account(string? name, string? email)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            // Guaranteed by the test above: annotated (NotNullWhen) on .NET 8, an oblique
            // parameter on net48 — hence the explicit bang so Hint is never null.
            string loggedInEmail = email!.Trim();
            return string.IsNullOrWhiteSpace(name)
                ? (loggedInEmail, string.Empty, ShowLogin: false, ShowLogout: true)
                : (name!.Trim(), loggedInEmail, ShowLogin: false, ShowLogout: true);
        }

        return ("Entre com sua conta Node.aec",
                "Para liberar seus plugins neste computador. O navegador abre para você entrar com segurança.",
                ShowLogin: true,
                ShowLogout: false);
    }

    /// <summary>
    /// Up to two initials for the account badge: first and last word of the name, or the
    /// first letter of the email. "?" when neither has a letter or digit.
    /// </summary>
    /// <param name="name">Session user name (optional).</param>
    /// <param name="email">Session email (optional).</param>
    /// <returns>One or two upper-case characters.</returns>
    internal static string Initials(string? name, string? email)
    {
        string[] words = (name ?? string.Empty)
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]))
            .ToArray();
        if (words.Length == 1)
        {
            return words[0].Substring(0, 1).ToUpperInvariant();
        }

        if (words.Length > 1)
        {
            return (words[0].Substring(0, 1) + words[words.Length - 1].Substring(0, 1)).ToUpperInvariant();
        }

        string mail = (email ?? string.Empty).Trim();
        return mail.Length > 0 && char.IsLetterOrDigit(mail[0]) ? mail.Substring(0, 1).ToUpperInvariant() : "?";
    }

    /// <summary>
    /// Status label of one plugin row and its tone. Order of checks: not granted or a
    /// non-active status → expired → ends within <see cref="ExpiringSoonDays"/> → OK.
    /// </summary>
    /// <param name="item">Lease item.</param>
    /// <param name="now">Current time (a parameter so tests are deterministic).</param>
    /// <returns>Label text and tone.</returns>
    internal static (string Text, PluginTone Tone) PluginStatus(EntitlementItem item, DateTimeOffset now)
    {
        if (!item.Granted)
        {
            return ("Não liberado", PluginTone.Problem);
        }

        string status = (item.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (status != "active")
        {
            string label = status switch
            {
                "cancelled" or "canceled" => "Cancelado",
                "suspended" => "Suspenso",
                "revoked" => "Revogado",
                "expired" => "Expirado",
                _ => "Inativo",
            };
            return (label, PluginTone.Problem);
        }

        if (item.ExpiresAt is not { } expires)
        {
            return ("Liberado, sem data para expirar", PluginTone.Ok);
        }

        if (expires < now)
        {
            return ($"Expirado em {expires:dd/MM/yyyy}", PluginTone.Problem);
        }

        int daysLeft = (int)Math.Ceiling((expires - now).TotalDays);
        if (daysLeft <= ExpiringSoonDays)
        {
            string text = daysLeft <= 0 ? "Vence hoje"
                : daysLeft == 1 ? "Vence amanhã"
                : $"Vence em {daysLeft} dias";
            return (text, PluginTone.Expiring);
        }

        return ($"Liberado até {expires:dd/MM/yyyy}", PluginTone.Ok);
    }

    /// <summary>
    /// Status label and tone of a row: the license status for owned products,
    /// "Sem licença nesta conta" for a plugin installed here that the account does not own.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="now">Current time.</param>
    /// <returns>Label text and tone.</returns>
    internal static (string Text, PluginTone Tone) RowStatus(PluginRow row, DateTimeOffset now)
    {
        return row.Entitlement == null
            ? ("Sem licença nesta conta", PluginTone.NotOwned)
            : PluginStatus(row.Entitlement, now);
    }

    /// <summary>
    /// Sorts rows for display: blocked first, then expiring soon, then the rest, then plugins
    /// installed without a license, keeping the input order inside each group (LINQ
    /// <c>OrderBy</c> is stable). Null rows are dropped.
    /// </summary>
    /// <param name="rows">Rows from <see cref="PluginInventory.Build"/>.</param>
    /// <param name="now">Current time.</param>
    /// <returns>Sorted copy, with no nulls.</returns>
    internal static IReadOnlyList<PluginRow> PluginsAttentionFirst(IEnumerable<PluginRow> rows, DateTimeOffset now)
    {
        return rows.Where(r => r != null).OrderBy(r => (int)RowStatus(r, now).Tone).ToList();
    }

    /// <summary>
    /// One-line summary above the list, e.g. "10 plugins · 9 liberados · 1 vence em breve ·
    /// 1 bloqueado · 2 com atualização · 1 instalado sem licença". The counts before the
    /// updates are about owned products; "liberados" includes expiring ones.
    /// </summary>
    /// <param name="rows">Rows shown in the list.</param>
    /// <param name="now">Current time.</param>
    /// <param name="updatesAvailable">Rows that offer "Atualizar".</param>
    /// <returns>The summary text.</returns>
    internal static string PluginsSummary(IReadOnlyCollection<PluginRow> rows, DateTimeOffset now, int updatesAvailable = 0)
    {
        var tones = rows.Where(r => r != null).Select(r => RowStatus(r, now).Tone).ToList();
        int notOwned = tones.Count(t => t == PluginTone.NotOwned);
        int total = tones.Count - notOwned;
        int expiring = tones.Count(t => t == PluginTone.Expiring);
        int blocked = tones.Count(t => t == PluginTone.Problem);
        int usable = total - blocked;

        var parts = new List<string>();
        if (total > 0)
        {
            parts.Add(total == 1 ? "1 plugin" : $"{total} plugins");
            if (usable > 0) parts.Add(usable == 1 ? "1 liberado" : $"{usable} liberados");
            if (expiring > 0) parts.Add($"{expiring} vence{(expiring == 1 ? "" : "m")} em breve");
            if (blocked > 0) parts.Add(blocked == 1 ? "1 bloqueado" : $"{blocked} bloqueados");
        }

        if (updatesAvailable > 0) parts.Add($"{updatesAvailable} com atualização");
        if (notOwned > 0) parts.Add(notOwned == 1 ? "1 instalado sem licença" : $"{notOwned} instalados sem licença");
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Keeps the rows whose name or slug contains <paramref name="query"/>, ignoring case
    /// and accents ("integracao" finds "Integração"). A blank query keeps everything.
    /// </summary>
    /// <param name="rows">Rows in display order.</param>
    /// <param name="query">Text typed in the filter box.</param>
    /// <returns>The matching rows, in the same order.</returns>
    internal static IReadOnlyList<PluginRow> FilterPlugins(IEnumerable<PluginRow> rows, string? query)
    {
        string q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            return rows.ToList();
        }

        CompareInfo compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return rows
            .Where(r => compare.IndexOf(r.Name ?? string.Empty, q, options) >= 0 ||
                        compare.IndexOf(r.Slug ?? string.Empty, q, options) >= 0)
            .ToList();
    }

    /// <summary>
    /// Maps the local lease to the license status text and its tone.
    /// Branch order: no token → unreadable → missing/out-of-range <c>exp</c> (M5) →
    /// expired → all good. Never prints 01/01/1970 and never throws.
    /// </summary>
    /// <param name="leaseJwt">Master lease token loaded from disk (or null/empty).</param>
    /// <param name="payload">Decoded payload; null when the token could not be read.</param>
    /// <returns>Displayed text and color tone.</returns>
    internal static (string Text, LicenseStatusTone Tone) LicenseStatus(string? leaseJwt, MasterLeasePayload? payload)
    {
        if (string.IsNullOrWhiteSpace(leaseJwt))
        {
            return ("Nenhuma licença encontrada neste computador ainda.", LicenseStatusTone.Neutral);
        }

        if (payload == null)
        {
            return ("Não conseguimos ler as licenças salvas. Clique em Atualizar licenças.", LicenseStatusTone.Warning);
        }

        // Out-of-range/missing `exp` becomes null (M5): never print 01/01/1970.
        if (payload.ExpiresAt is not { } exp)
        {
            return ("Não foi possível ler o prazo das licenças salvas. Clique em Atualizar licenças.", LicenseStatusTone.Warning);
        }

        if (payload.IsExpired)
        {
            return ($"Licenças desatualizadas desde {exp:dd/MM/yyyy}. Conecte-se à internet e clique em Atualizar licenças.",
                    LicenseStatusTone.Warning);
        }

        return ($"Tudo certo — licenças atualizadas até {exp:dd/MM/yyyy}.", LicenseStatusTone.Ok);
    }

    /// <summary>
    /// Picks the plugin-list branch: no session wins over any present lease;
    /// with a session, the list is either empty or renderable.
    /// </summary>
    /// <param name="isLoggedIn">Result of <c>LoginRequirement.IsLoggedIn()</c>.</param>
    /// <param name="entitlementCount">Item count of the decoded lease.</param>
    /// <returns>Branch to render.</returns>
    internal static PluginsView PluginsBranch(bool isLoggedIn, int entitlementCount)
    {
        if (!isLoggedIn) return PluginsView.LoggedOut;
        return entitlementCount == 0 ? PluginsView.Empty : PluginsView.List;
    }

    /// <summary>
    /// Drops the Connector's own entitlement (<see cref="ConnectorConfig.ProductSlug"/>) from
    /// the plugin list: downloading the Connector, a free product, grants a license for it,
    /// but it is not a plugin the user manages here.
    /// </summary>
    /// <param name="entitlements">Lease items, in token order; <c>null</c> means none.</param>
    /// <returns>The items to show, in the same order.</returns>
    internal static IReadOnlyList<EntitlementItem> PluginsToShow(IEnumerable<EntitlementItem>? entitlements)
    {
        if (entitlements == null)
        {
            return Array.Empty<EntitlementItem>();
        }

        return entitlements
            .Where(e => e == null || !string.Equals(e.Slug, ConnectorConfig.ProductSlug, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
