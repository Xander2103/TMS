namespace TransportationService.Api.Modules.CustomerPortal.Dtos;

public record CustomerMessageDto(
    Guid Id,
    Guid? TransportOrderId,
    string? OrderNumber,
    bool AuthorIsStaff,
    string AuthorName,
    string Body,
    DateTime CreatedAt);

public record SendCustomerMessageRequest(Guid? OrderId, string Body);

public record MarkMessagesReadRequest(Guid? OrderId);

public record PortalUnreadCountDto(int Count);

/// <summary>Empty success marker for portal actions that return no data (e.g. mark-as-read).</summary>
public record PortalMessageAckDto;
