using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KappiApi.Services;

public interface IClaudeService
{
    Task<string> GetBookingReplyAsync(string customerNumber, string message, int salonId);
}

public class ClaudeService : IClaudeService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClaudeService> _logger;
    private readonly AppDbContext _db;
    private readonly IGoogleCalendarService _calendarService;

    private static readonly Dictionary<string, List<object>> _conversationHistory = new();

    public ClaudeService(IConfiguration config, IHttpClientFactory httpClientFactory, ILogger<ClaudeService> logger, AppDbContext db, IGoogleCalendarService calendarService)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _db = db;
        _calendarService = calendarService;
    }

    public async Task<string> GetBookingReplyAsync(string customerNumber, string message, int salonId)
    {
        if (!_conversationHistory.ContainsKey(customerNumber))
            _conversationHistory[customerNumber] = new List<object>();

        _conversationHistory[customerNumber].Add(new { role = "user", content = message });

        var salon = await _db.Salons.FindAsync(salonId);
        var availabilityInfo = "";

        if (salon?.GoogleAccessToken != null)
        {
            try
            {
                var tomorrow = DateTime.Now.AddDays(1);
                var slots = await _calendarService.GetAvailableSlots(salon.GoogleAccessToken, tomorrow, 30);
                availabilityInfo = $"\nREAL AVAILABILITY FOR TOMORROW ({tomorrow:dddd d MMMM}):\n" +
                    (slots.Any() ? string.Join(", ", slots) : "No slots available tomorrow");
            }
            catch
            {
                availabilityInfo = "\nCalendar availability temporarily unavailable.";
            }
        }

        var tools = new[]
        {
            new
            {
                name = "create_booking",
                description = "Create a real appointment in the salon's Google Calendar when a customer has confirmed all booking details (date, time, service, and name).",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        customer_name = new { type = "string", description = "Full name of the customer" },
                        service = new { type = "string", description = "Service being booked e.g. Knippen, Highlights" },
                        stylist = new { type = "string", description = "Name of the stylist" },
                        date = new { type = "string", description = "Date in yyyy-MM-dd format" },
                        time = new { type = "string", description = "Time in HH:mm format" },
                        duration_minutes = new { type = "integer", description = "Duration of the appointment in minutes" }
                    },
                    required = new[] { "customer_name", "service", "stylist", "date", "time", "duration_minutes" }
                }
            }
        };

        var systemPrompt = GetSalonSystemPrompt(availabilityInfo);
        var requestBody = new
        {
            model = "claude-sonnet-4-6",
            max_tokens = 1024,
            system = systemPrompt,
            tools,
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
        var stopReason = result.GetProperty("stop_reason").GetString();

        // Handle tool use
        if (stopReason == "tool_use")
        {
            var toolUseBlock = result.GetProperty("content").EnumerateArray()
                .First(c => c.GetProperty("type").GetString() == "tool_use");

            var toolName = toolUseBlock.GetProperty("name").GetString();
            var toolInput = toolUseBlock.GetProperty("input");
            var toolUseId = toolUseBlock.GetProperty("id").GetString();

            if (toolName == "create_booking" && salon?.GoogleAccessToken != null)
            {
                var customerName = toolInput.GetProperty("customer_name").GetString()!;
                var service = toolInput.GetProperty("service").GetString()!;
                var stylist = toolInput.GetProperty("stylist").GetString()!;
                var date = toolInput.GetProperty("date").GetString()!;
                var time = toolInput.GetProperty("time").GetString()!;
                var duration = toolInput.GetProperty("duration_minutes").GetInt32();

                var appointmentStart = DateTime.Parse($"{date} {time}");
                var summary = $"{service} - {customerName} (via Kappi AI)";

                string eventId = "";
                string toolResult = "";

                try
                {
                    eventId = await _calendarService.CreateBooking(
                        salon.GoogleAccessToken,
                        summary,
                        appointmentStart,
                        duration,
                        ""
                    );

                    // Save booking to database
                    var booking = new Booking
                    {
                        SalonId = salonId,
                        Service = service,
                        Stylist = stylist,
                        AppointmentDate = appointmentStart,
                        Status = "confirmed"
                    };
                    _db.Bookings.Add(booking);
                    await _db.SaveChangesAsync();

                    toolResult = $"Booking created successfully. Calendar event ID: {eventId}";
                }
                catch (Exception ex)
                {
                    toolResult = $"Failed to create booking: {ex.Message}";
                }

                // Add assistant tool use and tool result to history
               var assistantContent = JsonSerializer.Deserialize<object>(result.GetProperty("content").GetRawText())!;
_conversationHistory[customerNumber].Add(new
{
    role = "assistant",
    content = assistantContent
});

                _conversationHistory[customerNumber].Add(new
                {
                    role = "user",
                    content = new[]
                    {
                        new
                        {
                            type = "tool_result",
                            tool_use_id = toolUseId,
                            content = toolResult
                        }
                    }
                });

                // Get final response from Claude
                var followUpBody = new
                {
                    model = "claude-sonnet-4-6",
                    max_tokens = 1024,
                    system = systemPrompt,
                    tools,
                    messages = _conversationHistory[customerNumber]
                };

                var followUpJson = JsonSerializer.Serialize(followUpBody);
                var followUpContent = new StringContent(followUpJson, Encoding.UTF8, "application/json");
                var followUpResponse = await client.PostAsync("https://api.anthropic.com/v1/messages", followUpContent);
                var followUpBody2 = await followUpResponse.Content.ReadAsStringAsync();
                var followUpResult = JsonSerializer.Deserialize<JsonElement>(followUpBody2);
                var finalReply = followUpResult.GetProperty("content")[0].GetProperty("text").GetString() ?? "Booking confirmed!";

                _conversationHistory[customerNumber].Add(new { role = "assistant", content = finalReply });
                return finalReply;
            }
        }

        var reply = result.GetProperty("content")[0].GetProperty("text").GetString() ?? "Sorry, I couldn't process that.";
        _conversationHistory[customerNumber].Add(new { role = "assistant", content = reply });
        return reply;
    }

    private string GetSalonSystemPrompt(string availabilityInfo)
    {
        var today = DateTime.Now.ToString("dddd d MMMM yyyy");
        var tomorrow = DateTime.Now.AddDays(1).ToString("dddd d MMMM yyyy");

        return $"""
                You are Kappi, the AI receptionist for Kapsalon Demo in Nijmegen.

                IMPORTANT: Today is {today}. Tomorrow is {tomorrow}.
                You always know the current date — never ask the customer what day it is.

                You handle appointment bookings, cancellations, and general questions.
                Always respond in the same language the customer uses (Dutch or English).
                Be friendly, professional, and concise — this is WhatsApp, not email.

                SALON INFO:
                - Name: Kapsalon Demo
                - Address: Molenstraat 12, Nijmegen
                - Hours: Monday-Friday 9:00-18:00, Saturday 9:00-16:00, Closed Sunday

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

                {availabilityInfo}

                BOOKING RULES:
                - Use the real availability above when suggesting time slots
                - Once you have customer name, date, time, service and stylist — call the create_booking tool
                - Confirm the booking by repeating all details back to the customer
                - If a slot isn't available, offer the nearest alternative

                IMPORTANT:
                - Never make up availability — only use the real slots provided above
                - For complex requests, let the customer know the salon owner will follow up
                - Always end with a warm closing in the customer's language
                """;
    }
}