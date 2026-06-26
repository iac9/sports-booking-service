using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Controllers;

[ApiController]
[Route("api/groups/{groupId:guid}/locations")]
public class LocationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<LocationResponse>>> List(Guid groupId)
    {
        var locations = await db.Locations
            .Where(l => l.GroupId == groupId)
            .OrderBy(l => l.Name)
            .Select(l => new LocationResponse(l.Id, l.Name, l.Address))
            .ToListAsync();

        return Ok(locations);
    }

    [HttpPost]
    public async Task<ActionResult<LocationResponse>> Create(Guid groupId, CreateLocationRequest request)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == groupId)) return NotFound();

        var location = new Location
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Address = request.Address,
            GroupId = groupId,
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();

        var response = new LocationResponse(location.Id, location.Name, location.Address);
        return CreatedAtAction(nameof(List), new { groupId }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LocationResponse>> Update(Guid groupId, Guid id, UpdateLocationRequest request)
    {
        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == id && l.GroupId == groupId);
        if (location is null) return NotFound();

        location.Name = request.Name;
        location.Address = request.Address;
        await db.SaveChangesAsync();

        return Ok(new LocationResponse(location.Id, location.Name, location.Address));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid groupId, Guid id)
    {
        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == id && l.GroupId == groupId);
        if (location is null) return NotFound();

        db.Locations.Remove(location);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
