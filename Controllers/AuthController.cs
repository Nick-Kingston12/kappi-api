using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace KappiApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AuthController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        // Check if email already exists
        if (await _db.SalonOwners.AnyAsync(s => s.Email == request.Email))
            return BadRequest(new { message = "E-mailadres al in gebruik" });

        // Create salon
        var salon = new Salon
        {
            Name = request.SalonName,
            WhatsAppNumber = request.Phone,
            ServicesJson = "[]",
            TeamJson = "[]",
            HoursJson = "{}"
        };
        _db.Salons.Add(salon);
        await _db.SaveChangesAsync();

        // Create owner
        var owner = new SalonOwner
        {
            Name = request.Name,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            SalonId = salon.Id
        };
        _db.SalonOwners.Add(owner);
        await _db.SaveChangesAsync();

        var token = GenerateToken(owner);
        return Ok(new { token, salonId = salon.Id, name = owner.Name });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var owner = await _db.SalonOwners.FirstOrDefaultAsync(s => s.Email == request.Email);
        if (owner == null || !BCrypt.Net.BCrypt.Verify(request.Password, owner.PasswordHash))
            return Unauthorized(new { message = "Onjuist e-mailadres of wachtwoord" });

        var token = GenerateToken(owner);
        return Ok(new { token, salonId = owner.SalonId, name = owner.Name });
    }

    private string GenerateToken(SalonOwner owner)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt__Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: new[]
            {
                new Claim("ownerId", owner.Id.ToString()),
                new Claim("salonId", owner.SalonId.ToString()),
                new Claim("name", owner.Name)
            },
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record RegisterRequest(string Name, string SalonName, string Email, string Phone, string Password);
public record LoginRequest(string Email, string Password);