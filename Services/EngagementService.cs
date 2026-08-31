using Microsoft.EntityFrameworkCore;

namespace KappiApi.Services;

public interface IEngagementService
{
    Task SendReviewRequestsAsync();
    Task SendBirthdayMessagesAsync();
    Task SendRebookingSuggestionsAsync();
}

public class EngagementService : IEngagementService
{
    private readonly AppDbContext _db;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<EngagementService> _logger;

    public EngagementService(AppDbContext db, IWhatsAppService whatsAppService, ILogger<EngagementService> logger)
    {
        _db = db;
        _whatsAppService = whatsAppService;
        _logger = logger;
    }

    // Runs hourly — finds appointments that finished 1-2 hours ago and asks for a review
    public async Task SendReviewRequestsAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var windowStart = nowUtc.AddHours(-2);
        var windowEnd = nowUtc.AddHours(-1);

        var bookingsToReview = await _db.Bookings
            .Include(b => b.Salon)
            .Where(b =>
                b.Status == "confirmed" &&
                b.AppointmentDate >= windowStart &&
                b.AppointmentDate <= windowEnd &&
                b.ReviewRequestSent == false)
            .ToListAsync();

        foreach (var booking in bookingsToReview)
        {
            try
            {
                if (string.IsNullOrEmpty(booking.CustomerPhone))
                {
                    booking.ReviewRequestSent = true;
                    continue;
                }

                var reviewLink = booking.Salon.GoogleReviewUrl;
                var linkLine = !string.IsNullOrEmpty(reviewLink) ? $"\n\n⭐ {reviewLink}" : "";

                var message = $"Hoi! We hopen dat je je nieuwe look geweldig vindt! 💇\n\n" +
                              $"Zou je een moment willen nemen om ons een review te geven? Dat helpt {booking.Salon.Name} enorm!{linkLine}\n\n" +
                              $"Bedankt! 🙏";

                await _whatsAppService.SendMessageAsync(booking.CustomerPhone, message);
                booking.ReviewRequestSent = true;
                await _db.SaveChangesAsync();
                _logger.LogInformation("Review request sent for booking {Id}", booking.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send review request for booking {Id}", booking.Id);
            }
        }
    }

    // Runs daily — checks customers whose birthday is today
    public async Task SendBirthdayMessagesAsync()
    {
        var amsterdamZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var todayAmsterdam = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, amsterdamZone);
        var currentYear = todayAmsterdam.Year;

        var customers = await _db.Customers
            .Include(c => c.Salon)
            .Where(c =>
                c.Birthday != null &&
                c.Birthday.Value.Month == todayAmsterdam.Month &&
                c.Birthday.Value.Day == todayAmsterdam.Day &&
                c.LastBirthdayMessageYear != currentYear &&
                c.PhoneNumber != "")
            .ToListAsync();

        foreach (var customer in customers)
        {
            try
            {
                var message = $"🎉 Gefeliciteerd met je verjaardag, {customer.Name}! 🎂\n\n" +
                              $"Team {customer.Salon.Name} wenst je een fantastische dag. " +
                              $"Trakteer jezelf op een nieuwe look — je bent altijd welkom! 💇✨";

                await _whatsAppService.SendMessageAsync(customer.PhoneNumber, message);
                customer.LastBirthdayMessageYear = currentYear;
                await _db.SaveChangesAsync();
                _logger.LogInformation("Birthday message sent to customer {Id}", customer.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send birthday message to customer {Id}", customer.Id);
            }
        }
    }

    // Runs daily — nudges customers whose last visit was 6+ weeks ago with no upcoming booking
    public async Task SendRebookingSuggestionsAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var sixWeeksAgo = nowUtc.AddDays(-42);
        var nudgeCooldown = nowUtc.AddDays(-30); // don't nudge again within 30 days

        var candidates = await _db.Customers
            .Include(c => c.Salon)
            .Where(c =>
                c.LastVisit != null &&
                c.LastVisit <= sixWeeksAgo &&
                (c.LastRebookingNudgeSent == null || c.LastRebookingNudgeSent <= nudgeCooldown) &&
                c.PhoneNumber != "")
            .ToListAsync();

        foreach (var customer in candidates)
        {
            try
            {
                var hasUpcoming = await _db.Bookings.AnyAsync(b =>
                    b.CustomerPhone == customer.PhoneNumber &&
                    b.SalonId == customer.SalonId &&
                    b.Status == "confirmed" &&
                    b.AppointmentDate > nowUtc);

                if (hasUpcoming) continue;

                var weeksAgo = (int)((nowUtc - customer.LastVisit!.Value).TotalDays / 7);
                var serviceMention = !string.IsNullOrEmpty(customer.PreferredService) ? $" een {customer.PreferredService}" : " een knipbeurt";

                var message = $"Hoi {customer.Name}! Het is alweer {weeksAgo} weken geleden sinds je laatste bezoek bij {customer.Salon.Name}. " +
                              $"Tijd voor{serviceMention}? 💇\n\nStuur ons gewoon een berichtje om in te plannen!";

                await _whatsAppService.SendMessageAsync(customer.PhoneNumber, message);
                customer.LastRebookingNudgeSent = nowUtc;
                await _db.SaveChangesAsync();
                _logger.LogInformation("Rebooking suggestion sent to customer {Id}", customer.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send rebooking suggestion to customer {Id}", customer.Id);
            }
        }
    }
}