using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.CustomerPortal.Controllers;

/// <summary>
/// Customer-portal endpoints. The customer context comes exclusively from the authenticated
/// user's linked customer; a client-supplied customer id is never accepted anywhere.
/// </summary>
[ApiController]
[Route("api/customer-portal")]
public class CustomerPortalController : ControllerBase
{
    private readonly ICustomerPortalService _service;
    private readonly ICustomerMessageService _messageService;
    private readonly IPortalAnnouncementService _announcementService;
    private readonly IPortalDashboardService _dashboardService;
    private readonly IPortalInvoiceService _invoiceService;
    private readonly IPortalDocumentService _documentService;

    public CustomerPortalController(
        ICustomerPortalService service,
        ICustomerMessageService messageService,
        IPortalAnnouncementService announcementService,
        IPortalDashboardService dashboardService,
        IPortalInvoiceService invoiceService,
        IPortalDocumentService documentService)
    {
        _service = service;
        _messageService = messageService;
        _announcementService = announcementService;
        _dashboardService = dashboardService;
        _invoiceService = invoiceService;
        _documentService = documentService;
    }

    [HttpGet("context")]
    [RequirePermission(PermissionCodes.CustomerPortalView)]
    public async Task<ActionResult<PortalContextDto>> Context(CancellationToken cancellationToken) =>
        Handle(await _service.GetContextAsync(cancellationToken));

    public record SetLanguageRequest(string Language);

    [HttpPut("profile/language")]
    [RequirePermission(PermissionCodes.CustomerPortalView)]
    public async Task<ActionResult<PortalContextDto>> SetLanguage(
        SetLanguageRequest request, CancellationToken cancellationToken) =>
        Handle(await _service.SetLanguageAsync(request.Language, cancellationToken));

    // Wave 11: the customer's own notification preferences (MessagingProfile surface).

    [HttpGet("notification-preferences")]
    [RequirePermission(PermissionCodes.CustomerPortalView)]
    public async Task<ActionResult<PortalNotificationPreferencesDto>> NotificationPreferences(CancellationToken cancellationToken) =>
        Handle(await _service.GetNotificationPreferencesAsync(cancellationToken));

    [HttpPut("notification-preferences")]
    [RequirePermission(PermissionCodes.CustomerPortalView)]
    public async Task<ActionResult<PortalNotificationPreferencesDto>> SaveNotificationPreferences(
        SavePortalNotificationPreferencesRequest request, CancellationToken cancellationToken) =>
        Handle(await _service.SaveNotificationPreferencesAsync(request, cancellationToken));

    [HttpGet("orders")]
    [RequirePermission(PermissionCodes.CustomerPortalView)]
    public async Task<ActionResult<IReadOnlyList<PortalOrderListItemDto>>> Orders(CancellationToken cancellationToken) =>
        Handle(await _service.ListMyOrdersAsync(cancellationToken));

    [HttpGet("orders/{id:guid}")]
    [RequirePermission(PermissionCodes.CustomerPortalView)]
    public async Task<ActionResult<PortalOrderDetailDto>> Order(Guid id, CancellationToken cancellationToken) =>
        Handle(await _service.GetMyOrderAsync(id, cancellationToken));

    [HttpPost("orders")]
    [RequirePermission(PermissionCodes.CustomerPortalSubmitOrders)]
    public async Task<ActionResult<PortalOrderDetailDto>> SubmitOrder(
        PortalCreateOrderRequest request, CancellationToken cancellationToken) =>
        Handle(await _service.SubmitOrderAsync(request, cancellationToken));

    [HttpGet("locations")]
    [RequirePermission(PermissionCodes.CustomerPortalView)]
    public async Task<ActionResult<IReadOnlyList<PortalLocationDto>>> Locations(CancellationToken cancellationToken) =>
        Handle(await _service.ListMyLocationsAsync(cancellationToken));

    [HttpPost("locations")]
    [RequirePermission(PermissionCodes.CustomerPortalManageLocations)]
    public async Task<ActionResult<PortalLocationDto>> CreateLocation(
        PortalCreateLocationRequest request, CancellationToken cancellationToken) =>
        Handle(await _service.CreateMyLocationAsync(request, cancellationToken));

    // --- Dashboard ---

    [HttpGet("dashboard")]
    [RequirePermission(PermissionCodes.CustomerPortalView)]
    public async Task<ActionResult<PortalDashboardDto>> Dashboard(CancellationToken cancellationToken) =>
        Handle(await _dashboardService.GetDashboardAsync(cancellationToken));

    // --- Announcements ---

