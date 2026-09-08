using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Services;

public class GroupService(AppDbContext db, EventMapper eventMapper, CurrentMemberService currentMemberService, ILogger<GroupService> logger)
{
    public async Task<GroupResponse> CreateAsync(CreateGroupRequest request)
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

        return await eventMapper.MapGroupAsync(group);
    }

    public async Task<ServiceResult<GroupResponse>> RenameAsync(Guid groupId, RenameGroupRequest request)
    {
        var group = await EventMapper.IncludeGroupFullGraph(db.Groups).FirstOrDefaultAsync(g => g.Id == groupId);
        if (group is null) return ServiceResult<GroupResponse>.Fail(ServiceErrorKind.NotFound);

        var member = await currentMemberService.FindMemberAsync(groupId);
        if (member is null || group.AdminMemberId != member.Id) return ServiceResult<GroupResponse>.Fail(ServiceErrorKind.Forbidden);

        group.Name = request.Name;
        await db.SaveChangesAsync();
        logger.LogInformation("Group {GroupId} renamed to {Name} by member {MemberId}", groupId, request.Name, member.Id);
        return ServiceResult<GroupResponse>.Success(await eventMapper.MapGroupAsync(group));
    }

    public async Task<ServiceResult<GroupResponse>> TransferAdminAsync(Guid groupId, TransferAdminRequest request)
    {
        var group = await EventMapper.IncludeGroupFullGraph(db.Groups).FirstOrDefaultAsync(g => g.Id == groupId);
        if (group is null) return ServiceResult<GroupResponse>.Fail(ServiceErrorKind.NotFound);

        var member = await currentMemberService.FindMemberAsync(groupId);
        if (member is null || group.AdminMemberId != member.Id) return ServiceResult<GroupResponse>.Fail(ServiceErrorKind.Forbidden);

        if (!group.Members.Any(m => m.Id == request.MemberId))
            return ServiceResult<GroupResponse>.Fail(ServiceErrorKind.BadRequest, "Target member is not in this group.");

        group.AdminMemberId = request.MemberId;
        await db.SaveChangesAsync();
        logger.LogInformation("Group {GroupId} admin transferred from {FromMemberId} to {ToMemberId}", groupId, member.Id, request.MemberId);
        return ServiceResult<GroupResponse>.Success(await eventMapper.MapGroupAsync(group));
    }

    public async Task<ServiceResult> DeleteAsync(Guid groupId)
    {
        var group = await db.Groups.FindAsync(groupId);
        if (group is null) return ServiceResult.Fail(ServiceErrorKind.NotFound);

        var member = await currentMemberService.FindMemberAsync(groupId);
        if (member is null || group.AdminMemberId != member.Id) return ServiceResult.Fail(ServiceErrorKind.Forbidden);

        // Expenses reference Members with ON DELETE RESTRICT, and Postgres resolves the
        // Group's cascades (Members, Events) before the Events -> Expenses cascade runs.
        // Deleting Events explicitly first clears Expenses before Members are cascaded away.
        await db.Events.Where(e => e.GroupId == groupId).ExecuteDeleteAsync();

        // Loading the admin Member above makes EF track it alongside the Group, and it fixes up
        // group.Members to include it. Group.AdminMemberId -> Member and Member.GroupId -> Group
        // then form a cycle EF can't order for deletion, so break it before removing the group.
        group.AdminMemberId = null;
        await db.SaveChangesAsync();

        db.Groups.Remove(group);
        await db.SaveChangesAsync();
        logger.LogInformation("Group {GroupId} deleted by member {MemberId}", groupId, member.Id);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> LeaveAsync(Guid groupId)
    {
        var member = await currentMemberService.FindMemberAsync(groupId);
        if (member is null) return ServiceResult.Fail(ServiceErrorKind.NotFound);

        var group = await db.Groups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == groupId);
        if (group is null) return ServiceResult.Fail(ServiceErrorKind.NotFound);

        if (group.AdminMemberId == member.Id)
        {
            if (group.Members.Count > 1)
                return ServiceResult.Fail(ServiceErrorKind.Conflict, "Group admin must transfer the admin role to another member before leaving.");

            await db.Events.Where(e => e.GroupId == groupId).ExecuteDeleteAsync();

            group.AdminMemberId = null;
            await db.SaveChangesAsync();

            db.Groups.Remove(group);
            await db.SaveChangesAsync();
            return ServiceResult.Success();
        }

        await db.Members.Where(m => m.Id == member.Id).ExecuteDeleteAsync();
        return ServiceResult.Success();
    }

    public async Task<GroupResponse?> JoinByCodeAsync(JoinGroupByCodeRequest request)
    {
        var groupId = await db.Groups
            .Where(g => g.Code == request.Code.Trim().ToUpperInvariant())
            .Select(g => (Guid?)g.Id)
            .FirstOrDefaultAsync();

        if (groupId is null)
        {
            logger.LogWarning("Failed join attempt: no group found for code {Code}", request.Code);
            return null;
        }

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
        return await eventMapper.MapGroupAsync(group);
    }

    public async Task<ServiceResult> SetPrimaryLocationAsync(Guid groupId, SetPrimaryLocationRequest request)
    {
        var group = await db.Groups.Include(g => g.Locations).FirstOrDefaultAsync(g => g.Id == groupId);
        if (group is null) return ServiceResult.Fail(ServiceErrorKind.NotFound);

        var member = await currentMemberService.FindMemberAsync(groupId);
        if (member is null) return ServiceResult.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        if (request.LocationId is not null && !group.Locations.Any(l => l.Id == request.LocationId))
            return ServiceResult.Fail(ServiceErrorKind.BadRequest, "Location does not belong to this group.");

        group.PrimaryLocationId = request.LocationId;
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    private static string GenerateCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Range(0, 6)
            .Select(_ => chars[Random.Shared.Next(chars.Length)])
            .ToArray());
    }
}
