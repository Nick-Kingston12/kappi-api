using KappiApi;
using KappiApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Hangfire;
using Hangfire.PostgreSql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();

// PostgreSQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Hangfire
builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHangfireServer();

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt__Secret"] ?? "KappiAI-Super-Secret-Key-2026-Nijmegen-Netherlands";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });

builder.Services.AddAuthorization();

// Kappi services
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();
builder.Services.AddScoped<IClaudeService, ClaudeService>();
builder.Services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddScoped<IReminderService, ReminderService>();
builder.Services.AddScoped<IEngagementService, EngagementService>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("https://kappi-web-gamma.vercel.app", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Auto-migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseHangfireDashboard("/hangfire");
app.MapControllers();

// Schedule reminder job — runs every hour
RecurringJob.AddOrUpdate<IReminderService>(
    "send-appointment-reminders",
    service => service.SendRemindersAsync(),
    "0 * * * *"
);
RecurringJob.AddOrUpdate<IEngagementService>(
    "send-review-requests",
    service => service.SendReviewRequestsAsync(),
    "0 * * * *"
);

RecurringJob.AddOrUpdate<IEngagementService>(
    "send-birthday-messages",
    service => service.SendBirthdayMessagesAsync(),
    "0 9 * * *"
);

RecurringJob.AddOrUpdate<IEngagementService>(
    "send-rebooking-suggestions",
    service => service.SendRebookingSuggestionsAsync(),
    "0 10 * * *"
);

app.Run();