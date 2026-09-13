using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Tests;

public class EmailIngestionServiceTests : IDisposable
{
    private const string Token = "ab12cd34ef56";
    private readonly SqliteConnection connection;
    private readonly Guid groupId = Guid.NewGuid();
    private readonly Guid locationId = Guid.NewGuid();

    public EmailIngestionServiceTests()
    {
        connection = TestServices.OpenSharedConnection();

        using var db = TestServices.NewContext(connection);
        db.Database.EnsureCreated();
        db.Groups.Add(new Group { Id = groupId, Name = "Badminton", Code = "BAD001", InboundEmailToken = Token });
        db.Locations.Add(new Location { Id = locationId, Name = "Melbourne Sports Centre", Address = "1 Court St", GroupId = groupId });
        db.SaveChanges();
    }

    public void Dispose() => connection.Dispose();

    private sealed class FakeExtractor(Func<BookingExtractionResult> extract) : IBookingExtractionService
    {
        public int CallCount { get; private set; }

        public Task<BookingExtractionResult> ExtractAsync(string fromEmail, string rawEmailText, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(extract());
        }
    }

    private static BookingExtractionResult FullResult(string venueName = "Melbourne Sports Centre", string? bookedByName = "Alice") => new(
        venueName, new DateOnly(2026, 10, 2), "18:00", "20:00", 48.50m, "AUD", bookedByName, 1.0, "{}");

    private static CloudMailinInboundWebhookPayload Payload(string to, string plain = "Your booking is confirmed.") => new(
        new CloudMailinEnvelope(to, "jane@gmail.com", [to]),
        new CloudMailinHeaders("noreply@yepbooking.com", "Booking confirmation"),
        plain,
        null);

    private static EmailIngestionService NewService(AppDbContext db, IBookingExtractionService extractor) =>
        new(db, extractor, TestServices.NewEventService(db, TestServices.NewCurrentMemberService(db, "clerk-nobody")),
            NullLogger<EmailIngestionService>.Instance);

    [Fact]
    public async Task Stores_a_pending_row_with_extracted_fields_and_a_matched_location()
    {
        var extractor = new FakeExtractor(() => FullResult());
        using (var db = TestServices.NewContext(connection))
            await NewService(db, extractor).IngestAsync(Payload($"inbox+{Token}@cloudmailin.net"));

        using var verify = TestServices.NewContext(connection);
        var pending = Assert.Single(verify.PendingBookings);
        Assert.Equal(PendingBookingStatus.Pending, pending.Status);
        Assert.Equal(groupId, pending.GroupId);
        Assert.Equal("Melbourne Sports Centre", pending.ExtractedVenueName);
        Assert.Equal(new DateOnly(2026, 10, 2), pending.ExtractedDate);
        Assert.Equal(48.50m, pending.ExtractedCost);
        Assert.Equal(locationId, pending.MatchedLocationId);
        Assert.Null(pending.ExtractionError);
    }

    [Fact]
    public async Task Captures_the_raw_email_verbatim()
    {
        var extractor = new FakeExtractor(() => FullResult());
        using (var db = TestServices.NewContext(connection))
            await NewService(db, extractor).IngestAsync(Payload($"inbox+{Token}@cloudmailin.net", "Court 3, 6-8pm, $48.50"));

        using var verify = TestServices.NewContext(connection);
        var pending = Assert.Single(verify.PendingBookings);
        Assert.Equal("Court 3, 6-8pm, $48.50", pending.RawEmailText);
        Assert.Equal("jane@gmail.com", pending.RawEmailFrom);
        Assert.Equal("Booking confirmation", pending.RawEmailSubject);
        Assert.Equal("gmail.com", pending.ProviderHint);
    }

    [Fact]
    public async Task Leaves_the_location_unmatched_when_no_saved_venue_resembles_it()
    {
        var extractor = new FakeExtractor(() => FullResult("Brisbane Badminton Hall"));
        using (var db = TestServices.NewContext(connection))
            await NewService(db, extractor).IngestAsync(Payload($"inbox+{Token}@cloudmailin.net"));

        using var verify = TestServices.NewContext(connection);
        Assert.Null(Assert.Single(verify.PendingBookings).MatchedLocationId);
    }

    [Fact]
    public async Task Links_an_existing_event_on_the_same_date_and_location()
    {
        var memberId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        using (var seed = TestServices.NewContext(connection))
        {
            seed.Members.Add(new Member { Id = memberId, Name = "Alice", GroupId = groupId, ClerkUserId = "clerk-alice" });
            seed.Events.Add(new Event
            {
                Id = eventId,
                Title = "Badminton",
                Date = new DateOnly(2026, 10, 2),
                GroupId = groupId,
                LocationId = locationId,
                CreatedByMemberId = memberId,
            });
            seed.SaveChanges();
        }

        var extractor = new FakeExtractor(() => FullResult());
        using (var db = TestServices.NewContext(connection))
            await NewService(db, extractor).IngestAsync(Payload($"inbox+{Token}@cloudmailin.net"));

        using var verify = TestServices.NewContext(connection);
        Assert.Equal(eventId, Assert.Single(verify.PendingBookings).MatchedEventId);
    }

    [Fact]
    public async Task Records_a_failed_row_that_still_holds_the_raw_email_when_extraction_throws()
    {
        var extractor = new FakeExtractor(() => throw new BookingExtractionException("Gemini returned 503."));
        using (var db = TestServices.NewContext(connection))
            await NewService(db, extractor).IngestAsync(Payload($"inbox+{Token}@cloudmailin.net", "Court 3, 6-8pm"));

        using var verify = TestServices.NewContext(connection);
        var pending = Assert.Single(verify.PendingBookings);
        Assert.Equal(PendingBookingStatus.Failed, pending.Status);
        Assert.Equal("Gemini returned 503.", pending.ExtractionError);
        Assert.Equal("Court 3, 6-8pm", pending.RawEmailText);
        Assert.Null(pending.ExtractedVenueName);
    }

    [Theory]
    [InlineData("inbox+zzzzzzzzzzzz@cloudmailin.net")]  // well-formed token, no such group
    [InlineData("inbox+short@cloudmailin.net")]         // wrong length
    [InlineData("inbox@cloudmailin.net")]               // no plus-address at all
    [InlineData("")]
    [InlineData(null)]
    public async Task Drops_an_email_whose_recipient_does_not_identify_a_group(string? recipient)
    {
        var extractor = new FakeExtractor(() => FullResult());
        using (var db = TestServices.NewContext(connection))
            await NewService(db, extractor).IngestAsync(Payload(recipient!));

        using var verify = TestServices.NewContext(connection);
        Assert.Empty(verify.PendingBookings);
        Assert.Equal(0, extractor.CallCount);
    }

    [Fact]
    public async Task Reads_the_token_case_insensitively()
    {
        var extractor = new FakeExtractor(() => FullResult());
        using (var db = TestServices.NewContext(connection))
            await NewService(db, extractor).IngestAsync(Payload($"inbox+{Token.ToUpperInvariant()}@cloudmailin.net"));

        using var verify = TestServices.NewContext(connection);
        Assert.Single(verify.PendingBookings);
    }
}
