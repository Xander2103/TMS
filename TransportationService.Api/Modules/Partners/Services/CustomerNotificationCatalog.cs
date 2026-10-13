using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Partners.Services;

/// <summary>The three business groups a normal user thinks in.</summary>
public enum CustomerNotificationGroup
{
    Transport,
    Facturatie,
    Algemeen,
}

/// <summary>
/// One choice on the contact form ("Ontvangt meldingen"), mapped onto the underlying
/// <see cref="CustomerCommunicationType"/> values.
/// </summary>
/// <param name="Key">Stable identifier used by the API and the UI translation keys.</param>
/// <param name="Types">
/// The routing types this choice covers. Several options cover two types because the business
/// asks one question ("ETA / vertraging") where the engine has always distinguished two events;
/// mapping instead of adding near-duplicate types keeps existing rules working.
/// </param>
public record CustomerNotificationOption(string Key, CustomerNotificationGroup Group, IReadOnlyList<CustomerCommunicationType> Types);

/// <summary>
/// Sprint 3: the simple, contact-centric vocabulary layered ON TOP of the existing
/// communication-rule engine. Normal users answer "who receives what?"; the routing rules,
/// CC addresses and fallback contacts stay exactly as they are underneath and remain editable
/// on the advanced screen.
/// </summary>
public static class CustomerNotificationCatalog
{
    public static readonly IReadOnlyList<CustomerNotificationOption> Options =
    [
        new("order-confirmation", CustomerNotificationGroup.Transport, [CustomerCommunicationType.OrderConfirmation]),
        new("planning", CustomerNotificationGroup.Transport, [CustomerCommunicationType.PlanningAlert, CustomerCommunicationType.DeliveryChange]),
        new("eta", CustomerNotificationGroup.Transport, [CustomerCommunicationType.EtaUpdate, CustomerCommunicationType.DelayNotification]),
        new("delivery-pod", CustomerNotificationGroup.Transport, [CustomerCommunicationType.ProofOfDelivery]),
        new("delivery-problem", CustomerNotificationGroup.Transport, [CustomerCommunicationType.Claims]),
        new("redelivery", CustomerNotificationGroup.Transport, [CustomerCommunicationType.Redelivery]),

        new("invoice", CustomerNotificationGroup.Facturatie, [CustomerCommunicationType.Invoice]),
        new("credit-note", CustomerNotificationGroup.Facturatie, [CustomerCommunicationType.CreditNote]),
        new("invoice-reminder", CustomerNotificationGroup.Facturatie, [CustomerCommunicationType.InvoiceReminder]),

        new("general", CustomerNotificationGroup.Algemeen, [CustomerCommunicationType.GeneralReminder]),
    ];

    private static readonly Dictionary<string, CustomerNotificationOption> ByKey =
        Options.ToDictionary(o => o.Key, StringComparer.OrdinalIgnoreCase);

    public static CustomerNotificationOption? Find(string key) => ByKey.GetValueOrDefault(key);

    /// <summary>Every routing type the simple layer knows about.</summary>
    public static readonly IReadOnlySet<CustomerCommunicationType> CoveredTypes =
        Options.SelectMany(o => o.Types).ToHashSet();

    /// <summary>
    /// Types that exist but are deliberately NOT offered on the contact form — legacy or
    /// tenant-specific routing. They stay visible and editable on the advanced screen, so no
    /// existing configuration is lost or silently reinterpreted.
    /// </summary>
    public static IEnumerable<CustomerCommunicationType> AdvancedOnlyTypes =>
        Enum.GetValues<CustomerCommunicationType>().Where(t => !CoveredTypes.Contains(t));

    /// <summary>The option a routing type belongs to, if the simple layer covers it.</summary>
    public static CustomerNotificationOption? OptionFor(CustomerCommunicationType type) =>
        Options.FirstOrDefault(o => o.Types.Contains(type));
}