    [HttpGet("announcements")]
    [RequirePermission(PermissionCodes.CustomerPortalView)]
    public async Task<ActionResult<IReadOnlyList<PortalAnnouncementDto>>> Announcements(CancellationToken cancellationToken) =>
        Handle(await _announcementService.ListForPortalAsync(cancellationToken));

    // --- Messages ---

    [HttpGet("messages")]
    [RequirePermission(PermissionCodes.CustomerPortalMessages)]
    public async Task<ActionResult<IReadOnlyList<CustomerMessageDto>>> Messages(
        [FromQuery] Guid? orderId, CancellationToken cancellationToken) =>
        Handle(await _messageService.ListPortalAsync(orderId, cancellationToken));

    [HttpPost("messages")]
    [RequirePermission(PermissionCodes.CustomerPortalMessages)]
    public async Task<ActionResult<CustomerMessageDto>> SendMessage(
        SendCustomerMessageRequest request, CancellationToken cancellationToken) =>
        Handle(await _messageService.SendPortalAsync(request, cancellationToken));

    [HttpPost("messages/read")]
    [RequirePermission(PermissionCodes.CustomerPortalMessages)]
    public async Task<ActionResult<PortalMessageAckDto>> MarkMessagesRead(
        MarkMessagesReadRequest request, CancellationToken cancellationToken) =>
        Handle(await _messageService.MarkPortalReadAsync(request.OrderId, cancellationToken));

    [HttpGet("messages/unread-count")]
    [RequirePermission(PermissionCodes.CustomerPortalMessages)]
    public async Task<ActionResult<PortalUnreadCountDto>> MessagesUnreadCount(CancellationToken cancellationToken) =>
        Handle(await _messageService.GetPortalUnreadCountAsync(cancellationToken));

    // --- Invoices ---

    [HttpGet("invoices")]
    [RequirePermission(PermissionCodes.CustomerPortalViewInvoices)]
    public async Task<ActionResult<IReadOnlyList<PortalInvoiceListItemDto>>> Invoices(CancellationToken cancellationToken) =>
        Handle(await _invoiceService.ListMyInvoicesAsync(cancellationToken));

    [HttpGet("invoices/{id:guid}")]
    [RequirePermission(PermissionCodes.CustomerPortalViewInvoices)]
    public async Task<ActionResult<PortalInvoiceDetailDto>> Invoice(Guid id, CancellationToken cancellationToken) =>
        Handle(await _invoiceService.GetMyInvoiceAsync(id, cancellationToken));

    [HttpGet("invoices/{id:guid}/pdf")]
    [RequirePermission(PermissionCodes.CustomerPortalViewInvoices)]
    public async Task<IActionResult> InvoicePdf(Guid id, CancellationToken cancellationToken) =>
        HandleFile(await _invoiceService.GetInvoicePdfAsync(id, cancellationToken));

    [HttpGet("invoices/{id:guid}/attachments/{attachmentId:guid}/content")]
    [RequirePermission(PermissionCodes.CustomerPortalViewInvoices)]
    public async Task<IActionResult> InvoiceAttachment(Guid id, Guid attachmentId, CancellationToken cancellationToken) =>
        HandleFile(await _invoiceService.GetInvoiceAttachmentAsync(id, attachmentId, cancellationToken));

    // --- Documents ---

    [HttpGet("documents")]
    [RequirePermission(PermissionCodes.CustomerPortalViewDocuments)]
    public async Task<ActionResult<IReadOnlyList<PortalDocumentDto>>> Documents(CancellationToken cancellationToken) =>
        Handle(await _documentService.ListMyDocumentsAsync(cancellationToken));

    [HttpGet("documents/{source}/{id:guid}/content")]
    [RequirePermission(PermissionCodes.CustomerPortalViewDocuments)]
    public async Task<IActionResult> DocumentContent(
        PortalDocumentSource source, Guid id, CancellationToken cancellationToken) =>
        HandleFile(await _documentService.GetDocumentContentAsync(source, id, cancellationToken));

    private ActionResult<T> Handle<T>(PortalResult<T> result) where T : class => result.Outcome switch
    {
        PortalOutcomeKind.Success => Ok(result.Value),
        PortalOutcomeKind.NoCustomerLink => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error }),
        PortalOutcomeKind.NotFound => NotFound(),
        _ => BadRequest(new { message = result.Error }),
    };

    private IActionResult HandleFile(PortalResult<PortalFileDto> result) => result.Outcome switch
    {
        PortalOutcomeKind.Success => File(result.Value!.Content, result.Value.ContentType, result.Value.FileName),
        PortalOutcomeKind.NoCustomerLink => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error }),
        PortalOutcomeKind.NotFound => NotFound(),
        _ => BadRequest(new { message = result.Error }),
    };
}
