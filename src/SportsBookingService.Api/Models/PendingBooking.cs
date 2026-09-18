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

    /// <summary>
    /// Every court/time-slot the email mentioned — one entry for a normal single-court booking,
    /// more when the email books several. <see cref="ExtractedStartTime"/>/<see
    /// cref="ExtractedEndTime"/> above always mirror the first entry here, for the review queue's
    /// one-line summary.
    /// </summary>
    public ICollection<ExtractedCourtBooking> ExtractedCourtBookings { get; set; } = new List<ExtractedCourtBooking>();

    /// <summary>
    /// The name the booking is under, as written in the email. Used to identify who paid:
    /// <see cref="Member"/> has no email column, and with auto-forward rules the envelope
    /// sender is the forwarder rather than the booker, so the body is the only signal.
    /// </summary>
    public string? ExtractedBookedByName { get; set; }

    /// <summary>
    /// Review-readiness rather than raw extraction accuracy: starts from how many fields were
    /// extracted, then drops when the venue or payer couldn't be matched to existing records.
    /// This is what the review UI's "Partial" badge reflects. Informational — the auto-accept
    /// gate checks the individual verifiable conditions, never this number.
    /// </summary>
    public double? ExtractionConfidence { get; set; }
    public string? ExtractedRawJson { get; set; }
    public string? ExtractionError { get; set; }

    // ── Matching ────────────────────────────────────────────────────────────────
    public Guid? MatchedLocationId { get; set; }
    public Location? MatchedLocation { get; set; }
    public Guid? MatchedEventId { get; set; }
    public Event? MatchedEvent { get; set; }
    public Guid? MatchedPayerMemberId { get; set; }
    public Member? MatchedPayerMember { get; set; }

    // ── Review outcome ──────────────────────────────────────────────────────────
    public Guid? CreatedEventId { get; set; }
    public Event? CreatedEvent { get; set; }
    public Guid? ReviewedByMemberId { get; set; }
    public Member? ReviewedByMember { get; set; }
    public DateTime? ReviewedAt { get; set; }

    /// <summary>
    /// True when the event was created by the auto-accept gate rather than by a member
    /// pressing confirm. Surfaced in the review queue so an unreviewed creation is never
    /// indistinguishable from a reviewed one.
    /// </summary>
    public bool AutoAccepted { get; set; }

    public DateTime CreatedAt { get; set; }
}
