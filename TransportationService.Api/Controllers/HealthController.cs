using Microsoft.AspNetCore.Mvc;

namespace TransportationService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "ok",
            message = "Transportation Service API is running",
            timestampUtc = DateTime.UtcNow
        });
    }
}
