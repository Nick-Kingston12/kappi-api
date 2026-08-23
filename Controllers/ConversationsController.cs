using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace KappiApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ConversationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ConversationsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetConversations()
    {
        var salonId = int.Parse(User.FindFirst("salonId")!.Value);

        var conversations = await _db.Conversations
            .Where(c => c.SalonId == salonId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(100)
            .ToListAsync();

        // Group by phone number
        var grouped = conversations
            .GroupBy(c => c.PhoneNumber)
            .Select(g => new
            {
                phoneNumber = g.Key,
                lastMessage = g.First().Message,
                lastMessageTime = g.First().CreatedAt,
                messageCount = g.Count(),
                messages = g.OrderBy(m => m.CreatedAt).Select(m => new
                {
                    m.Role,
                    m.Message,
                    m.CreatedAt
                })
            })
            .OrderByDescending(g => g.lastMessageTime)
            .ToList();

        return Ok(grouped);
    }
}