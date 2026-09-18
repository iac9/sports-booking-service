using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Services;

/// <summary>
/// The authenticated review-queue path. Confirming a booking goes through
/// <see cref="EventService"/> rather than creating an <see cref="Event"/> directly, so the
/// review queue can never drift from the normal creation flow.
/// </summary>
public class PendingBookingService(
    AppDbContext db,
    EventService eventService,
    EventMapper eventMapper,
    CurrentMemberService currentMemberService,
    ILogger<PendingBookingService> logger)
{
    public async Task<ServiceResult<List<PendingBookingResponse>>> ListAsync(Guid groupId)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == groupId))
            return ServiceResult<List<PendingBookingResponse>>.Fail(ServiceErrorKind.NotFound);

        if (await currentMemberService.FindMemberAsync(groupId) is null)
            return ServiceResult<List<PendingBookingResponse>>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        var bookings = await db.PendingBookings
            .Include(p => p.ExtractedCourtBookings)
            .Where(p => p.GroupId == groupId)
            .OrderByDescending(p => p.ReceivedAt)
            .ToListAsync();

        return ServiceResult<List<PendingBookingResponse>>.Success(bookings.Select(Map).ToList());
    }

    public async Task<ServiceResult<EventDetailResponse>> ConfirmAsync(Guid groupId, Guid id, ConfirmPendingBookingRequest request)
    {
        var pending = await db.PendingBookings.FirstOrDefaultAsync(p => p.Id == id && p.GroupId == groupId);
        if (pending is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.NotFound);

        var member = await currentMemberService.FindMemberAsync(groupId);
        if (member is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        if (pending.Status is not (PendingBookingStatus.Pending or PendingBookingStatus.Failed))
            return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.Conflict, "This booking has already been reviewed.");

        var created = await eventService.CreateAsync(groupId, new CreateEventRequest(
            request.Title, request.Date, request.LocationId, request.MaxAttendees, request.CourtBookings));
        if (!created.IsSuccess) return created;

        var eventId = created.Value!.Id;

        if (request.ExpenseAmount is { } amount)
        {
            var description = string.IsNullOrWhiteSpace(request.ExpenseDescription)
                ? "Court booking"
                : request.ExpenseDescription.Trim();

            var expense = await eventService.AddExpenseAsync(groupId, eventId, new AddExpenseRequest(description, amount, member.Id));
            if (!expense.IsSuccess) return ServiceResult<EventDetailResponse>.Fail(expense.Error!.Kind, expense.Error.Message);
        }

        pending.Status = PendingBookingStatus.Confirmed;
        pending.CreatedEventId = eventId;
        pending.ReviewedByMemberId = member.Id;
        pending.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("Pending booking {PendingBookingId} confirmed as event {EventId} by member {MemberId}", id, eventId, member.Id);

        var loaded = await eventMapper.LoadEvent(groupId, eventId);
        return ServiceResult<EventDetailResponse>.Success(eventMapper.MapDetail(loaded!));
    }

    public async Task<ServiceResult> DismissAsync(Guid groupId, Guid id)
    {
        var pending = await db.PendingBookings.FirstOrDefaultAsync(p => p.Id == id && p.GroupId == groupId);
        if (pending is null) return ServiceResult.Fail(ServiceErrorKind.NotFound);

        var member = await currentMemberService.FindMemberAsync(groupId);
        if (member is null) return ServiceResult.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        if (pending.Status is not (PendingBookingStatus.Pending or PendingBookingStatus.Failed))
            return ServiceResult.Fail(ServiceErrorKind.Conflict, "This booking has already been reviewed.");

        pending.Status = PendingBookingStatus.Dismissed;
        pending.ReviewedByMemberId = member.Id;
        pending.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return ServiceResult.Success();
    }

    /// <summary>
    /// Builds this group's forwarding address by splicing its token into the single shared
    /// inbound address as a plus-suffix — <c>localpart+{token}@domain</c>.
    /// </summary>
    public async Task<ServiceResult<InboundEmailAddressResponse>> GetInboundAddressAsync(Guid groupId, string? inboundAddress)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group is null) return ServiceResult<InboundEmailAddressResponse>.Fail(ServiceErrorKind.NotFound);

        if (await currentMemberService.FindMemberAsync(groupId) is null)
            return ServiceResult<InboundEmailAddressResponse>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        var at = inboundAddress?.LastIndexOf('@') ?? -1;
        if (string.IsNullOrWhiteSpace(inboundAddress) || at <= 0)
        {
            logger.LogWarning("Configuration 'Email:InboundAddress' is missing or malformed; cannot build a forwarding address.");
            return ServiceResult<InboundEmailAddressResponse>.Fail(ServiceErrorKind.NotFound, "Inbound email is not configured.");
        }

        // Tokens are backfilled by migration and generated on create, but a row could still
        // predate both if a migration was skipped — mint one on demand rather than 500.
        if (string.IsNullOrWhiteSpace(group.InboundEmailToken))
        {
            group.InboundEmailToken = GroupService.GenerateInboundToken();
            await db.SaveChangesAsync();
        }

        var localPart = inboundAddress[..at];
        var domain = inboundAddress[(at + 1)..];
        return ServiceResult<InboundEmailAddressResponse>.Success(
            new InboundEmailAddressResponse($"{localPart}+{group.InboundEmailToken}@{domain}"));
    }

    private static PendingBookingResponse Map(PendingBooking p) => new(
        p.Id,
        p.Status.ToString(),
        p.RawEmailFrom,
        p.RawEmailSubject,
        p.RawEmailText,
        p.ReceivedAt.ToString("o"),
        p.ProviderHint,
        p.ExtractedVenueName,
        p.ExtractedDate?.ToString("yyyy-MM-dd"),
        p.ExtractedStartTime,
        p.ExtractedEndTime,
        p.ExtractedCourtBookings
            .Select(c => new ExtractedCourtBookingResponse(c.Court, c.StartTime, c.EndTime))
            .ToList(),
        p.ExtractedCost,
        p.ExtractedCurrency,
        p.ExtractedBookedByName,
        p.ExtractionConfidence,
        p.ExtractionError,
        p.MatchedLocationId,
        p.MatchedEventId,
        p.MatchedPayerMemberId,
        p.CreatedEventId,
        p.ReviewedByMemberId,
        p.ReviewedAt?.ToString("o"),
        p.AutoAccepted
    );
}
