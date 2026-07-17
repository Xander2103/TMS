namespace TransportationService.Api.Models;

public class TransportOrder
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string PickupAddress { get; set; } = string.Empty;
    public string DeliveryAddress { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
