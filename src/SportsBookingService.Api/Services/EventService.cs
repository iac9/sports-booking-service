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

        var status = request.Status.ToLowerInvariant() == "draft" ? EventStatus.Draft : EventStatus.Confirmed;

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
            Status = status,
        };

        foreach (var cb in request.CourtBookings)
            ev.CourtBookings.Add(new CourtBooking { Id = Guid.NewGuid(), Court = cb.Court, StartTime = cb.StartTime, EndTime = cb.EndTime, EventId = ev.Id });

        switch (status)
        {
            case EventStatus.Draft when request.LocationOptions is not null:
            {
                foreach (var lo in request.LocationOptions)
                    ev.LocationOptions.Add(new LocationOption { Id = Guid.NewGuid(), LocationName = lo.LocationName, EventId = ev.Id });
                break;
            }
            case EventStatus.Confirmed:
                ev.Attendees.Add(new Attendee { EventId = ev.Id, MemberId = member.Id, Member = member, JoinedAt = DateTime.UtcNow });
                break;
        }

        db.Events.Add(ev);
        await db.SaveChangesAsync();

        return ServiceResult<EventDetailResponse>.Success(eventMapper.MapDetail(ev));
    }

    public async Task<ServiceResult<EventDetailResponse>> UpdateAsync(Guid groupId, Guid id, UpdateEventRequest request)
    {
        var ev = await db.Events
            .Include(e => e.CourtBookings)
            .Include(e => e.LocationOptions).ThenInclude(lo => lo.Votes)
            .FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId);

        if (ev is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.NotFound);

        if (request.Title is not null) ev.Title = request.Title;
        if (request.Date is not null) ev.Date = DateOnly.Parse(request.Date);
        if (request.LocationId is not null) ev.LocationId = request.LocationId;
        if (request.MaxAttendees is not null) ev.MaxAttendees = request.MaxAttendees.Value;

        if (request.CourtBookings is not null)
        {
            db.CourtBookings.RemoveRange(ev.CourtBookings);
            foreach (var cb in request.CourtBookings)
                ev.CourtBookings.Add(new CourtBooking { Id = Guid.NewGuid(), Court = cb.Court, StartTime = cb.StartTime, EndTime = cb.EndTime, EventId = ev.Id });
        }

        if (request.LocationOptions is not null)
        {
            var existingVotes = ev.LocationOptions.ToDictionary(
                lo => lo.LocationName,
                lo => lo.Votes.Select(v => v.MemberId).ToList());

            db.LocationOptions.RemoveRange(ev.LocationOptions);

            foreach (var lo in request.LocationOptions)
            {
                var newOption = new LocationOption { Id = lo.Id ?? Guid.NewGuid(), LocationName = lo.LocationName, EventId = ev.Id };
                if (existingVotes.TryGetValue(lo.LocationName, out var voters))
                    foreach (var voterId in voters)
                        newOption.Votes.Add(new LocationVote { LocationOptionId = newOption.Id, MemberId = voterId });
                ev.LocationOptions.Add(newOption);
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

        if (ev.Attendees.Count < ev.MaxAttendees)
            ev.Attendees.Add(new Attendee { EventId = id, MemberId = member.Id, JoinedAt = DateTime.UtcNow });
        else
            ev.Waitlist.Add(new WaitlistEntry { EventId = id, MemberId = member.Id, JoinedAt = DateTime.UtcNow });

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
            ev.Attendees.Remove(attendee);
            var first = ev.Waitlist.OrderBy(w => w.JoinedAt).FirstOrDefault();
            if (first is not null)
            {
                ev.Waitlist.Remove(first);
                ev.Attendees.Add(new Attendee { EventId = id, MemberId = first.MemberId, JoinedAt = DateTime.UtcNow });
            }
        }
        else
        {
            var waitlisted = ev.Waitlist.FirstOrDefault(w => w.MemberId == member.Id);
            if (waitlisted is not null) ev.Waitlist.Remove(waitlisted);
        }

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

    // ── Confirm draft ─────────────────────────────────────────────────────────────

    public async Task<ServiceResult<EventDetailResponse>> ConfirmAsync(Guid groupId, Guid id)
    {
        var ev = await db.Events
            .Include(e => e.LocationOptions).ThenInclude(lo => lo.Votes)
            .Include(e => e.Attendees)
            .FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId && e.Status == EventStatus.Draft);

        if (ev is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.NotFound);

        var member = await currentMemberService.FindMemberAsync(groupId);
        if (member is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        var winner = ev.LocationOptions
            .OrderByDescending(lo => lo.Votes.Count)
            .FirstOrDefault();

        ev.Status = EventStatus.Confirmed;
        ev.LocationId = ev.LocationOptions
            .Where(lo => lo.LocationName == winner?.LocationName)
            .Select(lo => (Guid?)lo.Id)
            .FirstOrDefault();

        var voterIds = ev.LocationOptions.SelectMany(lo => lo.Votes.Select(v => v.MemberId)).Distinct();
        var allMemberIds = ev.Attendees.Select(a => a.MemberId)
            .Union(voterIds)
            .Append(member.Id)
            .Distinct();

        db.Attendees.RemoveRange(ev.Attendees);
        foreach (var mid in allMemberIds)
            db.Attendees.Add(new Attendee { EventId = id, MemberId = mid, JoinedAt = DateTime.UtcNow });

        db.LocationOptions.RemoveRange(ev.LocationOptions);

        if (winner is not null)
        {
            var loc = await db.Locations.FirstOrDefaultAsync(l => l.Name == winner.LocationName && l.GroupId == groupId);
            ev.LocationId = loc?.Id;
        }

        await db.SaveChangesAsync();
        return ServiceResult<EventDetailResponse>.Success(eventMapper.MapDetail((await eventMapper.LoadEvent(groupId, id))!));
    }

    // ── Location option votes ─────────────────────────────────────────────────────

    public async Task<ServiceResult<EventDetailResponse>> VoteAsync(Guid groupId, Guid id, Guid optionId)
    {
        var option = await db.LocationOptions
            .Include(lo => lo.Votes)
            .FirstOrDefaultAsync(lo => lo.Id == optionId && lo.EventId == id);

        if (option is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.NotFound);

        var member = await currentMemberService.FindMemberAsync(groupId);
        if (member is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        if (option.Votes.Any(v => v.MemberId == member.Id))
            return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.Conflict, "Already voted.");

        option.Votes.Add(new LocationVote { LocationOptionId = optionId, MemberId = member.Id });
        await db.SaveChangesAsync();
        return ServiceResult<EventDetailResponse>.Success(eventMapper.MapDetail((await eventMapper.LoadEvent(groupId, id))!));
    }

    public async Task<ServiceResult<EventDetailResponse>> UnvoteAsync(Guid groupId, Guid id, Guid optionId)
    {
        var member = await currentMemberService.FindMemberAsync(groupId);
        if (member is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        var vote = await db.LocationVotes
            .FirstOrDefaultAsync(v => v.LocationOptionId == optionId && v.MemberId == member.Id);

        if (vote is null) return ServiceResult<EventDetailResponse>.Fail(ServiceErrorKind.NotFound);

        db.LocationVotes.Remove(vote);
        await db.SaveChangesAsync();
        return ServiceResult<EventDetailResponse>.Success(eventMapper.MapDetail((await eventMapper.LoadEvent(groupId, id))!));
    }
}
