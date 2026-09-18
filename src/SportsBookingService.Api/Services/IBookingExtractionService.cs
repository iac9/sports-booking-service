namespace SportsBookingService.Api.Services;

/// <summary>
/// One court/time-slot found in the email. Never constructed with a missing start or end time —
/// a slot without both simply isn't included in <see cref="BookingExtractionResult.CourtSlots"/>.
/// </summary>
public record ExtractedCourtSlot(string? Court, string StartTime, string EndTime);

/// <summary>
/// Structured booking details pulled out of a forwarded confirmation email.
/// Every field is optional — a partial extraction is still useful, since the review queue
/// shows whatever was found and lets a member fill in the rest by hand.
/// </summary>
/// <param name="StartTime">The first court slot's start time, kept for the review queue's
/// one-line summary — see <see cref="CourtSlots"/> for the complete list.</param>
/// <param name="EndTime">The first court slot's end time — see <see cref="CourtSlots"/>.</param>
/// <param name="CourtSlots">
/// Every court/time-slot the email mentions, in the order Gemini returned them — one entry for a
/// normal single-court booking, more for an email that books several courts. Empty when nothing
/// usable was found.
/// </param>
/// <param name="Confidence">
/// 1.0 when every field came back populated, lower when some are missing. Informational only:
/// it pre-selects fields in the review UI and is never used to create anything automatically.
/// </param>
public record BookingExtractionResult(
    string? VenueName,
    DateOnly? Date,
    string? StartTime,
    string? EndTime,
    decimal? Cost,
    string? Currency,
    string? BookedByName,
    IReadOnlyList<ExtractedCourtSlot> CourtSlots,
    double Confidence,
    string RawJson
);

/// <summary>
/// Thrown when extraction can't produce a result at all (transport failure, timeout, or a
/// response that isn't usable JSON). Callers turn this into a <c>Failed</c> review-queue row
/// with the raw email preserved for manual entry.
/// </summary>
public class BookingExtractionException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public interface IBookingExtractionService
{
    Task<BookingExtractionResult> ExtractAsync(string fromEmail, string rawEmailText, CancellationToken cancellationToken = default);
}
