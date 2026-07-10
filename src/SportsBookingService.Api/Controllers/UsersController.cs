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
        var groups = await EventMapper.IncludeGroupFullGraph(db.Groups)
            .Where(g => g.Members.Any(m => m.ClerkUserId == clerkUserId))
            .ToListAsync();

        var results = new List<GroupResponse>();
        foreach (var group in groups)
            results.Add(await eventMapper.MapGroupAsync(group));

        return Ok(results);
    }
}
