using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Tests;

public class PendingBookingServiceTests : IDisposable
{
    private const string AliceClerkId = "clerk-alice";
    private const string MalloryClerkId = "clerk-mallory";
    private const string Token = "ab12cd34ef56";

    private readonly SqliteConnection connection;
    private readonly Guid groupId = Guid.NewGuid();
    private readonly Guid aliceId = Guid.NewGuid();
    private readonly Guid locationId = Guid.NewGuid();
    private readonly Guid pendingId = Guid.NewGuid();

    public PendingBookingServiceTests()
    {
        connection = TestServices.OpenSharedConnection();

        using var db = TestServices.NewContext(connection);
        db.Database.EnsureCreated();
        db.Groups.Add(new Group { Id = groupId, Name = "Badminton", Code = "BAD001", InboundEmailToken = Token });
        db.Members.Add(new Member { Id = aliceId, Name = "Alice", GroupId = groupId, ClerkUserId = AliceClerkId });
        db.Locations.Add(new Location { Id = locationId, Name = "Melbourne Sports Centre", Address = "1 Court St", GroupId = groupId });

        // Mallory belongs to a different group, so she must not see or act on this queue.
        var otherGroupId = Guid.NewGuid();
        db.Groups.Add(new Group { Id = otherGroupId, Name = "Other", Code = "OTHER1", InboundEmailToken = "zz99yy88xx77" });
        db.Members.Add(new Member { Id = Guid.NewGuid(), Name = "Mallory", GroupId = otherGroupId, ClerkUserId = MalloryClerkId });

        db.PendingBookings.Add(new PendingBooking
        {
            Id = pendingId,
            GroupId = groupId,
            Status = PendingBookingStatus.Pending,
            RawEmailFrom = "jane@gmail.com",
            RawEmailSubject = "Booking confirmation",
            RawEmailText = "Court 3, 6-8pm, $48.50",
            ReceivedAt = DateTime.UtcNow,
            ExtractedVenueName = "Melbourne Sports Centre",
            ExtractedDate = new DateOnly(2026, 10, 2),
            ExtractedCost = 48.50m,
            ExtractionConfidence = 1.0,
            MatchedLocationId = locationId,
            CreatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    public void Dispose() => connection.Dispose();

    private static PendingBookingService NewService(AppDbContext db, string clerkUserId) =>
        TestServices.NewPendingBookingService(db, TestServices.NewCurrentMemberService(db, clerkUserId));

    private static ConfirmPendingBookingRequest Request(decimal? expenseAmount = null, string? expenseDescription = null) =>
        new("Badminton", "2026-10-02", null, null, [new CreateCourtBookingRequest("3", "18:00", "20:00")], expenseAmount, expenseDescription);

    // ── List ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_returns_the_group_queue_for_a_member()
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewService(db, AliceClerkId).ListAsync(groupId);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!);
        Assert.Equal(pendingId, row.Id);
        Assert.Equal("Pending", row.Status);
        Assert.Equal("2026-10-02", row.ExtractedDate);
        Assert.Equal(locationId, row.MatchedLocationId);
    }

    [Fact]
    public async Task List_rejects_a_caller_who_is_not_in_the_group()
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewService(db, MalloryClerkId).ListAsync(groupId);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorKind.BadRequest, result.Error!.Kind);
    }

    [Fact]
    public async Task List_returns_not_found_for_an_unknown_group()
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewService(db, AliceClerkId).ListAsync(Guid.NewGuid());

        Assert.Equal(ServiceErrorKind.NotFound, result.Error!.Kind);
    }

    // ── Confirm ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirm_creates_a_real_event_and_marks_the_row_reviewed()
    {
        using (var db = TestServices.NewContext(connection))
        {
            var result = await NewService(db, AliceClerkId).ConfirmAsync(groupId, pendingId, Request());
            Assert.True(result.IsSuccess);
            Assert.Equal("Badminton", result.Value!.Title);
            Assert.Equal("2026-10-02", result.Value.Date);
            Assert.Equal("3", Assert.Single(result.Value.CourtBookings).Court);
        }

        using var verify = TestServices.NewContext(connection);
        var pending = await verify.PendingBookings.SingleAsync();
        Assert.Equal(PendingBookingStatus.Confirmed, pending.Status);
        Assert.Equal(aliceId, pending.ReviewedByMemberId);
        Assert.NotNull(pending.ReviewedAt);
        Assert.NotNull(pending.CreatedEventId);
        Assert.Equal(pending.CreatedEventId, (await verify.Events.SingleAsync()).Id);
    }

    [Fact]
    public async Task Confirm_records_an_expense_when_an_amount_is_supplied()
    {
        using (var db = TestServices.NewContext(connection))
        {
            var result = await NewService(db, AliceClerkId).ConfirmAsync(groupId, pendingId, Request(48.50m, "Court hire"));
            var expense = Assert.Single(result.Value!.Expenses);
            Assert.Equal(48.50m, expense.Amount);
            Assert.Equal("Court hire", expense.Description);
            Assert.Equal(aliceId, expense.PaidBy.Id);
        }

        using var verify = TestServices.NewContext(connection);
        Assert.Equal(48.50m, (await verify.Expenses.SingleAsync()).Amount);
    }

    [Fact]
    public async Task Confirm_falls_back_to_a_default_expense_description()
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewService(db, AliceClerkId).ConfirmAsync(groupId, pendingId, Request(48.50m));

        Assert.Equal("Court booking", Assert.Single(result.Value!.Expenses).Description);
    }

    [Fact]
    public async Task Confirm_creates_no_expense_when_no_amount_is_supplied()
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewService(db, AliceClerkId).ConfirmAsync(groupId, pendingId, Request());

        Assert.Empty(result.Value!.Expenses);
    }

    [Fact]
    public async Task Confirming_twice_conflicts_and_creates_only_one_event()
    {
        using (var db = TestServices.NewContext(connection))
            Assert.True((await NewService(db, AliceClerkId).ConfirmAsync(groupId, pendingId, Request())).IsSuccess);

        using (var db = TestServices.NewContext(connection))
        {
            var second = await NewService(db, AliceClerkId).ConfirmAsync(groupId, pendingId, Request());
            Assert.Equal(ServiceErrorKind.Conflict, second.Error!.Kind);
        }

        using var verify = TestServices.NewContext(connection);
        Assert.Equal(1, await verify.Events.CountAsync());
    }

    [Fact]
    public async Task A_failed_row_can_still_be_confirmed_by_hand()
    {
        using (var seed = TestServices.NewContext(connection))
        {
            (await seed.PendingBookings.SingleAsync()).Status = PendingBookingStatus.Failed;
            await seed.SaveChangesAsync();
        }

        using var db = TestServices.NewContext(connection);
        Assert.True((await NewService(db, AliceClerkId).ConfirmAsync(groupId, pendingId, Request())).IsSuccess);
    }

    [Fact]
    public async Task Confirm_rejects_a_caller_who_is_not_in_the_group()
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewService(db, MalloryClerkId).ConfirmAsync(groupId, pendingId, Request());

        Assert.Equal(ServiceErrorKind.BadRequest, result.Error!.Kind);
        Assert.Empty(db.Events);
    }

    [Fact]
    public async Task Confirm_returns_not_found_for_a_booking_in_another_group()
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewService(db, AliceClerkId).ConfirmAsync(groupId, Guid.NewGuid(), Request());

        Assert.Equal(ServiceErrorKind.NotFound, result.Error!.Kind);
    }

    // ── Dismiss ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dismiss_marks_the_row_and_creates_nothing()
    {
        using (var db = TestServices.NewContext(connection))
            Assert.True((await NewService(db, AliceClerkId).DismissAsync(groupId, pendingId)).IsSuccess);

        using var verify = TestServices.NewContext(connection);
        var pending = await verify.PendingBookings.SingleAsync();
        Assert.Equal(PendingBookingStatus.Dismissed, pending.Status);
        Assert.Equal(aliceId, pending.ReviewedByMemberId);
        Assert.Empty(verify.Events);
    }

    [Fact]
    public async Task A_dismissed_row_cannot_then_be_confirmed()
    {
        using (var db = TestServices.NewContext(connection))
            await NewService(db, AliceClerkId).DismissAsync(groupId, pendingId);

        using var confirmDb = TestServices.NewContext(connection);
        var result = await NewService(confirmDb, AliceClerkId).ConfirmAsync(groupId, pendingId, Request());

        Assert.Equal(ServiceErrorKind.Conflict, result.Error!.Kind);
    }

    [Fact]
    public async Task Dismiss_rejects_a_caller_who_is_not_in_the_group()
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewService(db, MalloryClerkId).DismissAsync(groupId, pendingId);

        Assert.Equal(ServiceErrorKind.BadRequest, result.Error!.Kind);
    }

    // ── Inbound address ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Inbound_address_splices_the_group_token_in_as_a_plus_suffix()
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewService(db, AliceClerkId).GetInboundAddressAsync(groupId, "inbox@cloudmailin.net");

        Assert.Equal($"inbox+{Token}@cloudmailin.net", result.Value!.Address);
    }

    [Fact]
    public async Task Inbound_address_mints_a_token_for_a_group_that_somehow_lacks_one()
    {
        using (var seed = TestServices.NewContext(connection))
        {
            (await seed.Groups.SingleAsync(g => g.Id == groupId)).InboundEmailToken = null;
            await seed.SaveChangesAsync();
        }

        using var db = TestServices.NewContext(connection);
        var result = await NewService(db, AliceClerkId).GetInboundAddressAsync(groupId, "inbox@cloudmailin.net");

        Assert.True(result.IsSuccess);
        Assert.Matches(@"^inbox\+[a-z0-9]{12}@cloudmailin\.net$", result.Value!.Address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@cloudmailin.net")]
    public async Task Inbound_address_reports_not_found_when_inbound_mail_is_not_configured(string? configured)
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewService(db, AliceClerkId).GetInboundAddressAsync(groupId, configured);

        Assert.Equal(ServiceErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task Inbound_address_rejects_a_caller_who_is_not_in_the_group()
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewService(db, MalloryClerkId).GetInboundAddressAsync(groupId, "inbox@cloudmailin.net");

        Assert.Equal(ServiceErrorKind.BadRequest, result.Error!.Kind);
    }
}
