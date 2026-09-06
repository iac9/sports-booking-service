using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Controllers;

[ApiController]
[Route("api/groups/{groupId:guid}/events")]
public class EventsController(AppDbContext db, EventMapper eventMapper, EventService eventService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<EventSummaryResponse>>> List(Guid groupId)
    {
        var events = await db.Events
            .Include(e => e.Location)
            .Where(e => e.GroupId == groupId)
            .Include(e => e.Attendees)
            .OrderBy(e => e.Date)
            .ToListAsync();

        return Ok(events.Select(e => new EventSummaryResponse(
            e.Id, e.Title, e.Date.ToString("yyyy-MM-dd"),
            e.Attendees.Count, e.MaxAttendees,
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
        var result = await eventService.CreateAsync(groupId, request);
        if (!result.IsSuccess) return this.ToActionResult(result.Error!);
        return CreatedAtAction(nameof(Get), new { groupId, id = result.Value!.Id }, result.Value);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<EventDetailResponse>> Update(Guid groupId, Guid id, UpdateEventRequest request)
    {
        var result = await eventService.UpdateAsync(groupId, id, request);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid groupId, Guid id)
    {
        var result = await eventService.DeleteAsync(groupId, id);
        return result.IsSuccess ? NoContent() : this.ToActionResult(result.Error!);
    }

    // ── RSVP ────────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/rsvp")]
    public async Task<ActionResult<EventDetailResponse>> Rsvp(Guid groupId, Guid id)
    {
        var result = await eventService.RsvpAsync(groupId, id);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    [HttpDelete("{id:guid}/rsvp/me")]
    public async Task<ActionResult<EventDetailResponse>> Leave(Guid groupId, Guid id)
    {
        var result = await eventService.LeaveAsync(groupId, id);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    // ── Guests ───────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/guests")]
    public async Task<ActionResult<EventDetailResponse>> AddGuest(Guid groupId, Guid id, AddGuestRequest request)
    {
        var result = await eventService.AddGuestAsync(groupId, id, request);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    [HttpDelete("{id:guid}/guests/{guestId:guid}")]
    public async Task<ActionResult<EventDetailResponse>> RemoveGuest(Guid groupId, Guid id, Guid guestId)
    {
        var result = await eventService.RemoveGuestAsync(groupId, id, guestId);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    // ── Expenses ─────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/expenses")]
    public async Task<ActionResult<ExpenseResponse>> AddExpense(Guid groupId, Guid id, AddExpenseRequest request)
    {
        var result = await eventService.AddExpenseAsync(groupId, id, request);
        if (!result.IsSuccess) return this.ToActionResult(result.Error!);
        return CreatedAtAction(nameof(Get), new { groupId, id }, result.Value);
    }
}
