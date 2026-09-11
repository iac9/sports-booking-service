using Microsoft.AspNetCore.Mvc;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Controllers;

[ApiController]
[Route("api/groups/{groupId:guid}/pending-bookings")]
public class PendingBookingsController(PendingBookingService pendingBookingService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PendingBookingResponse>>> List(Guid groupId)
    {
        var result = await pendingBookingService.ListAsync(groupId);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<EventDetailResponse>> Confirm(Guid groupId, Guid id, ConfirmPendingBookingRequest request)
    {
        var result = await pendingBookingService.ConfirmAsync(groupId, id, request);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    [HttpPost("{id:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid groupId, Guid id)
    {
        var result = await pendingBookingService.DismissAsync(groupId, id);
        return result.IsSuccess ? NoContent() : this.ToActionResult(result.Error!);
    }
}
