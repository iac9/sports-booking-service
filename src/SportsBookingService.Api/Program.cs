using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using SportsBookingService.Api.Auth;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.Jobs;
using SportsBookingService.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

var clerkFrontendApiUrl = builder.Configuration["Clerk:FrontendApiUrl"]
    ?? throw new InvalidOperationException("Configuration 'Clerk:FrontendApiUrl' is not set.");
var clerkJwksAddress = $"{clerkFrontendApiUrl}/.well-known/jwks.json";

// Clerk's default session tokens carry no `aud` claim (that requires a dashboard-configured
// JWT template), so audience validation stays off. Instead we validate the `azp` claim Clerk
// does include by default, per Clerk's own recommendation, against the same origins CORS
// already trusts — this achieves the equivalent protection (reject tokens minted for a
// different frontend) without needing any external Clerk dashboard configuration.
var allowedFrontendOrigins = new[] { "http://localhost:5173", "https://sports-booking-ui.vercel.app" };

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
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var azp = context.Principal?.FindFirst("azp")?.Value;
                if (azp is not null && !allowedFrontendOrigins.Contains(azp))
                {
                    context.Fail("Token was not issued for an authorized origin.");
                }
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("GroupLookup", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

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

    // Hosted on Render, whose edge proxy has no fixed/publishable IP to add to KnownProxies —
    // the container's port is only reachable through Render's routing, never directly from the
    // public internet, so the immediate connecting peer is always that edge. ForwardLimit stays
    // at its default of 1 so only the single, closest hop is processed: Render appends to
    // X-Forwarded-For rather than replacing it, so the rightmost entry is always the value Render
    // itself observed, never something a client could prepend/spoof.
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedFrontendOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()));

builder.Services.AddScoped<CostService>();
builder.Services.AddScoped<EventMapper>();
builder.Services.AddScoped<CurrentMemberService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<DebtService>();
builder.Services.AddHostedService<EventRetentionCleanupService>();

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
app.UseRateLimiter();
app.MapControllers().RequireAuthorization();
app.MapGet("/health", () => Results.Ok());
app.MapGet("/health/db", async (AppDbContext db) => await db.Database.CanConnectAsync() ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.Run();

public partial class Program { }
