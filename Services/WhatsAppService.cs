using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace KappiApi.Services;

public interface IWhatsAppService
{
    Task HandleIncomingMessageAsync(string from, string body);
    Task SendMessageAsync(string to, string message);
}

public class WhatsAppService : IWhatsAppService
{
    private readonly IClaudeService _claudeService;
    private readonly IConfiguration _config;
    private readonly ILogger<WhatsAppService> _logger;

    public WhatsAppService(IClaudeService claudeService, IConfiguration config, ILogger<WhatsAppService> logger)
    {
        _claudeService = claudeService;
        _config = config;
        _logger = logger;

        // Initialise Twilio client
        TwilioClient.Init(
            _config["Twilio:AccountSid"],
            _config["Twilio:AuthToken"]
        );
    }

    public async Task HandleIncomingMessageAsync(string from, string body)
    {
        // Get AI response from Claude
        var reply = await _claudeService.GetBookingReplyAsync(from, body);

        // Send reply back via Twilio WhatsApp
        await SendMessageAsync(from, reply);
    }

    public async Task SendMessageAsync(string to, string message)
    {
        var from = _config["Twilio:WhatsAppNumber"]; // e.g. whatsapp:+14155238886

        var messageResource = await MessageResource.CreateAsync(
            body: message,
            from: new PhoneNumber(from),
            to: new PhoneNumber(to)
        );

        _logger.LogInformation("Message sent. SID: {Sid}", messageResource.Sid);
    }
}