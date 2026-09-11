namespace SportsBookingService.Api.Services;

/// <summary>
/// Structured booking details pulled out of a forwarded confirmation email.
/// Every field is optional — a partial extraction is still useful, since the review queue
/// shows whatever was found and lets a member fill in the rest by hand.
/// </summary>
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
