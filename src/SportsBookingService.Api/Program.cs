using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

string connectionString;
if (builder.Environment.IsDevelopment())
{
    var useNeon = builder.Configuration.GetValue<bool>("Database:UseNeon");
    var key = useNeon ? "Neon" : "Local";
    connectionString = builder.Configuration.GetConnectionString(key)
        ?? throw new InvalidOperationException($"Connection string '{key}' is not configured. Set it via 'dotnet user-secrets set \"ConnectionStrings:{key}\" \"...\"'.");
}
else
{
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173", "https://sports-booking-ui.vercel.app")
              .AllowAnyMethod()
              .AllowAnyHeader()));

builder.Services.AddScoped<CostService>();
builder.Services.AddScoped<EventMapper>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseForwardedHeaders();
app.UseCors();
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok());

app.Run();

public partial class Program { }
