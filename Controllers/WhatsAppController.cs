using Microsoft.AspNetCore.Mvc;
using KappiApi.Services;

namespace KappiApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WhatsAppController : ControllerBase
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<WhatsAppController> _logger;

    public WhatsAppController(IWhatsAppService whatsAppService, ILogger<WhatsAppController> logger)
    {
        _whatsAppService = whatsAppService;
        _logger = logger;
    }

    // Twilio sends a POST to this endpoint every time a WhatsApp message comes in
    [HttpPost("webhook")]
    public async Task<IActionResult> ReceiveMessage([FromForm] TwilioWebhookRequest request)
    {
        _logger.LogInformation("Incoming WhatsApp message from {From}: {Body}", request.From, request.Body);

        await _whatsAppService.HandleIncomingMessageAsync(request.From, request.Body);

        // Twilio expects a 200 OK with empty TwiML response
        return Content("<Response></Response>", "text/xml");
    }
}

public class TwilioWebhookRequest
{
    public string From { get; set; } = string.Empty;  // e.g. whatsapp:+31612345678
    public string Body { get; set; } = string.Empty;  // The message text
    public string To { get; set; } = string.Empty;    // Your Kappi WhatsApp number
}