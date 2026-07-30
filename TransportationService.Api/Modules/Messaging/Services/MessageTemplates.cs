using System.Text.RegularExpressions;
using TransportationService.Api.Modules.Messaging.Entities;

namespace TransportationService.Api.Modules.Messaging.Services;

/// <summary>Simple, dependency-free {{token}} rendering; unknown tokens stay visible for debugging.</summary>
public static partial class MessageTemplateRenderer
{
    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TokenPattern();

    public static string Render(string template, IReadOnlyDictionary<string, string> tokens) =>
        TokenPattern().Replace(template, match =>
            tokens.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

    /// <summary>Distinct {{token}} names referenced by a template, in first-seen order.</summary>
    public static IReadOnlyList<string> ExtractTokens(string? template) =>
        string.IsNullOrEmpty(template)
            ? []
            : TokenPattern().Matches(template).Select(m => m.Groups[1].Value).Distinct().ToList();
}

/// <summary>
/// Built-in Dutch templates per (kind, channel). Tenants override via MessageTemplate rows;
/// unknown languages fall back to these defaults so a message can always render.
/// </summary>
public static class BuiltInMessageTemplates
{
    public sealed record Template(string? Subject, string Body);

    private static readonly IReadOnlyDictionary<string, Template> Email = new Dictionary<string, Template>
    {
        [MessageKinds.OrderConfirmation] = new(
            "Bevestiging van uw transportopdracht {{orderNumber}}",
            "Beste {{customerName}},\n\nUw transportopdracht {{orderNumber}} is bevestigd.\n\nMet vriendelijke groeten,\n{{companyName}}"),
        [MessageKinds.TimeWindowConfirmation] = new(
            "Tijdvenster bevestigd voor {{orderNumber}}",
            "Beste {{customerName}},\n\nVoor opdracht {{orderNumber}} is het tijdvenster {{window}} bevestigd.\n\n{{companyName}}"),
        [MessageKinds.DriverEnRoute] = new(
            "Onze chauffeur is onderweg ({{orderNumber}})",
            "Beste {{customerName}},\n\nOnze chauffeur is onderweg voor opdracht {{orderNumber}}.\n\n{{companyName}}"),
        [MessageKinds.EtaUpdate] = new(
            "Nieuwe verwachte aankomsttijd voor {{orderNumber}}",
            "Beste {{customerName}},\n\nDe verwachte aankomst voor {{orderNumber}} is nu {{eta}}.\n\n{{companyName}}"),
        [MessageKinds.Delay] = new(
            "Vertraging voor {{orderNumber}}",
            "Beste {{customerName}},\n\nOpdracht {{orderNumber}} loopt vertraging op: {{reason}}.\n\n{{companyName}}"),
        [MessageKinds.DeliveryCompleted] = new(
            "Levering afgerond ({{orderNumber}})",
            "Beste {{customerName}},\n\nDe levering voor opdracht {{orderNumber}} is afgerond.\n\n{{companyName}}"),
        [MessageKinds.PodAvailable] = new(
            "Afleverbewijs beschikbaar voor {{orderNumber}}",
            "Beste {{customerName}},\n\nHet afleverbewijs voor opdracht {{orderNumber}} is beschikbaar.\n\n{{companyName}}"),
        [MessageKinds.LeaveSubmitted] = new(
            "Verlofaanvraag ontvangen",
            "Beste {{employeeName}},\n\nJe verlofaanvraag van {{period}} is ontvangen en wordt bekeken.\n\nHR"),
        [MessageKinds.LeaveApproved] = new(
            "Verlof goedgekeurd",
            "Beste {{employeeName}},\n\nJe verlof van {{period}} is goedgekeurd.\n\nHR"),
        [MessageKinds.LeaveRejected] = new(
            "Verlof afgewezen",
            "Beste {{employeeName}},\n\nJe verlofaanvraag van {{period}} is afgewezen. {{note}}\n\nHR"),
        [MessageKinds.PlanningChanged] = new(
            "Je planning is aangepast",
            "Beste {{employeeName}},\n\nJe planning is aangepast: {{details}}.\n\nPlanning"),
        [MessageKinds.QualificationExpiry] = new(
            "Kwalificatie vervalt binnenkort",
            "Beste {{employeeName}},\n\nJe kwalificatie {{qualification}} vervalt op {{expiryDate}}.\n\nHR"),

        // Notification-event kinds (corrections wave 4, phase 6) — order/invoice/leave events
        // most likely to reach a customer or employee inbox; the rest use the generic fallback.
        [MessageKinds.OrderAccepted] = new(
            "Uw opdracht {{orderNumber}} is geaccepteerd",
            "Beste {{customerName}},\n\nUw opdracht {{orderNumber}} ({{goodsDescription}}) is geaccepteerd en wordt ingepland.\n\nMet vriendelijke groeten,\n{{companyName}}"),
        [MessageKinds.OrderRejected] = new(
            "Uw opdracht {{orderNumber}} is geweigerd",
            "Beste {{customerName}},\n\nUw opdracht {{orderNumber}} ({{goodsDescription}}) kon helaas niet worden geaccepteerd. Neem contact met ons op voor meer informatie.\n\n{{companyName}}"),
        [MessageKinds.OrderDelayDetected] = new(
            "Vertraging bij opdracht {{orderNumber}}",
            "Beste {{customerName}},\n\nOpdracht {{orderNumber}} loopt vertraging op: {{reason}}.\n\n{{companyName}}"),
        [MessageKinds.OrderPodAvailable] = new(
            "Afleverbewijs beschikbaar voor {{orderNumber}}",
            "Beste {{customerName}},\n\nHet afleverbewijs voor opdracht {{orderNumber}} is beschikbaar.\n\n{{companyName}}"),
        [MessageKinds.InvoiceSent] = new(
            "Factuur {{invoiceNumber}}",
            "Beste {{customerName}},\n\nIn bijlage vindt u factuur {{invoiceNumber}}.\n\nMet vriendelijke groeten,\n{{companyName}}"),
        [MessageKinds.InvoiceCreditNote] = new(
            "Creditnota bij factuur {{invoiceNumber}}",
            "Beste {{customerName}},\n\nEr is een creditnota aangemaakt bij factuur {{invoiceNumber}}.\n\n{{companyName}}"),
        [MessageKinds.LeaveRequested] = new(
            "Verlofaanvraag ontvangen",
            "Beste {{employeeName}},\n\nJe verlofaanvraag van {{period}} is ontvangen en wordt bekeken.\n\nHR"),
        [MessageKinds.LeaveDecided] = new(
            "Verlofaanvraag {{decision}}",
            "Beste {{employeeName}},\n\nJe verlofaanvraag van {{period}} is {{decision}}. {{note}}\n\nHR"),
        [MessageKinds.PortalUserInvited] = new(
            "Uitnodiging voor het klantportaal van {{companyName}}",
            "Beste {{firstName}},\n\nU bent uitgenodigd voor het klantportaal van {{companyName}}. "
            + "Stel via onderstaande link uw wachtwoord in om te starten:\n\n{{activationLink}}\n\n"
            + "Deze link is 72 uur geldig.\n\nMet vriendelijke groeten,\n{{companyName}}"),
    };

