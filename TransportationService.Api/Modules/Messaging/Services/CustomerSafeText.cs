namespace TransportationService.Api.Modules.Messaging.Services;

/// <summary>
/// Customer-facing fallbacks for sensitive events when the office has not (yet) written its own
/// customer message. Never the driver's raw report: the templates already state what happened, so
/// the fallback adds nothing internal (empty = the optional paragraph disappears).
/// </summary>
public static class CustomerSafeText
{
    public const string DamageRegistered = "";
    public const string FailedDelivery = "";
}
