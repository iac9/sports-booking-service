using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Tests;

/// <summary>
/// Shared wiring for the Sqlite-in-memory test harness — the same hand-rolled DI the existing
/// suites do inline, factored out so new suites don't each re-derive it.
/// </summary>
public static class TestServices
{
    public static IConfiguration EmptyConfiguration { get; } =
        new ConfigurationBuilder().AddInMemoryCollection([]).Build();

    public static IConfiguration ConfigurationWith(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    public static SqliteConnection OpenSharedConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    public static AppDbContext NewContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

    public static CurrentMemberService NewCurrentMemberService(AppDbContext db, string clerkUserId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", clerkUserId)])),
            },
        };
        return new CurrentMemberService(httpContextAccessor, db, NullLogger<CurrentMemberService>.Instance);
    }

    public static EventService NewEventService(AppDbContext db, CurrentMemberService currentMemberService) =>
        new(db, new EventMapper(db, new CostService(), currentMemberService), currentMemberService);

    public static PendingBookingService NewPendingBookingService(AppDbContext db, CurrentMemberService currentMemberService)
    {
        var eventMapper = new EventMapper(db, new CostService(), currentMemberService);
        return new PendingBookingService(
            db,
            new EventService(db, eventMapper, currentMemberService),
            eventMapper,
            currentMemberService,
            NullLogger<PendingBookingService>.Instance);
    }
}