    private static readonly IReadOnlyDictionary<string, Template> Sms = new Dictionary<string, Template>
    {
        [MessageKinds.DriverEnRoute] = new(null, "{{companyName}}: onze chauffeur is onderweg voor {{orderNumber}}."),
        [MessageKinds.EtaUpdate] = new(null, "{{companyName}}: nieuwe verwachte aankomst voor {{orderNumber}}: {{eta}}."),
        [MessageKinds.Delay] = new(null, "{{companyName}}: vertraging voor {{orderNumber}}: {{reason}}."),
        [MessageKinds.DeliveryCompleted] = new(null, "{{companyName}}: levering {{orderNumber}} afgerond."),
        [MessageKinds.PodAvailable] = new(null, "{{companyName}}: afleverbewijs voor {{orderNumber}} beschikbaar."),
        [MessageKinds.TimeWindowConfirmation] = new(null, "{{companyName}}: tijdvenster {{window}} bevestigd voor {{orderNumber}}."),
    };

    public static Template Resolve(string kind, MessageChannel channel)
    {
        var source = channel == MessageChannel.Email ? Email : Sms;
        if (source.TryGetValue(kind, out var template))
        {
            return template;
        }

        // Every kind must render on every channel; the generic fallback keeps the pipeline honest.
        return channel == MessageChannel.Email
            ? new Template($"Bericht van {{{{companyName}}}}", "Beste,\n\n{{details}}\n\n{{companyName}}")
            : new Template(null, "{{companyName}}: {{details}}");
    }
}
