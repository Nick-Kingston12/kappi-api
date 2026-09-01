using Microsoft.EntityFrameworkCore;

namespace KappiApi;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Salon> Salons => Set<Salon>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<SalonOwner> SalonOwners => Set<SalonOwner>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
}

public class Salon
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string WhatsAppNumber { get; set; } = string.Empty;
    public string ServicesJson { get; set; } = "[]";
    public string TeamJson { get; set; } = "[]";
    public string HoursJson { get; set; } = "{}";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? HoursText { get; set; }
    public string? ServicesText { get; set; }
    public string? TeamText { get; set; }
    public string? GoogleAccessToken { get; set; }
    public string? GoogleRefreshToken { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? GoogleReviewUrl { get; set; }
}

public class Customer
{
    public int Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = "nl";
    public string? PreferredStylist { get; set; }
    public string? PreferredService { get; set; }
    public int TotalBookings { get; set; } = 0;
    public DateTime? LastVisit { get; set; }
    public int SalonId { get; set; }
    public Salon Salon { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int NoShowCount { get; set; } = 0;
    public string? Notes { get; set; }
    public DateTime? Birthday { get; set; }
    public int? LastBirthdayMessageYear { get; set; }
    public DateTime? LastRebookingNudgeSent { get; set; }
}

public class Booking
{
    public int Id { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int SalonId { get; set; }
    public Salon Salon { get; set; } = null!;
    public string Service { get; set; } = string.Empty;
    public string Stylist { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
    public string Status { get; set; } = "confirmed";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool ReminderSent { get; set; } = false;
    public string? CustomerPhone { get; set; }
    public string? EventId { get; set; }
    public bool ReviewRequestSent { get; set; } = false;
    public decimal Price { get; set; } = 0;
}

public class Conversation
{
    public int Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public int SalonId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SalonOwner
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int SalonId { get; set; }
    public Salon Salon { get; set; } = null!;
}

public class WaitlistEntry
{
    public int Id { get; set; }
    public int SalonId { get; set; }
    public Salon Salon { get; set; } = null!;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string PreferredService { get; set; } = string.Empty;
    public string PreferredStylist { get; set; } = string.Empty;
    public DayOfWeek PreferredDay { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Notified { get; set; } = false;
}
