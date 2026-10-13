using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Partners.Dtos;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Partners.Services;

public interface ICustomerHistoryService
{
    Task<CustomerHistoryPageDto?> GetHistoryAsync(
        Guid customerId, int page, int pageSize, string? category, CancellationToken cancellationToken);
}

/// <summary>
/// Read-time projection of the append-only audit trail into a readable customer history
/// (same approach as EmployeeHistoryService): the write side stores full readable snapshots
/// with names resolved and confidential values masked, so this class only diffs and labels.
/// </summary>
public class CustomerHistoryService : ICustomerHistoryService
{
    /// <summary>Hard cap: history is a recent-changes view, not a full export.</summary>
    private const int MaxRows = 1000;

    private static readonly string[] Categories = ["Klant", "Contactpersonen", "Locaties", "Facturatie", "Communicatie"];

    // Stabiele categoriecodes (i18n-wave): het filter accepteert code én legacy Nederlands
    // label (backward-compatible); de respons draagt beide. Frontendlogica hoort op de code.
    private static readonly Dictionary<string, string> CategoryCodeByLabel = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Klant"] = "customer",
        ["Contactpersonen"] = "contacts",
        ["Locaties"] = "locations",
        ["Facturatie"] = "billing",
        ["Communicatie"] = "communication",
    };

    private static string? NormalizeCategoryFilter(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        if (CategoryCodeByLabel.TryGetValue(category, out var byLabel))
        {
            return byLabel;
        }

        return CategoryCodeByLabel.Values.FirstOrDefault(code =>
            string.Equals(code, category, StringComparison.OrdinalIgnoreCase));
    }

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public CustomerHistoryService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<CustomerHistoryPageDto?> GetHistoryAsync(
        Guid customerId, int page, int pageSize, string? category, CancellationToken cancellationToken)
    {
        var categoryCodeFilter = NormalizeCategoryFilter(category);
        if (!string.IsNullOrWhiteSpace(category) && categoryCodeFilter is null)
        {
            throw new DomainValidationException("category", "Onbekende historiekcategorie.");
        }

        var customerExists = await _dbContext.Customers
            .AnyAsync(c => c.TenantId == _tenantContext.TenantId && c.Id == customerId, cancellationToken);
        if (!customerExists)
        {
            return null;
        }

        var customerKey = customerId.ToString();

        // Locations linked to this customer, soft-deleted ones included on purpose so their
        // history keeps rendering (IgnoreQueryFilters bypasses tenant + soft delete → explicit tenant predicate).
        var locationIds = await _dbContext.Locations.IgnoreQueryFilters()
            .Where(l => l.TenantId == _tenantContext.TenantId && l.CustomerId == customerId)
            .Select(l => l.Id.ToString())
            .ToListAsync(cancellationToken);

        // Sprint 2: the customer ↔ address relationships (link/unlink/defaults) are audited on
        // their own entity type; soft-deleted (unlinked) rows included so the history stays whole.
        var linkIds = await _dbContext.CustomerLocationLinks.IgnoreQueryFilters()
            .Where(l => l.TenantId == _tenantContext.TenantId && l.CustomerId == customerId)
            .Select(l => l.Id.ToString())
            .ToListAsync(cancellationToken);

        var logs = await _dbContext.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == _tenantContext.TenantId
                        && ((a.EntityType == "Customer" && a.EntityId == customerKey)
                            || (a.EntityType == "Location" && locationIds.Contains(a.EntityId))
                            || (a.EntityType == "CustomerLocationLink" && linkIds.Contains(a.EntityId))))
            .OrderByDescending(a => a.Timestamp)
            .Take(MaxRows)
            .ToListAsync(cancellationToken);

        var userIds = logs.Where(l => l.UserId is not null).Select(l => l.UserId!.Value).Distinct().ToList();
        var userNames = await _dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

        var entries = new List<CustomerHistoryEntryDto>();
        foreach (var log in logs)
        {
            var entryCategory = Categorize(log.EntityType, log.Action);
            var entryCategoryCode = CategoryCodeByLabel[entryCategory];
            if (categoryCodeFilter is not null && entryCategoryCode != categoryCodeFilter)
            {
                continue;
            }

            var changes = BuildChanges(log.OldValuesJson, log.NewValuesJson);

            // A no-op save produces an entry with zero effective changes — noise, drop it.
            if (log.Action == "Updated" && changes.Count == 0)
            {
                continue;
            }

            var userName = log.UserId is { } uid && userNames.TryGetValue(uid, out var name) && name.Length > 0
                ? name
                : null;

            entries.Add(new CustomerHistoryEntryDto(
                log.Id,
                log.Timestamp,
                userName,
                log.Action,
                ActionLabel(log.EntityType, log.Action),
                entryCategory,
                changes,
                Summarize(log.EntityType, log.Action, changes),
                entryCategoryCode));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var items = entries.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new CustomerHistoryPageDto(items, entries.Count, page, pageSize);
    }

    private static string Categorize(string entityType, string action) => entityType switch
    {
        "Location" => "Locaties",
        "CustomerLocationLink" => "Locaties",
        _ when action.StartsWith("Contact", StringComparison.Ordinal) => "Contactpersonen",
        _ when action.StartsWith("CommunicationRule", StringComparison.Ordinal) => "Communicatie",
        _ when action.StartsWith("Po", StringComparison.Ordinal)
               || action.StartsWith("Surcharge", StringComparison.Ordinal)
               || action.StartsWith("DieselSurcharge", StringComparison.Ordinal) => "Facturatie",
        _ => "Klant",
    };

    private static string ActionLabel(string entityType, string action) => action switch
    {
        "Created" => entityType == "Location" ? "Locatie aangemaakt" : "Aangemaakt",
        "Updated" => entityType switch
        {
            "Location" => "Locatie gewijzigd",
            "CustomerLocationLink" => "Adreskoppeling gewijzigd",
            _ => "Gewijzigd",
        },
        "Linked" => "Adres gekoppeld",
        "Relinked" => "Adres opnieuw gekoppeld",
        "Unlinked" => "Adres ontkoppeld",
        "Deleted" => entityType == "Location" ? "Locatie verwijderd" : "Verwijderd",
        "Activated" => "Geactiveerd",
        "Deactivated" => "Gedeactiveerd",
        "Blocked" => "Geblokkeerd",
        "Unblocked" => "Blokkering opgeheven",
        "NumberChanged" => "Klantnummer gewijzigd",
        "ContactAdded" => "Contactpersoon toegevoegd",
        "ContactUpdated" => "Contactpersoon gewijzigd",
        "ContactRemoved" => "Contactpersoon verwijderd",
        "CommunicationRuleAdded" => "Communicatievoorkeur toegevoegd",
        "CommunicationRuleChanged" => "Communicatievoorkeur gewijzigd",
        "CommunicationRuleRemoved" => "Communicatievoorkeur verwijderd",
        "PoPolicyChanged" => "PO-beleid gewijzigd",
        "DefaultsChanged" => "Standaardlocaties gewijzigd",
        "Imported" => "Geïmporteerd",
        _ => action,
    };

    private static readonly Dictionary<string, string> FieldLabels = new(StringComparer.Ordinal)
    {
        ["Name"] = "Naam",
        ["LegalName"] = "Officiële naam",
        ["Nickname"] = "Roepnaam",
        ["Category"] = "Categorie",
        ["CustomerNumber"] = "Klantnummer",
        ["Email"] = "E-mailadres",
        ["PhoneNumber"] = "Telefoon",
        ["MobilePhone"] = "GSM",
        ["Website"] = "Website",
        ["Street"] = "Straat",
        ["HouseNumber"] = "Huisnummer",
        ["PostalCode"] = "Postcode",
        ["City"] = "Gemeente",
        ["CountryCode"] = "Land",
        ["InvoiceEmail"] = "Facturatie-e-mail",
        ["PaymentTermDays"] = "Betaaltermijn (dagen)",
        ["DefaultLanguageCode"] = "Taal",
        ["InvoiceLanguageCode"] = "Factuurtaal",
        ["Notes"] = "Notities",
        ["IsActive"] = "Actief",
        ["VatTreatment"] = "BTW-regime",
        ["VatNumber"] = "BTW-nummer",
        ["CompanyNumber"] = "Ondernemingsnummer",
        ["CurrencyCode"] = "Valuta",
        ["Iban"] = "IBAN",
        ["Bic"] = "BIC",
        ["BankName"] = "Bank",
        ["BankAccountNumber"] = "Rekeningnummer",
        ["DefaultLegalEntity"] = "Facturerende entiteit",
        ["PeppolEnabled"] = "Peppol actief",
        ["PeppolId"] = "Peppol-ID",
        ["PeppolScheme"] = "Peppol-schema",
        ["PeppolDeliveryPreference"] = "Bezorgvoorkeur",
        ["BuyerReference"] = "Kopersreferentie",
        ["PurchaseOrderRequired"] = "PO verplicht",
        ["SignedDeliveryNoteRequired"] = "Getekende leverbon verplicht",
        ["CustomerReferenceRequired"] = "Klantreferentie verplicht",
        ["FirstName"] = "Voornaam",
        ["LastName"] = "Achternaam",
        ["DisplayName"] = "Weergavenaam",
        ["Role"] = "Functie",
        ["ContactType"] = "Type contactpersoon",
        ["IsPrimary"] = "Primair",
        ["Reason"] = "Reden",
        ["BlockReason"] = "Reden blokkering",
        ["IsBlocked"] = "Geblokkeerd",
        ["PortalSessionsRevoked"] = "Beëindigde portaalsessies",
        ["Policy"] = "PO-beleid",
        ["Code"] = "Code",
        ["Type"] = "Type",
        ["ContactName"] = "Contactpersoon",
        ["ContactPhone"] = "Telefoon contactpersoon",
        ["ContactEmail"] = "E-mail contactpersoon",
        ["OpeningHours"] = "Openingsuren",
        ["LoadingInstructions"] = "Laadinstructies",
        ["UnloadingInstructions"] = "Losinstructies",
        ["AccessInstructions"] = "Toegangsinstructies",
        ["AppointmentRequired"] = "Afspraak verplicht",
        // Location operational fields (master-data wave 2026-08-05).
        ["Gate"] = "Poort",
        ["AccessCode"] = "Toegangscode",
        ["ReceptionPoint"] = "Aanmeldpunt",
        ["Dock"] = "Kade/dok",
        ["RouteDescription"] = "Routebeschrijving",
        ["DriverInstructions"] = "Chauffeursinstructies",
        ["InternalMemo"] = "Interne memo",
        ["ExternalReference"] = "Externe referentie",
        ["ContactMobile"] = "GSM contactpersoon",
        ["CustomerContact"] = "Gekoppelde contactpersoon",
        ["DeliveryByAppointmentOnly"] = "Leveren enkel op afspraak",
        ["HeightRestrictionMeters"] = "Hoogtebeperking (m)",
        ["WeightRestrictionTons"] = "Gewichtsbeperking (t)",
        ["AdrAllowed"] = "ADR toegelaten",
        ["CraneRequired"] = "Kraan vereist",
        ["ForkliftAvailable"] = "Heftruck beschikbaar",
        ["DefaultLoadingMinutes"] = "Standaard laadtijd (min)",
        ["DefaultUnloadingMinutes"] = "Standaard lostijd (min)",
        ["PreferredArrivalFrom"] = "Voorkeursvenster van",
        ["PreferredArrivalTo"] = "Voorkeursvenster tot",
        ["EarliestArrival"] = "Vroegste aankomst",
        ["LatestArrival"] = "Laatste aankomst",
        ["OpeningIntervals"] = "Openingsuren",
    };

    /// <summary>Plumbing keys that would only show raw ids.</summary>
    private static readonly HashSet<string> HiddenKeys = new(StringComparer.Ordinal)
    {
        "Id", "TenantId", "CustomerId", "EntityId", "CategoryId", "DepartmentId", "DefaultLegalEntityId",
    };

    private static List<CustomerHistoryChangeDto> BuildChanges(string? oldJson, string? newJson)
    {
        var oldValues = ParseSnapshot(oldJson);
        var newValues = ParseSnapshot(newJson);

        var keys = oldValues.Keys.Union(newValues.Keys, StringComparer.Ordinal)
            .Where(k => !HiddenKeys.Contains(k));

        var changes = new List<CustomerHistoryChangeDto>();
        foreach (var key in keys)
        {
            oldValues.TryGetValue(key, out var before);
            newValues.TryGetValue(key, out var after);
            if (string.Equals(before, after, StringComparison.Ordinal))
            {
                continue;
            }

            var label = FieldLabels.TryGetValue(key, out var mapped) ? mapped : key;
            changes.Add(new CustomerHistoryChangeDto(label, before, after));
        }

        return changes;
    }

    private static Dictionary<string, string?> ParseSnapshot(string? json)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            result[property.Name] = FormatValue(property.Value);
        }

        return result;
    }

    private static string? FormatValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => "Ja",
        JsonValueKind.False => "Nee",
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.ToString(),
        _ => element.GetRawText(),
    };

    private static string Summarize(string entityType, string action, IReadOnlyList<CustomerHistoryChangeDto> changes)
    {
        if (changes.Count == 1)
        {
            var change = changes[0];
            return $"{change.Field}: {change.Before ?? "—"} → {change.After ?? "—"}";
        }

        if (changes.Count > 1 && action is "Updated" or "ContactUpdated")
        {
            var names = string.Join(", ", changes.Take(3).Select(c => c.Field));
            var suffix = changes.Count > 3 ? ", …" : string.Empty;
            return $"{changes.Count} velden gewijzigd ({names}{suffix})";
        }

        return ActionLabel(entityType, action);
    }
}
