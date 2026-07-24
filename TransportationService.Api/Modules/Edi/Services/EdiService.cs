using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Edi.Entities;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Edi.Services;

public enum EdiIngestOutcome
{
    Accepted,
    UnknownPartner,
    UnsupportedType,
}

public record EdiIngestResult(EdiIngestOutcome Outcome, EdiMessage? Message, string? Error = null);

public interface IEdiService
{
    /// <summary>Stores and immediately processes an inbound payload; duplicates are stored but never reprocessed.</summary>
    Task<EdiIngestResult> IngestAsync(string partnerCode, string messageType, string payload, CancellationToken cancellationToken);

    /// <summary>Re-runs a failed/dead-lettered message (after mapping fixes). Null when unknown.</summary>
    Task<EdiMessage?> ReplayAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Outbound status payload for an order that entered through EDI; no-op otherwise.</summary>
    Task QueueOutboundStatusAsync(Guid orderId, string status, CancellationToken cancellationToken);
}

public class EdiService : IEdiService
{
    private const string EntityType = "EdiMessage";
    private const int MaxAttempts = 3;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly ITransportOrderService _orderService;
    private readonly TimeProvider _timeProvider;

    public EdiService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        ITransportOrderService orderService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _orderService = orderService;
        _timeProvider = timeProvider;
    }

    public async Task<EdiIngestResult> IngestAsync(
        string partnerCode, string messageType, string payload, CancellationToken cancellationToken)
    {
        var partner = await _dbContext.TradingPartners.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId
                                      && p.Code == partnerCode && p.IsActive, cancellationToken);
        if (partner is null)
        {
            return new EdiIngestResult(EdiIngestOutcome.UnknownPartner, null, "Onbekende of inactieve handelspartner.");
        }

        if (!string.Equals(messageType, "order", StringComparison.OrdinalIgnoreCase))
        {
            return new EdiIngestResult(EdiIngestOutcome.UnsupportedType, null,
                $"Berichttype '{messageType}' wordt (nog) niet ondersteund.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var externalReference = TryReadExternalReference(payload);

        var duplicate = await _dbContext.EdiMessages.AsNoTracking()
            .AnyAsync(m => m.TenantId == _tenantContext.TenantId && m.TradingPartnerId == partner.Id
                           && m.Direction == EdiDirection.Inbound
                           && (m.PayloadHash == hash
                               || (externalReference != null && m.ExternalReference == externalReference))
                           && m.Status != EdiProcessingStatus.Duplicate, cancellationToken);

        var message = new EdiMessage
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Direction = EdiDirection.Inbound,
            TradingPartnerId = partner.Id,
            MessageType = "order",
            ExternalReference = externalReference,
            PayloadJson = payload,
            PayloadHash = hash,
            Status = duplicate ? EdiProcessingStatus.Duplicate : EdiProcessingStatus.Received,
            ErrorDetail = duplicate ? "Duplicaat van een eerder ontvangen bericht; niet opnieuw verwerkt." : null,
        };
        _dbContext.Add(message);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, message.Id.ToString(),
            duplicate ? "DuplicateReceived" : "Received", null,
            new { partner.Code, message.ExternalReference }, cancellationToken);

        if (!duplicate)
        {
            await ProcessAsync(message, partner, cancellationToken);
        }

        return new EdiIngestResult(EdiIngestOutcome.Accepted, message);
    }

    public async Task<EdiMessage?> ReplayAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await _dbContext.EdiMessages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.TenantId == _tenantContext.TenantId, cancellationToken);
        if (message is null)
        {
            return null;
        }

        if (message.Status is not (EdiProcessingStatus.Failed or EdiProcessingStatus.DeadLettered))
        {
            return message;
        }

        var partner = await _dbContext.TradingPartners.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == message.TradingPartnerId && p.TenantId == _tenantContext.TenantId, cancellationToken);
        if (partner is null)
        {
            return message;
        }

        // A manual replay of a dead-lettered message gets a fresh attempt budget.
        if (message.Status == EdiProcessingStatus.DeadLettered)
        {
            message.AttemptCount = 0;
        }

        await _auditService.RecordAsync(EntityType, message.Id.ToString(), "Replayed", null, null, cancellationToken);
        await ProcessAsync(message, partner, cancellationToken);
        return message;
    }

    public async Task QueueOutboundStatusAsync(Guid orderId, string status, CancellationToken cancellationToken)
    {
        // Only orders that ENTERED through EDI report their status back.
        var inbound = await _dbContext.EdiMessages.AsNoTracking()
            .Where(m => m.TenantId == _tenantContext.TenantId && m.Direction == EdiDirection.Inbound
                        && m.ResultEntityId == orderId.ToString() && m.Status == EdiProcessingStatus.Processed)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (inbound is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            externalOrderId = inbound.ExternalReference,
            orderId,
            status,
            timestamp = _timeProvider.GetUtcNow().UtcDateTime,
        });

        _dbContext.Add(new EdiMessage
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Direction = EdiDirection.Outbound,
            TradingPartnerId = inbound.TradingPartnerId,
            MessageType = "status",
            ExternalReference = inbound.ExternalReference,
            PayloadJson = payload,
            PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
            // The payload is ready for a transport provider; without one it is immediately final.
            Status = EdiProcessingStatus.Processed,
            ProcessedAt = _timeProvider.GetUtcNow().UtcDateTime,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessAsync(EdiMessage message, TradingPartner partner, CancellationToken cancellationToken)
    {
        message.AttemptCount += 1;
        var errors = new List<string>();
        try
        {
            var order = ParseGenericJson(message.PayloadJson, errors);
            if (order is null || errors.Count > 0)
            {
                Fail(message, "Validatie mislukt.", errors);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            if (partner.CustomerId is not { } customerId)
            {
                Fail(message, "De handelspartner is niet aan een klant gekoppeld.", errors);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            // Resolve partner location codes to master locations.
            var mappings = await _dbContext.EdiPartnerLocations.AsNoTracking()
                .Where(l => l.TenantId == _tenantContext.TenantId && l.TradingPartnerId == partner.Id)
                .ToDictionaryAsync(l => l.ExternalLocationCode, l => l.LocationId, StringComparer.OrdinalIgnoreCase,
                    cancellationToken);

            var stops = new List<TransportOrderStopInput>();
            foreach (var stop in order.Stops)
            {
                Guid? locationId = null;
                if (!string.IsNullOrWhiteSpace(stop.ExternalLocationCode))
                {
                    if (!mappings.TryGetValue(stop.ExternalLocationCode, out var mapped))
                    {
                        errors.Add($"Onbekende locatiecode '{stop.ExternalLocationCode}' voor deze partner.");
                        continue;
                    }

                    locationId = mapped;
                }

                stops.Add(new TransportOrderStopInput(
                    stop.Type, locationId, null, null, null, stop.City, null,
                    stop.PlannedFrom, stop.PlannedTo, stop.Reference, null));
            }

            if (errors.Count > 0)
            {
                Fail(message, "Locatiemapping onvolledig.", errors);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var cargoItems = await ResolveCargoUnitsAsync(customerId, order.CargoItems, cancellationToken);

            var result = await _orderService.CreateAsync(new CreateTransportOrderRequest(
                customerId, order.CustomerReference, null, order.GoodsDescription,
                null, null, null, null, null, false, false, null,
                $"EDI-bericht van {partner.Name} ({order.ExternalOrderId})",
                stops, cargoItems), cancellationToken);

            if (result.Outcome != TransportOrderOperationOutcome.Success)
            {
                Fail(message, result.Error ?? $"Opdracht kon niet worden aangemaakt ({result.Outcome}).", errors);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            message.Status = EdiProcessingStatus.Processed;
            message.ProcessedAt = _timeProvider.GetUtcNow().UtcDateTime;
            message.ErrorDetail = null;
            message.ValidationErrorsJson = null;
            message.ResultEntityType = "TransportOrder";
            message.ResultEntityId = result.Order!.Id.ToString();
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditService.RecordAsync(EntityType, message.Id.ToString(), "Processed", null,
                new { OrderId = result.Order.Id, result.Order.OrderNumber }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(message, exception.Message, errors);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Maps raw external unit strings onto managed unit codes: the customer's configured
    /// EDI code wins, then a direct match on the global unit code. Unresolvable units keep
    /// only the free-text QuantityUnit so the order still imports.
    /// </summary>
    private async Task<IReadOnlyList<CargoItemInput>> ResolveCargoUnitsAsync(
        Guid customerId, IReadOnlyList<CargoItemInput> items, CancellationToken cancellationToken)
    {
        if (!items.Any(i => !string.IsNullOrWhiteSpace(i.QuantityUnit)))
        {
            return items;
        }

        var tenantId = _tenantContext.TenantId;
        var customerUnits = await _dbContext.CustomerPreferredUnits.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.CustomerId == customerId && u.EdiCode != null)
            .Join(_dbContext.UnitTypes.Where(t => t.TenantId == tenantId),
                pu => pu.UnitTypeId, ut => ut.Id,
                (pu, ut) => new { pu.EdiCode, ut.Code })
            .ToListAsync(cancellationToken);
        var byEdiCode = customerUnits
            .GroupBy(u => u.EdiCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Code, StringComparer.OrdinalIgnoreCase);
        var globalCodes = (await _dbContext.UnitTypes.AsNoTracking()
                .Where(t => t.TenantId == tenantId)
                .Select(t => t.Code)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return items.Select(item =>
        {
            var raw = item.QuantityUnit?.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                return item;
            }

            string? resolved = null;
            if (byEdiCode.TryGetValue(raw, out var viaCustomer))
            {
                resolved = viaCustomer;
            }
            else if (globalCodes.TryGetValue(raw, out var viaGlobal))
            {
                resolved = viaGlobal;
            }

            return resolved is null ? item : item with { QuantityUnitCode = resolved };
        }).ToList();
    }

    private void Fail(EdiMessage message, string error, IReadOnlyList<string> validationErrors)
    {
        message.Status = message.AttemptCount >= MaxAttempts
            ? EdiProcessingStatus.DeadLettered
            : EdiProcessingStatus.Failed;
        message.ErrorDetail = error;
        message.ValidationErrorsJson = validationErrors.Count > 0 ? JsonSerializer.Serialize(validationErrors) : null;
    }

    private sealed record ParsedStop(
        StopType Type, string? ExternalLocationCode, string? City,
        DateTime? PlannedFrom, DateTime? PlannedTo, string? Reference);

    private sealed record ParsedOrder(
        string ExternalOrderId, string? CustomerReference, string GoodsDescription,
        IReadOnlyList<ParsedStop> Stops, IReadOnlyList<CargoItemInput> CargoItems);

    /// <summary>
    /// The "generic-json-v1" mapping profile — the only implemented format, deliberately ours.
    /// Real partner formats plug in as additional profiles once a specification exists.
    /// </summary>
    private static ParsedOrder? ParseGenericJson(string payload, List<string> errors)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException exception)
        {
            errors.Add($"Ongeldige JSON: {exception.Message}");
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            var externalOrderId = ReadString(root, "externalOrderId");
            if (string.IsNullOrWhiteSpace(externalOrderId))
            {
                errors.Add("externalOrderId ontbreekt.");
            }

            var goods = ReadString(root, "goodsDescription");
            if (string.IsNullOrWhiteSpace(goods))
            {
                errors.Add("goodsDescription ontbreekt.");
            }

            var stops = new List<ParsedStop>();
            if (root.TryGetProperty("stops", out var stopsElement) && stopsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var stop in stopsElement.EnumerateArray())
                {
                    var typeText = ReadString(stop, "type");
                    if (!Enum.TryParse<StopType>(typeText, ignoreCase: true, out var type))
                    {
                        errors.Add($"Onbekend stoptype '{typeText}'.");
                        continue;
                    }

                    stops.Add(new ParsedStop(
                        type,
                        ReadString(stop, "externalLocationCode"),
                        ReadString(stop, "city"),
                        ReadDate(stop, "plannedFrom"),
                        ReadDate(stop, "plannedTo"),
                        ReadString(stop, "reference")));
                }
            }

            if (stops.Count == 0)
            {
                errors.Add("Minstens één stop is verplicht.");
            }

            var cargo = new List<CargoItemInput>();
            if (root.TryGetProperty("cargoItems", out var cargoElement) && cargoElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in cargoElement.EnumerateArray())
                {
                    var description = ReadString(item, "description");
                    if (string.IsNullOrWhiteSpace(description))
                    {
                        errors.Add("Een goederenlijn mist een omschrijving.");
                        continue;
                    }

                    var quantity = item.TryGetProperty("quantity", out var quantityElement)
                                   && quantityElement.TryGetDecimal(out var parsed)
                        ? parsed
                        : 1;
                    // The raw external unit code lands in QuantityUnit; it is resolved to a
                    // managed unit code (customer EDI mapping first) once the customer is known.
                    cargo.Add(new CargoItemInput(description, ReadString(item, "barcode"), quantity, ReadString(item, "unit"), null));
                }
            }

            return errors.Count > 0
                ? null
                : new ParsedOrder(externalOrderId!, ReadString(root, "customerReference"), goods!, stops, cargo);
        }
    }

    private static string? TryReadExternalReference(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return ReadString(document.RootElement, "externalOrderId");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTime? ReadDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        && DateTime.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
}
