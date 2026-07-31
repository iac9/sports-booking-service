using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Controllers;

[ApiController]
[Route("api/groups")]
public class GroupsController(AppDbContext db, EventMapper eventMapper, CurrentMemberService currentMemberService) : ControllerBase
{
    [HttpGet("code/{code}")]
    public async Task<ActionResult<GroupResponse>> GetByCode(string code)
    {
        var group = await EventMapper.IncludeGroupFullGraph(db.Groups)
            .FirstOrDefaultAsync(g => g.Code == code.ToUpperInvariant());

        return group is null ? NotFound() : Ok(await eventMapper.MapGroupAsync(group));
    }

    [HttpPost]
    public async Task<ActionResult<GroupResponse>> Create(CreateGroupRequest request)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Code = GenerateCode(),
        };

        var creator = new Member { Id = Guid.NewGuid(), Name = request.CreatorName, GroupId = group.Id, ClerkUserId = currentMemberService.GetClerkUserId() };
        group.Members.Add(creator);
        group.AdminMemberId = creator.Id;

        db.Groups.Add(group);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetByCode), new { code = group.Code }, await eventMapper.MapGroupAsync(group));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GroupResponse>> Rename(Guid id, RenameGroupRequest request)
    {
        var group = await EventMapper.IncludeGroupFullGraph(db.Groups)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group is null) return NotFound();

        var member = await currentMemberService.FindMemberAsync(id);
        if (member is null || group.AdminMemberId != member.Id) return Forbid();

        group.Name = request.Name;
        await db.SaveChangesAsync();
        return Ok(await eventMapper.MapGroupAsync(group));
    }

    [HttpPut("{id:guid}/admin")]
    public async Task<ActionResult<GroupResponse>> TransferAdmin(Guid id, TransferAdminRequest request)
    {
        var group = await EventMapper.IncludeGroupFullGraph(db.Groups).FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return NotFound();

        var member = await currentMemberService.FindMemberAsync(id);
        if (member is null || group.AdminMemberId != member.Id) return Forbid();

        if (!group.Members.Any(m => m.Id == request.MemberId))
            return BadRequest("Target member is not in this group.");

        group.AdminMemberId = request.MemberId;
        await db.SaveChangesAsync();
        return Ok(await eventMapper.MapGroupAsync(group));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var group = await db.Groups.FindAsync(id);
        if (group is null) return NotFound();

        var member = await currentMemberService.FindMemberAsync(id);
        if (member is null || group.AdminMemberId != member.Id) return Forbid();

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

        var member = new Member { Id = Guid.NewGuid(), Name = request.Name, GroupId = id, ClerkUserId = currentMemberService.GetClerkUserId() };
        db.Members.Add(member);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetMembers), new { id },
            new MemberResponse(member.Id, member.Name));
    }

    [HttpDelete("{id:guid}/members/me")]
    public async Task<IActionResult> LeaveGroup(Guid id)
    {
        var member = await currentMemberService.FindMemberAsync(id);
        if (member is null) return NotFound();

        var group = await db.Groups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return NotFound();

        if (group.AdminMemberId == member.Id)
        {
            if (group.Members.Count > 1)
                return Conflict("Group admin must transfer the admin role to another member before leaving.");

            db.Groups.Remove(group);
            await db.SaveChangesAsync();
            return NoContent();
        }

        await db.Members.Where(m => m.Id == member.Id).ExecuteDeleteAsync();
        return NoContent();
    }

    [HttpPost("join")]
    public async Task<ActionResult<GroupResponse?>> JoinByCode(JoinGroupByCodeRequest request)
    {
        var groupId = await db.Groups
            .Where(g => g.Code == request.Code.Trim().ToUpperInvariant())
            .Select(g => (Guid?)g.Id)
            .FirstOrDefaultAsync();

        if (groupId is null) return Ok((GroupResponse?)null);

        var clerkUserId = currentMemberService.GetClerkUserId();
        var alreadyMember = await db.Members.AnyAsync(m => m.GroupId == groupId && m.ClerkUserId == clerkUserId);
        if (!alreadyMember)
        {
            db.Members.Add(new Member { Id = Guid.NewGuid(), Name = request.MemberName, GroupId = groupId.Value, ClerkUserId = clerkUserId });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // A concurrent request already added this member (unique index on GroupId+ClerkUserId) — treat as already joined.
            }
        }

        var group = await EventMapper.IncludeGroupFullGraph(db.Groups).FirstAsync(g => g.Id == groupId);
        return Ok(await eventMapper.MapGroupAsync(group));
    }

    [HttpPut("{id:guid}/primary-location")]
    public async Task<IActionResult> SetPrimaryLocation(Guid id, SetPrimaryLocationRequest request)
    {
        var group = await db.Groups.Include(g => g.Locations).FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return NotFound();

        if (request.LocationId is not null && !group.Locations.Any(l => l.Id == request.LocationId))
            return BadRequest("Location does not belong to this group.");

        group.PrimaryLocationId = request.LocationId;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static string GenerateCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Range(0, 6)
            .Select(_ => chars[Random.Shared.Next(chars.Length)])
            .ToArray());
    }
}
