namespace TransportationService.Api.Modules.Authentication;

/// <summary>Custom claim types issued in the access token.</summary>
public static class AppClaimTypes
{
    public const string TenantId = "tenant_id";
    public const string Permission = "permission";

    /// <summary>"true" when the user must change their (admin-set/temporary) password before use.</summary>
    public const string MustChangePassword = "must_change_password";

    /// <summary>Server-side session-revocation stamp; compared to the user's current stamp per request.</summary>
    public const string SecurityStamp = "security_stamp";
}
