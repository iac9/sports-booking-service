namespace SportsBookingService.Api.Models;

/// <summary>
/// One court/time-slot Gemini found in a booking email — plural when an email books more than
/// one court. Kept separate from the confirmed <see cref="CourtBooking"/> table: this is
/// unreviewed extraction output, not yet a real booking on any event.
/// </summary>
public class ExtractedCourtBooking
{
    public Guid Id { get; set; }
    public string? Court { get; set; }
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public Guid PendingBookingId { get; set; }
    public PendingBooking PendingBooking { get; set; } = null!;
}
