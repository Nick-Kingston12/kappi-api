using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using KappiApi.Services;
using Twilio.AspNet.Core;

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

    [HttpPost("webhook")]
    //[ValidateRequest]
    [EnableRateLimiting("webhook")]
    public async Task<IActionResult> ReceiveMessage([FromForm] TwilioWebhookRequest request)
    {
        _logger.LogInformation("Incoming WhatsApp message from {From}: {Body}", request.From, request.Body);

        await _whatsAppService.HandleIncomingMessageAsync(request.From, request.Body);

        return Content("<Response></Response>", "text/xml");
    }
}

public class TwilioWebhookRequest
{
    public string From { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}