using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;

// `System.Text.RegularExpressions` (needed for the recipient-token regex) also exports a
// `Group` type, which collides with our entity in method signatures below.
using Group = SportsBookingService.Api.Models.Group;

namespace SportsBookingService.Api.Services;

/// <summary>
/// The webhook write path: turns a forwarded booking email into a review-queue row, and — when
/// the group has opted in and every check in <see cref="AutoAcceptBlocker"/> passes — straight
/// into a real event. Called directly and synchronously from <c>WebhooksController</c>; this is
/// push-based, so there's no polling background service like <c>EventRetentionCleanupService</c>.
/// </summary>
public partial class EmailIngestionService(
    AppDbContext db,
    IBookingExtractionService extractionService,
    EventService eventService,
    ILogger<EmailIngestionService> logger)
{
    [GeneratedRegex(@"\+([a-z0-9]{12})@", RegexOptions.IgnoreCase)]
    private static partial Regex InboundTokenPattern { get; }

    public async Task IngestAsync(CloudMailinInboundWebhookPayload payload, CancellationToken cancellationToken = default)
    {
        var token = ExtractToken(payload.Envelope?.To);
        if (token is null)
        {
            logger.LogWarning("Dropping inbound email: recipient carries no group token");
            return;
        }

        var group = await db.Groups
            .Include(g => g.Locations)
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.InboundEmailToken == token, cancellationToken);

        if (group is null)
        {
            logger.LogWarning("Dropping inbound email: no group matches the recipient token");
            return;
        }

        // Persist the raw email first, so a failure anywhere below still leaves the member a
        // row they can transcribe by hand.
        var pending = new PendingBooking
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            Status = PendingBookingStatus.Pending,
            RawEmailFrom = payload.Envelope?.From ?? payload.Headers?.From ?? "",
            RawEmailSubject = payload.Headers?.Subject ?? "",
            RawEmailText = payload.Plain ?? "",
            RawEmailHtml = payload.Html,
            ReceivedAt = DateTime.UtcNow,
            ProviderHint = ProviderHintFrom(payload.Envelope?.From ?? payload.Headers?.From),
            CreatedAt = DateTime.UtcNow,
        };
        db.PendingBookings.Add(pending);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var extraction = await extractionService.ExtractAsync(pending.RawEmailFrom, pending.RawEmailText, cancellationToken);

            pending.ExtractedVenueName = extraction.VenueName;
            pending.ExtractedDate = extraction.Date;
            pending.ExtractedStartTime = extraction.StartTime;
            pending.ExtractedEndTime = extraction.EndTime;
            pending.ExtractedCost = extraction.Cost;
            pending.ExtractedCurrency = extraction.Currency;
            pending.ExtractedBookedByName = extraction.BookedByName;
            pending.ExtractedRawJson = extraction.RawJson;

            // Added via the DbSet rather than `pending.ExtractedCourtBookings.Add(...)` — `pending`
            // was already saved once above and is now tracked Unchanged, and EF only reliably
            // treats a new child as an insert here when it's added this way (same pattern as
            // EventService.UpdateAsync's CourtBookings).
            foreach (var slot in extraction.CourtSlots)
            {
                db.ExtractedCourtBookings.Add(new ExtractedCourtBooking
                {
                    Id = Guid.NewGuid(), Court = slot.Court, StartTime = slot.StartTime, EndTime = slot.EndTime, PendingBookingId = pending.Id,
                });
            }

            var locationMatch = LocationMatcher.Match(extraction.VenueName, group.Locations);
            pending.MatchedLocationId = locationMatch?.Location.Id;

            var payerMatch = MemberMatcher.Match(extraction.BookedByName, group.Members);
            pending.MatchedPayerMemberId = payerMatch?.Member.Id;

            Guid? existingEventId = null;
            if (extraction.Date is not null)
            {
                existingEventId = await db.Events
                    .Where(e => e.GroupId == group.Id && e.Date == extraction.Date.Value)
                    .Select(e => (Guid?)e.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                pending.MatchedEventId = existingEventId;
            }

            // Review-readiness, not extraction accuracy: a venue that only matched partially
            // still needs someone to confirm the dropdown guess, so it counts against this just
            // as a missing match does. The condition mirrors the auto-accept gate below, so a
            // confidence of 1.0 and "eligible on the matching criteria" mean the same thing.
            var matchesAreTrustworthy = locationMatch is { Confidence: >= 1.0 } && payerMatch is not null;
            pending.ExtractionConfidence = matchesAreTrustworthy
                ? extraction.Confidence
                : Math.Min(extraction.Confidence, 0.5);

            await TryAutoAcceptAsync(group, pending, extraction, locationMatch, payerMatch, existingEventId);

            logger.LogInformation(
                "Ingested booking email for group {GroupId} as pending booking {PendingBookingId} (confidence {Confidence}, autoAccepted {AutoAccepted})",
                group.Id, pending.Id, pending.ExtractionConfidence, pending.AutoAccepted);
        }
        catch (Exception ex) when (ex is BookingExtractionException or OperationCanceledException)
        {
            // Extraction is best-effort — the row survives as Failed with the raw email intact,
            // which is exactly the manual-entry fallback the review queue is there for.
            pending.Status = PendingBookingStatus.Failed;
            pending.ExtractionError = ex.Message;
            logger.LogWarning(ex, "Extraction failed for pending booking {PendingBookingId}", pending.Id);
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Creates a real event straight from the email, but only when every check passes.
    /// </summary>
    /// <remarks>
    /// The gate is built from facts the service can verify against its own data rather than
    /// from the extraction's own confidence, which measures completeness and not correctness.
    /// A misread or deliberately crafted email therefore cannot invent a <see cref="Location"/>
    /// the group has saved, a <see cref="Member"/> to bill, or a second event on a date that
    /// already has one.
    /// </remarks>
    private async Task TryAutoAcceptAsync(
        Group group,
        PendingBooking pending,
        BookingExtractionResult extraction,
        LocationMatch? locationMatch,
        MemberMatch? payerMatch,
        Guid? existingEventId)
    {
        // A group with the toggle on but no ceiling set is half-configured: fail closed rather
        // than reading "no limit" as permission to accept any amount.
        if (!group.AutoAcceptBookings || group.AutoAcceptMaxCost is not { } maxCost || maxCost <= 0) return;

        var blocker = AutoAcceptBlocker(extraction, locationMatch, payerMatch, existingEventId, maxCost);
        if (blocker is not null)
        {
            logger.LogInformation(
                "Auto-accept declined for pending booking {PendingBookingId}: {Reason}", pending.Id, blocker);
            return;
        }

        try
        {
            var created = await eventService.CreateAsAsync(group.Id, new CreateEventRequest(
                Title: locationMatch!.Location.Name,
                Date: extraction.Date!.Value.ToString("yyyy-MM-dd"),
                LocationId: locationMatch.Location.Id,
                MaxAttendees: null,
                CourtBookings: extraction.CourtSlots
                    .Select((slot, i) => new CreateCourtBookingRequest(slot.Court ?? (i + 1).ToString(), slot.StartTime, slot.EndTime))
                    .ToList()),
                payerMatch!.Member);

            if (!created.IsSuccess)
            {
                logger.LogWarning("Auto-accept could not create an event for pending booking {PendingBookingId}: {Error}",
                    pending.Id, created.Error!.Kind);
                return;
            }

            var expense = await eventService.AddExpenseWithoutCallerCheckAsync(group.Id, created.Value!.Id,
                new AddExpenseRequest("Court booking", extraction.Cost!.Value, payerMatch.Member.Id));

            if (!expense.IsSuccess)
            {
                // The event exists but the cost didn't attach. Leaving the row Pending would
                // invite a duplicate event, so record the outcome and let the cost be added by
                // hand on the event itself.
                logger.LogWarning("Auto-accepted event {EventId} but could not attach its expense: {Error}",
                    created.Value.Id, expense.Error!.Kind);
            }

            pending.Status = PendingBookingStatus.Confirmed;
            pending.AutoAccepted = true;
            pending.CreatedEventId = created.Value.Id;
            pending.ReviewedByMemberId = payerMatch.Member.Id;
            pending.ReviewedAt = DateTime.UtcNow;

            logger.LogInformation("Auto-accepted pending booking {PendingBookingId} as event {EventId} for member {MemberId}",
                pending.Id, created.Value.Id, payerMatch.Member.Id);
        }
        catch (Exception ex)
        {
            // Never let auto-accept lose the email: fall back to the normal review queue.
            logger.LogError(ex, "Auto-accept threw for pending booking {PendingBookingId}; leaving it for review", pending.Id);
        }
    }

    /// <summary>
    /// Returns why auto-accept must not fire, or null when every condition holds. Kept as one
    /// ordered list so the rules read in a single place and each refusal logs a specific reason.
    /// </summary>
    private static string? AutoAcceptBlocker(
        BookingExtractionResult extraction,
        LocationMatch? locationMatch,
        MemberMatch? payerMatch,
        Guid? existingEventId,
        decimal maxCost)
    {
        if (extraction.Confidence < 1.0) return "not every field was extracted";

        // Exact only. A partial match is fine for pre-selecting a dropdown someone is about to
        // check, but too loose when nobody is going to look.
        if (locationMatch is null || locationMatch.Confidence < 1.0) return "venue did not match a saved location exactly";
        if (payerMatch is null) return "booker name did not match exactly one member";
        if (extraction.Date is not { } date) return "no booking date";
        if (date < DateOnly.FromDateTime(DateTime.UtcNow)) return "booking date is in the past";
        // Every entry in CourtSlots already has both times (see ParseExtraction), so this alone
        // covers "found at least one usable court/time-slot" for however many courts were booked.
        if (extraction.CourtSlots.Count == 0) return "missing start or end time";
        if (extraction.Cost is not { } cost || cost <= 0) return "no usable cost";
        if (cost > maxCost) return $"cost {cost} exceeds the {maxCost} ceiling";

        // Forwarding rules do double-deliver, so this is the duplicate guard as well as a guard
        // against clashing with an event someone already created by hand.
        if (existingEventId is not null) return "an event already exists on that date";

        return null;
    }

    private static string? ExtractToken(string? recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient)) return null;
        var match = InboundTokenPattern.Match(recipient);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static string? ProviderHintFrom(string? fromAddress)
    {
        if (string.IsNullOrWhiteSpace(fromAddress)) return null;
        var at = fromAddress.LastIndexOf('@');
        if (at == -1 || at == fromAddress.Length - 1) return null;
        return fromAddress[(at + 1)..].Trim().TrimEnd('>').ToLowerInvariant();
    }
}
