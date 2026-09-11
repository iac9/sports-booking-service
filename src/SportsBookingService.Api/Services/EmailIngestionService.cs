using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Services;

/// <summary>
/// The webhook write path: turns a forwarded booking email into a review-queue row.
/// Called directly and synchronously from <c>WebhooksController</c> — this is push-based, so
/// there's no polling background service like <c>EventRetentionCleanupService</c>.
/// </summary>
public partial class EmailIngestionService(
    AppDbContext db,
    IBookingExtractionService extractionService,
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
            pending.ExtractionConfidence = extraction.Confidence;
            pending.ExtractedRawJson = extraction.RawJson;

            var match = LocationMatcher.Match(extraction.VenueName, group.Locations);
            pending.MatchedLocationId = match?.Location.Id;

            if (extraction.Date is not null)
            {
                pending.MatchedEventId = await db.Events
                    .Where(e => e.GroupId == group.Id
                        && e.Date == extraction.Date.Value
                        && (match == null || e.LocationId == match.Location.Id))
                    .Select(e => (Guid?)e.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            logger.LogInformation(
                "Ingested booking email for group {GroupId} as pending booking {PendingBookingId} (confidence {Confidence})",
                group.Id, pending.Id, extraction.Confidence);
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
