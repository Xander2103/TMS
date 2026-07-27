using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

public interface IEmployeeHistoryService
{
    /// <summary>
    /// The employee's complete human-readable change history: profile fields plus every audited
    /// child entity (qualifications, documents, issued items, absences, leave balances/
    /// adjustments, driver profile), newest first. Null = employee unknown for this tenant.
    /// </summary>
    Task<EmployeeHistoryPageDto?> GetHistoryAsync(Guid employeeId, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// Read-time projection over the existing append-only audit log (never a second audit system):
/// collects the audit rows of the employee and its child entities, diffs the stored before/
/// after JSON per field and translates technical names into Dutch labels. Old, partial audit
/// entries keep rendering through the same path — they simply produce fewer field diffs; stored
/// rows are never rewritten.
/// </summary>
public class EmployeeHistoryService : IEmployeeHistoryService
{
    private readonly TransportationDbContext _db;
    private readonly ITenantContext _tenant;

    public EmployeeHistoryService(TransportationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>Hard cap on rows projected per employee — far above any realistic history.</summary>
    private const int MaxRows = 1000;

    private static readonly IReadOnlyDictionary<string, string> CategoryByEntityType = new Dictionary<string, string>
    {
        ["Employee"] = "Profiel",
        ["EmployeeQualification"] = "Kwalificaties",
        ["EmployeeDocument"] = "Documenten",
        ["EmployeeIssuedItem"] = "Bedrijfsmiddelen",
        ["Absence"] = "Afwezigheden",
        ["EmployeeLeaveBalance"] = "Verlofsaldo",
        ["LeaveBalanceAdjustment"] = "Verlofsaldo",
        ["Driver"] = "Chauffeursprofiel",
    };

    private static readonly IReadOnlyDictionary<string, string> ActionLabels = new Dictionary<string, string>
    {
        ["Created"] = "Aangemaakt",
        ["Updated"] = "Gewijzigd",
        ["Deleted"] = "Verwijderd",
        ["Deactivated"] = "Gedeactiveerd",
        ["Reactivated"] = "Geheractiveerd",
        ["Added"] = "Toegevoegd",
        ["Verified"] = "Geverifieerd",
        ["Suspended"] = "Geschorst",
        ["Uploaded"] = "Geüpload",
        ["MetadataChanged"] = "Gegevens gewijzigd",
        ["FileReplaced"] = "Bestand vervangen",
        ["Archived"] = "Gearchiveerd",
        ["Unarchived"] = "Gedearchiveerd",
        ["DocumentUploaded"] = "Document geüpload",
        ["DocumentRemoved"] = "Document verwijderd",
        ["Issued"] = "Uitgereikt",
        ["Returned"] = "Teruggebracht",
        ["Approved"] = "Goedgekeurd",
        ["Rejected"] = "Afgekeurd",
        ["Cancelled"] = "Geannuleerd",
        ["ChangesRequested"] = "Wijzigingen gevraagd",
        ["StatusChanged"] = "Status gewijzigd",
        ["InternalNoteChanged"] = "Interne notitie gewijzigd",
    };

    private static readonly IReadOnlyDictionary<string, string> FieldLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Employee profile snapshot keys
        ["EmployeeNumber"] = "Personeelsnummer",
        ["FirstName"] = "Voornaam",
        ["LastName"] = "Achternaam",
        ["DateOfBirth"] = "Geboortedatum",
        ["PlaceOfBirth"] = "Geboorteplaats",
        ["Nationality"] = "Nationaliteit",
        ["NationalityCode"] = "Nationaliteit",
        ["Language"] = "Taal",
        ["Email"] = "E-mailadres",
        ["PhoneNumber"] = "Telefoonnummer",
        ["MobilePhone"] = "Gsm-nummer",
        ["Street"] = "Straat",
        ["HouseNumber"] = "Huisnummer",
        ["PostalCode"] = "Postcode",
        ["City"] = "Gemeente",
        ["Country"] = "Land",
        ["CivilStatus"] = "Burgerlijke staat",
        ["DependentChildren"] = "Kinderen ten laste",
        ["EmploymentStartDate"] = "Startdatum tewerkstelling",
        ["EmploymentEndDate"] = "Einddatum tewerkstelling",
        ["EmploymentStatus"] = "Status tewerkstelling",
        ["Department"] = "Afdeling",
        ["DepartmentId"] = "Afdeling",
        ["ContractType"] = "Contracttype",
        ["JobFunctions"] = "Functies",
        ["DimonaNumber"] = "DIMONA-nummer",
        ["IsActive"] = "Actief",
        ["Notes"] = "Notities",
        ["EmergencyContacts"] = "Noodcontacten",
        ["NationalRegisterNumber"] = "Rijksregisternummer",
        ["IdentityCardNumber"] = "Identiteitskaartnummer",
        ["Iban"] = "IBAN",
        ["Bic"] = "BIC",
        // Qualifications
        ["QualificationTypeId"] = "Kwalificatietype",
        ["DocumentNumber"] = "Documentnummer",
        ["ObtainedDate"] = "Behaald op",
        ["ExpiryDate"] = "Vervaldatum",
        ["IssuingCountryCode"] = "Uitgifteland",
        ["Status"] = "Status",
        ["VerifiedAt"] = "Geverifieerd op",
        ["VerifiedByUserId"] = "Geverifieerd door",
        // Documents
        ["Category"] = "Categorie",
        ["CustomLabel"] = "Label",
        ["FileName"] = "Bestandsnaam",
        ["SizeBytes"] = "Bestandsgrootte",
        ["IsArchived"] = "Gearchiveerd",
        // Issued items
        ["NameSnapshot"] = "Item",
        ["VariantSnapshot"] = "Variant",
        ["CategorySnapshot"] = "Categorie",
        ["Quantity"] = "Aantal",
        ["SerialNumber"] = "Serienummer",
        ["IssuedDate"] = "Uitgereikt op",
        ["ReturnedDate"] = "Teruggebracht op",
        ["ReturnCondition"] = "Staat bij terugname",
        // Absences
        ["Type"] = "Soort",
        ["StartDate"] = "Startdatum",
        ["EndDate"] = "Einddatum",
        ["PartDay"] = "Dagdeel",
        ["Reason"] = "Reden",
        ["Reden"] = "Reden",
        ["DecisionNote"] = "Beslissingsnota",
        ["InternalNote"] = "Interne notitie",
        ["LeaveTypeId"] = "Verloftype",
        // Leave balances
        ["Verlofcategorie"] = "Verlofcategorie",
        ["Jaar"] = "Jaar/periode",
        ["Eenheid"] = "Eenheid",
        ["BaseEntitlementDays"] = "Basisrecht (dagen)",
        ["CarryOverDays"] = "Overdracht (dagen)",
        ["Verschil"] = "Verschil",
        ["AanpassingenTotaal"] = "Saldo-aanpassingen (totaal)",
        ["Soort"] = "Soort aanpassing",
        ["Kind"] = "Soort aanpassing",
        ["Days"] = "Dagen",
        // Driver profile
        ["DriverNumber"] = "Chauffeursnummer",
        ["AvailabilityStatus"] = "Beschikbaarheid",
        ["IsBlocked"] = "Geblokkeerd",
        ["BlockReason"] = "Blokkeringsreden",
    };

    /// <summary>Well-known enum/boolean raw values → readable Dutch.</summary>
    private static readonly IReadOnlyDictionary<string, string> ValueLabels = new Dictionary<string, string>
    {
        ["True"] = "Ja",
        ["False"] = "Nee",
        ["Active"] = "Actief",
        ["OnLeave"] = "Met verlof",
        ["Suspended"] = "Geschorst",
        ["Terminated"] = "Uit dienst",
        ["Requested"] = "Aangevraagd",
        ["UnderReview"] = "In beoordeling",
        ["Approved"] = "Goedgekeurd",
        ["Rejected"] = "Afgekeurd",
        ["Cancelled"] = "Geannuleerd",
        ["Valid"] = "Geldig",
        ["Pending"] = "In afwachting",
        ["Expired"] = "Verlopen",
        ["FullDay"] = "Volledige dag",
        ["Morning"] = "Voormiddag",
        ["Afternoon"] = "Namiddag",
        ["Vacation"] = "Verlof",
        ["Sick"] = "Ziekte",
        ["Training"] = "Opleiding",
        ["PersonalLeave"] = "Klein verlet",
        ["Unpaid"] = "Onbetaald",
        ["Other"] = "Andere",
        ["Grant"] = "Toekenning",
        ["Seniority"] = "Anciënniteit",
        ["Correction"] = "Correctie",
        ["Override"] = "Manuele override",
    };

    /// <summary>Payload keys that are internal plumbing, never a user-facing field.</summary>
    private static readonly HashSet<string> HiddenKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "EmployeeId", "TenantId", "Id",
    };

    public async Task<EmployeeHistoryPageDto?> GetHistoryAsync(
        Guid employeeId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var tenantId = _tenant.TenantId;
        var exists = await _db.Employees.AnyAsync(e => e.TenantId == tenantId && e.Id == employeeId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        // Child ids — soft-deleted rows included on purpose: their history must stay readable.
        // Guid→string conversion happens CLIENT-side: audit EntityId strings are lowercase
        // (C# Guid.ToString()), while a server-translated ToString() is provider-specific
        // (SQLite uppercases) and would silently match nothing.
        static List<string> Keys(IEnumerable<Guid> ids) => ids.Select(id => id.ToString()).ToList();
        var qualificationIds = Keys(await _db.EmployeeQualifications.AsNoTracking()
            .Where(q => q.TenantId == tenantId && q.EmployeeId == employeeId)
            .Select(q => q.Id).ToListAsync(cancellationToken));
        var documentIds = Keys(await _db.EmployeeDocuments.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.EmployeeId == employeeId)
            .Select(d => d.Id).ToListAsync(cancellationToken));
        var issuedItemIds = Keys(await _db.EmployeeIssuedItems.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.EmployeeId == employeeId)
            .Select(i => i.Id).ToListAsync(cancellationToken));
        var absenceIds = Keys(await _db.Absences.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.EmployeeId == employeeId)
            .Select(a => a.Id).ToListAsync(cancellationToken));
        var balanceRowIds = await _db.EmployeeLeaveBalances.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.TenantId == tenantId && b.EmployeeId == employeeId)
            .Select(b => b.Id).ToListAsync(cancellationToken);
        var adjustmentIds = Keys(await _db.LeaveBalanceAdjustments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == tenantId && balanceRowIds.Contains(a.EmployeeLeaveBalanceId))
            .Select(a => a.Id).ToListAsync(cancellationToken));
        var balanceIds = Keys(balanceRowIds);
        var driverIds = Keys(await _db.Drivers.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.EmployeeId == employeeId)
            .Select(d => d.Id).ToListAsync(cancellationToken));

        var employeeKey = employeeId.ToString();
        var rows = await _db.AuditLogs.AsNoTracking()
            .Where(l => l.TenantId == tenantId && (
                (l.EntityType == "Employee" && l.EntityId == employeeKey)
                || (l.EntityType == "EmployeeQualification" && qualificationIds.Contains(l.EntityId))
                || (l.EntityType == "EmployeeDocument" && documentIds.Contains(l.EntityId))
                || (l.EntityType == "EmployeeIssuedItem" && issuedItemIds.Contains(l.EntityId))
                || (l.EntityType == "Absence" && absenceIds.Contains(l.EntityId))
                || (l.EntityType == "EmployeeLeaveBalance" && balanceIds.Contains(l.EntityId))
                || (l.EntityType == "LeaveBalanceAdjustment" && adjustmentIds.Contains(l.EntityId))
                || (l.EntityType == "Driver" && driverIds.Contains(l.EntityId))))
            .OrderByDescending(l => l.Timestamp)
            .Take(MaxRows)
            .ToListAsync(cancellationToken);

        var userIds = rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var userNames = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

        var entries = rows
            .Select(row => Project(row.Id, row.EntityType, row.Action, row.OldValuesJson, row.NewValuesJson,
                row.Timestamp, row.UserId is { } uid ? userNames.GetValueOrDefault(uid) : null))
            // A save that changed nothing meaningful never becomes a misleading "Gewijzigd" card.
            .Where(e => e.Changes.Count > 0 || e.Action is not "Updated")
            .ToList();

        var total = entries.Count;
        var items = entries.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new EmployeeHistoryPageDto(items, total, page, pageSize);
    }

    private static EmployeeHistoryEntryDto Project(
        Guid id, string entityType, string action, string? oldJson, string? newJson, DateTime timestamp, string? userName)
    {
        var oldValues = ParseFlat(oldJson);
        var newValues = ParseFlat(newJson);

        var keys = newValues.Keys.Concat(oldValues.Keys.Where(k => !newValues.ContainsKey(k))).ToList();
        var changes = new List<EmployeeHistoryChangeDto>();
        foreach (var key in keys)
        {
            if (HiddenKeys.Contains(key))
            {
                continue;
            }

            var before = FormatValue(oldValues.GetValueOrDefault(key));
            var after = FormatValue(newValues.GetValueOrDefault(key));
            if (before == after)
            {
                continue;
            }

            changes.Add(new EmployeeHistoryChangeDto(FieldLabels.GetValueOrDefault(key, key), before, after));
        }

        return new EmployeeHistoryEntryDto(
            id, timestamp, userName, action,
            ActionLabels.GetValueOrDefault(action, action),
            CategoryByEntityType.GetValueOrDefault(entityType, entityType),
            changes);
    }

    private static Dictionary<string, string?> ParseFlat(string? json)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.True => "True",
                    JsonValueKind.False => "False",
                    _ => property.Value.ToString(),
                };
            }
        }
        catch (JsonException)
        {
            // Legacy/hand-written payloads that aren't valid JSON objects render as no fields.
        }

        return result;
    }

    private static string? FormatValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (ValueLabels.TryGetValue(raw, out var label))
        {
            return label;
        }

        // ISO dates (and date-times) → consistent dd-MM-yyyy display.
        if (raw.Length >= 10 && DateOnly.TryParse(raw[..10], out var date)
            && raw.Length >= 10 && raw[4] == '-' && raw[7] == '-')
        {
            return date.ToString("dd-MM-yyyy");
        }

        return raw;
    }
}
