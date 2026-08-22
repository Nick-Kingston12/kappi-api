using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using System.Text.Json;

namespace KappiApi.Services;

public interface IGoogleCalendarService
{
    Task<List<string>> GetAvailableSlots(string accessToken, DateTime date, int durationMinutes);
    Task<string> CreateBooking(string accessToken, string summary, DateTime start, int durationMinutes, string attendeeEmail);
    Task DeleteBooking(string accessToken, string eventId);
    Task<string> RefreshAccessToken(string refreshToken);
}

public class GoogleCalendarService : IGoogleCalendarService
{
    private readonly IConfiguration _config;

    public GoogleCalendarService(IConfiguration config)
    {
        _config = config;
    }

    public async Task<string> RefreshAccessToken(string refreshToken)
    {
        using var http = new HttpClient();
        var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = _config["GoogleClientId"]!,
            ["client_secret"] = _config["GoogleClientSecret"]!,
            ["grant_type"] = "refresh_token"
        }));

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        return result.GetProperty("access_token").GetString()!;
    }

    public async Task<List<string>> GetAvailableSlots(string accessToken, DateTime date, int durationMinutes)
    {
        var service = GetCalendarService(accessToken);
        var startOfDay = date.Date.AddHours(9);
        var endOfDay = date.Date.AddHours(18);

        var request = service.Events.List("primary");
        request.TimeMinDateTimeOffset = startOfDay;
        request.TimeMaxDateTimeOffset = endOfDay;
        request.SingleEvents = true;
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

        var events = await request.ExecuteAsync();
        var bookedSlots = events.Items?
            .Where(e => e.Start?.DateTimeDateTimeOffset != null)
            .Select(e => new
            {
                Start = e.Start.DateTimeDateTimeOffset!.Value.DateTime,
                End = e.End.DateTimeDateTimeOffset!.Value.DateTime
            }).ToList() ?? new();

        var available = new List<string>();
        var current = startOfDay;

        while (current.AddMinutes(durationMinutes) <= endOfDay)
        {
            var slotEnd = current.AddMinutes(durationMinutes);
            var isBooked = bookedSlots.Any(b => current < b.End && slotEnd > b.Start);
            if (!isBooked)
                available.Add(current.ToString("HH:mm"));
            current = current.AddMinutes(30);
        }

        return available;
    }

    public async Task<string> CreateBooking(string accessToken, string summary, DateTime start, int durationMinutes, string attendeeEmail)
    {
        var service = GetCalendarService(accessToken);
        var newEvent = new Event
        {
            Summary = summary,
            Start = new EventDateTime { DateTimeDateTimeOffset = start },
            End = new EventDateTime { DateTimeDateTimeOffset = start.AddMinutes(durationMinutes) }
        };

        if (!string.IsNullOrEmpty(attendeeEmail))
        {
            newEvent.Attendees = new List<EventAttendee> { new() { Email = attendeeEmail } };
        }

        var created = await service.Events.Insert(newEvent, "primary").ExecuteAsync();
        return created.Id;
    }

    public async Task DeleteBooking(string accessToken, string eventId)
    {
        var service = GetCalendarService(accessToken);
        await service.Events.Delete("primary", eventId).ExecuteAsync();
    }

    private CalendarService GetCalendarService(string accessToken)
    {
        var credential = GoogleCredential.FromAccessToken(accessToken);
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Kappi AI"
        });
    }
}