using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Partners.Dtos;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Partners.Services;

public class CustomerService : ICustomerService
{
    private const string EntityType = "Customer";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly ICountryCodeValidator _countryValidator;

    public CustomerService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        ICountryCodeValidator countryValidator)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _countryValidator = countryValidator;
    }

    private IQueryable<Customer> TenantScoped() =>
        _dbContext.Customers.Where(c => c.TenantId == _tenantContext.TenantId);

    public async Task<PagedResult<CustomerListItemDto>> SearchAsync(
        string? search, bool? isActive, Guid? categoryId, PageRequest page, CancellationToken cancellationToken)
    {
        var query = TenantScoped().AsNoTracking();

        if (isActive is { } activeFilter)
        {
            query = query.Where(c => c.IsActive == activeFilter);
        }

        if (categoryId is { } category)
        {
            query = query.Where(c => c.CategoryId == category);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Case-insensitive on both PostgreSQL and SQLite (plain LIKE is case-sensitive on PostgreSQL).
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(c =>
                c.CustomerNumber.ToLower().Contains(term) ||
                c.Name.ToLower().Contains(term) ||
                (c.VatNumber != null && c.VatNumber.ToLower().Contains(term)) ||
                (c.City != null && c.City.ToLower().Contains(term)));
        }

        // Left join to category name via GroupJoin projection (tenant-scoped, defense in depth).
        var projected = from c in query
                        join cat in _dbContext.CustomerCategories.Where(cc => cc.TenantId == _tenantContext.TenantId)
                            on c.CategoryId equals cat.Id into cats
                        from cat in cats.DefaultIfEmpty()
                        orderby c.Name
                        select new CustomerListItemDto(
                            c.Id, c.CustomerNumber, c.Name, c.City, c.CountryCode,
                            cat != null ? cat.Name : null, c.IsActive, c.IsBlocked);

        return await projected.ToPagedResultAsync(page, dto => dto, cancellationToken);
    }

    public async Task<IReadOnlyList<Modules.Accounting.Services.FiscalWarning>?> GetFiscalWarningsAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var customer = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == id)
            .Select(c => new { c.VatTreatment, c.VatNumber, c.CountryCode, c.VatCountryCode, c.DefaultLegalEntityId })
            .FirstOrDefaultAsync(cancellationToken);
        if (customer is null)
        {
            return null;
        }

        // The comparison country is the entity that will invoice this customer: its own
        // default entity, else the tenant default.
        var entityCountry = await _dbContext.LegalEntities.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IsActive
                && (customer.DefaultLegalEntityId != null ? e.Id == customer.DefaultLegalEntityId : e.IsDefault))
            .Select(e => e.CountryCode)
            .FirstOrDefaultAsync(cancellationToken);

        return Modules.Accounting.Services.InvoiceLineFiscalResolver.Inspect(
            customer.VatTreatment, customer.VatNumber, customer.VatCountryCode ?? customer.CountryCode, entityCountry);
    }

    public async Task<CustomerDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await TenantScoped()
            .AsNoTracking()
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (customer is null)
        {
            return null;
        }

        var categoryName = await ResolveCategoryNameAsync(customer.CategoryId, cancellationToken);
        return MapToDetail(customer, categoryName,
            await LoadAllowedLegalEntityIdsAsync(customer.Id, cancellationToken));
    }

    public async Task<CustomerDetailDto> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken, bool canManageFiscal = true)
    {
        await EnsureCategoryInTenantAsync(request.CategoryId, cancellationToken);
        if (!canManageFiscal && AnyFiscalValueProvided(request))
        {
            throw new DomainValidationException("Je hebt geen toestemming om fiscale of bankgegevens in te voeren (customers.manage_fiscal).");
        }

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Name = request.Name.Trim(),
            LegalName = Trim(request.LegalName),
            VatNumber = VatNumberValidator.NormalizeAndValidate(request.VatNumber),
            CategoryId = request.CategoryId,
            Email = Trim(request.Email),
            PhoneNumber = Trim(request.PhoneNumber),
            Website = Trim(request.Website),
            Street = Trim(request.Street),
            HouseNumber = Trim(request.HouseNumber),
            PostalCode = Trim(request.PostalCode),
            City = Trim(request.City),
            CountryCode = await _countryValidator.NormalizeAndValidateAsync(request.CountryCode, "adresland", cancellationToken, "countryCode"),
            InvoiceEmail = Trim(request.InvoiceEmail),
            PaymentTermDays = request.PaymentTermDays < 0 ? 0 : request.PaymentTermDays,
            DefaultLanguageCode = Trim(request.DefaultLanguageCode),
            Notes = Trim(request.Notes),
            IsActive = true,
        };
        ApplyBankAndIdentityFields(customer,
            request.Nickname, request.CompanyNumber, request.CurrencyCode,
            request.Iban, request.Bic, request.BankName, request.BankAccountNumber);
        customer.DefaultLegalEntityId = await EnsureLegalEntityInTenantAsync(request.DefaultLegalEntityId, cancellationToken);
        await ApplyAllowedLegalEntitiesAsync(customer, request.AllowedLegalEntityIds, cancellationToken);
        ApplyInvoiceGrouping(customer, request.InvoiceGrouping);
        ApplyDocumentStrategy(customer, request.DocumentStrategy);
        await ApplyVatAndPeppolProfileAsync(customer,
            request.VatTreatment, request.DefaultVatRatePercent, request.VatCountryCode, request.VatNotes,
            request.PeppolId, request.PeppolScheme, request.InvoiceLanguageCode,
            request.PurchaseOrderRequired, request.SignedDeliveryNoteRequired, request.CustomerReferenceRequired,
            request.PeppolEnabled, request.PeppolDeliveryPreference, request.BuyerReference,
            cancellationToken);

        // Contact persons: either the multi-contact list or (legacy) the single InitialContact.
        // Field paths keep the shape the caller used so inline error mapping keeps working.
        var contactInputs = new List<(CreateCustomerContactRequest Request, string FieldPrefix)>();
        if (request.Contacts is { Count: > 0 } contactList)
        {
            for (var i = 0; i < contactList.Count; i++)
            {
                contactInputs.Add((contactList[i], $"contacts[{i}]"));
            }
        }
        else if (request.InitialContact is { } single)
        {
            contactInputs.Add((single, "initialContact"));
        }

        var contacts = new List<CustomerContact>();
        var primaryTypes = new HashSet<CustomerContactType>();
        foreach (var (contactRequest, fieldPrefix) in contactInputs)
        {
            var contact = await BuildContactAsync(customer.Id, contactRequest, fieldPrefix, cancellationToken);
            if (contact.IsPrimary && !primaryTypes.Add(contact.ContactType))
            {
                throw new DomainValidationException($"{fieldPrefix}.isPrimary",
                    $"Er is al een primaire contactpersoon van het type '{contact.ContactType}'.");
            }

            _dbContext.CustomerContacts.Add(contact);
            customer.Contacts.Add(contact);
            contacts.Add(contact);
        }

        // Customer + contacts + claimed number + audit trail commit (or roll back) as one unit.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _dbContext.Customers.Add(customer);
        if (Trim(request.CustomerNumber) is { } explicitNumber)
        {
            // Explicit (imported/accounting) number: duplicates are always refused.
            if (explicitNumber.Length > 30)
            {
                throw new DomainValidationException("customerNumber", "Het klantnummer mag maximaal 30 tekens lang zijn.");
            }

            var inUse = await TenantScoped().AnyAsync(c => c.CustomerNumber == explicitNumber, cancellationToken);
            if (inUse)
            {
                throw new DomainValidationException("customerNumber", $"Klantnummer '{explicitNumber}' is al in gebruik.");
            }

            customer.CustomerNumber = explicitNumber;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await TenantNumbering.SaveWithClaimedNumberAsync(
                _dbContext, settings,
                () => customer.CustomerNumber = GenerateCustomerNumber(settings),
                cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, customer.Id.ToString(), "Created", null,
            new { customer.CustomerNumber, customer.Name }, cancellationToken);
        foreach (var contact in contacts)
        {
            await _auditService.RecordAsync(EntityType, customer.Id.ToString(), "ContactAdded", null,
                ContactAuditSnapshot(contact), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var categoryName = await ResolveCategoryNameAsync(customer.CategoryId, cancellationToken);
        return MapToDetail(customer, categoryName,
            await LoadAllowedLegalEntityIdsAsync(customer.Id, cancellationToken));
    }

    /// <summary>Validates and materializes one contact input; <paramref name="fieldPrefix"/> shapes error paths.</summary>
    private async Task<CustomerContact> BuildContactAsync(
        Guid customerId, CreateCustomerContactRequest request, string fieldPrefix, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            throw new DomainValidationException($"{fieldPrefix}.firstName", "Voornaam van de contactpersoon is verplicht.");
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new DomainValidationException($"{fieldPrefix}.lastName", "Achternaam van de contactpersoon is verplicht.");
        }

        await EnsureContactDepartmentInTenantAsync(request.DepartmentId, cancellationToken);

        return new CustomerContact
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            CustomerId = customerId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            DisplayName = Trim(request.DisplayName),
            Nickname = Trim(request.Nickname),
            Role = Trim(request.Role),
            DepartmentId = request.DepartmentId,
            Email = Trim(request.Email),
            PhoneNumber = Trim(request.PhoneNumber),
            MobilePhone = Trim(request.MobilePhone),
            PreferredLanguageCode = Trim(request.PreferredLanguageCode)?.ToLowerInvariant(),
            ContactType = ParseContactType(request.ContactType, $"{fieldPrefix}.contactType"),
            IsPrimary = request.IsPrimary,
            IsActive = request.IsActive,
            Notes = Trim(request.Notes),
        };
    }

    /// <summary>IsDefined guards the string-stored column against numeric strings ("7").</summary>
    private static CustomerContactType ParseContactType(string? value, string fieldPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CustomerContactType.Algemeen;
        }

        if (!Enum.TryParse<CustomerContactType>(value.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new DomainValidationException(fieldPath,
                "Kies een geldig type contactpersoon (Algemeen, Planning, Facturatie, Magazijn, Directie, Operationeel of Overig).");
        }

        return parsed;
    }

    public async Task<CustomerDetailDto?> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken, bool canManageFiscal = true)
    {
        var customer = await TenantScoped().Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        await EnsureCategoryInTenantAsync(request.CategoryId, cancellationToken);

        var oldValues = await BuildCustomerSnapshotAsync(customer, cancellationToken);

        if (!canManageFiscal && FiscalValuesChanged(customer, request))
        {
            throw new DomainValidationException("Je hebt geen toestemming om fiscale of bankgegevens te wijzigen (customers.manage_fiscal).");
        }

        customer.Name = request.Name.Trim();
        customer.LegalName = Trim(request.LegalName);
        customer.VatNumber = VatNumberValidator.NormalizeAndValidate(request.VatNumber);
        customer.CategoryId = request.CategoryId;
        customer.Email = Trim(request.Email);
        customer.PhoneNumber = Trim(request.PhoneNumber);
        customer.Website = Trim(request.Website);
        customer.Street = Trim(request.Street);
        customer.HouseNumber = Trim(request.HouseNumber);
        customer.PostalCode = Trim(request.PostalCode);
        customer.City = Trim(request.City);
        customer.CountryCode = await _countryValidator.NormalizeAndValidateAsync(request.CountryCode, "adresland", cancellationToken, "countryCode");
        customer.InvoiceEmail = Trim(request.InvoiceEmail);
        customer.PaymentTermDays = request.PaymentTermDays < 0 ? 0 : request.PaymentTermDays;
        customer.DefaultLanguageCode = Trim(request.DefaultLanguageCode);
        customer.Notes = Trim(request.Notes);
        customer.IsActive = request.IsActive;
        ApplyBankAndIdentityFields(customer,
            request.Nickname, request.CompanyNumber, request.CurrencyCode,
            request.Iban, request.Bic, request.BankName, request.BankAccountNumber);
        customer.DefaultLegalEntityId = await EnsureLegalEntityInTenantAsync(request.DefaultLegalEntityId, cancellationToken);
        await ApplyAllowedLegalEntitiesAsync(customer, request.AllowedLegalEntityIds, cancellationToken);
        ApplyInvoiceGrouping(customer, request.InvoiceGrouping);
        ApplyDocumentStrategy(customer, request.DocumentStrategy);
        await ApplyVatAndPeppolProfileAsync(customer,
            request.VatTreatment, request.DefaultVatRatePercent, request.VatCountryCode, request.VatNotes,
            request.PeppolId, request.PeppolScheme, request.InvoiceLanguageCode,
            request.PurchaseOrderRequired, request.SignedDeliveryNoteRequired, request.CustomerReferenceRequired,
            request.PeppolEnabled, request.PeppolDeliveryPreference, request.BuyerReference,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customer.Id.ToString(), "Updated", oldValues,
            await BuildCustomerSnapshotAsync(customer, cancellationToken), cancellationToken);

        var categoryName = await ResolveCategoryNameAsync(customer.CategoryId, cancellationToken);
        return MapToDetail(customer, categoryName,
            await LoadAllowedLegalEntityIdsAsync(customer.Id, cancellationToken));
    }

    /// <summary>
    /// Full readable field snapshot for the audit trail (history projection diffs old vs new).
    /// Lookup ids are resolved to names at write time so history survives renames; the IBAN is
    /// masked so history shows THAT it changed, never the full number.
    /// </summary>
    private async Task<Dictionary<string, object?>> BuildCustomerSnapshotAsync(Customer c, CancellationToken cancellationToken)
    {
        var categoryName = await ResolveCategoryNameAsync(c.CategoryId, cancellationToken);
        string? legalEntityName = null;
        if (c.DefaultLegalEntityId is { } legalEntityId)
        {
            legalEntityName = await _dbContext.LegalEntities
                .Where(e => e.TenantId == _tenantContext.TenantId && e.Id == legalEntityId)
                .Select(e => e.LegalName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new Dictionary<string, object?>
        {
            ["Name"] = c.Name,
            ["LegalName"] = c.LegalName,
            ["Nickname"] = c.Nickname,
            ["Category"] = categoryName,
            ["Email"] = c.Email,
            ["PhoneNumber"] = c.PhoneNumber,
            ["Website"] = c.Website,
            ["Street"] = c.Street,
            ["HouseNumber"] = c.HouseNumber,
            ["PostalCode"] = c.PostalCode,
            ["City"] = c.City,
            ["CountryCode"] = c.CountryCode,
            ["InvoiceEmail"] = c.InvoiceEmail,
            ["PaymentTermDays"] = c.PaymentTermDays,
            ["DefaultLanguageCode"] = c.DefaultLanguageCode,
            ["InvoiceLanguageCode"] = c.InvoiceLanguageCode,
            ["Notes"] = c.Notes,
            ["IsActive"] = c.IsActive,
            ["VatTreatment"] = c.VatTreatment.ToString(),
            ["VatNumber"] = c.VatNumber,
            ["CompanyNumber"] = c.CompanyNumber,
            ["CurrencyCode"] = c.CurrencyCode,
            ["Iban"] = MaskIban(c.Iban),
            ["Bic"] = c.Bic,
            ["BankName"] = c.BankName,
            ["DefaultLegalEntity"] = legalEntityName,
            ["PeppolEnabled"] = c.PeppolEnabled,
            ["PeppolId"] = c.PeppolId,
            ["PeppolScheme"] = c.PeppolScheme,
            ["PeppolDeliveryPreference"] = c.PeppolDeliveryPreference.ToString(),
            ["BuyerReference"] = c.BuyerReference,
            ["PurchaseOrderRequired"] = c.PurchaseOrderRequired,
            ["SignedDeliveryNoteRequired"] = c.SignedDeliveryNoteRequired,
            ["CustomerReferenceRequired"] = c.CustomerReferenceRequired,
        };
    }

    private static string? MaskIban(string? iban) =>
        string.IsNullOrWhiteSpace(iban) ? null : iban.Length <= 4 ? "•••" : $"•••{iban[^4..]}";

    /// <summary>Readable contact payload for the audit trail.</summary>
    private static Dictionary<string, object?> ContactAuditSnapshot(CustomerContact c) => new()
    {
        ["FirstName"] = c.FirstName,
        ["LastName"] = c.LastName,
        ["DisplayName"] = c.DisplayName,
        ["Role"] = c.Role,
        ["Email"] = c.Email,
        ["PhoneNumber"] = c.PhoneNumber,
        ["MobilePhone"] = c.MobilePhone,
        ["ContactType"] = c.ContactType.ToString(),
        ["IsPrimary"] = c.IsPrimary,
        ["IsActive"] = c.IsActive,
    };

    public async Task<CustomerDetailDto?> ChangeNumberAsync(
        Guid id, ChangeCustomerNumberRequest request, CancellationToken cancellationToken)
    {
        var customer = await TenantScoped().Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var newNumber = request.CustomerNumber?.Trim();
        if (string.IsNullOrEmpty(newNumber) || newNumber.Length > 30)
        {
            throw new DomainValidationException("customerNumber", "Een klantnummer van maximaal 30 tekens is verplicht.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new DomainValidationException("reason", "Een reden is verplicht bij het wijzigen van een klantnummer.");
        }

        if (!string.Equals(customer.CustomerNumber, newNumber, StringComparison.Ordinal))
        {
            var inUse = await TenantScoped().AnyAsync(
                c => c.Id != customer.Id && c.CustomerNumber == newNumber, cancellationToken);
            if (inUse)
            {
                throw new DomainValidationException("customerNumber", $"Klantnummer '{newNumber}' is al in gebruik.");
            }

            var oldNumber = customer.CustomerNumber;
            customer.CustomerNumber = newNumber;
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditService.RecordAsync(EntityType, customer.Id.ToString(), "NumberChanged",
                new { CustomerNumber = oldNumber },
                new { CustomerNumber = newNumber, request.Reason }, cancellationToken);
        }

        var categoryName = await ResolveCategoryNameAsync(customer.CategoryId, cancellationToken);
        return MapToDetail(customer, categoryName,
            await LoadAllowedLegalEntityIdsAsync(customer.Id, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await TenantScoped().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
        {
            return false;
        }

        _dbContext.Customers.Remove(customer);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customer.Id.ToString(), "Deleted",
            new { customer.CustomerNumber, customer.Name }, null, cancellationToken);

        return true;
    }

    public async Task<bool> SetBlockedAsync(Guid id, SetCustomerBlockedRequest request, CancellationToken cancellationToken)
    {
        var customer = await TenantScoped().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
        {
            return false;
        }

        customer.IsBlocked = request.IsBlocked;
        customer.BlockReason = request.IsBlocked ? Trim(request.Reason) : null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customer.Id.ToString(),
            request.IsBlocked ? "Blocked" : "Unblocked", null,
            new { customer.IsBlocked, customer.BlockReason }, cancellationToken);

        return true;
    }

    public async Task<bool> SetActiveAsync(Guid id, SetCustomerActiveRequest request, CancellationToken cancellationToken)
    {
        var customer = await TenantScoped().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
        {
            return false;
        }

        if (customer.IsActive != request.IsActive)
        {
            customer.IsActive = request.IsActive;

            // Deactivating a customer must immediately end their portal users' sessions: revoke
            // refresh tokens and rotate security stamps so outstanding access tokens die too.
            var revokedSessions = 0;
            if (!request.IsActive)
            {
                var nowUtc = DateTime.UtcNow;
                var portalUsers = await _dbContext.Users
                    .Where(u => u.TenantId == _tenantContext.TenantId && u.CustomerId == customer.Id)
                    .ToListAsync(cancellationToken);
                var portalUserIds = portalUsers.Select(u => u.Id).ToList();
                var tokens = await _dbContext.Set<Modules.Authentication.Entities.RefreshToken>()
                    .Where(t => portalUserIds.Contains(t.UserId) && t.RevokedAt == null)
                    .ToListAsync(cancellationToken);
                foreach (var token in tokens)
                {
                    token.RevokedAt = nowUtc;
                }

                foreach (var portalUser in portalUsers)
                {
                    portalUser.SecurityStamp = Guid.NewGuid();
                }

                revokedSessions = tokens.Count;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditService.RecordAsync(EntityType, customer.Id.ToString(),
                request.IsActive ? "Activated" : "Deactivated", null,
                new { customer.IsActive, PortalSessionsRevoked = revokedSessions }, cancellationToken);
        }

        return true;
    }

    public async Task<CustomerContactDto?> AddContactAsync(Guid customerId, CreateCustomerContactRequest request, CancellationToken cancellationToken)
    {
        var customer = await TenantScoped().Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var contact = await BuildContactAsync(customerId, request, "contact", cancellationToken);

        if (contact.IsPrimary)
        {
            DemoteOtherPrimaries(customer, contact.ContactType, exceptContactId: contact.Id);
        }

        // Add through the DbSet (not the tracked parent's navigation): a new child with a
        // pre-set Guid key added to a tracked collection is mis-inferred by EF as an existing
        // row and issued as an UPDATE. DbSet.Add forces the Added state.
        _dbContext.CustomerContacts.Add(contact);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customer.Id.ToString(), "ContactAdded", null,
            ContactAuditSnapshot(contact), cancellationToken);

        return MapContact(contact);
    }

    public async Task<CustomerContactDto?> UpdateContactAsync(Guid customerId, Guid contactId, UpdateCustomerContactRequest request, CancellationToken cancellationToken)
    {
        var customer = await TenantScoped().Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        var contact = customer?.Contacts.FirstOrDefault(c => c.Id == contactId);
        if (customer is null || contact is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            throw new DomainValidationException("firstName", "Voornaam van de contactpersoon is verplicht.");
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new DomainValidationException("lastName", "Achternaam van de contactpersoon is verplicht.");
        }

        await EnsureContactDepartmentInTenantAsync(request.DepartmentId, cancellationToken);

        var oldSnapshot = ContactAuditSnapshot(contact);

        contact.FirstName = request.FirstName.Trim();
        contact.LastName = request.LastName.Trim();
        contact.DisplayName = Trim(request.DisplayName);
        contact.Nickname = Trim(request.Nickname);
        contact.Role = Trim(request.Role);
        contact.DepartmentId = request.DepartmentId;
        contact.Email = Trim(request.Email);
        contact.PhoneNumber = Trim(request.PhoneNumber);
        contact.MobilePhone = Trim(request.MobilePhone);
        contact.PreferredLanguageCode = Trim(request.PreferredLanguageCode)?.ToLowerInvariant();
        contact.ContactType = request.ContactType is null ? contact.ContactType : ParseContactType(request.ContactType, "contactType");
        contact.IsPrimary = request.IsPrimary;
        contact.IsActive = request.IsActive;
        contact.Notes = Trim(request.Notes);

        if (contact.IsPrimary)
        {
            DemoteOtherPrimaries(customer, contact.ContactType, exceptContactId: contact.Id);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customer.Id.ToString(), "ContactUpdated", oldSnapshot,
            ContactAuditSnapshot(contact), cancellationToken);

        return MapContact(contact);
    }

    public async Task<bool> RemoveContactAsync(Guid customerId, Guid contactId, CancellationToken cancellationToken)
    {
        var customer = await TenantScoped().Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        var contact = customer?.Contacts.FirstOrDefault(c => c.Id == contactId);
        if (customer is null || contact is null)
        {
            return false;
        }

        // A contact that other records refer to must be deactivated, not deleted, so the
        // historical references keep resolving (soft delete still leaves dangling FK rows
        // pointing at an invisible contact — the communication module joins them live).
        var referenced = await _dbContext.CustomerCommunicationRuleContacts
            .AnyAsync(r => r.TenantId == _tenantContext.TenantId && r.ContactId == contactId, cancellationToken);
        if (referenced)
        {
            throw new DomainValidationException(
                "Deze contactpersoon is al in gebruik (bv. bij communicatievoorkeuren) en kan niet worden verwijderd. " +
                "Je kunt de contactpersoon wel deactiveren.");
        }

        _dbContext.Set<CustomerContact>().Remove(contact);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customer.Id.ToString(), "ContactRemoved",
            ContactAuditSnapshot(contact), null, cancellationToken);

        return true;
    }

    /// <summary>Primary is scoped per contact type: promoting a planning contact leaves the invoicing primary alone.</summary>
    private static void DemoteOtherPrimaries(Customer customer, CustomerContactType contactType, Guid exceptContactId)
    {
        foreach (var other in customer.Contacts.Where(c =>
                     c.Id != exceptContactId && c.IsPrimary && c.ContactType == contactType))
        {
            other.IsPrimary = false;
        }
    }

    private async Task ApplyVatAndPeppolProfileAsync(Customer customer,
        VatTreatment vatTreatment, decimal? defaultVatRatePercent, string? vatCountryCode, string? vatNotes,
        string? peppolId, string? peppolScheme, string? invoiceLanguageCode,
        bool purchaseOrderRequired, bool signedDeliveryNoteRequired, bool customerReferenceRequired,
        bool peppolEnabled, string? peppolDeliveryPreference, string? buyerReference,
        CancellationToken cancellationToken)
    {
        if (defaultVatRatePercent is { } rate && rate is < 0 or > 100)
        {
            throw new DomainValidationException("Standaard BTW-tarief moet tussen 0 en 100 liggen.");
        }

        var trimmedPeppolId = Trim(peppolId);
        var trimmedPeppolScheme = Trim(peppolScheme);
        if (trimmedPeppolId is not null && trimmedPeppolId.Contains(':'))
        {
            throw new DomainValidationException("Peppol-ID mag geen schema bevatten; vul het schema apart in.");
        }

        if (trimmedPeppolScheme is not null)
        {
            if (trimmedPeppolScheme.Length != 4 || !trimmedPeppolScheme.All(char.IsAsciiDigit))
            {
                throw new DomainValidationException("Peppol-schema moet uit 4 cijfers bestaan (bv. 0208).");
            }

            if (trimmedPeppolId is null)
            {
                throw new DomainValidationException("Peppol-schema vereist ook een Peppol-ID.");
            }
        }

        if (peppolEnabled && (trimmedPeppolId is null || trimmedPeppolScheme is null))
        {
            throw new DomainValidationException("peppolEnabled",
                "Peppol kan pas worden ingeschakeld als er een Peppol-ID en -schema zijn ingevuld.");
        }

        // IsDefined guards the string-stored column against numeric strings ("7") that
        // Enum.TryParse would otherwise accept as an undefined enum value.
        var deliveryPreference = Modules.Peppol.Entities.PeppolDeliveryPreference.Peppol;
        if (!string.IsNullOrWhiteSpace(peppolDeliveryPreference)
            && (!Enum.TryParse(peppolDeliveryPreference, ignoreCase: true, out deliveryPreference)
                || !Enum.IsDefined(deliveryPreference)))
        {
            throw new DomainValidationException("peppolDeliveryPreference",
                "Kies een geldige bezorgvoorkeur (Peppol of EmailFallback).");
        }

        var trimmedBuyerReference = Trim(buyerReference);
        if (trimmedBuyerReference is { Length: > 200 })
        {
            throw new DomainValidationException("buyerReference", "De kopersreferentie mag maximaal 200 tekens lang zijn.");
        }

        // A changed participant identity invalidates any earlier provider lookup result.
        if (!string.Equals(customer.PeppolId, trimmedPeppolId, StringComparison.Ordinal)
            || !string.Equals(customer.PeppolScheme, trimmedPeppolScheme, StringComparison.Ordinal))
        {
            customer.PeppolValidationStatus = Modules.Peppol.Entities.CustomerPeppolValidationStatus.Unknown;
            customer.PeppolValidatedAt = null;
            customer.PeppolValidationReference = null;
        }

        customer.VatTreatment = vatTreatment;
        customer.DefaultVatRatePercent = defaultVatRatePercent;
        customer.VatCountryCode = await _countryValidator.NormalizeAndValidateAsync(vatCountryCode, "BTW-land", cancellationToken, "vatCountryCode");
        customer.VatNotes = Trim(vatNotes);
        customer.PeppolId = trimmedPeppolId;
        customer.PeppolScheme = trimmedPeppolScheme;
        customer.InvoiceLanguageCode = Trim(invoiceLanguageCode);
        customer.PurchaseOrderRequired = purchaseOrderRequired;
        // Keep the authoritative PurchaseOrderPolicy enum in sync with the legacy bool the
        // customer form edits (SetPoPolicyAsync does the inverse). "Optional" survives an
        // unchanged bool=false so the billing panel's nuance isn't clobbered by a form save.
        if (purchaseOrderRequired)
        {
            customer.PurchaseOrderPolicy = PurchaseOrderPolicy.Required;
        }
        else if (customer.PurchaseOrderPolicy == PurchaseOrderPolicy.Required)
        {
            customer.PurchaseOrderPolicy = PurchaseOrderPolicy.None;
        }

        customer.SignedDeliveryNoteRequired = signedDeliveryNoteRequired;
        customer.CustomerReferenceRequired = customerReferenceRequired;
        customer.PeppolEnabled = peppolEnabled;
        customer.PeppolDeliveryPreference = deliveryPreference;
        customer.BuyerReference = trimmedBuyerReference;
    }

    private async Task EnsureCategoryInTenantAsync(Guid? categoryId, CancellationToken cancellationToken)
    {
        if (categoryId is { } id
            && !await _dbContext.CustomerCategories.AnyAsync(
                c => c.Id == id && c.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("klantcategorie");
        }
    }

    private async Task<string?> ResolveCategoryNameAsync(Guid? categoryId, CancellationToken cancellationToken)
    {
        if (categoryId is not { } id)
        {
            return null;
        }

        return await _dbContext.CustomerCategories
            .Where(c => c.Id == id && c.TenantId == _tenantContext.TenantId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string GenerateCustomerNumber(TenantSettings? settings)
    {
        if (settings is null)
        {
            return $"KL-{Guid.NewGuid().ToString("N")[..8]}";
        }

        var number = $"{settings.CustomerNumberPrefix}{settings.CustomerNumberNextValue:D4}";
        settings.CustomerNumberNextValue++;
        return number;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool AnyFiscalValueProvided(CreateCustomerRequest r) =>
        !string.IsNullOrWhiteSpace(r.VatNumber) || r.VatTreatment != VatTreatment.DomesticVat
        || r.DefaultVatRatePercent is not null || !string.IsNullOrWhiteSpace(r.VatCountryCode)
        || !string.IsNullOrWhiteSpace(r.PeppolId) || !string.IsNullOrWhiteSpace(r.PeppolScheme)
        || r.PeppolEnabled || !string.IsNullOrWhiteSpace(r.BuyerReference)
        || (!string.IsNullOrWhiteSpace(r.PeppolDeliveryPreference)
            && !string.Equals(r.PeppolDeliveryPreference.Trim(), "Peppol", StringComparison.OrdinalIgnoreCase))
        || !string.IsNullOrWhiteSpace(r.CompanyNumber) || !string.IsNullOrWhiteSpace(r.Iban)
        || !string.IsNullOrWhiteSpace(r.Bic) || !string.IsNullOrWhiteSpace(r.BankAccountNumber)
        || (!string.IsNullOrWhiteSpace(r.CurrencyCode) && !string.Equals(r.CurrencyCode.Trim(), "EUR", StringComparison.OrdinalIgnoreCase));

    /// <summary>Round-trip comparison: the form echoes normalized values, so plain equality suffices.</summary>
    private static bool FiscalValuesChanged(Customer c, UpdateCustomerRequest r) =>
        !string.Equals(Trim(r.VatNumber), c.VatNumber, StringComparison.OrdinalIgnoreCase)
        || r.VatTreatment != c.VatTreatment
        || r.DefaultVatRatePercent != c.DefaultVatRatePercent
        || !string.Equals(Trim(r.VatCountryCode), c.VatCountryCode, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(Trim(r.PeppolId), c.PeppolId, StringComparison.Ordinal)
        || !string.Equals(Trim(r.PeppolScheme), c.PeppolScheme, StringComparison.Ordinal)
        || r.PeppolEnabled != c.PeppolEnabled
        || !string.Equals(Trim(r.BuyerReference), c.BuyerReference, StringComparison.Ordinal)
        || !string.Equals(Trim(r.PeppolDeliveryPreference) ?? "Peppol", c.PeppolDeliveryPreference.ToString(), StringComparison.OrdinalIgnoreCase)
        || !string.Equals(Trim(r.CompanyNumber), c.CompanyNumber, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(Trim(r.Iban)?.Replace(" ", string.Empty), c.Iban, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(Trim(r.Bic), c.Bic, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(Trim(r.BankAccountNumber), c.BankAccountNumber, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(r.CurrencyCode)
            && !string.Equals(r.CurrencyCode.Trim(), c.CurrencyCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>Nickname/company-number/currency/bank fields (all optional; validated).</summary>
    private static void ApplyBankAndIdentityFields(Customer customer,
        string? nickname, string? companyNumber, string? currencyCode,
        string? iban, string? bic, string? bankName, string? bankAccountNumber)
    {
        customer.Nickname = Trim(nickname);
        customer.CompanyNumber = Trim(companyNumber);

        var currency = Trim(currencyCode)?.ToUpperInvariant() ?? customer.CurrencyCode;
        if (string.IsNullOrEmpty(currency))
        {
            currency = "EUR";
        }

        if (currency.Length != 3 || !currency.All(char.IsAsciiLetter))
        {
            throw new DomainValidationException("currencyCode", "Een geldige valutacode van 3 letters is verplicht (bv. EUR).");
        }

        customer.CurrencyCode = currency;
        customer.Iban = Common.Validation.BankingValidators.NormalizeIban(iban);
        customer.Bic = Common.Validation.BankingValidators.NormalizeBic(bic);
        customer.BankName = Trim(bankName);
        customer.BankAccountNumber = Trim(bankAccountNumber);
    }

    private async Task<Guid?> EnsureLegalEntityInTenantAsync(Guid? legalEntityId, CancellationToken cancellationToken)
    {
        if (legalEntityId is { } id && !await _dbContext.LegalEntities
                .AnyAsync(e => e.TenantId == _tenantContext.TenantId && e.Id == id && e.IsActive, cancellationToken))
        {
            throw new DomainValidationException("defaultLegalEntityId", "De gekozen facturerende entiteit bestaat niet of is niet actief.");
        }

        return legalEntityId;
    }

    /// <summary>Wave 2 §4: stores the invoice grouping preference; the proposal engine that acts
    /// on it is Wave 10. Null = leave as-is; Manual = today's behavior.</summary>
    private static void ApplyInvoiceGrouping(Customer customer, string? requested)
    {
        if (requested is null)
        {
            return;
        }

        var normalized = requested.Trim();
        if (normalized is not ("PerDossier" or "Weekly" or "Monthly" or "ByReference" or "Manual"))
        {
            throw new DomainValidationException("invoiceGrouping",
                "Ongeldige factuurgroepering. Toegestaan: PerDossier, Weekly, Monthly, ByReference of Manual.");
        }

        customer.InvoiceGrouping = normalized;
    }

    /// <summary>P1: who supplies the transport document. Null = leave as-is.</summary>
    private static void ApplyDocumentStrategy(Customer customer, string? requested)
    {
        if (requested is null)
        {
            return;
        }

        var normalized = requested.Trim();
        if (normalized is not ("GenerateOwn" or "CustomerDocument" or "PerOrder"))
        {
            throw new DomainValidationException("documentStrategy",
                "Ongeldige documentstrategie. Toegestaan: GenerateOwn, CustomerDocument of PerOrder.");
        }

        customer.DocumentStrategy = normalized;
    }

    /// <summary>
    /// Wave 2 (spec Part O): replaces the customer's allowed-issuing-entities set. Null = leave
    /// as-is; empty = clear (every active entity allowed). Whatever the effective set becomes,
    /// a configured DefaultLegalEntityId must be inside it — the dossier/order/invoice guards
    /// rely on that invariant to skip re-checking inherited defaults. Returns the effective set
    /// for the detail DTO.
    /// </summary>
    private async Task<List<Guid>> ApplyAllowedLegalEntitiesAsync(
        Customer customer, IReadOnlyList<Guid>? requestedIds, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var existing = await _dbContext.CustomerAllowedLegalEntities
            .Where(x => x.TenantId == tenantId && x.CustomerId == customer.Id)
            .ToListAsync(cancellationToken);

        var effective = requestedIds is null
            ? existing.Select(x => x.LegalEntityId).ToList()
            : requestedIds.Distinct().ToList();

        if (requestedIds is not null && effective.Count > 0)
        {
            var validCount = await _dbContext.LegalEntities.CountAsync(
                e => e.TenantId == tenantId && effective.Contains(e.Id) && e.IsActive, cancellationToken);
            if (validCount != effective.Count)
            {
                throw new InvalidTenantReferenceException("facturerende entiteit");
            }
        }

        if (effective.Count > 0 && customer.DefaultLegalEntityId is { } defaultId && !effective.Contains(defaultId))
        {
            throw new DomainValidationException("allowedLegalEntityIds",
                "De standaardentiteit van de klant moet in de lijst van toegestane entiteiten staan.");
        }

        if (requestedIds is not null)
        {
            _dbContext.RemoveRange(existing.Where(x => !effective.Contains(x.LegalEntityId)));
            foreach (var entityId in effective.Where(id => existing.All(x => x.LegalEntityId != id)))
            {
                _dbContext.CustomerAllowedLegalEntities.Add(new CustomerAllowedLegalEntity
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customer.Id, LegalEntityId = entityId,
                });
            }
        }

        return effective;
    }

    private async Task EnsureContactDepartmentInTenantAsync(Guid? departmentId, CancellationToken cancellationToken)
    {
        if (departmentId is { } id && !await _dbContext.Set<ContactDepartment>()
                .AnyAsync(d => d.TenantId == _tenantContext.TenantId && d.Id == id, cancellationToken))
        {
            throw new DomainValidationException("departmentId", "De gekozen afdeling bestaat niet.");
        }
    }

    private static CustomerContactDto MapContact(CustomerContact c) =>
        new(c.Id, c.FirstName, c.LastName, c.Role, c.Email, c.PhoneNumber, c.IsPrimary, c.Notes,
            c.DisplayName, c.Nickname, c.MobilePhone, c.DepartmentId, c.PreferredLanguageCode, c.IsActive,
            c.ContactType.ToString());

    private static CustomerDetailDto MapToDetail(
        Customer c, string? categoryName, IReadOnlyList<Guid>? allowedLegalEntityIds = null) => new(
        c.Id, c.CustomerNumber, c.Name, c.LegalName, c.VatNumber, c.CategoryId, categoryName,
        c.Email, c.PhoneNumber, c.Website,
        c.Street, c.HouseNumber, c.PostalCode, c.City, c.CountryCode,
        c.InvoiceEmail, c.PaymentTermDays, c.DefaultLanguageCode, c.Notes,
        c.IsActive, c.IsBlocked, c.BlockReason,
        c.VatTreatment, c.DefaultVatRatePercent, c.VatCountryCode, c.VatNotes,
        c.PeppolId, c.PeppolScheme, c.InvoiceLanguageCode,
        c.PurchaseOrderRequired, c.SignedDeliveryNoteRequired, c.CustomerReferenceRequired,
        c.Contacts.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.LastName).Select(MapContact).ToList(),
        c.Nickname, c.CompanyNumber, c.CurrencyCode,
        c.Iban, c.Bic, c.BankName, c.BankAccountNumber,
        c.DefaultLegalEntityId,
        c.PeppolEnabled, c.PeppolDeliveryPreference.ToString(), c.BuyerReference,
        c.PeppolValidationStatus.ToString(), c.PeppolValidatedAt, c.PeppolValidationReference,
        allowedLegalEntityIds, c.InvoiceGrouping, c.DocumentStrategy);

    private Task<List<Guid>> LoadAllowedLegalEntityIdsAsync(Guid customerId, CancellationToken cancellationToken) =>
        _dbContext.CustomerAllowedLegalEntities.AsNoTracking()
            .Where(x => x.TenantId == _tenantContext.TenantId && x.CustomerId == customerId)
            .Select(x => x.LegalEntityId)
            .ToListAsync(cancellationToken);
}
