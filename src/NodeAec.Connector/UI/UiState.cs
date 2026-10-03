using System;
using System.Collections.Generic;
using System.Linq;
using NodeAec.Connector.Models;

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

/// <summary>Plugin list branch in <c>PluginsWindow.RenderPlugins</c>.</summary>
internal enum PluginsView
{
    /// <summary>No local session — sign-in call to action.</summary>
    LoggedOut,

    /// <summary>Active session but no linked products — empty state + catalog.</summary>
    Empty,

    /// <summary>Active session with products — render the cards (active first).</summary>
    List,
}

/// <summary>
/// Pure state → text/order/branch mappings used by
/// <c>ConnectorWindow.RenderUiFromStorage</c> and <c>PluginsWindow.RenderPlugins</c> (M7).
/// No WPF/Revit dependencies: stays linked into the test project and covers the
/// "logged-out / unreadable lease / expired / empty / active-first" branches.
/// </summary>
internal static class UiState
{
    /// <summary>"Logged out" branch text of the plugin list.</summary>
    internal const string LoggedOutPluginsText = "Entre com sua conta para ver seus plugins aqui.";

    /// <summary>Empty-state text of the plugin list.</summary>
    internal const string NoPluginsText = "Nenhum plugin vinculado à sua conta ainda.";

    /// <summary>
    /// Maps the local session to the account block of the window (title, hint, login/logout
    /// button visibility). A missing session or one without email counts as logged out.
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
            string loggedInEmail = email!;
            return string.IsNullOrWhiteSpace(name)
                ? ("Olá! Você está conectado como:", loggedInEmail, ShowLogin: false, ShowLogout: true)
                : ($"Olá, {name}! Você está conectado como:", loggedInEmail, ShowLogin: false, ShowLogout: true);
        }

        return ("Você ainda não entrou.",
                "Entre com sua conta para liberar seus plugins neste computador.",
                ShowLogin: true,
                ShowLogout: false);
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
            return ("Não conseguimos ler as licenças salvas. Tente atualizar.", LicenseStatusTone.Warning);
        }

        // Out-of-range/missing `exp` becomes null (M5): never print 01/01/1970.
        if (payload.ExpiresAt is not { } exp)
        {
            return ("Não foi possível ler o prazo das licenças salvas. Clique em atualizar.", LicenseStatusTone.Warning);
        }

        if (payload.IsExpired)
        {
            return ($"Suas licenças estão desatualizadas desde {exp:dd/MM/yyyy}. Conecte-se à internet e clique em atualizar.",
                    LicenseStatusTone.Warning);
        }

        return ($"Tudo certo — suas licenças estão atualizadas até {exp:dd/MM/yyyy}.", LicenseStatusTone.Ok);
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
    /// Sorts products for display: active grants first, keeping the relative order
    /// of each group (LINQ <c>OrderBy</c> is stable). Null entries from a malformed lease
    /// are dropped before sorting (defense — the issuer does not emit them).
    /// </summary>
    /// <param name="entitlements">Lease items, in token order.</param>
    /// <returns>Sorted copy (active first), with no nulls.</returns>
    internal static IReadOnlyList<EntitlementItem> PluginsActiveFirst(IEnumerable<EntitlementItem> entitlements)
    {
        return entitlements.Where(e => e != null).OrderBy(e => e.IsActive() ? 0 : 1).ToList();
    }
}
