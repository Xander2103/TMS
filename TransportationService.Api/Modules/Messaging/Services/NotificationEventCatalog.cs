using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Notifications.Entities;

namespace TransportationService.Api.Modules.Messaging.Services;

/// <summary>
/// Groups shown in the (Phase 7) admin UI; Dutch, matching the module vocabulary used elsewhere.
/// </summary>
public static class NotificationEventGroups
{
    public const string Orders = "Orders";
    public const string Facturatie = "Facturatie";
    public const string Personeel = "Personeel";
    public const string Vloot = "Vloot";
    public const string Portaal = "Portaal";
}

/// <summary>
/// One catalog entry describing an event: its Dutch label, admin-UI group, the placeholder
/// tokens its templates may use, sensible channel/recipient defaults (applied when a tenant has
/// no <see cref="NotificationRule"/> row yet) and the linked <see cref="MessageKinds"/> value.
/// <see cref="DefaultRecipients"/> doubles as "supported recipient types" for the admin UI (each
/// entry's Type) and as the concrete out-of-the-box routing (each entry's Value) — there is no
/// separate "recipient type catalog"; a tenant overrides by writing a NotificationRule row.
/// </summary>
public sealed record NotificationEventInfo(
    string EventKey,
    string Label,
    string Group,
    IReadOnlyList<string> AllowedTokens,
    bool DefaultInApp,
    bool DefaultEmail,
    IReadOnlyList<RecipientSpec> DefaultRecipients,
    string MessageKind,
    NotificationSeverity DefaultSeverity,
    /// <summary>True for the three Peppol events: cataloged now, wired in Phase 12/13.</summary>
    bool PeppolPending = false);

public static class NotificationEventCatalog
{
    private static readonly string[] OrderTokens = ["orderNumber", "customerName", "goodsDescription"];

