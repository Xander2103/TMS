using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Attendance.Security;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Attendance.Services;

public interface IAttendanceCredentialService
{
    Task<AttendanceCredentialStatusDto> GetStatusAsync(Guid employeeId, CancellationToken cancellationToken);

    /// <summary>PIN zetten (pin != null) of laten genereren (pin == null). De PIN wordt nooit opgeslagen of teruggelezen.</summary>
    Task<AttendanceCredentialResult> SetPinAsync(Guid employeeId, string? pin, CancellationToken cancellationToken);

    Task<AttendanceCredentialResult> DisableAsync(Guid employeeId, CancellationToken cancellationToken);
}

/// <summary>
/// PIN-beheer voor de prikklok. Een beheerder kan een PIN zetten, genereren of
/// intrekken maar nooit uitlezen. Codes zijn per tenant uniek (vereiste voor de
/// PIN-only kioskidentificatie; databankindex dwingt dit af). Elke wijziging wordt
/// geauditeerd zonder dat de code zelf ooit in de auditlog belandt.
/// Bij deactivering van een medewerker hoort de credential mee uitgeschakeld te
/// worden — zie EmployeeService.DeactivateAsync.
/// </summary>
public class AttendanceCredentialService : IAttendanceCredentialService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAttendancePinHasher _pinHasher;
    private readonly IAuditService _auditService;

    public AttendanceCredentialService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAttendancePinHasher pinHasher,
        IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _pinHasher = pinHasher;
        _auditService = auditService;
    }

    public async Task<AttendanceCredentialStatusDto> GetStatusAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var credential = await Scoped().AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmployeeId == employeeId, cancellationToken);
        return ToStatus(credential);
    }

    public async Task<AttendanceCredentialResult> SetPinAsync(Guid employeeId, string? pin, CancellationToken cancellationToken)
    {
        if (!_pinHasher.IsConfigured)
        {
            return new AttendanceCredentialResult(AttendanceCredentialOutcome.NotConfigured, null, null,
                "De prikklok is niet geconfigureerd op de server (Attendance:PinPepper ontbreekt).");
        }

        var employee = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && e.Id == employeeId)
            .Select(e => new { e.Id })
            .FirstOrDefaultAsync(cancellationToken);
        if (employee is null)
        {
            return new AttendanceCredentialResult(AttendanceCredentialOutcome.EmployeeNotFound, null, null,
                "Medewerker niet gevonden.");
        }

        var pinLength = await PinLengthAsync(cancellationToken);
        var generated = pin is null;
        string chosenPin;
        string lookupHash;

        if (generated)
        {
            var attempt = 0;
            do
            {
                chosenPin = GeneratePin(pinLength);
                lookupHash = _pinHasher.ComputeLookupHash(_tenantContext.TenantId, chosenPin);
            }
            while (await LookupInUseAsync(lookupHash, employeeId, cancellationToken) && ++attempt < 25);

            if (attempt >= 25)
            {
                return new AttendanceCredentialResult(AttendanceCredentialOutcome.PinInUse, null, null,
                    "Er kon geen vrije code gegenereerd worden. Verhoog de PIN-lengte in de instellingen.");
            }
        }
        else
        {
            chosenPin = pin!.Trim();
            if (chosenPin.Length != pinLength || !chosenPin.All(char.IsAsciiDigit))
            {
                return new AttendanceCredentialResult(AttendanceCredentialOutcome.InvalidPin, null, null,
                    $"De code moet uit exact {pinLength} cijfers bestaan.");
            }

            lookupHash = _pinHasher.ComputeLookupHash(_tenantContext.TenantId, chosenPin);
            if (await LookupInUseAsync(lookupHash, employeeId, cancellationToken))
            {
                return new AttendanceCredentialResult(AttendanceCredentialOutcome.PinInUse, null, null,
                    "Deze code is al in gebruik. Kies een andere code.");
            }
        }

        var credential = await Scoped().FirstOrDefaultAsync(c => c.EmployeeId == employeeId, cancellationToken);
        var isNew = credential is null;
        if (credential is null)
        {
            credential = new AttendanceCredential
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                EmployeeId = employeeId,
            };
            _dbContext.AttendanceCredentials.Add(credential);
        }

        credential.Type = AttendanceCredentialType.Pin;
        credential.SecretHash = _pinHasher.HashPin(chosenPin);
        credential.LookupHash = lookupHash;
        credential.IsActive = true;
        credential.FailedAttemptCount = 0;
        credential.LockedUntil = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("AttendanceCredential", credential.Id.ToString(),
            isNew ? "Created" : "Reset",
            null, new { credential.EmployeeId, Generated = generated }, cancellationToken);

        return new AttendanceCredentialResult(
            AttendanceCredentialOutcome.Success, ToStatus(credential), generated ? chosenPin : null, null);
    }

    public async Task<AttendanceCredentialResult> DisableAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var credential = await Scoped().FirstOrDefaultAsync(c => c.EmployeeId == employeeId, cancellationToken);
        if (credential is null)
        {
            return new AttendanceCredentialResult(AttendanceCredentialOutcome.NoCredential, null, null,
                "Deze medewerker heeft geen prikklokcode.");
        }

        credential.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("AttendanceCredential", credential.Id.ToString(), "Disabled",
            null, new { credential.EmployeeId }, cancellationToken);

        return new AttendanceCredentialResult(AttendanceCredentialOutcome.Success, ToStatus(credential), null, null);
    }

    private IQueryable<AttendanceCredential> Scoped() =>
        _dbContext.AttendanceCredentials.Where(c => c.TenantId == _tenantContext.TenantId);

    private Task<bool> LookupInUseAsync(string lookupHash, Guid employeeId, CancellationToken cancellationToken) =>
        Scoped().AnyAsync(c => c.LookupHash == lookupHash && c.EmployeeId != employeeId, cancellationToken);

    private async Task<int> PinLengthAsync(CancellationToken cancellationToken)
    {
        var length = await _dbContext.AttendanceSettings.AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId)
            .Select(s => (int?)s.PinLength)
            .FirstOrDefaultAsync(cancellationToken);
        return Math.Clamp(length ?? 4, 4, 8);
    }

    private static string GeneratePin(int length)
    {
        Span<char> digits = stackalloc char[length];
        for (var i = 0; i < length; i++)
        {
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        }

        return new string(digits);
    }

    private static AttendanceCredentialStatusDto ToStatus(AttendanceCredential? credential) =>
        credential is null
            ? new AttendanceCredentialStatusDto(false, false, null, null)
            : new AttendanceCredentialStatusDto(true, credential.IsActive, credential.LastUsedAt, credential.LockedUntil);
}
