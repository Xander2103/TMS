using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Models;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Controllers;

/// <summary>
/// Legacy demo transport-order endpoints from the initial scaffold, now permission-protected.
/// Phase 5 (Transport Operations) replaces this module with the real tenant-scoped order domain.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TransportOrdersController : ControllerBase
{
    private const int ReferenceMaxLength = 50;
    private const int CustomerNameMaxLength = 200;
    private const int AddressMaxLength = 500;

    private static readonly string[] AllowedStatuses =
    [
        "New", "Planned", "InProgress", "Delivered", "Cancelled"
    ];

    private readonly TransportationDbContext _dbContext;

    public TransportOrdersController(TransportationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.PlanningView)]
    public async Task<ActionResult<IEnumerable<TransportOrder>>> GetAll()
    {
        var orders = await _dbContext.TransportOrders
            .AsNoTracking()
            .OrderBy(order => order.Reference)
            .ToListAsync();

        return Ok(orders);
    }

    [HttpGet("{id}")]
    [RequirePermission(PermissionCodes.PlanningView)]
    public async Task<ActionResult<TransportOrder>> GetById(Guid id)
    {
        var order = await _dbContext.TransportOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
        {
            return NotFound();
        }

        return Ok(order);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.PlanningCreate)]
    public async Task<ActionResult<TransportOrder>> Create(TransportOrder order)
    {
        var reference = order.Reference.Trim();
        var customerName = order.CustomerName.Trim();
        var pickupAddress = order.PickupAddress.Trim();
        var deliveryAddress = order.DeliveryAddress.Trim();
        var status = order.Status.Trim();

        if (string.IsNullOrWhiteSpace(reference) || reference.Length > ReferenceMaxLength)
        {
            return BadRequest($"Reference is required and must be at most {ReferenceMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(customerName) || customerName.Length > CustomerNameMaxLength)
        {
            return BadRequest($"CustomerName is required and must be at most {CustomerNameMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(pickupAddress) || pickupAddress.Length > AddressMaxLength)
        {
            return BadRequest($"PickupAddress is required and must be at most {AddressMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(deliveryAddress) || deliveryAddress.Length > AddressMaxLength)
        {
            return BadRequest($"DeliveryAddress is required and must be at most {AddressMaxLength} characters.");
        }

        if (!AllowedStatuses.Contains(status))
        {
            return BadRequest($"Status must be one of: {string.Join(", ", AllowedStatuses)}.");
        }

        var referenceExists = await _dbContext.TransportOrders
            .AsNoTracking()
            .AnyAsync(o => o.Reference == reference);

        if (referenceExists)
        {
            return Conflict("A transport order with this reference already exists.");
        }

        var newOrder = new TransportOrder
        {
            Id = Guid.NewGuid(),
            Reference = reference,
            CustomerName = customerName,
            PickupAddress = pickupAddress,
            DeliveryAddress = deliveryAddress,
            Status = status
        };

        _dbContext.TransportOrders.Add(newOrder);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("A transport order with this reference already exists.");
        }

        return CreatedAtAction(nameof(GetById), new { id = newOrder.Id }, newOrder);
    }
}
