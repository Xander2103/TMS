using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

public record IssuedItemTemplateDto(
    Guid Id, string Name, string Category, string? ApplicableJobFunctionCodes,
    int DefaultQuantity, bool RequiresSerialNumber, bool RequiresReceivedDate, bool ReturnRequired,
    bool IsActive, int SortOrder);

public record SaveIssuedItemTemplateRequest(
    string Name, string Category, string? ApplicableJobFunctionCodes,
    int DefaultQuantity, bool RequiresSerialNumber, bool RequiresReceivedDate, bool ReturnRequired,
    bool IsActive, int SortOrder);

public record EmployeeIssuedItemDto(
    Guid Id, Guid? TemplateId, string Name, string Category, IssuedItemStatus Status,
    DateOnly? IssuedDate, int Quantity, string? SerialNumber, string? Notes, Guid? IssuedByUserId,
    DateOnly? ReturnedDate, string? ReturnCondition, Guid? ReceivedBackByUserId);

public record SaveEmployeeIssuedItemRequest(
    Guid? TemplateId, string? Name, string? Category, IssuedItemStatus Status,
    DateOnly? IssuedDate, int Quantity, string? SerialNumber, string? Notes,
    DateOnly? ReturnedDate, string? ReturnCondition);

public interface IIssuedItemService
{
    Task<IReadOnlyList<IssuedItemTemplateDto>> ListTemplatesAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<IssuedItemTemplateDto> CreateTemplateAsync(SaveIssuedItemTemplateRequest request, CancellationToken cancellationToken);
    Task<IssuedItemTemplateDto?> UpdateTemplateAsync(Guid id, SaveIssuedItemTemplateRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteTemplateAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<EmployeeIssuedItemDto>?> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);
    Task<EmployeeIssuedItemDto?> UpsertAsync(Guid employeeId, Guid? itemId, SaveEmployeeIssuedItemRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteItemAsync(Guid employeeId, Guid itemId, CancellationToken cancellationToken);
    Task<byte[]?> BuildAcknowledgementAsync(Guid employeeId, CancellationToken cancellationToken);
}

public class IssuedItemService : IIssuedItemService
{
    private const string TemplateEntity = "IssuedItemTemplate";
    private const string ItemEntity = "EmployeeIssuedItem";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAuditService _auditService;

    public IssuedItemService(TransportationDbContext dbContext, ITenantContext tenantContext,
        ICurrentUserContext currentUser, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<IssuedItemTemplateDto>> ListTemplatesAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        return await _dbContext.IssuedItemTemplates
            .Where(t => t.TenantId == _tenantContext.TenantId && (includeInactive || t.IsActive))
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Category).ThenBy(t => t.Name)
            .Select(t => Map(t))
            .ToListAsync(cancellationToken);
    }

    public async Task<IssuedItemTemplateDto> CreateTemplateAsync(SaveIssuedItemTemplateRequest request, CancellationToken cancellationToken)
    {
        var template = new IssuedItemTemplate { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId };
        Apply(template, request);
        _dbContext.IssuedItemTemplates.Add(template);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(TemplateEntity, template.Id.ToString(), "Created", null, new { template.Name, template.Category }, cancellationToken);
        return Map(template);
    }

    public async Task<IssuedItemTemplateDto?> UpdateTemplateAsync(Guid id, SaveIssuedItemTemplateRequest request, CancellationToken cancellationToken)
    {
        var template = await _dbContext.IssuedItemTemplates
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == id, cancellationToken);
        if (template is null)
        {
            return null;
        }

        Apply(template, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(TemplateEntity, template.Id.ToString(), "Updated", null, new { template.Name, template.IsActive }, cancellationToken);
        return Map(template);
    }

