using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController(AppDbContext db, CurrentMemberService currentMemberService) : ControllerBase
{
    [HttpGet("me/groups")]
    public async Task<ActionResult<List<GroupSummaryResponse>>> GetGroups()
    {
        var clerkUserId = currentMemberService.GetClerkUserId();

        var groups = await db.Groups
            .Where(g => g.Members.Any(m => m.ClerkUserId == clerkUserId))
            .Select(g => new GroupSummaryResponse(g.Id, g.Name, g.Code, g.Members.Count))
            .ToListAsync();

        return Ok(groups);
    }
}
