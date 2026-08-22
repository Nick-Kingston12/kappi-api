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

        var tools = new[]
        {
            new
            {
                name = "create_booking",
                description = "Create an appointment in the salon calendar. Call this as soon as you have the customer name, service, stylist, date and time. Do not wait for additional confirmation.",
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
                        duration_minutes = new { type = "integer", description = "Duration in minutes. Knippen=30, Knippen+Wassen=45, Verven=90, Highlights=90, Baard=15" }
                    },
                    required = new[] { "customer_name", "service", "stylist", "date", "time", "duration_minutes" }
                }
            }
        };

        var systemPrompt = GetSalonSystemPrompt();
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
        var contentArray = result.GetProperty("content");

        if (stopReason == "tool_use")
        {
            var toolUseBlock = contentArray.EnumerateArray()
                .FirstOrDefault(c => c.GetProperty("type").GetString() == "tool_use");

            var toolName = toolUseBlock.GetProperty("name").GetString();
            var toolInput = toolUseBlock.GetProperty("input");
            var toolUseId = toolUseBlock.GetProperty("id").GetString();

            string toolResult = "Booking failed";

            if (toolName == "create_booking")
            {
                try
                {
                    var customerName = toolInput.GetProperty("customer_name").GetString()!;
                    var service = toolInput.GetProperty("service").GetString()!;
                    var stylist = toolInput.GetProperty("stylist").GetString()!;
                    var date = toolInput.GetProperty("date").GetString()!;
                    var time = toolInput.GetProperty("time").GetString()!;
                    var duration = toolInput.GetProperty("duration_minutes").GetInt32();
                    var appointmentStart = DateTime.SpecifyKind(DateTime.Parse($"{date} {time}"), DateTimeKind.Utc);
                    var summary = $"{service} - {customerName} (via Kappi AI)";

                    string eventId = "saved";
                    if (salon?.GoogleAccessToken != null)
                    {
                        if (salon.GoogleRefreshToken != null)
                        {
                            salon.GoogleAccessToken = await _calendarService.RefreshAccessToken(salon.GoogleRefreshToken);
                        }
                        eventId = await _calendarService.CreateBooking(
                            salon.GoogleAccessToken,
                            summary,
                            appointmentStart,
                            duration,
                            ""
                        );
                        salon.GoogleAccessToken = salon.GoogleAccessToken;
                        await _db.SaveChangesAsync();
                    }

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

                    toolResult = $"Booking successfully created. Appointment: {service} for {customerName} with {stylist} on {date} at {time}.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create booking: {Message}", ex.Message);
                    toolResult = $"Booking created in system but calendar sync failed. Details: {ex.Message}";
                }
            }

            var updatedMessages = new List<object>(_conversationHistory[customerNumber]);

            updatedMessages.Add(new
            {
                role = "assistant",
                content = contentArray.EnumerateArray()
                    .Select(c => JsonSerializer.Deserialize<object>(c.GetRawText())!)
                    .ToArray()
            });

            updatedMessages.Add(new
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

            var followUpBody = new
            {
                model = "claude-sonnet-4-6",
                max_tokens = 1024,
                system = systemPrompt,
                tools,
                messages = updatedMessages
            };

            var followUpJson = JsonSerializer.Serialize(followUpBody);
            var followUpContent = new StringContent(followUpJson, Encoding.UTF8, "application/json");
            var followUpResponse = await client.PostAsync("https://api.anthropic.com/v1/messages", followUpContent);
            var followUpResponseBody = await followUpResponse.Content.ReadAsStringAsync();
            var followUpResult = JsonSerializer.Deserialize<JsonElement>(followUpResponseBody);
            var finalReply = followUpResult.GetProperty("content")[0].GetProperty("text").GetString() ?? "Boeking bevestigd!";

            _conversationHistory[customerNumber] = updatedMessages;
            _conversationHistory[customerNumber].Add(new { role = "assistant", content = finalReply });

            return finalReply;
        }

        var reply = result.GetProperty("content")[0].GetProperty("text").GetString() ?? "Sorry, I couldn't process that.";
        _conversationHistory[customerNumber].Add(new { role = "assistant", content = reply });
        return reply;
    }

    private string GetSalonSystemPrompt()
    {
        var today = DateTime.Now.ToString("dddd d MMMM yyyy");
        var tomorrow = DateTime.Now.AddDays(1).ToString("dddd d MMMM yyyy");

        return $"""
                You are Kappi, the AI receptionist for Kapsalon Demo in Nijmegen.

                Today is {today}. Tomorrow is {tomorrow}.
                You always know the current date. Never ask the customer what day it is.

                SALON INFO:
                - Name: Kapsalon Demo
                - Address: Molenstraat 12, Nijmegen
                - Hours: Monday-Friday 9:00-18:00, Saturday 9:00-16:00, Closed Sunday

                SERVICES & PRICES:
                - Knippen (Haircut): €25 — 30 min
                - Knippen + Wassen (Haircut + Wash): €35 — 45 min
                - Verven (Colour): from €55 — 90 min
                - Highlights: from €65 — 90 min
                - Baard trimmen (Beard trim): €15 — 15 min
                - Kinderen (Children under 12): €18 — 30 min

                TEAM:
                - Sarah (available Mon, Wed, Fri, Sat)
                - Kevin (available Tue, Thu, Fri, Sat)

                BOOKING RULES:
                - When a customer gives you their name, preferred date, time and service — call create_booking IMMEDIATELY
                - Do NOT ask for confirmation before calling the tool
                - Do NOT say you cannot check availability for future dates — just book it
                - If the stylist works on that day and the time is within salon hours — BOOK IT
                - After the tool succeeds, confirm the booking details to the customer
                - Respond in the same language the customer uses (Dutch or English)
                - Be friendly and concise — this is WhatsApp, not email
                """;
    }
}