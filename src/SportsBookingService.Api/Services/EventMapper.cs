using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Services;

public class EventMapper(AppDbContext db, CostService costService, CurrentMemberService currentMemberService)
{
    public async Task<Event?> LoadEvent(Guid groupId, Guid id) =>
        await IncludeFullGraph(db.Events)
            .FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId);

    public static IQueryable<Event> IncludeFullGraph(IQueryable<Event> query) =>
        query
            .Include(e => e.Location)
            .Include(e => e.CourtBookings)
            .Include(e => e.Attendees).ThenInclude(a => a.Member)
            .Include(e => e.Waitlist).ThenInclude(w => w.Member)
            .Include(e => e.Expenses).ThenInclude(ex => ex.PaidBy)
            .Include(e => e.LocationOptions).ThenInclude(lo => lo.Votes).ThenInclude(v => v.Member)
            .Include(e => e.CreatedBy);

    public static IQueryable<Group> IncludeGroupFullGraph(IQueryable<Group> query) =>
        query
            .Include(g => g.Members)
            .Include(g => g.Locations)
            .Include(g => g.Events).ThenInclude(e => e.Location)
            .Include(g => g.Events).ThenInclude(e => e.CourtBookings)
            .Include(g => g.Events).ThenInclude(e => e.Attendees).ThenInclude(a => a.Member)
            .Include(g => g.Events).ThenInclude(e => e.Waitlist).ThenInclude(w => w.Member)
            .Include(g => g.Events).ThenInclude(e => e.Expenses).ThenInclude(ex => ex.PaidBy)
            .Include(g => g.Events).ThenInclude(e => e.LocationOptions).ThenInclude(lo => lo.Votes).ThenInclude(v => v.Member)
            .Include(g => g.Events).ThenInclude(e => e.CreatedBy);

    public EventDetailResponse MapDetail(Event ev) => new(
        ev.Id,
        ev.Title,
        ev.Date.ToString("yyyy-MM-dd"),
        ev.CourtBookings.Select(cb => new CourtBookingResponse(cb.Id, cb.Court, cb.StartTime, cb.EndTime)).ToList(),
        ev.LocationId,
        ev.Location?.Name,
        ev.MaxAttendees,
        ev.Attendees.OrderBy(a => a.JoinedAt).Select(a => new MemberResponse(a.Member.Id, a.Member.Name)).ToList(),
        ev.Waitlist.OrderBy(w => w.JoinedAt).Select(w => new MemberResponse(w.Member.Id, w.Member.Name)).ToList(),
        ev.Expenses.OrderBy(e => e.CreatedAt).Select(e => new ExpenseResponse(
            e.Id, e.Description, e.Amount,
            new MemberResponse(e.PaidBy.Id, e.PaidBy.Name),
            e.CreatedAt.ToString("o"))).ToList(),
        new MemberResponse(ev.CreatedBy.Id, ev.CreatedBy.Name),
        StatusString(ev.Status),
        ev.Status == EventStatus.Draft
            ? ev.LocationOptions.Select(lo => new LocationOptionResponse(
                lo.Id, lo.LocationName,
                lo.Votes.Select(v => new MemberResponse(v.Member.Id, v.Member.Name)).ToList())).ToList()
            : null,
        costService.Calculate(ev.Attendees, ev.Expenses)
    );

    public Task<GroupResponse> MapGroupAsync(Group group)
    {
        var clerkUserId = currentMemberService.GetClerkUserId();
        var currentMember = group.Members.FirstOrDefault(m => m.ClerkUserId == clerkUserId);
        return Task.FromResult(new GroupResponse(
            group.Id,
            group.Name,
            group.Code,
            group.Members.Select(m => new MemberResponse(m.Id, m.Name)).ToList(),
            group.Events.OrderBy(e => e.Date).Select(MapDetail).ToList(),
            group.Locations.Select(l => new LocationResponse(l.Id, l.Name, l.Address)).ToList(),
            group.PrimaryLocationId,
            currentMember?.Id,
            group.AdminMemberId
        ));
    }

    public GroupResponse MapGroupSummary(Group group, string clerkUserId)
    {
        var currentMember = group.Members.FirstOrDefault(m => m.ClerkUserId == clerkUserId);
        return new(
            group.Id,
            group.Name,
            group.Code,
            group.Members.Select(m => new MemberResponse(m.Id, m.Name)).ToList(),
            [],
            group.Locations.Select(l => new LocationResponse(l.Id, l.Name, l.Address)).ToList(),
            group.PrimaryLocationId,
            currentMember?.Id,
            group.AdminMemberId
        );
    }

    public static string StatusString(EventStatus status) =>
        status == EventStatus.Draft ? "draft" : "confirmed";
}
