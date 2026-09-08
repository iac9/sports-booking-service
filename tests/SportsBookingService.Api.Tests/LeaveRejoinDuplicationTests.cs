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

// Empirically verifies the reported "I join twice" bug (duplicate attendee row after
// create -> leave -> rejoin on a self-created event) against the real EventService/EventMapper
// code, backed by a real relational (Sqlite) provider. Each simulated HTTP request gets its own
// fresh AppDbContext (mirroring ASP.NET Core's scoped-per-request lifetime) sharing only the
// underlying connection, rather than reasoning about EF Core's tracked-query behavior from
// documentation alone.
public class LeaveRejoinDuplicationTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly Guid groupId = Guid.NewGuid();
    private readonly Guid creatorId = Guid.NewGuid();
    private const string ClerkUserId = "clerk-alice";

    public LeaveRejoinDuplicationTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Groups.Add(new Group { Id = groupId, Name = "Test Group", Code = "TEST01" });
        db.Members.Add(new Member { Id = creatorId, Name = "Alice", GroupId = groupId, ClerkUserId = ClerkUserId });
        db.SaveChanges();
    }

    public void Dispose() => connection.Dispose();

    private AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        return new AppDbContext(options);
    }

    private EventService NewEventService(AppDbContext db)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", ClerkUserId)])),
            },
        };
        var currentMemberService = new CurrentMemberService(httpContextAccessor, db, NullLogger<CurrentMemberService>.Instance);
        var eventMapper = new EventMapper(db, new CostService(), currentMemberService);
        return new EventService(db, eventMapper, currentMemberService);
    }

    [Fact]
    public async Task CreateThenLeaveThenRejoin_TwiceOver_NeverDuplicatesAttendee()
    {
        Guid eventId;
        using (var db = NewContext())
        {
            var createResult = await NewEventService(db).CreateAsync(groupId, new CreateEventRequest(
                "Badminton", "2026-06-21", null, null, []));
            Assert.True(createResult.IsSuccess);
            eventId = createResult.Value!.Id;
        }

        AssertSingleAttendee(eventId, "after create");

        for (var cycle = 0; cycle < 2; cycle++)
        {
            using (var db = NewContext())
            {
                var leaveResult = await NewEventService(db).LeaveAsync(groupId, eventId);
                Assert.True(leaveResult.IsSuccess);
            }
            using (var db = NewContext())
            {
                Assert.Empty(await db.Attendees.Where(a => a.EventId == eventId).ToListAsync());
            }

            using (var db = NewContext())
            {
                var rsvpResult = await NewEventService(db).RsvpAsync(groupId, eventId);
                Assert.True(rsvpResult.IsSuccess);
            }
            AssertSingleAttendee(eventId, $"after rejoin cycle {cycle}");
        }
    }

    private void AssertSingleAttendee(Guid eventId, string when)
    {
        using var db = NewContext();
        var attendees = db.Attendees.Where(a => a.EventId == eventId).AsNoTracking().ToList();
        Assert.True(attendees.Count == 1, $"expected exactly 1 attendee row {when}, found {attendees.Count}");
        Assert.Equal(creatorId, attendees[0].MemberId);
    }

    // Regression test for the actual bug found: with a single DbContext/EventService reused across
    // Create -> Leave (mirroring, e.g., an in-process test harness or any code path that doesn't get
    // a fresh scope per call), the Member entity from CreateAsync stays tracked with its shadow
    // Attendee-inverse collection marked loaded. Removing the Attendee only from ev.Attendees
    // (not from db.Attendees directly) then silently fails to delete the row, because EF's
    // orphan-detection also consults that still-loaded Member-side collection. This previously
    // failed with "Collection was not empty" before the db.Attendees.Remove/db.WaitlistEntries.Remove
    // fix in EventService.LeaveAsync.
    [Fact]
    public async Task SharedDbContext_CreateThenLeave_StillDeletesAttendeeRow()
    {
        using var db = NewContext();
        var service = NewEventService(db);

        var createResult = await service.CreateAsync(groupId, new CreateEventRequest(
            "Badminton", "2026-06-21", null, null, []));
        Assert.True(createResult.IsSuccess);
        var eventId = createResult.Value!.Id;

        var leaveResult = await service.LeaveAsync(groupId, eventId);
        Assert.True(leaveResult.IsSuccess);

        var attendees = await db.Attendees.Where(a => a.EventId == eventId).AsNoTracking().ToListAsync();
        Assert.Empty(attendees);
    }

    // Reproduces the "join twice" duplicate seen live: within a *single* RsvpAsync call, the guard
    // query at the top tracks the Event with Attendees marked IsLoaded, then db.Attendees.Add(...)
    // fixes the new row into that already-loaded collection, and the eventMapper.LoadEvent(...)
    // re-query at the end re-materializes the same already-loaded collection, appending the new row
    // a second time instead of recognizing it's already present — so the *response to a single join
    // request* comes back with the joining member listed twice.
    [Fact]
    public async Task Rsvp_SingleRequest_DoesNotDuplicateAttendeeInResponse()
    {
        using var db = NewContext();
        var service = NewEventService(db);

        var createResult = await service.CreateAsync(groupId, new CreateEventRequest(
            "Badminton", "2026-06-21", null, null, []));
        Assert.True(createResult.IsSuccess);
        var eventId = createResult.Value!.Id;

        var leaveResult = await service.LeaveAsync(groupId, eventId);
        Assert.True(leaveResult.IsSuccess);

        var rsvpResult = await service.RsvpAsync(groupId, eventId);
        Assert.True(rsvpResult.IsSuccess);

        var attendeeIds = rsvpResult.Value!.Attendees.Select(a => a.Id).ToList();
        Assert.Single(attendeeIds);
        Assert.Equal(creatorId, attendeeIds[0]);
    }

    // Same bug class as Rsvp_SingleRequest_DoesNotDuplicateAttendeeInResponse, but for
    // UpdateAsync's CourtBookings replacement: the guard query loads Event.CourtBookings as
    // IsLoaded, then db.CourtBookings.Add(...) auto-fixes-up the new booking into that collection,
    // so an additional explicit ev.CourtBookings.Add(...) would double it up before the final
    // LoadEvent() re-query.
    [Fact]
    public async Task Update_SingleRequest_DoesNotDuplicateCourtBookingsInResponse()
    {
        using var db = NewContext();
        var service = NewEventService(db);

        var createResult = await service.CreateAsync(groupId, new CreateEventRequest(
            "Badminton", "2026-06-21", null, null, []));
        Assert.True(createResult.IsSuccess);
        var eventId = createResult.Value!.Id;

        var updateResult = await service.UpdateAsync(groupId, eventId, new UpdateEventRequest(
            "Badminton", "2026-06-21", null, null,
            [
                new CreateCourtBookingRequest("Court 1", "18:00", "19:00"),
                new CreateCourtBookingRequest("Court 2", "19:00", "20:00"),
            ]));
        Assert.True(updateResult.IsSuccess);

        Assert.Equal(2, updateResult.Value!.CourtBookings.Count);
    }
}
