using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Peppol.Dtos;
using TransportationService.Api.Modules.Peppol.Services;

namespace TransportationService.Api.Modules.Peppol.Controllers;

/// <summary>
/// Provider callback endpoint. Anonymous by necessity (providers don't hold user tokens),
/// authenticated by <see cref="PeppolWebhookAuthenticator"/> instead: a constant-time shared
/// secret header as the baseline, an HMAC signature with replay window when configured, both
/// with zero-downtime secret rotation and optional per-provider scoping. Without configured
/// secret material the endpoint refuses everything — secure by default.
/// The body is read raw (the HMAC covers the exact bytes) and deserialized manually.
/// Payload amounts/statuses are never trusted blindly: processing validates and
/// deduplicates before persisting (see <see cref="PeppolWebhookService"/>).
/// </summary>
[ApiController]
[Route("api/peppol/webhook")]
public class PeppolWebhookController : ControllerBase
{
    public const string SecretHeaderName = PeppolWebhookAuthenticator.SecretHeaderName;

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly IPeppolWebhookService _webhookService;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;

    public PeppolWebhookController(
        IPeppolWebhookService webhookService, IConfiguration configuration, TimeProvider timeProvider)
    {
        _webhookService = webhookService;
        _configuration = configuration;
        _timeProvider = timeProvider;
    }

    [HttpPost("{providerKey}")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(
        TransportationService.Api.Modules.Authentication.RateLimitingServiceCollectionExtensions.WebhookPolicy)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Receive(string providerKey, CancellationToken cancellationToken)
    {
        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            rawBody = await reader.ReadToEndAsync(cancellationToken);
        }

        if (!PeppolWebhookAuthenticator.Authenticate(
                _configuration, providerKey, Request.Headers, rawBody, _timeProvider.GetUtcNow()))
        {
            return Unauthorized();
        }

        PeppolWebhookRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<PeppolWebhookRequest>(rawBody, WebJson);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.ProviderMessageId))
        {
            return BadRequest();
        }

        var outcome = await _webhookService.ProcessAsync(providerKey, request, cancellationToken);
        // Always 200 once authenticated and persisted/deduped — a non-2xx would make the
        // provider redeliver forever; the outcome body says what happened.
        return Ok(outcome);
    }
}
