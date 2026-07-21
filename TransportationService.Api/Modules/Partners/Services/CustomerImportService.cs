using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Partners.Services;

public enum CustomerImportRowAction
{
    Create,
    Update,
    Error,
}

public sealed record CustomerImportRowResult(
    int RowNumber,
    CustomerImportRowAction Action,
    string? CustomerNumber,
    string Name,
    IReadOnlyList<string> Messages);

public sealed record CustomerImportPreviewDto(
    int TotalRows,
    int Creates,
    int Updates,
    int Errors,
    IReadOnlyList<CustomerImportRowResult> Rows);

public sealed record CustomerImportCommitDto(
    int Created,
    int Updated,
    int Failed,
    bool Committed,
    IReadOnlyList<CustomerImportRowResult> Rows,
    string? ErrorWorkbookBase64);

public interface ICustomerImportService
{
    byte[] BuildTemplate();

    Task<(CustomerImportPreviewDto? Preview, string? Error)> PreviewAsync(
        Stream workbook, CancellationToken cancellationToken);

    /// <summary>
    /// allOrNothing (default in the UI): any invalid row aborts the whole import — no partial
    /// customer files. allowUpdates permits matching EXISTING customers by CustomerNumber;
    /// otherwise a known number is an error, never a silent merge.
    /// </summary>
    Task<(CustomerImportCommitDto? Result, string? Error)> CommitAsync(
        Stream workbook, bool allOrNothing, bool allowUpdates, CancellationToken cancellationToken);
}

/// <summary>
/// Excel (.xlsx) customer import with existing customer numbers, mirroring the package
/// import architecture: template + preview + commit, exact row/value error reporting and a
/// downloadable error workbook. Customer numbers are commercially significant (accounting
/// links) — duplicates are always rejected, both inside the file and against the database.
/// </summary>
public class CustomerImportService : ICustomerImportService
{
    private const int MaxRows = 1000;

    private static readonly string[] Headers =
    [
        "Klantnummer*", "Naam*", "Officiële naam", "BTW-nummer", "E-mail", "Telefoon", "Website",
        "Straat", "Huisnummer", "Postcode", "Gemeente", "Landcode", "Factuur-e-mail",
        "Betaaltermijn (dagen)", "Taalcode", "Notities",
    ];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public CustomerImportService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    public byte[] BuildTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Klanten");
        for (var column = 0; column < Headers.Length; column += 1)
        {
            sheet.Cell(1, column + 1).SetValue(Headers[column]).Style.Font.Bold = true;
        }

        // One example row (text values only — never formulas).
        sheet.Cell(2, 1).SetValue("KL-0001");
        sheet.Cell(2, 2).SetValue("Haven Logistiek BV");
        sheet.Cell(2, 4).SetValue("BE0417497106");
        sheet.Cell(2, 12).SetValue("BE");
        sheet.Cell(2, 14).SetValue(30);
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();

        var help = workbook.AddWorksheet("Uitleg");
        var lines = new[]
        {
            "Kolommen met * zijn verplicht.",
            "Klantnummer: het bestaande klantnummer uit uw boekhouding. Moet uniek zijn binnen het bedrijf.",
            "Een bestaand klantnummer wordt alleen bijgewerkt wanneer 'bestaande klanten bijwerken' aan staat.",
            "BTW-nummer: Belgische nummers worden streng gevalideerd (BE + 10 cijfers).",
            "Landcode: ISO-code van 2 letters (bv. BE, NL, FR).",
            "Betaaltermijn: geheel aantal dagen (0-365); leeg = 30.",
            $"Maximaal {MaxRows} rijen per import.",
        };
        for (var index = 0; index < lines.Length; index += 1)
        {
            help.Cell(index + 1, 1).SetValue(lines[index]);
        }

        help.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<(CustomerImportPreviewDto? Preview, string? Error)> PreviewAsync(
        Stream workbook, CancellationToken cancellationToken)
    {
        var parsed = await ParseAndValidateAsync(workbook, allowUpdates: true, cancellationToken);
        if (parsed.Error is not null)
        {
            return (null, parsed.Error);
        }

        var rows = parsed.Rows.Select(r => r.Result).ToList();
        return (new CustomerImportPreviewDto(
            rows.Count,
            rows.Count(r => r.Action == CustomerImportRowAction.Create),
            rows.Count(r => r.Action == CustomerImportRowAction.Update),
            rows.Count(r => r.Action == CustomerImportRowAction.Error),
            rows), null);
    }

