using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController(AppDbContext db, EventMapper eventMapper) : ControllerBase
{
    [HttpGet("{userId:guid}/groups")]
    public async Task<ActionResult<List<GroupResponse>>> GetGroups(Guid userId)
    {
        var groups = await EventMapper.IncludeGroupFullGraph(db.Groups)
            .Where(g => g.Members.Any(m => m.Id == userId))
            .ToListAsync();

        return Ok(groups.Select(eventMapper.MapGroup).ToList());
    }
}
