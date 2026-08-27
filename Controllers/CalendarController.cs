using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2;

namespace KappiApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CalendarController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public CalendarController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    // Step 1 — redirect salon owner to Google OAuth
    [HttpGet("connect")]
[AllowAnonymous]
public IActionResult Connect([FromQuery] int salonId)
{
    var clientId = _config["GoogleClientId"];
    var redirectUri = "https://kappi-api-1.onrender.com/api/calendar/callback";
    var scope = "https://www.googleapis.com/auth/calendar";
    var url = $"https://accounts.google.com/o/oauth2/v2/auth?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope={Uri.EscapeDataString(scope)}&access_type=offline&prompt=consent&state={salonId}";
    return Redirect(url);
}

    // Step 2 — Google redirects back here with auth code
    [HttpGet("callback")]
[AllowAnonymous]
public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state)
{
    var clientId = _config["GoogleClientId"];
    var clientSecret = _config["GoogleClientSecret"];
    var redirectUri = "https://kappi-api-1.onrender.com/api/calendar/callback";

    using var http = new HttpClient();
    var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["code"] = code,
        ["client_id"] = clientId!,
        ["client_secret"] = clientSecret!,
        ["redirect_uri"] = redirectUri,
        ["grant_type"] = "authorization_code"
    }));

    var json = await response.Content.ReadAsStringAsync();
    var tokenData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

    var accessToken = tokenData.GetProperty("access_token").GetString();
    var refreshToken = tokenData.GetProperty("refresh_token").GetString();

    // Save tokens to salon — use salonId from state parameter
    if (int.TryParse(state, out int salonId))
    {
        var salon = await _db.Salons.FindAsync(salonId);
        if (salon != null)
        {
            salon.GoogleAccessToken = accessToken;
            salon.GoogleRefreshToken = refreshToken;
            await _db.SaveChangesAsync();
        }
    }

    // Redirect to dashboard with success message
    return Redirect("https://kappi-web-gamma.vercel.app/dashboard?calendar=connected");
}

    // Get available slots for a date
    [HttpGet("slots")]
    public IActionResult GetSlots([FromQuery] string date)
    {
        return Ok(new { message = $"Slots for {date} — calendar integration coming soon" });
    }
    [HttpGet("debug")]
[AllowAnonymous]
public async Task<IActionResult> Debug()
{
    var salon = await _db.Salons.FindAsync(1);
    return Ok(new
    {
        salonFound = salon != null,
        hasAccessToken = !string.IsNullOrEmpty(salon?.GoogleAccessToken),
        hasRefreshToken = !string.IsNullOrEmpty(salon?.GoogleRefreshToken),
        accessTokenPreview = salon?.GoogleAccessToken?.Substring(0, 20) + "..."
    });
}
[HttpGet("fix-bookings")]
[AllowAnonymous]
public async Task<IActionResult> FixBookings()
{
    var bookings = await _db.Bookings.Where(b => b.CustomerPhone == null).ToListAsync();
    foreach (var booking in bookings)
    {
        booking.CustomerPhone = "whatsapp:+31611792610";
    }
    await _db.SaveChangesAsync();
    return Ok(new { fixed_count = bookings.Count });
}
}