    private static readonly IReadOnlyDictionary<string, NotificationEventInfo> Entries =
        new List<NotificationEventInfo>
        {
            // --- Orders ---
            new(MessageKinds.OrderCreated, "Opdracht aangemaakt", NotificationEventGroups.Orders,
                OrderTokens, DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.OrdersChangeStatus)],
                MessageKinds.OrderCreated, NotificationSeverity.Info),
            new(MessageKinds.OrderSubmittedPortal, "Opdracht ingediend via klantportaal", NotificationEventGroups.Orders,
                OrderTokens, DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.OrdersChangeStatus)],
                MessageKinds.OrderSubmittedPortal, NotificationSeverity.Info),
            new(MessageKinds.OrderAccepted, "Opdracht geaccepteerd", NotificationEventGroups.Orders,
                OrderTokens, DefaultInApp: false, DefaultEmail: true,
                [new RecipientSpec(NotificationRecipientType.CustomerPrimaryContact, null)],
                MessageKinds.OrderAccepted, NotificationSeverity.Info),
            new(MessageKinds.OrderRejected, "Opdracht geweigerd", NotificationEventGroups.Orders,
                OrderTokens, DefaultInApp: false, DefaultEmail: true,
                [new RecipientSpec(NotificationRecipientType.CustomerPrimaryContact, null)],
                MessageKinds.OrderRejected, NotificationSeverity.Warning),
            new(MessageKinds.OrderInfoRequested, "Extra informatie gevraagd", NotificationEventGroups.Orders,
                [.. OrderTokens, "reason"], DefaultInApp: false, DefaultEmail: true,
                [new RecipientSpec(NotificationRecipientType.CustomerPrimaryContact, null)],
                MessageKinds.OrderInfoRequested, NotificationSeverity.Warning),
            new(MessageKinds.OrderPlanned, "Opdracht ingepland", NotificationEventGroups.Orders,
                OrderTokens, DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.Driver, null)],
                MessageKinds.OrderPlanned, NotificationSeverity.Info),
            new(MessageKinds.OrderPickupWindow, "Ophaalvenster bevestigd", NotificationEventGroups.Orders,
                OrderTokens, DefaultInApp: false, DefaultEmail: true,
                [new RecipientSpec(NotificationRecipientType.CustomerPrimaryContact, null)],
                MessageKinds.OrderPickupWindow, NotificationSeverity.Info),
            new(MessageKinds.OrderDeliveryWindow, "Leveringsvenster bevestigd", NotificationEventGroups.Orders,
                OrderTokens, DefaultInApp: false, DefaultEmail: true,
                [new RecipientSpec(NotificationRecipientType.CustomerPrimaryContact, null)],
                MessageKinds.OrderDeliveryWindow, NotificationSeverity.Info),
            new(MessageKinds.OrderPickupCompleted, "Ophaling afgerond", NotificationEventGroups.Orders,
                OrderTokens, DefaultInApp: false, DefaultEmail: true,
                [new RecipientSpec(NotificationRecipientType.CustomerPrimaryContact, null)],
                MessageKinds.OrderPickupCompleted, NotificationSeverity.Info),
            new(MessageKinds.OrderDeliveryCompleted, "Levering afgerond", NotificationEventGroups.Orders,
                OrderTokens, DefaultInApp: false, DefaultEmail: true,
                [new RecipientSpec(NotificationRecipientType.CustomerPrimaryContact, null)],
                MessageKinds.OrderDeliveryCompleted, NotificationSeverity.Info),
            new(MessageKinds.OrderDelayDetected, "Vertraging vastgesteld", NotificationEventGroups.Orders,
                [.. OrderTokens, "reason"], DefaultInApp: false, DefaultEmail: true,
                [new RecipientSpec(NotificationRecipientType.CustomerPrimaryContact, null)],
                MessageKinds.OrderDelayDetected, NotificationSeverity.Warning),
            new(MessageKinds.OrderFailedDelivery, "Levering mislukt", NotificationEventGroups.Orders,
                [.. OrderTokens, "reason"], DefaultInApp: true, DefaultEmail: true,
                [
                    new RecipientSpec(NotificationRecipientType.CustomerPrimaryContact, null),
                    new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.ExceptionsResolve),
                ],
                MessageKinds.OrderFailedDelivery, NotificationSeverity.Warning),
            new(MessageKinds.OrderDamageRegistered, "Schade geregistreerd bij opdracht", NotificationEventGroups.Orders,
                [.. OrderTokens, "reason"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.ExceptionsResolve)],
                MessageKinds.OrderDamageRegistered, NotificationSeverity.Warning),
            new(MessageKinds.OrderPodAvailable, "Afleverbewijs beschikbaar", NotificationEventGroups.Orders,
                OrderTokens, DefaultInApp: false, DefaultEmail: true,
                [new RecipientSpec(NotificationRecipientType.CustomerPrimaryContact, null)],
                MessageKinds.OrderPodAvailable, NotificationSeverity.Info),

            // --- Facturatie ---
            new(MessageKinds.InvoiceDraftReady, "Conceptfactuur klaar", NotificationEventGroups.Facturatie,
                ["invoiceNumber", "customerName"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.InvoicesView)],
                MessageKinds.InvoiceDraftReady, NotificationSeverity.Info),
            new(MessageKinds.InvoiceSent, "Factuur verzonden", NotificationEventGroups.Facturatie,
                ["invoiceNumber", "customerName"], DefaultInApp: false, DefaultEmail: true,
                [new RecipientSpec(NotificationRecipientType.CustomerCommunicationRule, "Invoice")],
                MessageKinds.InvoiceSent, NotificationSeverity.Info),
            new(MessageKinds.InvoicePeppolQueued, "Peppol-factuur in wachtrij", NotificationEventGroups.Facturatie,
                ["invoiceNumber", "customerName"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.InvoicesView)],
                MessageKinds.InvoicePeppolQueued, NotificationSeverity.Info, PeppolPending: true),
            new(MessageKinds.InvoicePeppolDelivered, "Peppol-factuur afgeleverd", NotificationEventGroups.Facturatie,
                ["invoiceNumber", "customerName"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.InvoicesView)],
                MessageKinds.InvoicePeppolDelivered, NotificationSeverity.Info, PeppolPending: true),
            new(MessageKinds.InvoicePeppolFailed, "Peppol-factuur mislukt", NotificationEventGroups.Facturatie,
                ["invoiceNumber", "customerName", "reason"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.InvoicesView)],
                MessageKinds.InvoicePeppolFailed, NotificationSeverity.Warning, PeppolPending: true),
            new(MessageKinds.InvoiceCreditNote, "Creditnota aangemaakt", NotificationEventGroups.Facturatie,
                ["invoiceNumber", "customerName"], DefaultInApp: false, DefaultEmail: true,
                [new RecipientSpec(NotificationRecipientType.CustomerCommunicationRule, "Invoice")],
                MessageKinds.InvoiceCreditNote, NotificationSeverity.Info),

            // --- Personeel ---
            new(MessageKinds.PersonnelQualificationExpiry, "Kwalificatie vervalt binnenkort", NotificationEventGroups.Personeel,
                ["employeeName", "qualification", "expiryDate"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.Driver, null)],
                MessageKinds.PersonnelQualificationExpiry, NotificationSeverity.Warning),
            new(MessageKinds.PersonnelMedicalExpiry, "Medische keuring vervalt binnenkort", NotificationEventGroups.Personeel,
                ["employeeName", "qualification", "expiryDate"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.Driver, null)],
                MessageKinds.PersonnelMedicalExpiry, NotificationSeverity.Warning),
            new(MessageKinds.PersonnelDocumentExpiry, "Persoonsdocument vervalt binnenkort", NotificationEventGroups.Personeel,
                ["employeeName", "documentType", "expiryDate"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.Driver, null)],
                MessageKinds.PersonnelDocumentExpiry, NotificationSeverity.Warning),
            new(MessageKinds.LeaveRequested, "Verlofaanvraag ontvangen", NotificationEventGroups.Personeel,
                ["employeeName", "period"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.AbsencesApprove)],
                MessageKinds.LeaveRequested, NotificationSeverity.Info),
            new(MessageKinds.LeaveDecided, "Verlofaanvraag beslist", NotificationEventGroups.Personeel,
                ["employeeName", "period", "note", "decision"], DefaultInApp: true, DefaultEmail: true,
                [new RecipientSpec(NotificationRecipientType.Driver, null)],
                MessageKinds.LeaveDecided, NotificationSeverity.Info),
            new(MessageKinds.EmployeeNotePinned, "Notitie vastgepind aan dashboard", NotificationEventGroups.Personeel,
                ["employeeName"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.EmployeeNotesView)],
                MessageKinds.EmployeeNotePinned, NotificationSeverity.Info),

            // --- Vloot ---
            new(MessageKinds.FleetMaintenanceDue, "Onderhoud binnenkort verschuldigd", NotificationEventGroups.Vloot,
                ["vehicle", "dueDate"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.MaintenancePoliciesView)],
                MessageKinds.FleetMaintenanceDue, NotificationSeverity.Warning),
            new(MessageKinds.FleetInspectionDue, "Keuring binnenkort verschuldigd", NotificationEventGroups.Vloot,
                ["vehicle", "dueDate"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.MaintenancePoliciesView)],
                MessageKinds.FleetInspectionDue, NotificationSeverity.Warning),
            new(MessageKinds.FleetDocumentExpiry, "Vlootdocument vervalt binnenkort", NotificationEventGroups.Vloot,
                ["target", "documentType", "expiryDate"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.FleetDocumentsView)],
                MessageKinds.FleetDocumentExpiry, NotificationSeverity.Warning),
            new(MessageKinds.FleetDamageCreated, "Schadegeval geregistreerd", NotificationEventGroups.Vloot,
                ["vehicle", "description"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.MaintenancePoliciesView)],
                MessageKinds.FleetDamageCreated, NotificationSeverity.Warning),

            // --- Portaal ---
            // Delivered by a DIRECT IMessageOutboxService.QueueAsync call in
            // CustomerPortalUserService (see MessageKinds.PortalUserInvited) — the invited
            // address is intrinsic to the invite action, not a resolvable RecipientSpec type.
            // Cataloged anyway so the admin UI can show/disable it like any other event; the
            // DefaultRecipients entry below is informational only and is never consulted.
            new(MessageKinds.PortalUserInvited, "Klantportaalgebruiker uitgenodigd", NotificationEventGroups.Portaal,
                ["firstName", "companyName", "activationLink"], DefaultInApp: false, DefaultEmail: true,
                [new RecipientSpec(NotificationRecipientType.ExplicitEmail, null)],
                MessageKinds.PortalUserInvited, NotificationSeverity.Info),

            // Corrections wave 4, phase 9: customer portal messages thread. Two directions —
            // customer -> staff (in-app only, no e-mail: staff already live in the app) and
            // staff -> customer (e-mail only: the customer's only channel is their inbox/portal).
            new(MessageKinds.CustomerMessageReceived, "Bericht van klant ontvangen", NotificationEventGroups.Portaal,
                ["customerName", "preview"], DefaultInApp: true, DefaultEmail: false,
                [new RecipientSpec(NotificationRecipientType.InternalPermission, PermissionCodes.CustomerMessagesView)],
                MessageKinds.CustomerMessageReceived, NotificationSeverity.Info),
            new(MessageKinds.CustomerMessageReply, "Antwoord op klantbericht", NotificationEventGroups.Portaal,
                ["customerName", "preview"], DefaultInApp: false, DefaultEmail: true,
                [new RecipientSpec(NotificationRecipientType.CustomerPrimaryContact, null)],
                MessageKinds.CustomerMessageReply, NotificationSeverity.Info),
        }.ToDictionary(e => e.EventKey, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<NotificationEventInfo> All => (IReadOnlyCollection<NotificationEventInfo>)Entries.Values;

    public static NotificationEventInfo? Resolve(string eventKey) =>
        Entries.GetValueOrDefault(eventKey);
}
