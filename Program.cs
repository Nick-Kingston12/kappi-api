using KappiApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Register Kappi services
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();
builder.Services.AddScoped<IClaudeService, ClaudeService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.MapControllers();
app.Run();