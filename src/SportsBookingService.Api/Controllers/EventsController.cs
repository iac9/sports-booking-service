using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Controllers;

[ApiController]
[Route("api/groups/{groupId:guid}/events")]
public class EventsController(AppDbContext db, EventMapper eventMapper) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<EventSummaryResponse>>> List(Guid groupId, [FromQuery] string? status)
    {
        var query = db.Events
            .Include(e => e.Location)
            .Where(e => e.GroupId == groupId);

        if (status is not null)
        {
            var parsed = status.ToLowerInvariant() == "draft" ? EventStatus.Draft : EventStatus.Confirmed;
            query = query.Where(e => e.Status == parsed);
        }

        var events = await query
            .Include(e => e.Attendees)
            .OrderBy(e => e.Date)
            .ToListAsync();

        return Ok(events.Select(e => new EventSummaryResponse(
            e.Id, e.Title, e.Date.ToString("yyyy-MM-dd"),
            EventMapper.StatusString(e.Status), e.Attendees.Count, e.MaxAttendees,
            e.Location?.Name)).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EventDetailResponse>> Get(Guid groupId, Guid id)
    {
        var ev = await eventMapper.LoadEvent(groupId, id);
        return ev is null ? NotFound() : Ok(eventMapper.MapDetail(ev));
    }

    [HttpPost]
    public async Task<ActionResult<EventDetailResponse>> Create(Guid groupId, CreateEventRequest request)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == groupId)) return NotFound();
        if (!await db.Members.AnyAsync(m => m.Id == request.CreatedByMemberId && m.GroupId == groupId))
            return BadRequest("Member not found in group.");

        var status = request.Status.ToLowerInvariant() == "draft" ? EventStatus.Draft : EventStatus.Confirmed;

        var ev = new Event
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            Date = DateOnly.Parse(request.Date),
            LocationId = request.LocationId,
            MaxAttendees = request.MaxAttendees,
            GroupId = groupId,
            CreatedByMemberId = request.CreatedByMemberId,
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
                ev.Attendees.Add(new Attendee { EventId = ev.Id, MemberId = request.CreatedByMemberId, JoinedAt = DateTime.UtcNow });
                break;
        }

        db.Events.Add(ev);
        await db.SaveChangesAsync();

        var created = await eventMapper.LoadEvent(groupId, ev.Id);
        return CreatedAtAction(nameof(Get), new { groupId, id = ev.Id }, eventMapper.MapDetail(created!));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<EventDetailResponse>> Update(Guid groupId, Guid id, UpdateEventRequest request)
    {
        var ev = await db.Events
            .Include(e => e.CourtBookings)
            .Include(e => e.LocationOptions).ThenInclude(lo => lo.Votes)
            .FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId);

        if (ev is null) return NotFound();

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
        return Ok(eventMapper.MapDetail(updated!));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid groupId, Guid id)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId);
        if (ev is null) return NotFound();
        db.Events.Remove(ev);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── RSVP ────────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/rsvp")]
    public async Task<ActionResult<EventDetailResponse>> Rsvp(Guid groupId, Guid id, RsvpRequest request)
    {
        var ev = await db.Events
            .Include(e => e.Attendees)
            .Include(e => e.Waitlist)
            .FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId);

        if (ev is null) return NotFound();
        if (!await db.Members.AnyAsync(m => m.Id == request.MemberId && m.GroupId == groupId))
            return BadRequest("Member not found in group.");
        if (ev.Attendees.Any(a => a.MemberId == request.MemberId) || ev.Waitlist.Any(w => w.MemberId == request.MemberId))
            return Conflict("Already joined or waitlisted.");

        if (ev.Attendees.Count < ev.MaxAttendees)
            ev.Attendees.Add(new Attendee { EventId = id, MemberId = request.MemberId, JoinedAt = DateTime.UtcNow });
        else
            ev.Waitlist.Add(new WaitlistEntry { EventId = id, MemberId = request.MemberId, JoinedAt = DateTime.UtcNow });

        await db.SaveChangesAsync();
        return Ok(eventMapper.MapDetail((await eventMapper.LoadEvent(groupId, id))!));
    }

    [HttpDelete("{id:guid}/rsvp/{memberId:guid}")]
    public async Task<ActionResult<EventDetailResponse>> Leave(Guid groupId, Guid id, Guid memberId)
    {
        var ev = await db.Events
            .Include(e => e.Attendees)
            .Include(e => e.Waitlist)
            .FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId);

        if (ev is null) return NotFound();

        var attendee = ev.Attendees.FirstOrDefault(a => a.MemberId == memberId);
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
            var waitlisted = ev.Waitlist.FirstOrDefault(w => w.MemberId == memberId);
            if (waitlisted is not null) ev.Waitlist.Remove(waitlisted);
        }

        await db.SaveChangesAsync();
        return Ok(eventMapper.MapDetail((await eventMapper.LoadEvent(groupId, id))!));
    }

    // ── Expenses ─────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/expenses")]
    public async Task<ActionResult<ExpenseResponse>> AddExpense(Guid groupId, Guid id, AddExpenseRequest request)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId);
        if (ev is null) return NotFound();

        var paidBy = await db.Members.FirstOrDefaultAsync(m => m.Id == request.PaidByMemberId && m.GroupId == groupId);
        if (paidBy is null) return BadRequest("Member not found in group.");

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

        return CreatedAtAction(nameof(Get), new { groupId, id },
            new ExpenseResponse(expense.Id, expense.Description, expense.Amount,
                new MemberResponse(paidBy.Id, paidBy.Name),
                expense.CreatedAt.ToString("o")));
    }

    // ── Confirm draft ─────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<EventDetailResponse>> Confirm(Guid groupId, Guid id, RsvpRequest request)
    {
        var ev = await db.Events
            .Include(e => e.LocationOptions).ThenInclude(lo => lo.Votes)
            .Include(e => e.Attendees)
            .FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId && e.Status == EventStatus.Draft);

        if (ev is null) return NotFound();

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
            .Append(request.MemberId)
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
        return Ok(eventMapper.MapDetail((await eventMapper.LoadEvent(groupId, id))!));
    }

    // ── Location option votes ─────────────────────────────────────────────────────

    [HttpPost("{id:guid}/location-options/{optionId:guid}/votes")]
    public async Task<ActionResult<EventDetailResponse>> Vote(Guid groupId, Guid id, Guid optionId, VoteRequest request)
    {
        var option = await db.LocationOptions
            .Include(lo => lo.Votes)
            .FirstOrDefaultAsync(lo => lo.Id == optionId && lo.EventId == id);

        if (option is null) return NotFound();
        if (option.Votes.Any(v => v.MemberId == request.MemberId)) return Conflict("Already voted.");

        option.Votes.Add(new LocationVote { LocationOptionId = optionId, MemberId = request.MemberId });
        await db.SaveChangesAsync();
        return Ok(eventMapper.MapDetail((await eventMapper.LoadEvent(groupId, id))!));
    }

    [HttpDelete("{id:guid}/location-options/{optionId:guid}/votes/{memberId:guid}")]
    public async Task<ActionResult<EventDetailResponse>> Unvote(Guid groupId, Guid id, Guid optionId, Guid memberId)
    {
        var vote = await db.LocationVotes
            .FirstOrDefaultAsync(v => v.LocationOptionId == optionId && v.MemberId == memberId);

        if (vote is null) return NotFound();
        db.LocationVotes.Remove(vote);
        await db.SaveChangesAsync();
        return Ok(eventMapper.MapDetail((await eventMapper.LoadEvent(groupId, id))!));
    }
}
