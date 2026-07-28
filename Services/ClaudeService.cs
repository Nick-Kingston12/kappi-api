using System.Text;
using System.Text.Json;

namespace KappiApi.Services;

public interface IClaudeService
{
    Task<string> GetBookingReplyAsync(string customerNumber, string message);
}

public class ClaudeService : IClaudeService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClaudeService> _logger;

    // In-memory conversation history per customer (keyed by phone number)
    // In production this would be stored in PostgreSQL
    private static readonly Dictionary<string, List<object>> _conversationHistory = new();

    public ClaudeService(IConfiguration config, IHttpClientFactory httpClientFactory, ILogger<ClaudeService> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> GetBookingReplyAsync(string customerNumber, string message)
    {
        // Get or create conversation history for this customer
        if (!_conversationHistory.ContainsKey(customerNumber))
            _conversationHistory[customerNumber] = new List<object>();

        // Add the customer's message to history
        _conversationHistory[customerNumber].Add(new { role = "user", content = message });

        var systemPrompt = GetSalonSystemPrompt();
        var requestBody = new
        {
            model = "claude-sonnet-4-6",
            max_tokens = 1024,
            system = systemPrompt,
            messages = _conversationHistory[customerNumber]
        };

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", _config["Anthropic:ApiKey"]);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("https://api.anthropic.com/v1/messages", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
        var reply = result.GetProperty("content")[0].GetProperty("text").GetString() ?? "Sorry, I couldn't process that.";

        // Add Claude's reply to history
        _conversationHistory[customerNumber].Add(new { role = "assistant", content = reply });

        return reply;
    }

    private string GetSalonSystemPrompt()
    {
        // This will eventually be loaded per-tenant from the database
        // For now this is the demo salon config
        return """
            You are Kappi, the AI receptionist for Kapsalon Demo in Nijmegen.
            
            You handle appointment bookings, cancellations, and general questions.
            Always respond in the same language the customer uses (Dutch or English).
            Be friendly, professional, and concise — this is WhatsApp, not email.
            
            SALON INFO:
            - Name: Kapsalon Demo
            - Address: Molenstraat 12, Nijmegen
            - Hours: Monday-Friday 9:00-18:00, Saturday 9:00-16:00, Closed Sunday
            - Phone: +31 24 000 0000
            
            SERVICES & PRICES:
            - Knippen (Haircut): €25
            - Knippen + Wassen (Haircut + Wash): €35
            - Verven (Colour): from €55
            - Highlights: from €65
            - Baard trimmen (Beard trim): €15
            - Kinderen (Children under 12): €18
            
            TEAM:
            - Sarah (available Mon, Wed, Fri, Sat)
            - Kevin (available Tue, Thu, Fri, Sat)
            
            BOOKING RULES:
            - Appointments are 30 or 60 minutes depending on service
            - Ask for preferred date, time, service, and stylist preference
            - Confirm the booking by repeating all details back to the customer
            - If a slot isn't available, offer the nearest alternative
            
            IMPORTANT:
            - Never make up availability — if unsure, say you'll check and confirm shortly
            - For complex requests, let the customer know the salon owner will follow up
            - Always end with a warm closing in the customer's language
            """;
    }
}