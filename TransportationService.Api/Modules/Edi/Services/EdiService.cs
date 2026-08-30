using System.Globalization;
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

/// <summary>What would be created if the payload were ingested for real.</summary>
public record EdiValidationSummary(
    string ExternalOrderId, string? CustomerReference, string GoodsDescription,
    int StopCount, int CargoLineCount,
    IReadOnlyList<string> ResolvedLocationCodes, IReadOnlyList<string> ResolvedUnitCodes);

/// <summary>Dry-run outcome: never persists an <see cref="EdiMessage"/> or transport order.</summary>
public record EdiValidationResult(bool Valid, IReadOnlyList<string> Errors, EdiValidationSummary? WouldCreate);

public interface IEdiService
{
    /// <summary>Stores and immediately processes an inbound payload; duplicates are stored but never reprocessed.</summary>
    Task<EdiIngestResult> IngestAsync(string partnerCode, string messageType, string payload, CancellationToken cancellationToken);

    /// <summary>Re-runs a failed/dead-lettered message (after mapping fixes). Null when unknown.</summary>
    Task<EdiMessage?> ReplayAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Outbound status payload for an order that entered through EDI; no-op otherwise.</summary>
    Task QueueOutboundStatusAsync(Guid orderId, string status, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the same parse + partner/location + unit resolution pipeline as ingestion, WITHOUT
    /// creating an <see cref="EdiMessage"/> row or a transport order �?" for the "Testen" tab's
    /// "Valideren zonder te versturen" action.
    /// </summary>
    Task<EdiValidationResult> ValidateAsync(string partnerCode, string messageType, string payload, CancellationToken cancellationToken);
}

public class EdiService : IEdiService
{
    private const string EntityType = "EdiMessage";
    private const int MaxAttempts = 3;

    /// <summary>Public marker used both as EdiMessage.FailureKind and in the mapping-issue filter.</summary>
    public const string FailureKindMapping = "mapping";
    public const string FailureKindValidation = "validation";
    public const string FailureKindProcessing = "processing";

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

    public async Task<EdiValidationResult> ValidateAsync(
        string partnerCode, string messageType, string payload, CancellationToken cancellationToken)
    {
        var partner = await _dbContext.TradingPartners.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId
                                      && p.Code == partnerCode && p.IsActive, cancellationToken);
        if (partner is null)
        {
            return new EdiValidationResult(false, ["Onbekende of inactieve handelspartner."], null);
        }

        if (!string.Equals(messageType, "order", StringComparison.OrdinalIgnoreCase))
        {
            return new EdiValidationResult(false,
                [$"Berichttype '{messageType}' wordt (nog) niet ondersteund."], null);
        }

        var prepared = await PrepareAsync(partner, payload, cancellationToken);
        if (prepared.Order is null)
        {
            var errors = prepared.Errors.Count > 0 ? prepared.Errors : [prepared.ErrorDetail ?? "Validatie mislukt."];
            return new EdiValidationResult(false, errors, null);
        }

        var summary = new EdiValidationSummary(
            prepared.Order.ExternalOrderId, prepared.Order.CustomerReference, prepared.Order.GoodsDescription,
            prepared.Stops.Count, prepared.CargoItems.Count,
            prepared.Order.Stops.Where(s => s.ExternalLocationCode is not null).Select(s => s.ExternalLocationCode!).ToList(),
            prepared.CargoItems.Where(c => c.QuantityUnitCode is not null).Select(c => c.QuantityUnitCode!).Distinct().ToList());
        return new EdiValidationResult(true, [], summary);
    }

