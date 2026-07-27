using TransportationService.Api.Modules.Accounting.Entities;

namespace TransportationService.Api.Modules.Accounting.Dtos;

public record LedgerAccountDto(
    Guid Id, string AccountNumber, string Name, string? ExternalCode, string? Description, bool IsActive);

public record SaveLedgerAccountRequest(
    string AccountNumber, string Name, string? ExternalCode = null, string? Description = null, bool IsActive = true);

public record SalesCategoryDto(
    Guid Id, string Code, string Name, SalesCategorySystemRole SystemRole,
    Guid? LedgerAccountId, string? LedgerAccountNumber, string? LedgerAccountName,
    bool IsActive, int SortOrder);

public record SaveSalesCategoryRequest(
    string Code, string Name, SalesCategorySystemRole SystemRole = SalesCategorySystemRole.None,
    Guid? LedgerAccountId = null, bool IsActive = true, int SortOrder = 0);

/// <summary>Configuration health: which active sales categories still miss a ledger account.</summary>
public record AccountingHealthDto(IReadOnlyList<SalesCategoryDto> UnmappedCategories);
