using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Tests;

/// <summary>
/// The auto-accept gate. Every condition gets its own test asserting the booking falls back to
/// the review queue and creates nothing, because the cost of a false positive here is a real
/// event with a real expense feeding the debt calculations, created with nobody looking.
/// </summary>
public class AutoAcceptBookingTests : IDisposable
{
    private const string Token = "ab12cd34ef56";
    private const string VenueName = "Melbourne Sports Centre";

    private readonly SqliteConnection connection;
    private readonly Guid groupId = Guid.NewGuid();
    private readonly Guid locationId = Guid.NewGuid();
    private readonly Guid aliceId = Guid.NewGuid();

    // Far enough ahead that the "not in the past" rule can't be tripped by clock drift.
    private static readonly DateOnly FutureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    public AutoAcceptBookingTests()
    {
        connection = TestServices.OpenSharedConnection();

        using var db = TestServices.NewContext(connection);
        db.Database.EnsureCreated();
        db.Groups.Add(new Group
        {
            Id = groupId,
            Name = "Badminton",
            Code = "BAD001",
            InboundEmailToken = Token,
            AutoAcceptBookings = true,
            AutoAcceptMaxCost = 100m,
        });
        db.Members.Add(new Member { Id = aliceId, Name = "Alice", GroupId = groupId, ClerkUserId = "clerk-alice" });
        db.Locations.Add(new Location { Id = locationId, Name = VenueName, Address = "1 Court St", GroupId = groupId });
        db.SaveChanges();
    }

    public void Dispose() => connection.Dispose();

    private sealed class FakeExtractor(BookingExtractionResult result) : IBookingExtractionService
    {
        public Task<BookingExtractionResult> ExtractAsync(string fromEmail, string rawEmailText, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private static BookingExtractionResult Extraction(
        string? venueName = VenueName,
        DateOnly? date = null,
        string? startTime = "18:00",
        string? endTime = "20:00",
        decimal? cost = 48.50m,
        string? currency = "AUD",
        string? bookedByName = "Alice",
        double confidence = 1.0) =>
        new(venueName, date ?? FutureDate, startTime, endTime, cost, currency, bookedByName, confidence, "{}");

    private async Task IngestAsync(BookingExtractionResult extraction)
    {
        using var db = TestServices.NewContext(connection);
        var eventService = TestServices.NewEventService(db, TestServices.NewCurrentMemberService(db, "clerk-anonymous"));
        var service = new EmailIngestionService(db, new FakeExtractor(extraction), eventService, NullLogger<EmailIngestionService>.Instance);

        await service.IngestAsync(new CloudMailinInboundWebhookPayload(
            new CloudMailinEnvelope($"inbox+{Token}@cloudmailin.net", "alice@gmail.com", null),
            new CloudMailinHeaders("noreply@yepbooking.com", "Booking confirmation"),
            "Your booking is confirmed.",
            null));
    }

    private async Task SetGroupAsync(Action<Group> mutate)
    {
        using var db = TestServices.NewContext(connection);
        mutate(await db.Groups.SingleAsync(g => g.Id == groupId));
        await db.SaveChangesAsync();
    }

    private async Task AssertWentToReviewAsync()
    {
        using var db = TestServices.NewContext(connection);
        var pending = await db.PendingBookings.SingleAsync();
        Assert.Equal(PendingBookingStatus.Pending, pending.Status);
        Assert.False(pending.AutoAccepted);
        Assert.Null(pending.CreatedEventId);
        Assert.Empty(db.Events);
        Assert.Empty(db.Expenses);
    }

    // ── The happy path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Creates_the_event_and_expense_when_every_condition_holds()
    {
        await IngestAsync(Extraction());

        using var db = TestServices.NewContext(connection);
        var pending = await db.PendingBookings.SingleAsync();
        Assert.Equal(PendingBookingStatus.Confirmed, pending.Status);
        Assert.True(pending.AutoAccepted);
        Assert.NotNull(pending.ReviewedAt);
        Assert.Equal(aliceId, pending.ReviewedByMemberId);
        Assert.Equal(aliceId, pending.MatchedPayerMemberId);
        Assert.Equal(1.0, pending.ExtractionConfidence);

        var ev = await db.Events.Include(e => e.CourtBookings).SingleAsync();
        Assert.Equal(pending.CreatedEventId, ev.Id);
        Assert.Equal(VenueName, ev.Title);
        Assert.Equal(FutureDate, ev.Date);
        Assert.Equal(locationId, ev.LocationId);
        Assert.Equal(aliceId, ev.CreatedByMemberId);
        var court = Assert.Single(ev.CourtBookings);
        Assert.Equal("18:00", court.StartTime);
        Assert.Equal("20:00", court.EndTime);

        // The payer is the matched member, never a guess — this is what feeds the debt maths.
        var expense = await db.Expenses.SingleAsync();
        Assert.Equal(48.50m, expense.Amount);
        Assert.Equal(aliceId, expense.PaidByMemberId);
    }

    [Fact]
    public async Task The_payer_is_enrolled_as_an_attendee_so_the_cost_split_has_someone_to_split_across()
    {
        await IngestAsync(Extraction());

        using var db = TestServices.NewContext(connection);
        var attendee = Assert.Single(await db.Attendees.ToListAsync());
        Assert.Equal(aliceId, attendee.MemberId);
    }

    // ── Each condition, individually ────────────────────────────────────────────

    [Fact]
    public async Task Declines_when_the_group_has_not_opted_in()
    {
        await SetGroupAsync(g => g.AutoAcceptBookings = false);
        await IngestAsync(Extraction());
        await AssertWentToReviewAsync();
    }

    [Fact]
    public async Task Declines_when_no_cost_ceiling_is_configured()
    {
        // Half-configured must fail closed: "no limit" is not permission to accept any amount.
        await SetGroupAsync(g => g.AutoAcceptMaxCost = null);
        await IngestAsync(Extraction());
        await AssertWentToReviewAsync();
    }

    [Fact]
    public async Task Declines_when_the_cost_exceeds_the_ceiling()
    {
        await IngestAsync(Extraction(cost: 250m));
        await AssertWentToReviewAsync();
    }

    [Fact]
    public async Task Declines_when_extraction_was_incomplete()
    {
        await IngestAsync(Extraction(currency: null, confidence: 0.5));
        await AssertWentToReviewAsync();
    }

    [Fact]
    public async Task Declines_when_the_venue_only_matches_partially()
    {
        // "Melbourne Sports" would pre-select the dropdown for a human, but is too loose to
        // create an event unattended.
        await IngestAsync(Extraction(venueName: "Melbourne Sports"));
        await AssertWentToReviewAsync();

        using var db = TestServices.NewContext(connection);
        var pending = await db.PendingBookings.SingleAsync();
        Assert.Equal(locationId, pending.MatchedLocationId);
        Assert.Equal(0.5, pending.ExtractionConfidence);
    }

    [Fact]
    public async Task Declines_when_the_venue_matches_nothing()
    {
        await IngestAsync(Extraction(venueName: "Brisbane Badminton Hall"));
        await AssertWentToReviewAsync();
    }

    [Fact]
    public async Task Declines_when_the_booker_name_matches_nobody()
    {
        await IngestAsync(Extraction(bookedByName: "Zachary"));
        await AssertWentToReviewAsync();

        using var db = TestServices.NewContext(connection);
        var pending = await db.PendingBookings.SingleAsync();
        Assert.Null(pending.MatchedPayerMemberId);
        Assert.Equal(0.5, pending.ExtractionConfidence);
    }

    [Fact]
    public async Task Declines_when_the_booker_name_is_ambiguous()
    {
        // Two members whose names both contain "Sam" and neither of which matches it exactly,
        // so there is no principled way to pick the payer.
        using (var seed = TestServices.NewContext(connection))
        {
            seed.Members.Add(new Member { Id = Guid.NewGuid(), Name = "Sam Smith", GroupId = groupId, ClerkUserId = "clerk-sam-1" });
            seed.Members.Add(new Member { Id = Guid.NewGuid(), Name = "Sam Jones", GroupId = groupId, ClerkUserId = "clerk-sam-2" });
            await seed.SaveChangesAsync();
        }

        await IngestAsync(Extraction(bookedByName: "Sam"));

        using var db = TestServices.NewContext(connection);
        var pending = await db.PendingBookings.SingleAsync();
        Assert.Equal(PendingBookingStatus.Pending, pending.Status);
        Assert.Null(pending.MatchedPayerMemberId);
        Assert.Empty(db.Events);
    }

    [Fact]
    public async Task Declines_when_the_booking_date_is_in_the_past()
    {
        await IngestAsync(Extraction(date: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)));
        await AssertWentToReviewAsync();
    }

