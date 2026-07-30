using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Peppol.Dtos;
using TransportationService.Api.Modules.Peppol.Services;

namespace TransportationService.Api.Modules.Peppol.Controllers;

/// <summary>
/// Provider callback endpoint. Anonymous by necessity (providers don't hold user tokens),
/// authenticated by a shared secret header instead: the request must carry
/// <c>X-Peppol-Webhook-Secret</c> matching configuration key <c>Peppol:Webhook:Secret</c>.
/// Without a configured secret the endpoint refuses everything — secure by default.
/// Payload amounts/statuses are never trusted blindly: processing validates and
/// deduplicates before persisting (see <see cref="PeppolWebhookService"/>).
/// </summary>
[ApiController]
[Route("api/peppol/webhook")]
public class PeppolWebhookController : ControllerBase
{
    public const string SecretHeaderName = "X-Peppol-Webhook-Secret";

    private readonly IPeppolWebhookService _webhookService;
    private readonly IConfiguration _configuration;

    public PeppolWebhookController(IPeppolWebhookService webhookService, IConfiguration configuration)
    {
        _webhookService = webhookService;
        _configuration = configuration;
    }

    [HttpPost("{providerKey}")]
    [AllowAnonymous]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Receive(
        string providerKey, PeppolWebhookRequest request, CancellationToken cancellationToken)
    {
        var configuredSecret = _configuration["Peppol:Webhook:Secret"];
        if (string.IsNullOrWhiteSpace(configuredSecret)
            || !Request.Headers.TryGetValue(SecretHeaderName, out var providedSecret)
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(configuredSecret),
                System.Text.Encoding.UTF8.GetBytes(providedSecret.ToString())))
        {
            return Unauthorized();
        }

        var outcome = await _webhookService.ProcessAsync(providerKey, request, cancellationToken);
        // Always 200 once authenticated and persisted/deduped — a non-2xx would make the
        // provider redeliver forever; the outcome body says what happened.
        return Ok(outcome);
    }
}
