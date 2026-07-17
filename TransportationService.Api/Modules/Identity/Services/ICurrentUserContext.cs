namespace TransportationService.Api.Modules.Identity.Services;

public interface ICurrentUserContext
{
    Guid? CurrentUserId { get; }
}
