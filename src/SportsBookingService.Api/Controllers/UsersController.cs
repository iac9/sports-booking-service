using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController(AppDbContext db, EventMapper eventMapper, CurrentMemberService currentMemberService) : ControllerBase
{
    [HttpGet("me/groups")]
    public async Task<ActionResult<List<GroupResponse>>> GetGroups()
    {
        var clerkUserId = currentMemberService.GetClerkUserId();
        var groups = await db.Groups
            .Include(g => g.Members)
            .Include(g => g.Locations)
            .Where(g => g.Members.Any(m => m.ClerkUserId == clerkUserId))
            .ToListAsync();

        return Ok(groups.Select(g => eventMapper.MapGroupSummary(g, clerkUserId)).ToList());
    }
}
