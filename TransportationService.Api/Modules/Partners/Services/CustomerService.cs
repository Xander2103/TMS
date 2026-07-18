using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
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

    public CustomerService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
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
        return MapToDetail(customer, categoryName);
    }

    public async Task<CustomerDetailDto> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        await EnsureCategoryInTenantAsync(request.CategoryId, cancellationToken);

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Name = request.Name.Trim(),
            LegalName = Trim(request.LegalName),
            VatNumber = Trim(request.VatNumber),
            CategoryId = request.CategoryId,
            Email = Trim(request.Email),
            PhoneNumber = Trim(request.PhoneNumber),
            Website = Trim(request.Website),
            Street = Trim(request.Street),
            HouseNumber = Trim(request.HouseNumber),
            PostalCode = Trim(request.PostalCode),
            City = Trim(request.City),
            CountryCode = Trim(request.CountryCode),
            InvoiceEmail = Trim(request.InvoiceEmail),
            PaymentTermDays = request.PaymentTermDays < 0 ? 0 : request.PaymentTermDays,
            DefaultLanguageCode = Trim(request.DefaultLanguageCode),
            Notes = Trim(request.Notes),
            IsActive = true,
        };

        _dbContext.Customers.Add(customer);
        await TenantNumbering.SaveWithClaimedNumberAsync(
            _dbContext, settings,
            () => customer.CustomerNumber = GenerateCustomerNumber(settings),
            cancellationToken);

        await _auditService.RecordAsync(EntityType, customer.Id.ToString(), "Created", null,
            new { customer.CustomerNumber, customer.Name }, cancellationToken);

        var categoryName = await ResolveCategoryNameAsync(customer.CategoryId, cancellationToken);
        return MapToDetail(customer, categoryName);
    }

    public async Task<CustomerDetailDto?> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await TenantScoped().Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        await EnsureCategoryInTenantAsync(request.CategoryId, cancellationToken);

        var oldValues = new { customer.Name, customer.IsActive, customer.CategoryId };

        customer.Name = request.Name.Trim();
        customer.LegalName = Trim(request.LegalName);
        customer.VatNumber = Trim(request.VatNumber);
        customer.CategoryId = request.CategoryId;
        customer.Email = Trim(request.Email);
        customer.PhoneNumber = Trim(request.PhoneNumber);
        customer.Website = Trim(request.Website);
        customer.Street = Trim(request.Street);
        customer.HouseNumber = Trim(request.HouseNumber);
        customer.PostalCode = Trim(request.PostalCode);
        customer.City = Trim(request.City);
        customer.CountryCode = Trim(request.CountryCode);
        customer.InvoiceEmail = Trim(request.InvoiceEmail);
        customer.PaymentTermDays = request.PaymentTermDays < 0 ? 0 : request.PaymentTermDays;
        customer.DefaultLanguageCode = Trim(request.DefaultLanguageCode);
        customer.Notes = Trim(request.Notes);
        customer.IsActive = request.IsActive;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customer.Id.ToString(), "Updated", oldValues,
            new { customer.Name, customer.IsActive, customer.CategoryId }, cancellationToken);

        var categoryName = await ResolveCategoryNameAsync(customer.CategoryId, cancellationToken);
        return MapToDetail(customer, categoryName);
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

    public async Task<CustomerContactDto?> AddContactAsync(Guid customerId, CreateCustomerContactRequest request, CancellationToken cancellationToken)
    {
        var customer = await TenantScoped().Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var contact = new CustomerContact
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            CustomerId = customerId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Role = Trim(request.Role),
            Email = Trim(request.Email),
            PhoneNumber = Trim(request.PhoneNumber),
            IsPrimary = request.IsPrimary,
            Notes = Trim(request.Notes),
        };

        if (contact.IsPrimary)
        {
            DemoteOtherPrimaries(customer, exceptContactId: contact.Id);
        }

        // Add through the DbSet (not the tracked parent's navigation): a new child with a
        // pre-set Guid key added to a tracked collection is mis-inferred by EF as an existing
        // row and issued as an UPDATE. DbSet.Add forces the Added state.
        _dbContext.CustomerContacts.Add(contact);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customer.Id.ToString(), "ContactAdded", null,
            new { contact.Id, contact.FirstName, contact.LastName }, cancellationToken);

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

        contact.FirstName = request.FirstName.Trim();
        contact.LastName = request.LastName.Trim();
        contact.Role = Trim(request.Role);
        contact.Email = Trim(request.Email);
        contact.PhoneNumber = Trim(request.PhoneNumber);
        contact.IsPrimary = request.IsPrimary;
        contact.Notes = Trim(request.Notes);

        if (contact.IsPrimary)
        {
            DemoteOtherPrimaries(customer, exceptContactId: contact.Id);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customer.Id.ToString(), "ContactUpdated", null,
            new { contact.Id, contact.FirstName, contact.LastName }, cancellationToken);

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

        _dbContext.Set<CustomerContact>().Remove(contact);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customer.Id.ToString(), "ContactRemoved",
            new { contact.Id, contact.FirstName, contact.LastName }, null, cancellationToken);

        return true;
    }

    private static void DemoteOtherPrimaries(Customer customer, Guid exceptContactId)
    {
        foreach (var other in customer.Contacts.Where(c => c.Id != exceptContactId && c.IsPrimary))
        {
            other.IsPrimary = false;
        }
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

    private static CustomerContactDto MapContact(CustomerContact c) =>
        new(c.Id, c.FirstName, c.LastName, c.Role, c.Email, c.PhoneNumber, c.IsPrimary, c.Notes);

    private static CustomerDetailDto MapToDetail(Customer c, string? categoryName) => new(
        c.Id, c.CustomerNumber, c.Name, c.LegalName, c.VatNumber, c.CategoryId, categoryName,
        c.Email, c.PhoneNumber, c.Website,
        c.Street, c.HouseNumber, c.PostalCode, c.City, c.CountryCode,
        c.InvoiceEmail, c.PaymentTermDays, c.DefaultLanguageCode, c.Notes,
        c.IsActive, c.IsBlocked, c.BlockReason,
        c.Contacts.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.LastName).Select(MapContact).ToList());
}
