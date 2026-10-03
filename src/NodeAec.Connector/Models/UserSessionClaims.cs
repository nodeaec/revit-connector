using System.Text.Json.Serialization;

namespace NodeAec.Connector.Models;

/// <summary>
/// Identity claims of the user session token issued by the Node.aec
/// platform (id, email, name). The master lease carries no email/name — only the public
/// id in "sub" — so the identity shown in the UI must come from the
/// user token saved in the session.
/// </summary>
public class UserSessionClaims
{
    /// <summary>Public user id (14 Base62 characters).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Account email, used in the "Minha Conta" UI.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>User display name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
