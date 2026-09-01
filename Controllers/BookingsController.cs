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
            .Where(b => b.SalonId == salonId && b.Status != "cancelled")
            .OrderBy(b => b.AppointmentDate)
            .Select(b => new
            {
                b.Id,
                b.Service,
                b.Stylist,
                b.AppointmentDate,
                b.Status,
                b.CreatedAt,
                b.CustomerPhone
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

    [HttpPatch("{id}/no-show")]
    public async Task<IActionResult> MarkNoShow(int id)
    {
        var salonId = int.Parse(User.FindFirst("salonId")!.Value);
        var booking = await _db.Bookings
            .FirstOrDefaultAsync(b => b.Id == id && b.SalonId == salonId);

        if (booking == null)
            return NotFound();

        booking.Status = "no-show";

        if (booking.CustomerPhone != null)
        {
            var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.PhoneNumber == booking.CustomerPhone && c.SalonId == salonId);
            if (customer != null)
                customer.NoShowCount += 1;
        }

        await _db.SaveChangesAsync();
        return Ok(new { booking.Id, booking.Status });
    }
    [HttpGet("staff-performance")]
public async Task<IActionResult> GetStaffPerformance()
{
    var salonId = int.Parse(User.FindFirst("salonId")!.Value);

    var bookings = await _db.Bookings
        .Where(b => b.SalonId == salonId && b.Status == "confirmed")
        .ToListAsync();

    var byStylist = bookings
        .GroupBy(b => b.Stylist)
        .Select(g => new
        {
            stylist = g.Key,
            totalBookings = g.Count(),
            totalRevenue = g.Sum(b => b.Price),
            services = g.GroupBy(b => b.Service)
                        .Select(sg => new { service = sg.Key, count = sg.Count(), revenue = sg.Sum(b => b.Price) })
                        .OrderByDescending(sg => sg.count)
                        .ToList()
        })
        .OrderByDescending(s => s.totalRevenue)
        .ToList();

    return Ok(byStylist);
}

[HttpGet("revenue-forecast")]
public async Task<IActionResult> GetRevenueForecast()
{
    var salonId = int.Parse(User.FindFirst("salonId")!.Value);
    var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);

    var pastBookings = await _db.Bookings
        .Where(b => b.SalonId == salonId && b.Status == "confirmed" && b.AppointmentDate >= sixMonthsAgo && b.AppointmentDate <= DateTime.UtcNow)
        .ToListAsync();

    var monthlyRevenue = pastBookings
        .GroupBy(b => new { b.AppointmentDate.Year, b.AppointmentDate.Month })
        .Select(g => new
        {
            year = g.Key.Year,
            month = g.Key.Month,
            revenue = g.Sum(b => b.Price),
            bookingCount = g.Count()
        })
        .OrderBy(m => m.year).ThenBy(m => m.month)
        .ToList();

    decimal forecastNextMonth = 0;
    string method = "insufficient_data";

    if (monthlyRevenue.Count >= 3)
    {
        var recentMonths = monthlyRevenue.TakeLast(3).ToList();
        forecastNextMonth = recentMonths.Average(m => m.revenue);
        method = "3_month_average";
    }
    else if (monthlyRevenue.Count > 0)
    {
        forecastNextMonth = monthlyRevenue.Average(m => m.revenue);
        method = "simple_average";
    }

    return Ok(new
    {
        historicalMonths = monthlyRevenue,
        forecastNextMonth = Math.Round(forecastNextMonth, 2),
        forecastMethod = method
    });
}
}