    public async Task<(CustomerImportCommitDto? Result, string? Error)> CommitAsync(
        Stream workbook, bool allOrNothing, bool allowUpdates, CancellationToken cancellationToken)
    {
        var parsed = await ParseAndValidateAsync(workbook, allowUpdates, cancellationToken);
        if (parsed.Error is not null)
        {
            return (null, parsed.Error);
        }

        var rows = parsed.Rows;
        var errorRows = rows.Where(r => r.Result.Action == CustomerImportRowAction.Error).ToList();
        if (allOrNothing && errorRows.Count > 0)
        {
            return (new CustomerImportCommitDto(0, 0, errorRows.Count, Committed: false,
                rows.Select(r => r.Result).ToList(), BuildErrorWorkbook(errorRows)), null);
        }

        var created = 0;
        foreach (var row in rows.Where(r => r.Result.Action == CustomerImportRowAction.Create))
        {
            _dbContext.Customers.Add(new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                CustomerNumber = row.CustomerNumber!,
                Name = row.Name!,
                LegalName = row.LegalName,
                VatNumber = row.VatNumber,
                Email = row.Email,
                PhoneNumber = row.Phone,
                Website = row.Website,
                Street = row.Street,
                HouseNumber = row.HouseNumber,
                PostalCode = row.PostalCode,
                City = row.City,
                CountryCode = row.CountryCode,
                InvoiceEmail = row.InvoiceEmail,
                PaymentTermDays = row.PaymentTermDays ?? 30,
                DefaultLanguageCode = row.LanguageCode,
                Notes = row.Notes,
                IsActive = true,
            });
            created += 1;
        }

        var updated = 0;
        foreach (var row in rows.Where(r => r.Result.Action == CustomerImportRowAction.Update))
        {
            var customer = parsed.ExistingByNumber[row.CustomerNumber!];
            customer.Name = row.Name!;
            if (row.LegalName is not null) customer.LegalName = row.LegalName;
            if (row.VatNumber is not null) customer.VatNumber = row.VatNumber;
            if (row.Email is not null) customer.Email = row.Email;
            if (row.Phone is not null) customer.PhoneNumber = row.Phone;
            if (row.Website is not null) customer.Website = row.Website;
            if (row.Street is not null) customer.Street = row.Street;
            if (row.HouseNumber is not null) customer.HouseNumber = row.HouseNumber;
            if (row.PostalCode is not null) customer.PostalCode = row.PostalCode;
            if (row.City is not null) customer.City = row.City;
            if (row.CountryCode is not null) customer.CountryCode = row.CountryCode;
            if (row.InvoiceEmail is not null) customer.InvoiceEmail = row.InvoiceEmail;
            if (row.PaymentTermDays is { } term) customer.PaymentTermDays = term;
            if (row.LanguageCode is not null) customer.DefaultLanguageCode = row.LanguageCode;
            if (row.Notes is not null) customer.Notes = row.Notes;
            updated += 1;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("Customer", _tenantContext.TenantId.ToString(), "Imported", null,
            new { Created = created, Updated = updated, Failed = errorRows.Count }, cancellationToken);

        return (new CustomerImportCommitDto(
            created, updated, errorRows.Count, Committed: true,
            rows.Select(r => r.Result).ToList(),
            errorRows.Count > 0 ? BuildErrorWorkbook(errorRows) : null), null);
    }

    // ------------------------------------------------------------- parsing

    private sealed record ParsedRow(
        CustomerImportRowResult Result,
        string? CustomerNumber,
        string? Name,
        string? LegalName,
        string? VatNumber,
        string? Email,
        string? Phone,
        string? Website,
        string? Street,
        string? HouseNumber,
        string? PostalCode,
        string? City,
        string? CountryCode,
        string? InvoiceEmail,
        int? PaymentTermDays,
        string? LanguageCode,
        string? Notes,
        IReadOnlyList<string> RawCells);

    private sealed record ParseOutcome(
        List<ParsedRow> Rows, Dictionary<string, Customer> ExistingByNumber, string? Error);

    private async Task<ParseOutcome> ParseAndValidateAsync(
        Stream stream, bool allowUpdates, CancellationToken cancellationToken)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch
        {
            return new ParseOutcome([], [], "Het bestand is geen geldig Excel-werkboek (.xlsx).");
        }

        using var _ = workbook;
        var sheet = workbook.Worksheets.FirstOrDefault();
        if (sheet is null)
        {
            return new ParseOutcome([], [], "Het werkboek bevat geen werkblad.");
        }

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        if (lastRow - 1 > MaxRows)
        {
            return new ParseOutcome([], [], $"Maximaal {MaxRows} rijen per import; dit bestand bevat er {lastRow - 1}.");
        }

        var existingCustomers = await _dbContext.Customers
            .Where(c => c.TenantId == _tenantContext.TenantId)
            .ToListAsync(cancellationToken);
        var existingByNumber = existingCustomers.ToDictionary(c => c.CustomerNumber, StringComparer.OrdinalIgnoreCase);

