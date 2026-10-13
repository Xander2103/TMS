using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Services;
using TransportationService.Api.Modules.Authentication;

namespace TransportationService.Api.Modules.Attendance.Controllers;

/// <summary>
/// Prikklok-endpoints. BEWUST [AllowAnonymous]: een kiosk is geen gebruikerssessie en
/// authenticeert per verzoek met de X-Kiosk-Device-header (deviceId + 256-bit secret,
/// server-side als hash bewaard, constant-time geverifieerd in KioskPunchService) —
/// het Peppol-webhookprecedent. Zonder geldig device-secret geeft geen enkel endpoint
/// data terug; met device-secret is de scope beperkt tot de identify/punch-flow van de
/// tenant van dat device. Zie Phase1ConfigAndAuthTests (allowlist) en
/// docs/attendance/security.md. Rate-limitpolicy "kiosk" per client-IP bovenop de
/// device-lockout met backoff.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingServiceCollectionExtensions.KioskPolicy)]
[Route("api/attendance/kiosk")]
public class KioskPunchController : ControllerBase
{
    public const string DeviceKeyHeader = "X-Kiosk-Device";

    private readonly IKioskPunchService _kioskPunchService;

    public KioskPunchController(IKioskPunchService kioskPunchService) => _kioskPunchService = kioskPunchService;

    /// <summary>Devicecheck bij het opstarten van het kioskscherm (naam/locatie, geen persoonsgegevens).</summary>
    [HttpGet("ping")]
    public async Task<ActionResult<KioskPingResult>> Ping(CancellationToken cancellationToken)
    {
        var result = await _kioskPunchService.PingAsync(DeviceKey(), cancellationToken);
        return result.Outcome == KioskOutcome.Success ? Ok(result) : Unauthorized(result);
    }

    public sealed record IdentifyRequest(string Pin);

    [HttpPost("identify")]
    public async Task<ActionResult<KioskIdentifyResult>> Identify(
        [FromBody] IdentifyRequest request, CancellationToken cancellationToken)
    {
        var result = await _kioskPunchService.IdentifyAsync(DeviceKey(), request.Pin, cancellationToken);
        return result.Outcome switch
        {
            KioskOutcome.Success => Ok(result),
            KioskOutcome.InvalidDevice => Unauthorized(result),
            _ => UnprocessableEntity(result),
        };
    }

    public sealed record PunchRequest(string InteractionToken, KioskPunchAction Action);

    [HttpPost("punch")]
    public async Task<ActionResult<KioskPunchResult>> Punch(
        [FromBody] PunchRequest request, CancellationToken cancellationToken)
    {
        var result = await _kioskPunchService.PunchAsync(
            DeviceKey(), request.InteractionToken, request.Action, cancellationToken);
        return result.Outcome switch
        {
            KioskOutcome.Success => Ok(result),
            KioskOutcome.InvalidDevice => Unauthorized(result),
            _ => UnprocessableEntity(result),
        };
    }

    private string? DeviceKey() =>
        Request.Headers.TryGetValue(DeviceKeyHeader, out var values) ? values.ToString() : null;
}