    public async Task<bool> DeleteTemplateAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await _dbContext.IssuedItemTemplates
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == id, cancellationToken);
        if (template is null)
        {
            return false;
        }

        _dbContext.Remove(template); // soft delete; historical employee items keep their snapshot
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(TemplateEntity, template.Id.ToString(), "Deleted", new { template.Name }, null, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<EmployeeIssuedItemDto>?> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return null;
        }

        return await _dbContext.EmployeeIssuedItems
            .Where(i => i.TenantId == _tenantContext.TenantId && i.EmployeeId == employeeId)
            .OrderBy(i => i.CategorySnapshot).ThenBy(i => i.NameSnapshot)
            .Select(i => Map(i))
            .ToListAsync(cancellationToken);
    }

    public async Task<EmployeeIssuedItemDto?> UpsertAsync(
        Guid employeeId, Guid? itemId, SaveEmployeeIssuedItemRequest request, CancellationToken cancellationToken)
    {
        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return null;
        }

        EmployeeIssuedItem item;
        if (itemId is { } id)
        {
            item = await _dbContext.EmployeeIssuedItems
                .FirstOrDefaultAsync(i => i.TenantId == _tenantContext.TenantId && i.EmployeeId == employeeId && i.Id == id, cancellationToken)
                ?? throw new DomainValidationException("Het item bestaat niet.");
        }
        else
        {
            item = new EmployeeIssuedItem { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId, EmployeeId = employeeId };

            // Snapshot the name/category from the template (or the request) at issue time.
            if (request.TemplateId is { } templateId)
            {
                var template = await _dbContext.IssuedItemTemplates
                    .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == templateId, cancellationToken)
                    ?? throw new DomainValidationException("templateId", "Het sjabloon bestaat niet.");
                item.TemplateId = templateId;
                item.NameSnapshot = template.Name;
                item.CategorySnapshot = template.Category;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    throw new DomainValidationException("name", "Een naam of sjabloon is verplicht.");
                }

                item.NameSnapshot = request.Name.Trim();
                item.CategorySnapshot = string.IsNullOrWhiteSpace(request.Category) ? "Algemeen" : request.Category.Trim();
            }

            _dbContext.EmployeeIssuedItems.Add(item);
        }

        ApplyItemState(item, request);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(ItemEntity, item.Id.ToString(),
            item.Status == IssuedItemStatus.Returned ? "Returned" : "Recorded", null,
            new { item.EmployeeId, item.NameSnapshot, item.Status }, cancellationToken);

        return Map(item);
    }

    public async Task<bool> DeleteItemAsync(Guid employeeId, Guid itemId, CancellationToken cancellationToken)
    {
        var item = await _dbContext.EmployeeIssuedItems
            .FirstOrDefaultAsync(i => i.TenantId == _tenantContext.TenantId && i.EmployeeId == employeeId && i.Id == itemId, cancellationToken);
        if (item is null)
        {
            return false;
        }

        _dbContext.Remove(item);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(ItemEntity, item.Id.ToString(), "Deleted", new { item.NameSnapshot }, null, cancellationToken);
        return true;
    }

    public async Task<byte[]?> BuildAcknowledgementAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == _tenantContext.TenantId, cancellationToken);
        if (employee is null)
        {
            return null;
        }

        var items = await _dbContext.EmployeeIssuedItems.AsNoTracking()
            .Where(i => i.TenantId == _tenantContext.TenantId && i.EmployeeId == employeeId
                        && (i.Status == IssuedItemStatus.Issued || i.Status == IssuedItemStatus.Returned))
            .OrderBy(i => i.CategorySnapshot).ThenBy(i => i.NameSnapshot)
            .ToListAsync(cancellationToken);

        var companyName = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId)
            .Select(s => s.TradingName ?? s.CompanyLegalName)
            .FirstOrDefaultAsync(cancellationToken) ?? "";

        return IssuedItemAcknowledgementRenderer.Render(companyName, $"{employee.FirstName} {employee.LastName}",
            employee.EmployeeNumber, items);
    }

    private static void ApplyItemState(EmployeeIssuedItem item, SaveEmployeeIssuedItemRequest request)
    {
        item.Status = request.Status;
        item.IssuedDate = request.IssuedDate;
        item.Quantity = Math.Max(1, request.Quantity);
        item.SerialNumber = Trim(request.SerialNumber);
        item.Notes = Trim(request.Notes);
        item.ReturnedDate = request.ReturnedDate;
        item.ReturnCondition = Trim(request.ReturnCondition);
    }

    private void Apply(IssuedItemTemplate template, SaveIssuedItemTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "De naam is verplicht.");
        }

        template.Name = request.Name.Trim();
        template.Category = string.IsNullOrWhiteSpace(request.Category) ? "Algemeen" : request.Category.Trim();
        template.ApplicableJobFunctionCodes = Trim(request.ApplicableJobFunctionCodes);
        template.DefaultQuantity = Math.Max(1, request.DefaultQuantity);
        template.RequiresSerialNumber = request.RequiresSerialNumber;
        template.RequiresReceivedDate = request.RequiresReceivedDate;
        template.ReturnRequired = request.ReturnRequired;
        template.IsActive = request.IsActive;
        template.SortOrder = request.SortOrder;
    }

    private Task<bool> EmployeeExistsAsync(Guid employeeId, CancellationToken cancellationToken) =>
        _dbContext.Employees.AnyAsync(e => e.TenantId == _tenantContext.TenantId && e.Id == employeeId, cancellationToken);

    private static IssuedItemTemplateDto Map(IssuedItemTemplate t) => new(
        t.Id, t.Name, t.Category, t.ApplicableJobFunctionCodes,
        t.DefaultQuantity, t.RequiresSerialNumber, t.RequiresReceivedDate, t.ReturnRequired, t.IsActive, t.SortOrder);

    private static EmployeeIssuedItemDto Map(EmployeeIssuedItem i) => new(
        i.Id, i.TemplateId, i.NameSnapshot, i.CategorySnapshot, i.Status,
        i.IssuedDate, i.Quantity, i.SerialNumber, i.Notes, i.IssuedByUserId,
        i.ReturnedDate, i.ReturnCondition, i.ReceivedBackByUserId);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
