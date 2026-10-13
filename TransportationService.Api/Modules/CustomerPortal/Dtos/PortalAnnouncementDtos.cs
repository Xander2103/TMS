namespace TransportationService.Api.Modules.CustomerPortal.Dtos;

public record PortalAnnouncementDto(
    Guid Id, string Title, string Body, DateTime? ActiveFrom, DateTime? ActiveUntil, bool IsActive);

public record SavePortalAnnouncementRequest(
    string Title, string Body, DateTime? ActiveFrom, DateTime? ActiveUntil, bool IsActive);
