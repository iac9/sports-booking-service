using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Services;

public class EventService(AppDbContext db, EventMapper eventMapper, CurrentMemberService currentMemberService)
{
    public async Task<ServiceResult<EventDetailResponse>> CreateAsync(Guid groupId, CreateEventRequest request)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == groupId))
            return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.NotFound);

        var member = await currentMemberService.FindMemberAsync(groupId);
        if (member is null)
            return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        var ev = new Event
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            Date = DateOnly.Parse(request.Date),
            LocationId = request.LocationId,
            Location = request.LocationId is not null ? await db.Locations.FindAsync(request.LocationId) : null,
            MaxAttendees = request.MaxAttendees,
            GroupId = groupId,
            CreatedByMemberId = member.Id,
            CreatedBy = member,
        };

        foreach (var cb in request.CourtBookings)
            ev.CourtBookings.Add(new CourtBooking { Id = Guid.NewGuid(), Court = cb.Court, StartTime = cb.StartTime, EndTime = cb.EndTime, EventId = ev.Id });

        ev.Attendees.Add(new Attendee { EventId = ev.Id, MemberId = member.Id, Member = member, JoinedAt = DateTime.UtcNow });

        db.Events.Add(ev);
        await db.SaveChangesAsync();

        return ServiceResult<EventDetailResponse>.Success(eventMapper.MapDetail(ev));
    }

    public async Task<ServiceResult<EventDetailResponse>> UpdateAsync(Guid groupId, Guid id, UpdateEventRequest request)
    {
        var ev = await db.Events
            .Include(e => e.CourtBookings)
            .FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId);

        if (ev is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.NotFound);

        if (request.Title is not null) ev.Title = request.Title;
        if (request.Date is not null) ev.Date = DateOnly.Parse(request.Date);
        if (request.LocationId is not null) ev.LocationId = request.LocationId;
        ev.MaxAttendees = request.MaxAttendees;

        if (request.CourtBookings is not null)
        {
            foreach (var cb in ev.CourtBookings.ToList())
                db.CourtBookings.Remove(cb);

            foreach (var cb in request.CourtBookings)
            {
                var newBooking = new CourtBooking { Id = Guid.NewGuid(), Court = cb.Court, StartTime = cb.StartTime, EndTime = cb.EndTime, EventId = ev.Id };
                db.CourtBookings.Add(newBooking);
            }
        }

        await db.SaveChangesAsync();
        var updated = await eventMapper.LoadEvent(groupId, id);
        return ServiceResult<EventDetailResponse>.Success(eventMapper.MapDetail(updated!));
    }

    public async Task<ServiceResult> DeleteAsync(Guid groupId, Guid id)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId);
        if (ev is null) return ServiceResult.Fail(ServiceErrorKind.NotFound);

        db.Events.Remove(ev);
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    // ── RSVP ────────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<EventDetailResponse>> RsvpAsync(Guid groupId, Guid id)
    {
        var ev = await db.Events
            .Include(e => e.Attendees)
            .Include(e => e.Waitlist)
            .FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId);

        if (ev is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.NotFound);

        var member = await currentMemberService.FindMemberAsync(groupId);
        if (member is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        if (ev.Attendees.Any(a => a.MemberId == member.Id) || ev.Waitlist.Any(w => w.MemberId == member.Id))
            return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.Conflict, "Already joined or waitlisted.");

        if (ev.MaxAttendees is null || ev.Attendees.Count < ev.MaxAttendees)
        {
            var attendee = new Attendee { EventId = id, MemberId = member.Id, JoinedAt = DateTime.UtcNow };
            db.Attendees.Add(attendee);
        }
        else
        {
            var waitlistEntry = new WaitlistEntry { EventId = id, MemberId = member.Id, JoinedAt = DateTime.UtcNow };
            db.WaitlistEntries.Add(waitlistEntry);
        }

        await db.SaveChangesAsync();
        return ServiceResult<EventDetailResponse>.Success(eventMapper.MapDetail((await eventMapper.LoadEvent(groupId, id))!));
    }

    public async Task<ServiceResult<EventDetailResponse>> LeaveAsync(Guid groupId, Guid id)
    {
        var ev = await db.Events
            .Include(e => e.Attendees)
            .Include(e => e.Waitlist)
            .FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId);

        if (ev is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.NotFound);

        var member = await currentMemberService.FindMemberAsync(groupId);
        if (member is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        var attendee = ev.Attendees.FirstOrDefault(a => a.MemberId == member.Id);
        if (attendee is not null)
        {
            db.Attendees.Remove(attendee);
            var first = ev.Waitlist.OrderBy(w => w.JoinedAt).FirstOrDefault();
            if (first is not null)
            {
                db.WaitlistEntries.Remove(first);
                var promoted = new Attendee { EventId = id, MemberId = first.MemberId, JoinedAt = DateTime.UtcNow };
                db.Attendees.Add(promoted);
            }
        }
        else
        {
            var waitlisted = ev.Waitlist.FirstOrDefault(w => w.MemberId == member.Id);
            if (waitlisted is not null) db.WaitlistEntries.Remove(waitlisted);
        }

        await db.SaveChangesAsync();
        return ServiceResult<EventDetailResponse>.Success(eventMapper.MapDetail((await eventMapper.LoadEvent(groupId, id))!));
    }

    // ── Guests ───────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<EventDetailResponse>> AddGuestAsync(Guid groupId, Guid id, AddGuestRequest request)
    {
        var ev = await db.Events
            .Include(e => e.Attendees).ThenInclude(a => a.Member)
            .Include(e => e.Waitlist).ThenInclude(w => w.Member)
            .FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId);

        if (ev is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.NotFound);

        var host = await currentMemberService.FindMemberAsync(groupId);
        if (host is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        var existingGuestCount = ev.Attendees.Select(a => a.Member)
            .Concat(ev.Waitlist.Select(w => w.Member))
            .Count(m => m.AddedByMemberId == host.Id);

        var name = string.IsNullOrWhiteSpace(request.Name) ? $"{host.Name} + {existingGuestCount + 1}" : request.Name.Trim();
        var guest = new Member { Id = Guid.NewGuid(), Name = name, GroupId = groupId, IsGuest = true, AddedByMemberId = host.Id };
        db.Members.Add(guest);

        if (ev.MaxAttendees is null || ev.Attendees.Count < ev.MaxAttendees)
            db.Attendees.Add(new Attendee { EventId = id, MemberId = guest.Id, JoinedAt = DateTime.UtcNow });
        else
            db.WaitlistEntries.Add(new WaitlistEntry { EventId = id, MemberId = guest.Id, JoinedAt = DateTime.UtcNow });

        await db.SaveChangesAsync();
        return ServiceResult<EventDetailResponse>.Success(eventMapper.MapDetail((await eventMapper.LoadEvent(groupId, id))!));
    }

    public async Task<ServiceResult<EventDetailResponse>> RemoveGuestAsync(Guid groupId, Guid id, Guid guestMemberId)
    {
        var ev = await db.Events
            .Include(e => e.Attendees)
            .Include(e => e.Waitlist)
            .FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId);

        if (ev is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.NotFound);

        var host = await currentMemberService.FindMemberAsync(groupId);
        if (host is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        var guest = await db.Members.FirstOrDefaultAsync(m => m.Id == guestMemberId && m.GroupId == groupId && m.IsGuest);
        if (guest is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.NotFound);
        if (guest.AddedByMemberId != host.Id) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.Forbidden);

        var attendee = ev.Attendees.FirstOrDefault(a => a.MemberId == guestMemberId);
        if (attendee is not null)
        {
            db.Attendees.Remove(attendee);
            var first = ev.Waitlist.OrderBy(w => w.JoinedAt).FirstOrDefault();
            if (first is not null)
            {
                db.WaitlistEntries.Remove(first);
                var promoted = new Attendee { EventId = id, MemberId = first.MemberId, JoinedAt = DateTime.UtcNow };
                db.Attendees.Add(promoted);
            }
        }
        else
        {
            var waitlisted = ev.Waitlist.FirstOrDefault(w => w.MemberId == guestMemberId);
            if (waitlisted is not null) db.WaitlistEntries.Remove(waitlisted);
        }

        db.Members.Remove(guest);
        await db.SaveChangesAsync();
        return ServiceResult<EventDetailResponse>.Success(eventMapper.MapDetail((await eventMapper.LoadEvent(groupId, id))!));
    }

    // ── Expenses ─────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<ExpenseResponse>> AddExpenseAsync(Guid groupId, Guid id, AddExpenseRequest request)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId);
        if (ev is null) return ServiceResult<ExpenseResponse>.Fail(ServiceErrorKind.NotFound);

        var paidBy = await db.Members.FirstOrDefaultAsync(m => m.Id == request.PaidByMemberId && m.GroupId == groupId);
        if (paidBy is null) return ServiceResult<ExpenseResponse>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            Description = request.Description,
            Amount = request.Amount,
            PaidByMemberId = request.PaidByMemberId,
            CreatedAt = DateTime.UtcNow,
            EventId = id,
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();

        var response = new ExpenseResponse(expense.Id, expense.Description, expense.Amount,
            new MemberResponse(paidBy.Id, paidBy.Name),
            expense.CreatedAt.ToString("o"));

        return ServiceResult<ExpenseResponse>.Success(response);
    }

    public async Task<ServiceResult<ExpenseResponse>> UpdateExpenseAsync(Guid groupId, Guid id, Guid expenseId, UpdateExpenseRequest request)
    {
        var expense = await db.Expenses
            .Include(e => e.PaidBy)
            .FirstOrDefaultAsync(e => e.Id == expenseId && e.EventId == id && e.Event.GroupId == groupId);
        if (expense is null) return ServiceResult<ExpenseResponse>.Fail(ServiceErrorKind.NotFound);

        if (request.Description is not null) expense.Description = request.Description;
        if (request.Amount is not null) expense.Amount = request.Amount.Value;
        if (request.PaidByMemberId is not null)
        {
            var paidBy = await db.Members.FirstOrDefaultAsync(m => m.Id == request.PaidByMemberId && m.GroupId == groupId);
            if (paidBy is null) return ServiceResult<ExpenseResponse>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");
            expense.PaidByMemberId = paidBy.Id;
            expense.PaidBy = paidBy;
        }

        await db.SaveChangesAsync();

        var response = new ExpenseResponse(expense.Id, expense.Description, expense.Amount,
            new MemberResponse(expense.PaidBy.Id, expense.PaidBy.Name),
            expense.CreatedAt.ToString("o"));

        return ServiceResult<ExpenseResponse>.Success(response);
    }

    public async Task<ServiceResult> DeleteExpenseAsync(Guid groupId, Guid id, Guid expenseId)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == expenseId && e.EventId == id && e.Event.GroupId == groupId);
        if (expense is null) return ServiceResult.Fail(ServiceErrorKind.NotFound);

        db.Expenses.Remove(expense);
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }
}
