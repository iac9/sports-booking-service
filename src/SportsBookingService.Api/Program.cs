using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using SportsBookingService.Api.Auth;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

var clerkFrontendApiUrl = builder.Configuration["Clerk:FrontendApiUrl"]
    ?? throw new InvalidOperationException("Configuration 'Clerk:FrontendApiUrl' is not set.");
var clerkJwksAddress = $"{clerkFrontendApiUrl}/.well-known/jwks.json";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            clerkJwksAddress,
            new ClerkJwksConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = clerkFrontendApiUrl,
            ValidateAudience = false,
        };
    });
builder.Services.AddAuthorization();

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
builder.Services.AddScoped<CurrentMemberService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<EventService>();

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

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (exception is not null)
    {
        context.RequestServices.GetRequiredService<ILogger<Program>>()
            .LogError(exception, "Unhandled exception while processing {Method} {Path}", context.Request.Method, context.Request.Path);
    }

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync("""{"error":"Internal server error"}""");
}));

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers().RequireAuthorization();
app.MapGet("/health", () => Results.Ok());
app.MapGet("/health/db", async (AppDbContext db) => await db.Database.CanConnectAsync() ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.Run();

public partial class Program { }
