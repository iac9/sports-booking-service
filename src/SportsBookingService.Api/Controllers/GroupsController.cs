using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Controllers;

[ApiController]
[Route("api/groups")]
public class GroupsController(AppDbContext db) : ControllerBase
{
    [HttpGet("code/{code}")]
    public async Task<ActionResult<GroupResponse>> GetByCode(string code)
    {
        var group = await db.Groups
            .Include(g => g.Members)
            .Include(g => g.Locations)
            .FirstOrDefaultAsync(g => g.Code == code.ToUpperInvariant());

        return group is null ? NotFound() : Ok(MapGroup(group));
    }

    [HttpPost]
    public async Task<ActionResult<CreateGroupResponse>> Create(CreateGroupRequest request)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Code = GenerateCode(),
        };

        var creator = new Member { Id = Guid.NewGuid(), Name = request.CreatorName, GroupId = group.Id };
        group.Members.Add(creator);

        db.Groups.Add(group);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetByCode), new { code = group.Code },
            new CreateGroupResponse(group.Id, group.Name, group.Code, creator.Id));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GroupResponse>> Rename(Guid id, RenameGroupRequest request)
    {
        var group = await db.Groups
            .Include(g => g.Members)
            .Include(g => g.Locations)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group is null) return NotFound();

        group.Name = request.Name;
        await db.SaveChangesAsync();
        return Ok(MapGroup(group));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var group = await db.Groups.FindAsync(id);
        if (group is null) return NotFound();

        db.Groups.Remove(group);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<List<MemberResponse>>> GetMembers(Guid id)
    {
        var members = await db.Members
            .Where(m => m.GroupId == id)
            .OrderBy(m => m.Name)
            .Select(m => new MemberResponse(m.Id, m.Name))
            .ToListAsync();

        return Ok(members);
    }

    [HttpPost("{id:guid}/members")]
    public async Task<ActionResult<MemberResponse>> JoinGroup(Guid id, JoinGroupRequest request)
    {
        var groupExists = await db.Groups.AnyAsync(g => g.Id == id);
        if (!groupExists) return NotFound();

        var member = new Member { Id = Guid.NewGuid(), Name = request.Name, GroupId = id };
        db.Members.Add(member);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetMembers), new { id },
            new MemberResponse(member.Id, member.Name));
    }

    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> LeaveGroup(Guid id, Guid memberId)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == memberId && m.GroupId == id);
        if (member is null) return NotFound();

        db.Members.Remove(member);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static GroupResponse MapGroup(Group group) => new(
        group.Id,
        group.Name,
        group.Code,
        group.Members.Select(m => new MemberResponse(m.Id, m.Name)).ToList(),
        group.Locations.Select(l => new LocationResponse(l.Id, l.Name, l.Address)).ToList()
    );

    private static string GenerateCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Range(0, 6)
            .Select(_ => chars[Random.Shared.Next(chars.Length)])
            .ToArray());
    }
}
