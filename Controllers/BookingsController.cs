using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace KappiApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly AppDbContext _db;

    public BookingsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetBookings()
    {
        var salonId = int.Parse(User.FindFirst("salonId")!.Value);

        var bookings = await _db.Bookings
            .Where(b => b.SalonId == salonId)
            .OrderBy(b => b.AppointmentDate)
            .Select(b => new
            {
                b.Id,
                b.Service,
                b.Stylist,
                b.AppointmentDate,
                b.Status,
                b.CreatedAt
            })
            .ToListAsync();

        return Ok(bookings);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var salonId = int.Parse(User.FindFirst("salonId")!.Value);
        var today = DateTime.UtcNow.Date;
        var weekStart = today.AddDays(-(int)today.DayOfWeek);

        var todayCount = await _db.Bookings.CountAsync(b => b.SalonId == salonId && b.AppointmentDate.Date == today);
        var weekCount = await _db.Bookings.CountAsync(b => b.SalonId == salonId && b.AppointmentDate >= weekStart);
        var totalCount = await _db.Bookings.CountAsync(b => b.SalonId == salonId);

        return Ok(new { todayCount, weekCount, totalCount });
    }
}