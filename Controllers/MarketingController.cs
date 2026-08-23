using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using KappiApi.Services;

namespace KappiApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MarketingController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWhatsAppService _whatsAppService;

    public MarketingController(AppDbContext db, IWhatsAppService whatsAppService)
    {
        _db = db;
        _whatsAppService = whatsAppService;
    }

    [HttpPost("blast")]
    public async Task<IActionResult> SendBlast([FromBody] BlastRequest request)
    {
        var salonId = int.Parse(User.FindFirst("salonId")!.Value);
        var salon = await _db.Salons.FindAsync(salonId);
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        // Get customers who haven't booked in 30 days
        var inactiveCustomers = await _db.Customers
            .Where(c => c.SalonId == salonId
                && c.PhoneNumber != ""
                && (c.LastVisit == null || c.LastVisit < thirtyDaysAgo))
            .ToListAsync();

        var sent = 0;
        foreach (var customer in inactiveCustomers)
        {
            try
            {
                var message = request.Message
                    .Replace("{naam}", customer.Name != "" ? customer.Name : "daar")
                    .Replace("{salon}", salon?.Name ?? "de salon");

                await _whatsAppService.SendMessageAsync(customer.PhoneNumber, message);
                sent++;
            }
            catch { }
        }

        return Ok(new { sent, total = inactiveCustomers.Count });
    }

    [HttpGet("customers")]
    public async Task<IActionResult> GetInactiveCustomers()
    {
        var salonId = int.Parse(User.FindFirst("salonId")!.Value);
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        var count = await _db.Customers
            .CountAsync(c => c.SalonId == salonId
                && (c.LastVisit == null || c.LastVisit < thirtyDaysAgo));

        return Ok(new { inactiveCount = count });
    }
}

public record BlastRequest(string Message);