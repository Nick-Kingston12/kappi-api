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

        // Fallback dictionary only used for older bookings created before Price existed (Price == 0)
        var serviceRevenueFallback = new Dictionary<string, decimal>
        {
            { "Knippen", 25 }, { "Knippen + Wassen", 35 }, { "Verven", 55 },
            { "Highlights", 65 }, { "Baard trimmen", 15 }, { "Kinderen", 18 }
        };

        var totalRevenue = bookings.Sum(b =>
            b.Price > 0 ? b.Price : (serviceRevenueFallback.TryGetValue(b.Service, out var price) ? price : 25));

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