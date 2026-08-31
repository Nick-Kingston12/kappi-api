using Microsoft.EntityFrameworkCore;

namespace KappiApi.Services;

public interface IReminderService
{
    Task SendRemindersAsync();
}

public class ReminderService : IReminderService
{
    private readonly AppDbContext _db;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<ReminderService> _logger;

    public ReminderService(AppDbContext db, IWhatsAppService whatsAppService, ILogger<ReminderService> logger)
    {
        _db = db;
        _whatsAppService = whatsAppService;
        _logger = logger;
    }

    public async Task SendRemindersAsync()
    {
        var amsterdamZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var nowUtc = DateTime.UtcNow;
        var nowAmsterdam = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, amsterdamZone);

        // Find bookings 24 hours from now (within a 1 hour window)
        var reminderWindowStart = nowUtc.AddHours(23);
        var reminderWindowEnd = nowUtc.AddHours(25);

        var bookingsToRemind = await _db.Bookings
            .Include(b => b.Salon)
            .Where(b =>
                b.Status == "confirmed" &&
                b.AppointmentDate >= reminderWindowStart &&
                b.AppointmentDate <= reminderWindowEnd &&
                b.ReminderSent == false)
            .ToListAsync();

        foreach (var booking in bookingsToRemind)
        {
            try
            {
                var appointmentAmsterdam = TimeZoneInfo.ConvertTimeFromUtc(booking.AppointmentDate, amsterdamZone);
                var message = $"📅 Herinnering: Je hebt morgen een afspraak bij {booking.Salon.Name}!\n\n" +
                              $"✂️ {booking.Service} met {booking.Stylist}\n" +
                              $"🕐 {appointmentAmsterdam:HH:mm}\n" +
                              $"📍 {booking.Salon.Name}\n\n" +
                              $"Tot morgen! 😊";

                if (!string.IsNullOrEmpty(booking.CustomerPhone))
{
    await _whatsAppService.SendMessageAsync(booking.CustomerPhone!, message);
    booking.ReminderSent = true;
    await _db.SaveChangesAsync();
    _logger.LogInformation("Reminder sent for booking {Id}", booking.Id);
}
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send reminder for booking {Id}", booking.Id);
            }
        }
    }
}