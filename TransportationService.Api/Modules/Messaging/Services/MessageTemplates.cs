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
