using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using KappiApi.Services;

namespace KappiApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CalendarController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IEncryptionService _encryption;

    public CalendarController(AppDbContext db, IConfiguration config, IEncryptionService encryption)
    {
        _db = db;
        _config = config;
        _encryption = encryption;
    }

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

        if (int.TryParse(state, out int salonId))
        {
            var salon = await _db.Salons.FindAsync(salonId);
            if (salon != null)
            {
                salon.GoogleAccessToken = accessToken != null ? _encryption.Encrypt(accessToken) : null;
                salon.GoogleRefreshToken = refreshToken != null ? _encryption.Encrypt(refreshToken) : null;
                await _db.SaveChangesAsync();
            }
        }

        return Redirect("https://kappi-web-gamma.vercel.app/dashboard?calendar=connected");
    }

    [HttpGet("slots")]
    public IActionResult GetSlots([FromQuery] string date)
    {
        return Ok(new { message = $"Slots for {date} — calendar integration coming soon" });
    }
}