    [Fact]
    public async Task Declines_when_a_time_is_missing()
    {
        await IngestAsync(Extraction(endTime: null, confidence: 1.0));
        await AssertWentToReviewAsync();
    }

    [Fact]
    public async Task Declines_when_no_cost_was_extracted()
    {
        await IngestAsync(Extraction(cost: null));
        await AssertWentToReviewAsync();
    }

    [Fact]
    public async Task Declines_when_the_cost_is_zero()
    {
        await IngestAsync(Extraction(cost: 0m));
        await AssertWentToReviewAsync();
    }

    // ── Duplicate protection ────────────────────────────────────────────────────

    [Fact]
    public async Task Declines_when_an_event_already_exists_on_that_date()
    {
        using (var seed = TestServices.NewContext(connection))
        {
            seed.Events.Add(new Event
            {
                Id = Guid.NewGuid(),
                Title = "Booked by hand",
                Date = FutureDate,
                GroupId = groupId,
                CreatedByMemberId = aliceId,
            });
            await seed.SaveChangesAsync();
        }

        await IngestAsync(Extraction());

        using var db = TestServices.NewContext(connection);
        Assert.Equal(PendingBookingStatus.Pending, (await db.PendingBookings.SingleAsync()).Status);
        Assert.Equal(1, await db.Events.CountAsync());
    }

    [Fact]
    public async Task The_same_email_delivered_twice_creates_only_one_event()
    {
        // Forwarding rules really do double-deliver, so this is the case that matters most.
        await IngestAsync(Extraction());
        await IngestAsync(Extraction());

        using var db = TestServices.NewContext(connection);
        Assert.Equal(1, await db.Events.CountAsync());
        Assert.Equal(1, await db.Expenses.CountAsync());

        var rows = await db.PendingBookings.OrderBy(p => p.ReceivedAt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.AutoAccepted);
        Assert.Contains(rows, r => !r.AutoAccepted && r.Status == PendingBookingStatus.Pending);
    }

    // ── Prompt injection ────────────────────────────────────────────────────────

    [Fact]
    public async Task An_email_claiming_a_venue_the_group_never_saved_cannot_auto_accept()
    {
        // The shape of a successful prompt injection: the model returns attacker-chosen values
        // at full confidence. The gate holds anyway, because the venue and payer have to exist
        // in this group's own records — which no email can conjure.
        await IngestAsync(Extraction(venueName: "Attacker Arena", bookedByName: "Mallory", cost: 99m));
        await AssertWentToReviewAsync();
    }
}
