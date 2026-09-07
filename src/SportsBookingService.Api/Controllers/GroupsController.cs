using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Controllers;

[ApiController]
[Route("api/groups")]
public class GroupsController(AppDbContext db, EventMapper eventMapper, GroupService groupService, CurrentMemberService currentMemberService) : ControllerBase
{
    [HttpGet("code/{code}")]
    [EnableRateLimiting("GroupLookup")]
    public async Task<ActionResult<GroupResponse>> GetByCode(string code)
    {
        var group = await EventMapper.IncludeGroupFullGraph(db.Groups)
            .FirstOrDefaultAsync(g => g.Code == code.ToUpperInvariant());

        if (group is null) return NotFound();
        if (await currentMemberService.FindMemberAsync(group.Id) is null) return NotFound();

        return Ok(await eventMapper.MapGroupAsync(group));
    }

    [HttpPost]
    public async Task<ActionResult<GroupResponse>> Create(CreateGroupRequest request)
    {
        var response = await groupService.CreateAsync(request);
        return CreatedAtAction(nameof(GetByCode), new { code = response.Code }, response);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GroupResponse>> Rename(Guid id, RenameGroupRequest request)
    {
        var result = await groupService.RenameAsync(id, request);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    [HttpPut("{id:guid}/admin")]
    public async Task<ActionResult<GroupResponse>> TransferAdmin(Guid id, TransferAdminRequest request)
    {
        var result = await groupService.TransferAdminAsync(id, request);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await groupService.DeleteAsync(id);
        return result.IsSuccess ? NoContent() : this.ToActionResult(result.Error!);
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<List<MemberResponse>>> GetMembers(Guid id)
    {
        if (await currentMemberService.FindMemberAsync(id) is null) return NotFound();

        var members = await db.Members
            .Where(m => m.GroupId == id && !m.IsGuest)
            .OrderBy(m => m.Name)
            .Select(m => new MemberResponse(m.Id, m.Name))
            .ToListAsync();

        return Ok(members);
    }

    [HttpDelete("{id:guid}/members/me")]
    public async Task<IActionResult> LeaveGroup(Guid id)
    {
        var result = await groupService.LeaveAsync(id);
        return result.IsSuccess ? NoContent() : this.ToActionResult(result.Error!);
    }

    [HttpPost("join")]
    [EnableRateLimiting("GroupLookup")]
    public async Task<ActionResult<GroupResponse?>> JoinByCode(JoinGroupByCodeRequest request) =>
        Ok(await groupService.JoinByCodeAsync(request));

    [HttpPut("{id:guid}/primary-location")]
    public async Task<IActionResult> SetPrimaryLocation(Guid id, SetPrimaryLocationRequest request)
    {
        var result = await groupService.SetPrimaryLocationAsync(id, request);
        return result.IsSuccess ? NoContent() : this.ToActionResult(result.Error!);
    }
}
