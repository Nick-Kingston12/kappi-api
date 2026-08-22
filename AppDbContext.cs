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
}

public class Salon
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string WhatsAppNumber { get; set; } = string.Empty;
    public string ServicesJson { get; set; } = string.Empty;
    public string TeamJson { get; set; } = string.Empty;
    public string HoursJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? GoogleAccessToken { get; set; }
public string? GoogleRefreshToken { get; set; }
}

public class Customer
{
    public int Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = "nl";
    public int SalonId { get; set; }
    public Salon Salon { get; set; } = null!;
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