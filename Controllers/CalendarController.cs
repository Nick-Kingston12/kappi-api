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
    public IActionResult Connect()
    {
        var clientId = _config["Google__ClientId"];
        var redirectUri = "https://kappi-api-1.onrender.com/api/calendar/callback";
        var scope = "https://www.googleapis.com/auth/calendar";
        var url = $"https://accounts.google.com/o/oauth2/v2/auth?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope={Uri.EscapeDataString(scope)}&access_type=offline&prompt=consent";
        return Redirect(url);
    }

    // Step 2 — Google redirects back here with auth code
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] string code)
    {
        var clientId = _config["Google__ClientId"];
        var clientSecret = _config["Google__ClientSecret"];
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
        return Ok(new { message = "Calendar connected!", tokens = json });
    }

    // Get available slots for a date
    [HttpGet("slots")]
    public IActionResult GetSlots([FromQuery] string date)
    {
        return Ok(new { message = $"Slots for {date} — calendar integration coming soon" });
    }
}