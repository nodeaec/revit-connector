using NodeAec.Connector.Storage;

namespace NodeAec.Connector.Auth;

/// <summary>
/// Headless session check for command availability (e.g. the "Meus Plugins" button).
/// Reads only local storage, with no network calls, so it can run on the ribbon and in tests.
/// </summary>
public static class LoginRequirement
{
    /// <summary>
    /// Returns true when a saved session with an email exists (login already done).    /// </summary>
    public static bool IsLoggedIn()
    {
        return HasLoginEmail(LeaseStorage.LoadSession());
    }

    /// <summary>
    /// Decides whether a loaded session counts as signed in: it must exist and carry a
    /// non-empty email — a token without an email identifies nobody. Pure, for headless
    /// testing (the full path goes through DPAPI and cannot be seeded outside an interactive session).
    /// </summary>
    /// <param name="session">Loaded session, or null when no session is saved.</param>
    /// <returns><c>true</c> when a session with a filled-in email exists.</returns>
    internal static bool HasLoginEmail((string? Name, string? Email, string? Token)? session)
    {
        return session.HasValue && !string.IsNullOrWhiteSpace(session.Value.Email);
    }
}
