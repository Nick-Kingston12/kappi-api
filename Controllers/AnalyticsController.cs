using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace KappiApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AnalyticsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAnalytics()
    {
        var salonId = int.Parse(User.FindFirst("salonId")!.Value);
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        var bookings = await _db.Bookings
            .Where(b => b.SalonId == salonId && b.AppointmentDate >= thirtyDaysAgo)
            .ToListAsync();

        var totalBookings = bookings.Count;

        var serviceRevenue = new Dictionary<string, decimal>
        {
            { "Knippen", 25 }, { "Knippen + Wassen", 35 }, { "Verven", 55 },
            { "Highlights", 65 }, { "Baard trimmen", 15 }, { "Kinderen", 18 }
        };

        var totalRevenue = bookings.Sum(b =>
            serviceRevenue.TryGetValue(b.Service, out var price) ? price : 25);

        var popularServices = bookings
            .GroupBy(b => b.Service)
            .Select(g => new { service = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count)
            .Take(5)
            .ToList();

        var busiestDays = bookings
            .GroupBy(b => b.AppointmentDate.DayOfWeek.ToString())
            .Select(g => new { day = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count)
            .ToList();

        var bookingsByWeek = bookings
            .GroupBy(b => b.AppointmentDate.Date.AddDays(-(int)b.AppointmentDate.DayOfWeek))
            .Select(g => new { week = g.Key.ToString("dd MMM"), count = g.Count() })
            .OrderBy(g => g.week)
            .ToList();

        var totalCustomers = await _db.Customers.CountAsync(c => c.SalonId == salonId);
        var returningCustomers = await _db.Customers.CountAsync(c => c.SalonId == salonId && c.TotalBookings > 1);

        return Ok(new
        {
            totalBookings,
            totalRevenue,
            totalCustomers,
            returningCustomers,
            popularServices,
            busiestDays,
            bookingsByWeek
        });
    }
}