    private async Task ProcessAsync(EdiMessage message, TradingPartner partner, CancellationToken cancellationToken)
    {
        message.AttemptCount += 1;
        try
        {
            var prepared = await PrepareAsync(partner, message.PayloadJson, cancellationToken);
            if (prepared.Order is null)
            {
                Fail(message, prepared.ErrorDetail ?? "Validatie mislukt.", prepared.Errors, prepared.FailureKind ?? FailureKindValidation);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var result = await _orderService.CreateAsync(new CreateTransportOrderRequest(
                prepared.CustomerId, prepared.Order.CustomerReference, null, prepared.Order.GoodsDescription,
                null, null, null, null, null, false, false, null,
                $"EDI-bericht van {partner.Name} ({prepared.Order.ExternalOrderId})",
                prepared.Stops, prepared.CargoItems), cancellationToken);

            if (result.Outcome != TransportOrderOperationOutcome.Success)
            {
                Fail(message, result.Error ?? $"Opdracht kon niet worden aangemaakt ({result.Outcome}).",
                    prepared.Errors, FailureKindProcessing);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            message.Status = EdiProcessingStatus.Processed;
            message.ProcessedAt = _timeProvider.GetUtcNow().UtcDateTime;
            message.ErrorDetail = null;
            message.ValidationErrorsJson = null;
            message.FailureKind = null;
            message.ResultEntityType = "TransportOrder";
            message.ResultEntityId = result.Order!.Id.ToString();
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditService.RecordAsync(EntityType, message.Id.ToString(), "Processed", null,
                new { OrderId = result.Order.Id, result.Order.OrderNumber }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(message, exception.Message, [], FailureKindProcessing);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed record PreparedOrder(
        ParsedOrder? Order, Guid CustomerId, IReadOnlyList<TransportOrderStopInput> Stops,
        IReadOnlyList<CargoItemInput> CargoItems, List<string> Errors, string? ErrorDetail, string? FailureKind);

    /// <summary>
    /// The reusable core behind both real processing and the dry-run validate endpoint: parses
    /// the generic-JSON payload, requires the partner to be linked to a customer, resolves the
    /// partner's location codes to master locations, and resolves cargo unit codes. Never
    /// touches <c>EdiMessage</c> or persists anything itself �?" callers decide what to do with
    /// the result.
    /// </summary>
    private async Task<PreparedOrder> PrepareAsync(TradingPartner partner, string payloadJson, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var order = ParseGenericJson(payloadJson, errors);
        if (order is null || errors.Count > 0)
        {
            return new PreparedOrder(null, Guid.Empty, [], [], errors, "Validatie mislukt.", FailureKindValidation);
        }

        if (partner.CustomerId is not { } customerId)
        {
            return new PreparedOrder(null, Guid.Empty, [], [], errors,
                "De handelspartner is niet aan een klant gekoppeld.", FailureKindMapping);
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

            // C-03: what a partner sends is what the partner ASKS for, not what the planner has
            // decided — it belongs in the requested window. Writing it to PlannedFrom/To made an
            // EDI order look planned before a dispatcher had seen it, and let an inbound message
            // silently overwrite real planning.
            stops.Add(new TransportOrderStopInput(
                stop.Type, locationId, null, null, null, stop.City, null,
                null, null, stop.Reference, null,
                RequestedFrom: stop.RequestedFrom, RequestedTo: stop.RequestedTo));
        }

        if (errors.Count > 0)
        {
            return new PreparedOrder(null, Guid.Empty, [], [], errors, "Locatiemapping onvolledig.", FailureKindMapping);
        }

        var cargoItems = await ResolveCargoUnitsAsync(customerId, order.CargoItems, cancellationToken);
        return new PreparedOrder(order, customerId, stops, cargoItems, errors, null, null);
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

    private void Fail(EdiMessage message, string error, IReadOnlyList<string> validationErrors, string failureKind)
    {
        message.Status = message.AttemptCount >= MaxAttempts
            ? EdiProcessingStatus.DeadLettered
            : EdiProcessingStatus.Failed;
        message.ErrorDetail = error;
        message.ValidationErrorsJson = validationErrors.Count > 0 ? JsonSerializer.Serialize(validationErrors) : null;
        message.FailureKind = failureKind;
    }

    /// <summary>
    /// The partner's stop. The window is the partner's REQUEST (see MapStops); the inbound JSON
    /// keys stay `plannedFrom`/`plannedTo` for the published generic-json-v1 contract, with
    /// `requestedFrom`/`requestedTo` accepted as the clearer aliases.
    /// </summary>
    private sealed record ParsedStop(
        StopType Type, string? ExternalLocationCode, string? City,
        DateTime? RequestedFrom, DateTime? RequestedTo, string? Reference);

    private sealed record ParsedOrder(
        string ExternalOrderId, string? CustomerReference, string GoodsDescription,
        IReadOnlyList<ParsedStop> Stops, IReadOnlyList<CargoItemInput> CargoItems);

    /// <summary>
    /// The "generic-json-v1" mapping profile �?" the only implemented format, deliberately ours.
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
                    if (!TransportationService.Api.Common.EnumParsing.TryParseDefined<StopType>(typeText, out var type))
                    {
                        errors.Add($"Onbekend stoptype '{typeText}'.");
                        continue;
                    }

                    stops.Add(new ParsedStop(
                        type,
                        ReadString(stop, "externalLocationCode"),
                        ReadString(stop, "city"),
                        ReadDate(stop, "requestedFrom") ?? ReadDate(stop, "plannedFrom"),
                        ReadDate(stop, "requestedTo") ?? ReadDate(stop, "plannedTo"),
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

    /// <summary>
    /// C-03 — partner timestamps land as UTC instants or not at all. `DateTime.TryParse` with
    /// its defaults yields Kind=Local for an offset-carrying value (converted to the SERVER's
    /// zone, so the same payload means different things on a Brussels box and a UTC container)
    /// and Kind=Unspecified for a bare one; neither can be written to a `timestamp with time
    /// zone` column. AdjustToUniversal|AssumeUniversal makes the contract explicit and the
    /// result always Kind=Utc: a partner value carrying an offset ("2026-07-15T08:00:00+02:00")
    /// is converted; a value WITHOUT an offset ("2026-07-15T08:00:00") is taken to be UTC
    /// already — partners must send the offset if they mean local time. InvariantCulture keeps
    /// the parse independent of the server locale.
    /// </summary>
    private static DateTime? ReadDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        && DateTime.TryParse(
            value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
