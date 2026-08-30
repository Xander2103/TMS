namespace TransportationService.Api.Modules.Identity.Services;

/// <summary>
/// THE single definition of "is this a customer-portal permission" (H-14). Three separate places
/// need the rule — the single-code evaluator, the bulk evaluator's SQL predicate and the
/// portal-role validation — and each of them used to spell it out again, which is exactly how a
/// security rule drifts.
///
/// The semantics are deliberately ORDINAL (case-sensitive), because that is what the database
/// predicate does: EF translates <c>Code.StartsWith(Prefix)</c> to a case-sensitive
/// <c>LIKE 'customer_portal.%'</c> on PostgreSQL, and an in-memory check that were more lenient
/// would classify a code as "portal" that the database calls internal. Every permission code in
/// the catalog is a lowercase constant, so this changes nothing today; it fails CLOSED (fewer
/// codes count as portal ⇒ more refusals for a customer-linked identity) if one ever is not.
/// </summary>
public static class PortalPermissionScope
{
    /// <summary>The customer_portal.* namespace. Use this constant in EF queries so the SQL
    /// predicate and <see cref="Covers"/> can never diverge.</summary>
    public const string Prefix = "customer_portal.";

    /// <summary>True when a permission code belongs to the customer-portal namespace.</summary>
    public static bool Covers(string? permissionCode) =>
        permissionCode is { } code && code.Trim().StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>True when EVERY code belongs to the customer-portal namespace (empty = vacuously true).</summary>
    public static bool CoversAll(IEnumerable<string> permissionCodes) => permissionCodes.All(Covers);
}
