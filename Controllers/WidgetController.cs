using Microsoft.AspNetCore.Mvc;
using KappiApi.Services;

namespace KappiApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WidgetController : ControllerBase
{
    private readonly IClaudeService _claudeService;

    public WidgetController(IClaudeService claudeService)
    {
        _claudeService = claudeService;
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] WidgetChatRequest request)
    {
        var sessionId = $"widget_{request.SalonId}_{HttpContext.Connection.RemoteIpAddress}";
        var reply = await _claudeService.GetBookingReplyAsync(sessionId, request.Message, request.SalonId);
        return Ok(new { reply });
    }
}

public record WidgetChatRequest(string Message, int SalonId);