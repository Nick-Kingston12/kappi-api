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

        // Save message to database
        var incomingMsg = new Conversation
        {
            PhoneNumber = customerNumber,
            SalonId = salonId,
            Role = "user",
            Message = message,
            CreatedAt = DateTime.UtcNow
        };
        _db.Conversations.Add(incomingMsg);
        await _db.SaveChangesAsync();

        var salon = await _db.Salons.FindAsync(salonId);

        // Find or create customer profile
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == customerNumber && c.SalonId == salonId);
        if (customer == null)
        {
            customer = new Customer
            {
                PhoneNumber = customerNumber,
                SalonId = salonId,
                Name = "",
                Language = "nl"
            };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();
        }

        var customerContext = customer.Name != ""
            ? $"\nRETURNING CUSTOMER: {customer.Name} | Preferred stylist: {customer.PreferredStylist ?? "no preference"} | Preferred service: {customer.PreferredService ?? "unknown"} | Total bookings: {customer.TotalBookings} | Last visit: {(customer.LastVisit.HasValue ? customer.LastVisit.Value.ToString("d MMMM yyyy") : "first time")}"
            : "\nNEW CUSTOMER: No profile yet. When they give their name, remember it.";

        var tools = new object[]
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
            },
            new
{
    name = "cancel_booking",
    description = "Cancel an existing appointment when a customer requests cancellation. The system already knows the customer's phone number automatically — never ask the customer for it.",
    input_schema = new
    {
        type = "object",
        properties = new
        {
            appointment_date = new { type = "string", description = "Date of appointment in yyyy-MM-dd format, if the customer specifies one" }
        },
        required = new string[] { }
    }
},
            new
            {
                name = "add_to_waitlist",
                description = "Add a customer to the waitlist when their preferred slot is unavailable.",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        customer_name = new { type = "string", description = "Customer name" },
                        customer_phone = new { type = "string", description = "Customer phone number" },
                        preferred_service = new { type = "string", description = "Service they want" },
                        preferred_stylist = new { type = "string", description = "Stylist preference" },
                        preferred_day = new { type = "string", description = "Preferred day e.g. Monday, Wednesday" }
                    },
                    required = new[] { "customer_name", "customer_phone", "preferred_service", "preferred_day" }
                }
            }
        };

        var systemPrompt = GetSalonSystemPrompt(customerContext, salon);
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

            string toolResult = "Operation failed";

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
                    var amsterdamZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
                    var localTime = DateTime.Parse($"{date} {time}");
                    var appointmentStart = TimeZoneInfo.ConvertTimeToUtc(localTime, amsterdamZone);
                    var summary = $"{service} - {customerName} (via Kappi AI)";

                    string eventId = "saved";
                    if (salon?.GoogleAccessToken != null)
                    {
                        if (salon.GoogleRefreshToken != null)
                            salon.GoogleAccessToken = await _calendarService.RefreshAccessToken(salon.GoogleRefreshToken);

                        eventId = await _calendarService.CreateBooking(
                            salon.GoogleAccessToken,
                            summary,
                            appointmentStart,
                            duration,
                            ""
                        );
                        await _db.SaveChangesAsync();
                    }

                    var booking = new Booking
                    {
                        SalonId = salonId,
                        Service = service,
                        Stylist = stylist,
                        AppointmentDate = appointmentStart,
                        Status = "confirmed",
                        CustomerPhone = customerNumber
                    };
                    _db.Bookings.Add(booking);

                    customer.Name = customerName;
                    customer.PreferredStylist = stylist;
                    customer.PreferredService = service;
                    customer.TotalBookings += 1;
                    customer.LastVisit = appointmentStart;

                    await _db.SaveChangesAsync();

                    var loyaltyMessage = "";
                    if (customer.TotalBookings % 5 == 0)
                        loyaltyMessage = $" This is their {customer.TotalBookings}th booking — trigger a loyalty reward message.";

                    toolResult = $"Booking successfully created. Appointment: {service} for {customerName} with {stylist} on {date} at {time}.{loyaltyMessage}";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create booking: {Message}", ex.Message);
                    toolResult = $"Booking created in system but calendar sync failed. Details: {ex.Message}";
                }
            }
            else if (toolName == "cancel_booking")
{
    try
    {
        var normalizedPhone = customerNumber.Replace("whatsapp:", "");
        var booking = await _db.Bookings.FirstOrDefaultAsync(b =>
            b.SalonId == salonId &&
            (b.CustomerPhone == customerNumber ||
             b.CustomerPhone == $"whatsapp:{normalizedPhone}" ||
             b.CustomerPhone!.Contains(normalizedPhone)) &&
            b.Status == "confirmed");

        if (booking == null)
        {
            booking = await _db.Bookings
                .Where(b => b.SalonId == salonId && b.Status == "confirmed" && b.AppointmentDate > DateTime.UtcNow)
                .OrderBy(b => b.AppointmentDate)
                .FirstOrDefaultAsync();
        }
                    if (booking != null)
                    {
                        booking.Status = "cancelled";
                        await _db.SaveChangesAsync();

                        // Notify waitlist
                        var waitlistEntries = await _db.WaitlistEntries
                            .Where(w => w.SalonId == salonId && !w.Notified)
                            .ToListAsync();

                        foreach (var entry in waitlistEntries)
{
    try
    {
        var notifyMessage = $"Goed nieuws {entry.CustomerName}! 🎉 Er is een plek vrijgekomen bij {salon?.Name}. Wil je een afspraak maken? Stuur ons een bericht!";
        
        var twilioSid = _config["Twilio__AccountSid"];
        var twilioToken = _config["Twilio__AuthToken"];
        var twilioFrom = _config["Twilio__WhatsAppNumber"];

        using var twilioClient = new HttpClient();
        var authBytes = System.Text.Encoding.ASCII.GetBytes($"{twilioSid}:{twilioToken}");
        twilioClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

        var twilioContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["From"] = twilioFrom!,
            ["To"] = entry.CustomerPhone,
            ["Body"] = notifyMessage
        });

        await twilioClient.PostAsync($"https://api.twilio.com/2010-04-01/Accounts/{twilioSid}/Messages.json", twilioContent);
        entry.Notified = true;
    }
    catch { }
}
await _db.SaveChangesAsync();

toolResult = $"Booking cancelled successfully. {waitlistEntries.Count} waitlist customers notified.";
                    }
                    else
                    {
                        toolResult = "No booking found for that date.";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to cancel booking");
                    toolResult = $"Cancellation failed: {ex.Message}";
                }
            }
            else if (toolName == "add_to_waitlist")
            {
                try
                {
                    var customerName = toolInput.GetProperty("customer_name").GetString()!;
                    var customerPhone = toolInput.GetProperty("customer_phone").GetString()!;
                    var preferredService = toolInput.GetProperty("preferred_service").GetString()!;
                    var preferredStylist = toolInput.TryGetProperty("preferred_stylist", out var stylistProp) ? stylistProp.GetString()! : "";
                    var preferredDay = toolInput.GetProperty("preferred_day").GetString()!;
                    var dayOfWeek = Enum.TryParse<DayOfWeek>(preferredDay, true, out var day) ? day : DayOfWeek.Monday;

                    var entry = new WaitlistEntry
                    {
                        SalonId = salonId,
                        CustomerPhone = customerPhone,
                        CustomerName = customerName,
                        PreferredService = preferredService,
                        PreferredStylist = preferredStylist,
                        PreferredDay = dayOfWeek
                    };

                    _db.WaitlistEntries.Add(entry);
                    await _db.SaveChangesAsync();

                    toolResult = $"{customerName} added to waitlist for {preferredService} on {preferredDay}.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add to waitlist");
                    toolResult = $"Waitlist failed: {ex.Message}";
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

        // Save reply to database
        var outgoingMsg = new Conversation
        {
            PhoneNumber = customerNumber,
            SalonId = salonId,
            Role = "assistant",
            Message = reply,
            CreatedAt = DateTime.UtcNow
        };
        _db.Conversations.Add(outgoingMsg);
        await _db.SaveChangesAsync();

        return reply;
    }

    private string GetSalonSystemPrompt(string customerContext, Salon? salon)
    {
        var today = DateTime.Now.ToString("dddd d MMMM yyyy");
        var tomorrow = DateTime.Now.AddDays(1).ToString("dddd d MMMM yyyy");

        var salonName = salon?.Name ?? "Kapsalon Demo";
        var address = salon?.Address ?? "Molenstraat 12, Nijmegen";
        var hours = salon?.HoursText ?? "Monday-Friday 9:00-18:00, Saturday 9:00-16:00, Closed Sunday";
        var services = salon?.ServicesText ?? "Knippen - €25 - 30 min\nKnippen + Wassen - €35 - 45 min\nVerven - €55 - 90 min\nHighlights - €65 - 90 min\nBaard trimmen - €15 - 15 min\nKinderen - €18 - 30 min";
        var team = salon?.TeamText ?? "Sarah - Ma, Wo, Vr, Za\nKevin - Di, Do, Vr, Za";

        return $"""
                You are Kappi, the AI receptionist for {salonName}.

                Today is {today}. Tomorrow is {tomorrow}.
                You always know the current date. Never ask the customer what day it is.

                {customerContext}

                SALON INFO:
                - Name: {salonName}
                - Address: {address}
                - Hours: {hours}

                SERVICES & PRICES:
                {services}

                TEAM & AVAILABILITY:
                {team}

                BOOKING RULES:
                - If this is a returning customer, greet them by name warmly
                - When a customer gives their name, date, time and service — call create_booking IMMEDIATELY
                - Do NOT ask for confirmation before calling the tool
                - If the stylist works on that day and the time is within salon hours — BOOK IT
                - After the tool succeeds, confirm the booking details to the customer
                - If the tool result mentions a loyalty milestone, congratulate them and mention a reward
                - When a customer wants to cancel, call cancel_booking immediately
                - When a customer asks to be on a waitlist, call add_to_waitlist immediately
                - When cancellation succeeds, tell the customer it is cancelled and wish them well
                - Respond in the same language the customer uses (Dutch or English)
                - Be friendly and concise — this is WhatsApp, not email
                """;
    }
}