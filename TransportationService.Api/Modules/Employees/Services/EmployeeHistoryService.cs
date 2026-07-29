using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
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
    /// <paramref name="category"/>, when given, must be one of the Dutch category labels
    /// (Profiel, Kwalificaties, …) — an unknown value is a validation error, not a silent no-op.
    /// </summary>
    Task<EmployeeHistoryPageDto?> GetHistoryAsync(
        Guid employeeId, int page, int pageSize, string? category, CancellationToken cancellationToken);
}

/// <summary>
/// Read-time projection over the existing append-only audit log (never a second audit system):
/// collects the audit rows of the employee and its child entities, diffs the stored before/
/// after JSON per field and translates technical names into Dutch labels. Old, partial audit
/// entries keep rendering through the same path — they simply produce fewer field diffs; stored
/// rows are never rewritten.
///
/// Id-valued fields (qualification type, leave type, balance type, department, verifying/
/// deciding user) are resolved to display names at READ time, over the current page only —
/// batched per lookup type, soft-deleted rows included via <c>IgnoreQueryFilters</c>, unknown
/// ids rendering as "<see cref="UnknownLookupLabel"/>". This is what lets even legacy rows
/// (written before a lookup existed, or referencing a since-deleted row) resolve correctly:
/// the resolution is driven by whatever raw ids appear in the stored JSON, not by write-time
/// bookkeeping.
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

    /// <summary>Placeholder shown for a resolved id that no longer matches any row.</summary>
    private const string UnknownLookupLabel = "Onbekend (verwijderd)";

    private static readonly IReadOnlyDictionary<string, string> CategoryByEntityType = new Dictionary<string, string>
    {
        ["Employee"] = "Profiel",
        ["EmployeeQualification"] = "Kwalificaties",
        ["EmployeeDocument"] = "Documenten",
        ["EmployeeNote"] = "Notities",
        ["EmployeeIssuedItem"] = "Bedrijfsmiddelen",
        ["Absence"] = "Afwezigheden",
        ["EmployeeLeaveBalance"] = "Verlofsaldo",
        ["LeaveBalanceAdjustment"] = "Verlofsaldo",
        ["Driver"] = "Chauffeursprofiel",
    };

    /// <summary>Valid values for the `category` query filter — the chip labels shown in the UI.</summary>
    private static readonly HashSet<string> KnownCategories = new(CategoryByEntityType.Values, StringComparer.Ordinal);

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
        ["Pinned"] = "Toegevoegd aan startscherm",
        ["Unpinned"] = "Verwijderd van startscherm",
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
        // Notes
        ["Text"] = "Tekst",
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
        ["DecidedByUserId"] = "Beslist door",
        // Leave balances
        ["Verlofcategorie"] = "Verlofcategorie",
        ["Jaar"] = "Jaar/periode",
        ["year"] = "Jaar",
        ["Eenheid"] = "Eenheid",
        ["BaseEntitlementDays"] = "Basisrecht (dagen)",
        ["CarryOverDays"] = "Overdracht (dagen)",
        ["Verschil"] = "Verschil",
        ["AanpassingenTotaal"] = "Saldo-aanpassingen (totaal)",
        ["Soort"] = "Soort aanpassing",
        ["Kind"] = "Soort aanpassing",
        ["Days"] = "Dagen",
        ["BalanceTypeId"] = "Saldotype",
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
        // Issued item status (EmployeeIssuedItem.Status)
        ["NotIssued"] = "Niet uitgereikt",
        ["Issued"] = "Uitgereikt",
        ["Returned"] = "Teruggebracht",
        ["Missing"] = "Vermist",
        ["Damaged"] = "Beschadigd",
        // Document category (EmployeeDocument.Category)
        ["IdentityCardFront"] = "Identiteitskaart (voorzijde)",
        ["IdentityCardBack"] = "Identiteitskaart (achterzijde)",
        ["DrivingLicenceFront"] = "Rijbewijs (voorzijde)",
        ["DrivingLicenceBack"] = "Rijbewijs (achterzijde)",
        ["EmploymentDocument"] = "Arbeidsdocument",
        ["MedicalDocument"] = "Medisch document",
        ["Certificate"] = "Certificaat",
        ["Contract"] = "Contract",
        ["AdditionalDocument"] = "Bijkomend document",
        // Shift type (EmployeePlanning.Shift.ShiftType)
        ["Work"] = "Werk",
        ["Standby"] = "Stand-by",
        // Driver availability status (Driver.AvailabilityStatus)
        ["Available"] = "Beschikbaar",
        ["Unavailable"] = "Niet beschikbaar",
        ["OnTrip"] = "Onderweg",
    };

    /// <summary>Payload keys that are internal plumbing, never a user-facing field.</summary>
    private static readonly HashSet<string> HiddenKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "EmployeeId", "TenantId", "Id",
    };

    private enum LookupKind
    {
        QualificationType,
        LeaveType,
        LeaveBalanceType,
        Department,
        User,
    }

    /// <summary>Id-valued payload keys that need a name lookup rather than plain formatting.</summary>
    private static readonly IReadOnlyDictionary<string, LookupKind> IdFieldLookups = new Dictionary<string, LookupKind>(StringComparer.OrdinalIgnoreCase)
    {
        ["QualificationTypeId"] = LookupKind.QualificationType,
        ["LeaveTypeId"] = LookupKind.LeaveType,
        ["BalanceTypeId"] = LookupKind.LeaveBalanceType,
        ["DepartmentId"] = LookupKind.Department,
        ["VerifiedByUserId"] = LookupKind.User,
        ["DecidedByUserId"] = LookupKind.User,
    };

    /// <summary>Field labels that represent an absolute day count — the leave-balance summary anchors on these.</summary>
    private static readonly HashSet<string> DaysFieldLabels = new(StringComparer.Ordinal)
    {
        FieldLabels["BaseEntitlementDays"],
        FieldLabels["AanpassingenTotaal"],
    };

    public async Task<EmployeeHistoryPageDto?> GetHistoryAsync(
        Guid employeeId, int page, int pageSize, string? category, CancellationToken cancellationToken)
    {
        if (category is not null && !KnownCategories.Contains(category))
        {
            throw new DomainValidationException("category", "Onbekende categorie.");
        }

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
        var noteIds = Keys(await _db.EmployeeNotes.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.TenantId == tenantId && n.EmployeeId == employeeId)
            .Select(n => n.Id).ToListAsync(cancellationToken));
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
                || (l.EntityType == "EmployeeNote" && noteIds.Contains(l.EntityId))
                || (l.EntityType == "EmployeeIssuedItem" && issuedItemIds.Contains(l.EntityId))
                || (l.EntityType == "Absence" && absenceIds.Contains(l.EntityId))
                || (l.EntityType == "EmployeeLeaveBalance" && balanceIds.Contains(l.EntityId))
                || (l.EntityType == "LeaveBalanceAdjustment" && adjustmentIds.Contains(l.EntityId))
                || (l.EntityType == "Driver" && driverIds.Contains(l.EntityId))))
            .OrderByDescending(l => l.Timestamp)
            .Take(MaxRows)
            .ToListAsync(cancellationToken);

        var pendingEntries = rows
            .Select(row => ProjectPending(row.Id, row.EntityType, row.Action, row.OldValuesJson, row.NewValuesJson, row.Timestamp, row.UserId))
            // A save that changed nothing meaningful never becomes a misleading "Gewijzigd" card.
            .Where(e => e.Changes.Count > 0 || e.Action is not "Updated")
            .Where(e => category is null || e.Category == category)
            .ToList();

        var total = pendingEntries.Count;
        var pageItems = pendingEntries.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        // Batch-resolve every id-bearing field over just this page's rows — legacy rows included,
        // since resolution is driven by whatever ids the stored JSON happens to contain.
        var userIds = pageItems.Where(e => e.ActorUserId is not null).Select(e => e.ActorUserId!.Value)
            .Concat(IdsFor(pageItems, LookupKind.User))
            .Distinct().ToList();
        var qualificationTypeIds = IdsFor(pageItems, LookupKind.QualificationType);
        var leaveTypeIds = IdsFor(pageItems, LookupKind.LeaveType);
        var leaveBalanceTypeIds = IdsFor(pageItems, LookupKind.LeaveBalanceType);
        var departmentIds = IdsFor(pageItems, LookupKind.Department);

        var userNames = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);
        var qualificationTypeNames = await _db.QualificationTypes.AsNoTracking()
            .Where(t => qualificationTypeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var leaveTypeNames = await _db.LeaveTypes.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.TenantId == tenantId && leaveTypeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var leaveBalanceTypeNames = await _db.LeaveBalanceTypes.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.TenantId == tenantId && leaveBalanceTypeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var departmentNames = await _db.Departments.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.TenantId == tenantId && departmentIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        var lookups = new IdLookups(userNames, qualificationTypeNames, leaveTypeNames, leaveBalanceTypeNames, departmentNames);
        var items = pageItems.Select(entry => Finalize(entry, lookups)).ToList();

        return new EmployeeHistoryPageDto(items, total, page, pageSize);
    }

    private static List<Guid> IdsFor(IEnumerable<PendingEntry> entries, LookupKind kind) =>
        entries.SelectMany(e => e.Changes)
            .Where(c => IdFieldLookups.TryGetValue(c.FieldKey, out var k) && k == kind)
            .SelectMany(c => new[] { c.Before, c.After })
            .Where(v => v is not null)
            .Select(v => Guid.TryParse(v, out var id) ? (Guid?)id : null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

    private sealed record IdLookups(
        IReadOnlyDictionary<Guid, string> Users,
        IReadOnlyDictionary<Guid, string> QualificationTypes,
        IReadOnlyDictionary<Guid, string> LeaveTypes,
        IReadOnlyDictionary<Guid, string> LeaveBalanceTypes,
        IReadOnlyDictionary<Guid, string> Departments);

    /// <summary>One field-level change before id resolution — <see cref="Before"/>/<see cref="After"/>
    /// hold the raw id string (not yet a name) when <see cref="FieldKey"/> is a known id field.</summary>
    private sealed record PendingChange(string FieldKey, string Label, string? Before, string? After);

    private sealed record PendingEntry(
        Guid Id, DateTime Timestamp, Guid? ActorUserId, string Action, string ActionLabel, string Category,
        string? LeaveCategoryName, string? LeaveYear, List<PendingChange> Changes);

    private static PendingEntry ProjectPending(
        Guid id, string entityType, string action, string? oldJson, string? newJson, DateTime timestamp, Guid? actorUserId)
    {
        var oldValues = ParseFlat(oldJson);
        var newValues = ParseFlat(newJson);

        var keys = newValues.Keys.Concat(oldValues.Keys.Where(k => !newValues.ContainsKey(k))).ToList();
        var changes = new List<PendingChange>();
        foreach (var key in keys)
        {
            if (HiddenKeys.Contains(key))
            {
                continue;
            }

            string? before;
            string? after;
            if (IdFieldLookups.ContainsKey(key))
            {
                // Resolved later, in bulk, over the page — keep the raw id for now.
                before = NormalizeRaw(oldValues.GetValueOrDefault(key));
                after = NormalizeRaw(newValues.GetValueOrDefault(key));
            }
            else
            {
                before = FormatValue(oldValues.GetValueOrDefault(key));
                after = FormatValue(newValues.GetValueOrDefault(key));
            }

            if (before == after)
            {
                continue;
            }

            var label = FieldLabels.TryGetValue(key, out var mapped) ? mapped : Humanize(key);
            changes.Add(new PendingChange(key, label, before, after));
        }

        var category = CategoryByEntityType.GetValueOrDefault(entityType, entityType);
        return new PendingEntry(
            id, timestamp, actorUserId, action, ActionLabels.GetValueOrDefault(action, action), category,
            FormatValue(newValues.GetValueOrDefault("Verlofcategorie")), FormatValue(newValues.GetValueOrDefault("Jaar")),
            changes);
    }

    private static EmployeeHistoryEntryDto Finalize(PendingEntry entry, IdLookups lookups)
    {
        var changes = entry.Changes.Select(c => ResolveChange(c, lookups)).ToList();
        var actorName = entry.ActorUserId is { } uid ? lookups.Users.GetValueOrDefault(uid) : null;
        var summary = BuildSummary(entry, changes);
        return new EmployeeHistoryEntryDto(
            entry.Id, entry.Timestamp, actorName, entry.Action, entry.ActionLabel, entry.Category, changes, summary);
    }

    private static EmployeeHistoryChangeDto ResolveChange(PendingChange change, IdLookups lookups)
    {
        if (!IdFieldLookups.TryGetValue(change.FieldKey, out var kind))
        {
            return new EmployeeHistoryChangeDto(change.Label, change.Before, change.After);
        }

        var table = kind switch
        {
            LookupKind.QualificationType => lookups.QualificationTypes,
            LookupKind.LeaveType => lookups.LeaveTypes,
            LookupKind.LeaveBalanceType => lookups.LeaveBalanceTypes,
            LookupKind.Department => lookups.Departments,
            LookupKind.User => lookups.Users,
            _ => throw new InvalidOperationException($"Onbehandelde opzoeksoort: {kind}."),
        };

        return new EmployeeHistoryChangeDto(change.Label, ResolveName(change.Before, table), ResolveName(change.After, table));
    }

    private static string? ResolveName(string? raw, IReadOnlyDictionary<Guid, string> table)
    {
        if (raw is null)
        {
            return null;
        }

        return Guid.TryParse(raw, out var id) && table.TryGetValue(id, out var name) ? name : UnknownLookupLabel;
    }

    /// <summary>
    /// Compact Dutch summary line shown on the collapsed card. Leave-balance saves that touch an
    /// absolute day count get the "Categorie Jaar: voor → na dagen" style; everything else gets a
    /// single "Field: before → after" line, or a "N velden gewijzigd (…)" roll-up for the rest.
    /// </summary>
    private static string BuildSummary(PendingEntry entry, IReadOnlyList<EmployeeHistoryChangeDto> changes)
    {
        if (entry.Category == "Verlofsaldo" && entry.LeaveCategoryName is not null && entry.LeaveYear is not null)
        {
            var daysChange = changes.FirstOrDefault(c => DaysFieldLabels.Contains(c.Field) && c.Before is not null && c.After is not null);
            if (daysChange is not null)
            {
                return $"{entry.LeaveCategoryName} {entry.LeaveYear}: {daysChange.Before} → {daysChange.After} dagen";
            }
        }

        if (changes.Count == 0)
        {
            return entry.ActionLabel;
        }

        if (changes.Count == 1)
        {
            var change = changes[0];
            return string.IsNullOrEmpty(change.Before)
                ? $"{change.Field}: {change.After ?? "—"}"
                : $"{change.Field}: {change.Before} → {change.After ?? "—"}";
        }

        var fieldNames = changes.Select(c => c.Field).Take(3);
        var suffix = changes.Count > 3 ? ", …" : string.Empty;
        return $"{changes.Count} velden gewijzigd ({string.Join(", ", fieldNames)}{suffix})";
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

    private static string? NormalizeRaw(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

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

    /// <summary>Unmapped payload key → readable words ("SomeWeirdKey" → "Some Weird Key"), deterministic.</summary>
    private static string Humanize(string key)
    {
        if (key.Length == 0)
        {
            return key;
        }

        var builder = new System.Text.StringBuilder(key.Length + 8);
        builder.Append(char.ToUpperInvariant(key[0]));
        for (var i = 1; i < key.Length; i++)
        {
            var current = key[i];
            var previous = key[i - 1];
            if (char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous)
                || (i + 1 < key.Length && char.IsLower(key[i + 1]) && char.IsUpper(previous))))
            {
                builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}
