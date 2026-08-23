using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace KappiApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;

    public SettingsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        var salonId = int.Parse(User.FindFirst("salonId")!.Value);
        var salon = await _db.Salons.FindAsync(salonId);
        if (salon == null) return NotFound();

        return Ok(new
        {
            salonName = salon.Name,
            address = salon.Address ?? "Molenstraat 12, Nijmegen",
            phone = salon.Phone ?? "+31 24 000 0000",
            hours = salon.HoursText ?? "Ma-Vr 9:00-18:00, Za 9:00-16:00, Zo Gesloten",
            services = salon.ServicesText ?? "Knippen - €25 - 30 min\nKnippen + Wassen - €35 - 45 min\nVerven - €55 - 90 min\nHighlights - €65 - 90 min\nBaard trimmen - €15 - 15 min\nKinderen - €18 - 30 min",
            team = salon.TeamText ?? "Sarah - Ma, Wo, Vr, Za\nKevin - Di, Do, Vr, Za"
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveSettings([FromBody] SettingsRequest request)
    {
        var salonId = int.Parse(User.FindFirst("salonId")!.Value);
        var salon = await _db.Salons.FindAsync(salonId);
        if (salon == null) return NotFound();

        salon.Name = request.SalonName;
        salon.Address = request.Address;
        salon.Phone = request.Phone;
        salon.HoursText = request.Hours;
        salon.ServicesText = request.Services;
        salon.TeamText = request.Team;

        await _db.SaveChangesAsync();
        return Ok(new { message = "Settings saved" });
    }
}

public record SettingsRequest(string SalonName, string Address, string Phone, string Hours, string Services, string Team);