using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Tests;

// Verifies the A04 fix: settlement amounts are always recomputed server-side from
// current attendees/expenses, never trusted from the client-supplied request.
public class SettleDebtTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly Guid groupId = Guid.NewGuid();
    private readonly Guid aliceId = Guid.NewGuid();
    private readonly Guid bobId = Guid.NewGuid();
    private const string AliceClerkId = "clerk-alice";
    private const string BobClerkId = "clerk-bob";

    public SettleDebtTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Groups.Add(new Group { Id = groupId, Name = "Alice's Group", Code = "ALICE1" });
        db.Members.Add(new Member { Id = aliceId, Name = "Alice", GroupId = groupId, ClerkUserId = AliceClerkId });
        db.Members.Add(new Member { Id = bobId, Name = "Bob", GroupId = groupId, ClerkUserId = BobClerkId });
        db.SaveChanges();
    }

    public void Dispose() => connection.Dispose();

    private AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        return new AppDbContext(options);
    }

    private static CurrentMemberService NewCurrentMemberService(AppDbContext db, string clerkUserId)
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

    private EventService NewEventService(AppDbContext db, string clerkUserId)
    {
        var currentMemberService = NewCurrentMemberService(db, clerkUserId);
        var eventMapper = new EventMapper(db, new CostService(), currentMemberService);
        return new EventService(db, eventMapper, currentMemberService);
    }

    private DebtService NewDebtService(AppDbContext db, string clerkUserId) =>
        new(db, new CostService(), NewCurrentMemberService(db, clerkUserId));

    // Alice pays a $20 expense, Alice and Bob both attend, so Bob owes Alice $10.
    private async Task<Guid> CreateEventWithDebtAsync()
    {
        using var db = NewContext();
        var created = await NewEventService(db, AliceClerkId).CreateAsync(groupId, new CreateEventRequest(
            "Badminton", "2026-06-21", null, null, []));
        var eventId = created.Value!.Id;

        await NewEventService(db, BobClerkId).RsvpAsync(groupId, eventId);
        await NewEventService(db, AliceClerkId).AddExpenseAsync(groupId, eventId, new AddExpenseRequest("Court fee", 20m, aliceId));

        return eventId;
    }

    [Fact]
    public async Task SettleAsync_RecordsServerComputedAmount_NotClientSupplied()
    {
        var eventId = await CreateEventWithDebtAsync();

        using (var db = NewContext())
        {
            var owed = await NewDebtService(db, AliceClerkId).GetOwedToMemberAsync(groupId, aliceId);
            Assert.True(owed.IsSuccess);
            Assert.Equal(10m, owed.Value!.Single(d => d.EventId == eventId).Amount);
        }

        using (var db = NewContext())
        {
            var result = await NewDebtService(db, AliceClerkId).SettleAsync(groupId, aliceId,
                new SettleDebtRequest(eventId, bobId));

            Assert.True(result.IsSuccess);
            Assert.Equal(10m, result.Value!.Amount);
        }

        using (var db = NewContext())
        {
            var settlement = await db.Settlements.SingleAsync(s => s.EventId == eventId && s.FromMemberId == bobId && s.ToMemberId == aliceId);
            Assert.Equal(10m, settlement.Amount);
        }
    }

    [Fact]
    public async Task SettleAsync_NoOutstandingDebt_ReturnsBadRequest()
    {
        using var db = NewContext();
        var created = await NewEventService(db, AliceClerkId).CreateAsync(groupId, new CreateEventRequest(
            "Badminton", "2026-06-21", null, null, []));
        var eventId = created.Value!.Id;

        // Bob never joined and no expenses exist, so there is nothing to settle.
        var result = await NewDebtService(db, AliceClerkId).SettleAsync(groupId, aliceId,
            new SettleDebtRequest(eventId, bobId));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorKind.BadRequest, result.Error!.Kind);
    }

    [Fact]
    public async Task SettleAsync_CalledTwice_IsIdempotent()
    {
        var eventId = await CreateEventWithDebtAsync();

        using (var db = NewContext())
        {
            var first = await NewDebtService(db, AliceClerkId).SettleAsync(groupId, aliceId,
                new SettleDebtRequest(eventId, bobId));
            Assert.True(first.IsSuccess);
        }

        using (var db = NewContext())
        {
            var second = await NewDebtService(db, AliceClerkId).SettleAsync(groupId, aliceId,
                new SettleDebtRequest(eventId, bobId));
            Assert.True(second.IsSuccess);
            Assert.Equal(10m, second.Value!.Amount);
        }

        using (var db = NewContext())
        {
            var count = await db.Settlements.CountAsync(s => s.EventId == eventId && s.FromMemberId == bobId && s.ToMemberId == aliceId);
            Assert.Equal(1, count);
        }
    }
}
