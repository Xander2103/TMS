using System.Text.RegularExpressions;
using TransportationService.Api.Modules.Messaging.Entities;

namespace TransportationService.Api.Modules.Messaging.Services;

/// <summary>
/// Simple, dependency-free {{token}} rendering. Unknown tokens stay visible in the rendered text so
/// <see cref="Unresolved"/> can catch them: a message that still contains a {{placeholder}} is never
/// queued for sending (EX-02, see MessageOutboxService) — it is suppressed with the missing names.
/// </summary>
public static partial class MessageTemplateRenderer
{
    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"\{\{\s*[^{}]*?\s*\}\}")]
    private static partial Regex AnyPlaceholderPattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();

    public static string Render(string template, IReadOnlyDictionary<string, string> tokens) =>
        // An optional token rendered empty must not leave a hole of blank lines behind.
        BlankLines().Replace(
            TokenPattern().Replace(template, match =>
                tokens.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value),
            "\n\n");

    /// <summary>Every {{…}} left in rendered text (also malformed ones like "{{ details }}"), in first-seen order.</summary>
    public static IReadOnlyList<string> Unresolved(params string?[] rendered) =>
        rendered
            .Where(text => !string.IsNullOrEmpty(text))
            .SelectMany(text => AnyPlaceholderPattern().Matches(text!).Select(m => m.Value))
            .Distinct()
            .ToList();

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
        [MessageKinds.OrderInfoRequested] = new(
            "Extra informatie gevraagd voor {{orderNumber}}",
            "Beste {{customerName}},\n\nVoor opdracht {{orderNumber}} ({{goodsDescription}}) hebben wij extra informatie nodig:\n\n"
            + "\"{{reason}}\"\n\nGelieve te reageren via het klantportaal.\n\nMet vriendelijke groeten,\n{{companyName}}"),
        [MessageKinds.OrderDelayDetected] = new(
            "Vertraging bij opdracht {{orderNumber}}",
            "Beste {{customerName}},\n\nOpdracht {{orderNumber}} loopt vertraging op: {{reason}}.\n\n{{companyName}}"),
        [MessageKinds.OrderPodAvailable] = new(
            "Afleverbewijs beschikbaar voor {{orderNumber}}",
            "Beste {{customerName}},\n\nHet afleverbewijs voor opdracht {{orderNumber}} is beschikbaar.\n\n{{companyName}}"),
        // EX-02: the sensitive kinds had no template and fell back to the generic {{details}} body.
        // {{details}} carries the internal report for staff and the customer-facing wording (or
        // nothing) for the customer — see NotificationEventContext.CustomerTokens.
        [MessageKinds.OrderFailedDelivery] = new(
            "Levering van opdracht {{orderNumber}} niet uitgevoerd",
            "Beste {{customerName}},\n\nDe levering van opdracht {{orderNumber}} ({{goodsDescription}}) kon helaas niet worden uitgevoerd.\n\n"
            + "{{details}}\n\nWij nemen contact met u op om een nieuwe levering af te spreken.\n\nMet vriendelijke groeten,\n{{companyName}}"),
        [MessageKinds.OrderDamageRegistered] = new(
            "Schade vastgesteld bij opdracht {{orderNumber}}",
            "Beste {{customerName}},\n\nBij opdracht {{orderNumber}} ({{goodsDescription}}) werd schade vastgesteld.\n\n"
            + "{{details}}\n\nWij nemen contact met u op over de verdere afhandeling.\n\nMet vriendelijke groeten,\n{{companyName}}"),
        [MessageKinds.InvoiceSent] = new(
            "Factuur {{invoiceNumber}}",
            "Beste {{customerName}},\n\nFactuur {{invoiceNumber}} is opgemaakt. U vindt de factuur in het klantportaal; heeft u daar geen toegang, dan bezorgen wij ze u afzonderlijk.\n\nMet vriendelijke groeten,\n{{companyName}}"),
        [MessageKinds.InvoiceCreditNote] = new(
            "Creditnota bij factuur {{invoiceNumber}}",
            "Beste {{customerName}},\n\nEr is een creditnota aangemaakt bij factuur {{invoiceNumber}}.\n\n{{companyName}}"),
        [MessageKinds.LeaveRequested] = new(
            "Verlofaanvraag ontvangen",
            "Beste {{employeeName}},\n\nJe verlofaanvraag van {{period}} is ontvangen en wordt bekeken.\n\nHR"),
        [MessageKinds.LeaveDecided] = new(
            "Verlofaanvraag {{decision}}",
            "Beste {{employeeName}},\n\nJe verlofaanvraag van {{period}} is {{decision}}. {{note}}\n\nHR"),
        [MessageKinds.CustomerMessageReply] = new(
            "Nieuw bericht van {{companyName}}",
            "Beste {{customerName}},\n\nU heeft een nieuw bericht ontvangen via het klantportaal:\n\n\"{{preview}}\"\n\n"
            + "Log in op het klantportaal om te reageren.\n\nMet vriendelijke groeten,\n{{companyName}}"),
        [MessageKinds.EmployeeMessageReceived] = new(
            "Nieuw intern bericht: {{subject}}",
            "Beste {{employeeName}},\n\nJe hebt een nieuw intern bericht van {{senderName}}:\n\n\"{{preview}}\"\n\n"
            + "Meld je aan om het volledige bericht te lezen.\n\n{{companyName}}"),
        [MessageKinds.PortalMessagePublished] = new(
            "{{title}}",
            "Beste {{recipientName}},\n\n{{body}}\n\nMet vriendelijke groeten,\n{{companyName}}"),
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

    /// <summary>
    /// Wave 2 §3: built-in FR/EN content for the customer-facing invoice/ETA/delivery kinds.
    /// The DB-template resolution chain already handled language — only built-in content was
    /// missing. Kinds without a translation (internal/HR) fall back to Dutch; DE is deferred
    /// until a German-speaking customer exists (the chain falls back to nl).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Template> EmailFr = new Dictionary<string, Template>
    {
        [MessageKinds.OrderConfirmation] = new(
            "Confirmation de votre ordre de transport {{orderNumber}}",
            "Bonjour {{customerName}},\n\nVotre ordre de transport {{orderNumber}} est confirmé.\n\nCordialement,\n{{companyName}}"),
        [MessageKinds.TimeWindowConfirmation] = new(
            "Créneau horaire confirmé pour {{orderNumber}}",
            "Bonjour {{customerName}},\n\nPour l'ordre {{orderNumber}}, le créneau {{window}} est confirmé.\n\n{{companyName}}"),
        [MessageKinds.DriverEnRoute] = new(
            "Notre chauffeur est en route ({{orderNumber}})",
            "Bonjour {{customerName}},\n\nNotre chauffeur est en route pour l'ordre {{orderNumber}}.\n\n{{companyName}}"),
        [MessageKinds.EtaUpdate] = new(
            "Nouvelle heure d'arrivée prévue pour {{orderNumber}}",
            "Bonjour {{customerName}},\n\nL'arrivée prévue pour {{orderNumber}} est maintenant {{eta}}.\n\n{{companyName}}"),
        [MessageKinds.Delay] = new(
            "Retard pour {{orderNumber}}",
            "Bonjour {{customerName}},\n\nL'ordre {{orderNumber}} subit un retard : {{reason}}.\n\n{{companyName}}"),
        [MessageKinds.DeliveryCompleted] = new(
            "Livraison effectuée ({{orderNumber}})",
            "Bonjour {{customerName}},\n\nLa livraison de l'ordre {{orderNumber}} est effectuée.\n\n{{companyName}}"),
        [MessageKinds.PodAvailable] = new(
            "Preuve de livraison disponible pour {{orderNumber}}",
            "Bonjour {{customerName}},\n\nLa preuve de livraison de l'ordre {{orderNumber}} est disponible.\n\n{{companyName}}"),
        [MessageKinds.InvoiceSent] = new(
            "Facture {{invoiceNumber}} de {{companyName}}",
            "Bonjour {{customerName}},\n\nLa facture {{invoiceNumber}} a été émise. Vous la trouverez dans le portail client ; si vous n'y avez pas accès, nous vous la transmettrons séparément.\n\nCordialement,\n{{companyName}}"),
        [MessageKinds.InvoiceCreditNote] = new(
            "Note de crédit {{invoiceNumber}} de {{companyName}}",
            "Bonjour {{customerName}},\n\nLa note de crédit {{invoiceNumber}} a été émise. Vous la trouverez dans le portail client ; si vous n'y avez pas accès, nous vous la transmettrons séparément.\n\nCordialement,\n{{companyName}}"),
        // P8: the event-catalog order kinds a French-speaking customer actually receives.
        [MessageKinds.OrderAccepted] = new(
            "Votre ordre {{orderNumber}} est accepté",
            "Bonjour {{customerName}},\n\nVotre ordre {{orderNumber}} ({{goodsDescription}}) est accepté et sera planifié.\n\nCordialement,\n{{companyName}}"),
        [MessageKinds.OrderRejected] = new(
            "Votre ordre {{orderNumber}} est refusé",
            "Bonjour {{customerName}},\n\nVotre ordre {{orderNumber}} ({{goodsDescription}}) n'a malheureusement pas pu être accepté. Contactez-nous pour plus d'informations.\n\n{{companyName}}"),
        [MessageKinds.OrderInfoRequested] = new(
            "Informations complémentaires demandées pour {{orderNumber}}",
            "Bonjour {{customerName}},\n\nPour l'ordre {{orderNumber}} ({{goodsDescription}}), nous avons besoin d'informations complémentaires :\n\n"
            + "\"{{reason}}\"\n\nVeuillez répondre via le portail client.\n\nCordialement,\n{{companyName}}"),
        [MessageKinds.OrderFailedDelivery] = new(
            "Livraison de l'ordre {{orderNumber}} non effectuée",
            "Bonjour {{customerName}},\n\nLa livraison de l'ordre {{orderNumber}} ({{goodsDescription}}) n'a malheureusement pas pu être effectuée.\n\n"
            + "{{details}}\n\nNous vous contacterons pour convenir d'une nouvelle livraison.\n\nCordialement,\n{{companyName}}"),
        [MessageKinds.OrderDamageRegistered] = new(
            "Dommage constaté pour l'ordre {{orderNumber}}",
            "Bonjour {{customerName}},\n\nUn dommage a été constaté pour l'ordre {{orderNumber}} ({{goodsDescription}}).\n\n"
            + "{{details}}\n\nNous vous contacterons pour la suite du traitement.\n\nCordialement,\n{{companyName}}"),
    };

    private static readonly IReadOnlyDictionary<string, Template> EmailEn = new Dictionary<string, Template>
    {
        [MessageKinds.OrderConfirmation] = new(
            "Confirmation of your transport order {{orderNumber}}",
            "Dear {{customerName}},\n\nYour transport order {{orderNumber}} has been confirmed.\n\nKind regards,\n{{companyName}}"),
        [MessageKinds.TimeWindowConfirmation] = new(
            "Time window confirmed for {{orderNumber}}",
            "Dear {{customerName}},\n\nFor order {{orderNumber}} the time window {{window}} has been confirmed.\n\n{{companyName}}"),
        [MessageKinds.DriverEnRoute] = new(
            "Our driver is on the way ({{orderNumber}})",
            "Dear {{customerName}},\n\nOur driver is on the way for order {{orderNumber}}.\n\n{{companyName}}"),
        [MessageKinds.EtaUpdate] = new(
            "New expected arrival time for {{orderNumber}}",
            "Dear {{customerName}},\n\nThe expected arrival for {{orderNumber}} is now {{eta}}.\n\n{{companyName}}"),
        [MessageKinds.Delay] = new(
            "Delay for {{orderNumber}}",
            "Dear {{customerName}},\n\nOrder {{orderNumber}} is delayed: {{reason}}.\n\n{{companyName}}"),
        [MessageKinds.DeliveryCompleted] = new(
            "Delivery completed ({{orderNumber}})",
            "Dear {{customerName}},\n\nThe delivery for order {{orderNumber}} has been completed.\n\n{{companyName}}"),
        [MessageKinds.PodAvailable] = new(
            "Proof of delivery available for {{orderNumber}}",
            "Dear {{customerName}},\n\nThe proof of delivery for order {{orderNumber}} is available.\n\n{{companyName}}"),
        [MessageKinds.InvoiceSent] = new(
            "Invoice {{invoiceNumber}} from {{companyName}}",
            "Dear {{customerName}},\n\nInvoice {{invoiceNumber}} has been issued. You can find it in the customer portal; if you do not have access, we will send it to you separately.\n\nKind regards,\n{{companyName}}"),
        [MessageKinds.InvoiceCreditNote] = new(
            "Credit note {{invoiceNumber}} from {{companyName}}",
            "Dear {{customerName}},\n\nCredit note {{invoiceNumber}} has been issued. You can find it in the customer portal; if you do not have access, we will send it to you separately.\n\nKind regards,\n{{companyName}}"),
        // P8: the event-catalog order kinds an English-speaking customer actually receives.
        [MessageKinds.OrderAccepted] = new(
            "Your order {{orderNumber}} has been accepted",
            "Dear {{customerName}},\n\nYour order {{orderNumber}} ({{goodsDescription}}) has been accepted and will be planned.\n\nKind regards,\n{{companyName}}"),
        [MessageKinds.OrderRejected] = new(
            "Your order {{orderNumber}} was declined",
            "Dear {{customerName}},\n\nUnfortunately your order {{orderNumber}} ({{goodsDescription}}) could not be accepted. Please contact us for more information.\n\n{{companyName}}"),
        [MessageKinds.OrderInfoRequested] = new(
            "Additional information requested for {{orderNumber}}",
            "Dear {{customerName}},\n\nFor order {{orderNumber}} ({{goodsDescription}}) we need additional information:\n\n"
            + "\"{{reason}}\"\n\nPlease respond via the customer portal.\n\nKind regards,\n{{companyName}}"),
        [MessageKinds.OrderFailedDelivery] = new(
            "Delivery of order {{orderNumber}} could not be completed",
            "Dear {{customerName}},\n\nUnfortunately the delivery of order {{orderNumber}} ({{goodsDescription}}) could not be completed.\n\n"
            + "{{details}}\n\nWe will contact you to arrange a new delivery.\n\nKind regards,\n{{companyName}}"),
        [MessageKinds.OrderDamageRegistered] = new(
            "Damage recorded for order {{orderNumber}}",
            "Dear {{customerName}},\n\nDamage was recorded for order {{orderNumber}} ({{goodsDescription}}).\n\n"
            + "{{details}}\n\nWe will contact you about the further handling.\n\nKind regards,\n{{companyName}}"),
    };

    private static readonly IReadOnlyDictionary<string, Template> SmsFr = new Dictionary<string, Template>
    {
        [MessageKinds.DriverEnRoute] = new(null, "{{companyName}} : notre chauffeur est en route pour {{orderNumber}}."),
        [MessageKinds.EtaUpdate] = new(null, "{{companyName}} : nouvelle arrivée prévue pour {{orderNumber}} : {{eta}}."),
        [MessageKinds.Delay] = new(null, "{{companyName}} : retard pour {{orderNumber}} : {{reason}}."),
        [MessageKinds.DeliveryCompleted] = new(null, "{{companyName}} : livraison {{orderNumber}} effectuée."),
        [MessageKinds.PodAvailable] = new(null, "{{companyName}} : preuve de livraison pour {{orderNumber}} disponible."),
        [MessageKinds.TimeWindowConfirmation] = new(null, "{{companyName}} : créneau {{window}} confirmé pour {{orderNumber}}."),
    };

    private static readonly IReadOnlyDictionary<string, Template> SmsEn = new Dictionary<string, Template>
    {
        [MessageKinds.DriverEnRoute] = new(null, "{{companyName}}: our driver is on the way for {{orderNumber}}."),
        [MessageKinds.EtaUpdate] = new(null, "{{companyName}}: new expected arrival for {{orderNumber}}: {{eta}}."),
        [MessageKinds.Delay] = new(null, "{{companyName}}: delay for {{orderNumber}}: {{reason}}."),
        [MessageKinds.DeliveryCompleted] = new(null, "{{companyName}}: delivery {{orderNumber}} completed."),
        [MessageKinds.PodAvailable] = new(null, "{{companyName}}: proof of delivery for {{orderNumber}} available."),
        [MessageKinds.TimeWindowConfirmation] = new(null, "{{companyName}}: time window {{window}} confirmed for {{orderNumber}}."),
    };

    public static Template Resolve(string kind, MessageChannel channel, string? language = null)
    {
        // Translated built-ins first (fr/en, customer-facing kinds only), then the full Dutch
        // catalogue, then the generic fallback — every kind renders on every channel.
        var translated = (channel, language?.Trim().ToLowerInvariant()) switch
        {
            (MessageChannel.Email, "fr") => EmailFr,
            (MessageChannel.Email, "en") => EmailEn,
            (MessageChannel.Sms, "fr") => SmsFr,
            (MessageChannel.Sms, "en") => SmsEn,
            _ => null,
        };
        if (translated is not null && translated.TryGetValue(kind, out var translatedTemplate))
        {
            return translatedTemplate;
        }

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