        var validCountryCodes = (await _dbContext.Countries.AsNoTracking()
                .Where(c => c.IsActive)
                .Select(c => c.Code)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<ParsedRow>();
        var seenNumbersInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber += 1)
        {
            var row = sheet.Row(rowNumber);
            if (row.CellsUsed().All(c => string.IsNullOrWhiteSpace(c.GetString())))
            {
                continue;
            }

            string Cell(int column) => row.Cell(column).GetString().Trim();
            var raw = Enumerable.Range(1, Headers.Length).Select(Cell).ToList();
            var messages = new List<string>();

            var customerNumber = NullIfEmpty(Cell(1));
            var name = NullIfEmpty(Cell(2));
            var legalName = NullIfEmpty(Cell(3));
            var vatNumber = NullIfEmpty(Cell(4));
            var email = NullIfEmpty(Cell(5));
            var phone = NullIfEmpty(Cell(6));
            var website = NullIfEmpty(Cell(7));
            var street = NullIfEmpty(Cell(8));
            var houseNumber = NullIfEmpty(Cell(9));
            var postalCode = NullIfEmpty(Cell(10));
            var city = NullIfEmpty(Cell(11));
            var countryCode = NullIfEmpty(Cell(12))?.ToUpperInvariant();
            var invoiceEmail = NullIfEmpty(Cell(13));
            var paymentTerm = ParseInt(Cell(14), "Betaaltermijn", messages);
            var languageCode = NullIfEmpty(Cell(15))?.ToLowerInvariant();
            var notes = NullIfEmpty(Cell(16));

            var action = CustomerImportRowAction.Create;

            if (customerNumber is null)
            {
                messages.Add("Klantnummer is verplicht.");
            }
            else if (customerNumber.Length > 30)
            {
                messages.Add($"Klantnummer '{customerNumber}' is langer dan 30 tekens.");
            }
            else if (!seenNumbersInFile.Add(customerNumber))
            {
                messages.Add($"Klantnummer '{customerNumber}' staat meermaals in het bestand.");
            }
            else if (existingByNumber.ContainsKey(customerNumber))
            {
                if (allowUpdates)
                {
                    action = CustomerImportRowAction.Update;
                }
                else
                {
                    messages.Add($"Klantnummer '{customerNumber}' is al in gebruik.");
                }
            }

            if (name is null)
            {
                messages.Add("Naam is verplicht.");
            }

            if (vatNumber is not null)
            {
                try
                {
                    vatNumber = VatNumberValidator.NormalizeAndValidate(vatNumber);
                }
                catch (DomainValidationException ex)
                {
                    messages.Add(ex.Message);
                }
            }

            if (countryCode is not null && !validCountryCodes.Contains(countryCode))
            {
                messages.Add($"Landcode '{countryCode}' is onbekend.");
            }

            if (paymentTerm is { } term && term is < 0 or > 365)
            {
                messages.Add($"Betaaltermijn '{term}' moet tussen 0 en 365 dagen liggen.");
            }

            if (messages.Count > 0)
            {
                action = CustomerImportRowAction.Error;
            }

            rows.Add(new ParsedRow(
                new CustomerImportRowResult(rowNumber, action, customerNumber, name ?? "?", messages),
                customerNumber, name, legalName, vatNumber, email, phone, website,
                street, houseNumber, postalCode, city, countryCode, invoiceEmail,
                paymentTerm, languageCode, notes, raw));
        }

        if (rows.Count == 0)
        {
            return new ParseOutcome([], existingByNumber, "Het bestand bevat geen datarijen.");
        }

        return new ParseOutcome(rows, existingByNumber, null);
    }

    /// <summary>Error workbook: the offending rows verbatim + a Fouten column (text values only).</summary>
    private static string BuildErrorWorkbook(IReadOnlyList<ParsedRow> errorRows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Fouten");
        sheet.Cell(1, 1).SetValue("Rij").Style.Font.Bold = true;
        for (var column = 0; column < Headers.Length; column += 1)
        {
            sheet.Cell(1, column + 2).SetValue(Headers[column]).Style.Font.Bold = true;
        }

        sheet.Cell(1, Headers.Length + 2).SetValue("Fouten").Style.Font.Bold = true;

        var rowIndex = 2;
        foreach (var row in errorRows)
        {
            sheet.Cell(rowIndex, 1).SetValue(row.Result.RowNumber);
            for (var column = 0; column < row.RawCells.Count; column += 1)
            {
                sheet.Cell(rowIndex, column + 2).SetValue(row.RawCells[column]);
            }

            sheet.Cell(rowIndex, Headers.Length + 2).SetValue(string.Join("; ", row.Result.Messages));
            rowIndex += 1;
        }

        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Convert.ToBase64String(stream.ToArray());
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int? ParseInt(string value, string label, List<string> messages)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value, out var parsed))
        {
            return parsed;
        }

        messages.Add($"{label} '{value}' is geen geldig geheel getal.");
        return null;
    }
}
