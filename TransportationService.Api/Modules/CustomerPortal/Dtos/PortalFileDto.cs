namespace TransportationService.Api.Modules.CustomerPortal.Dtos;

/// <summary>Generic file payload for portal file-download endpoints (PDFs, attachments, documents).</summary>
public record PortalFileDto(byte[] Content, string FileName, string ContentType);
