using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// Follow-up wave P1+P2: decides which transport document (if any) an order needs, and why.
/// Precedence: explicit order preference → customer strategy → tenant document rules
/// (Priority order, first full match wins) → built-in reference defaults (ADR → CMR,
/// cross-border → CMR, otherwise delivery note). On-site work (D2, CraneJobKind.OnSiteLifting)
/// short-circuits the kind to a work order: nothing is carried, so a CMR is never REQUIRED. Static and side-effect free so every
/// caller (single render, trip batch, customer/day batch, UI hint) shares one truth.
/// </summary>
public static class DocumentStrategyResolver
{
    public const string KindDeliveryNote = "DeliveryNote";
    public const string KindCmr = "Cmr";
    public const string KindNone = "None";

    /// <summary>D2: werkbon — the own document of on-site work (site, work description, lift data, signatures).</summary>
    public const string KindWorkOrder = "WorkOrder";

    public sealed record Decision(
        /// <summary>Resolved own-document kind (DeliveryNote/Cmr/WorkOrder); null when no own document.</summary>
        string? Kind,
        bool UsesCustomerDocument,
        bool NoneRequired,
        /// <summary>Customer strategy is PerOrder and the order has not chosen yet.</summary>
        bool Undecided,
        /// <summary>OrderOverride | CustomerDefault | TenantRule | BuiltInDefault.</summary>
        string Source,
        string Reason,
        /// <summary>A user with order-manage rights may always override per order.</summary>
        bool OverrideAllowed = true)
    {
        /// <summary>True when our own document should be part of generated batches. An undecided
        /// order (customer strategy PerOrder without a choice) is "missing information", never
        /// silently printed.</summary>
        public bool GeneratesOwnDocument => Kind is not null && !UsesCustomerDocument && !NoneRequired && !Undecided;
    }

    public static Decision Resolve(
        string? orderPreference,
        string customerStrategy,
        bool crossBorder,
        bool adrRequired,
        Guid? activityTypeId,
        IReadOnlyList<TenantDocumentRule> rules,
        /// <summary>D2: the order is on-site work (CraneJobKind.OnSiteLifting).</summary>
        bool onSiteWork = false)
    {
        switch (orderPreference)
        {
            case "CustomerDocument":
                return new Decision(null, UsesCustomerDocument: true, NoneRequired: false, Undecided: false,
                    "OrderOverride", "Op deze opdracht is gekozen voor het document van de klant.");
            case "NoneRequired":
                return new Decision(null, UsesCustomerDocument: false, NoneRequired: true, Undecided: false,
                    "OrderOverride", "Op deze opdracht is aangegeven dat geen document nodig is.");
            case "Own":
            {
                var (kind, source, why) = ResolveKind(crossBorder, adrRequired, activityTypeId, rules, onSiteWork);
                return kind == KindNone
                    ? new Decision(null, false, NoneRequired: true, Undecided: false, source, why)
                    : new Decision(kind, false, false, false, "OrderOverride",
                        $"Op deze opdracht is gekozen voor een eigen document ({why}).");
            }
        }

        switch (customerStrategy)
        {
            case "CustomerDocument":
                return new Decision(null, UsesCustomerDocument: true, NoneRequired: false, Undecided: false,
                    "CustomerDefault", "Klantinstelling: de klant levert het transportdocument aan.");
            case "PerOrder":
            {
                var (kind, _, why) = ResolveKind(crossBorder, adrRequired, activityTypeId, rules, onSiteWork);
                return new Decision(kind == KindNone ? null : kind, false, kind == KindNone, Undecided: true,
                    "CustomerDefault", $"Klantinstelling: per opdracht beslissen — nog geen keuze gemaakt (voorstel: {why}).");
            }
            default:
            {
                var (kind, source, why) = ResolveKind(crossBorder, adrRequired, activityTypeId, rules, onSiteWork);
                return kind == KindNone
                    ? new Decision(null, false, NoneRequired: true, Undecided: false, source, why)
                    : new Decision(kind, false, false, false, source, why);
            }
        }
    }

    private static (string Kind, string Source, string Reason) ResolveKind(
        bool crossBorder, bool adrRequired, Guid? activityTypeId, IReadOnlyList<TenantDocumentRule> rules,
        bool onSiteWork)
    {
        // D2: on-site work carries no goods — the transport rules below (tenant rules, ADR and
        // cross-border → CMR) describe carriage and do not apply. Order preference and customer
        // strategy keep their precedence: they are evaluated by the caller, before this.
        if (onSiteWork)
        {
            return (KindWorkOrder, "BuiltInDefault", "Kraanwerk ter plaatse → werkbon (standaardregel).");
        }

        foreach (var rule in rules.Where(r => !r.IsDeleted).OrderBy(r => r.Priority).ThenBy(r => r.Id))
        {
            var matches =
                (rule.MatchCrossBorder is null || rule.MatchCrossBorder == crossBorder)
                && (rule.MatchAdr is null || rule.MatchAdr == adrRequired)
                && (rule.MatchActivityTypeId is null || rule.MatchActivityTypeId == activityTypeId);
            if (matches)
            {
                return (rule.DocumentKind, "TenantRule",
                    $"Documentregel {rule.Priority}: {KindLabel(rule.DocumentKind)}.");
            }
        }

        if (adrRequired)
        {
            return (KindCmr, "BuiltInDefault", "ADR-transport → CMR (standaardregel).");
        }

        if (crossBorder)
        {
            return (KindCmr, "BuiltInDefault", "Grensoverschrijdend transport → CMR (standaardregel).");
        }

        return (KindDeliveryNote, "BuiltInDefault", "Binnenlands transport → leveringsbon (standaardregel).");
    }

    private static string KindLabel(string kind) => kind switch
    {
        KindCmr => "CMR",
        KindWorkOrder => "werkbon",
        KindNone => "geen document",
        _ => "leveringsbon",
    };
}
