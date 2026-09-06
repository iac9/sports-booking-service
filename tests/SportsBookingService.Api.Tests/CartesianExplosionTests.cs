using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Tests;

// EventMapper.IncludeFullGraph joins four sibling collections (CourtBookings, Attendees,
// Waitlist, Expenses) off the same Event in one query — the classic EF Core "cartesian
// explosion" shape. Empirically verifies that a real relational provider still returns each
// collection with the correct count (no duplicate entries), rather than trusting that EF's
// single-query collection materialization dedupes correctly from documentation alone.
public class CartesianExplosionTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly Guid groupId = Guid.NewGuid();
    private readonly Guid creatorId = Guid.NewGuid();
    private readonly Guid otherMemberId = Guid.NewGuid();
    private const string ClerkUserId = "clerk-alice";

    public CartesianExplosionTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Groups.Add(new Group { Id = groupId, Name = "Test Group", Code = "TEST01" });
        db.Members.Add(new Member { Id = creatorId, Name = "Alice", GroupId = groupId, ClerkUserId = ClerkUserId });
        db.Members.Add(new Member { Id = otherMemberId, Name = "Bob", GroupId = groupId, ClerkUserId = "clerk-bob" });
        db.SaveChanges();
    }

    public void Dispose() => connection.Dispose();

    private AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        return new AppDbContext(options);
    }

    private EventService NewEventService(AppDbContext db, string clerkUserId = ClerkUserId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", clerkUserId)])),
            },
        };
        var currentMemberService = new CurrentMemberService(httpContextAccessor, db);
        var eventMapper = new EventMapper(db, new CostService(), currentMemberService);
        return new EventService(db, eventMapper, currentMemberService);
    }

    [Fact]
    public async Task LoadEvent_WithMultipleSiblingCollections_DoesNotDuplicateEntries()
    {
        Guid eventId;
        using (var db = NewContext())
        {
            var createResult = await NewEventService(db).CreateAsync(groupId, new CreateEventRequest(
                "Badminton", "2026-06-21", null, 1,
                [
                    new CreateCourtBookingRequest("Court 1", "18:00", "19:00"),
                    new CreateCourtBookingRequest("Court 2", "19:00", "20:00"),
                ]));
            Assert.True(createResult.IsSuccess);
            eventId = createResult.Value!.Id;
        }

        // Bob RSVPs — event has MaxAttendees = 1 and Alice (creator) already occupies the only
        // spot, so Bob lands on the waitlist, giving us a non-empty Waitlist collection too.
        using (var db = NewContext())
        {
            var rsvpResult = await NewEventService(db, "clerk-bob").RsvpAsync(groupId, eventId);
            Assert.True(rsvpResult.IsSuccess);
        }

        using (var db = NewContext())
        {
            var expenseResult1 = await NewEventService(db).AddExpenseAsync(groupId, eventId,
                new AddExpenseRequest("Shuttlecocks", 10m, creatorId));
            Assert.True(expenseResult1.IsSuccess);
        }
        using (var db = NewContext())
        {
            var expenseResult2 = await NewEventService(db).AddExpenseAsync(groupId, eventId,
                new AddExpenseRequest("Court fee", 20m, creatorId));
            Assert.True(expenseResult2.IsSuccess);
        }

        using var verifyDb = NewContext();
        var eventMapper = new EventMapper(verifyDb, new CostService(),
            new CurrentMemberService(new HttpContextAccessor(), verifyDb));
        var loaded = await eventMapper.LoadEvent(groupId, eventId);
        Assert.NotNull(loaded);

        Assert.Equal(2, loaded!.CourtBookings.Count);
        Assert.Equal(1, loaded.Attendees.Count);
        Assert.Equal(1, loaded.Waitlist.Count);
        Assert.Equal(2, loaded.Expenses.Count);

        var detail = eventMapper.MapDetail(loaded);
        Assert.Equal(2, detail.CourtBookings.Count);
        Assert.Single(detail.Attendees);
        Assert.Single(detail.Waitlist);
        Assert.Equal(2, detail.Expenses.Count);
    }
}
