namespace SportsBookingService.Api.Models;

public enum PendingBookingStatus
{
    Pending,
    Confirmed,
    Dismissed,
    Failed,
}

/// <summary>
/// A booking-confirmation email forwarded into the app, captured verbatim before any
/// extraction runs so a failed parse never loses the original. Nothing here becomes a real
/// <see cref="Event"/> until a member reviews and confirms it in the review queue.
/// </summary>
public class PendingBooking
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public PendingBookingStatus Status { get; set; }

    // ── Raw email, persisted before extraction ──────────────────────────────────
    public string RawEmailFrom { get; set; } = "";
    public string RawEmailSubject { get; set; } = "";
    public string RawEmailText { get; set; } = "";
    public string? RawEmailHtml { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? ProviderHint { get; set; }

    // ── Extraction output ───────────────────────────────────────────────────────
    public string? ExtractedVenueName { get; set; }
    public DateOnly? ExtractedDate { get; set; }
    public string? ExtractedStartTime { get; set; }
    public string? ExtractedEndTime { get; set; }
    public decimal? ExtractedCost { get; set; }
    public string? ExtractedCurrency { get; set; }
    public double? ExtractionConfidence { get; set; }
    public string? ExtractedRawJson { get; set; }
    public string? ExtractionError { get; set; }

    // ── Matching ────────────────────────────────────────────────────────────────
    public Guid? MatchedLocationId { get; set; }
    public Location? MatchedLocation { get; set; }
    public Guid? MatchedEventId { get; set; }
    public Event? MatchedEvent { get; set; }

    // ── Review outcome ──────────────────────────────────────────────────────────
    public Guid? CreatedEventId { get; set; }
    public Event? CreatedEvent { get; set; }
    public Guid? ReviewedByMemberId { get; set; }
    public Member? ReviewedByMember { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
