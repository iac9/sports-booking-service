using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Services;

public class CurrentMemberService(IHttpContextAccessor httpContextAccessor, AppDbContext db, ILogger<CurrentMemberService> logger)
{
    public string GetClerkUserId()
    {
        var clerkUserId = httpContextAccessor.HttpContext?.User.FindFirstValue("sub");
        return clerkUserId ?? throw new InvalidOperationException("Request is missing an authenticated Clerk user id.");
    }

    public async Task<Member?> FindMemberAsync(Guid groupId)
    {
        var clerkUserId = GetClerkUserId();
        var member = await db.Members.FirstOrDefaultAsync(m => m.GroupId == groupId && m.ClerkUserId == clerkUserId);
        if (member is null)
        {
            logger.LogWarning("Authorization failure: Clerk user {ClerkUserId} is not a member of group {GroupId}", clerkUserId, groupId);
        }
        return member;
    }
}
