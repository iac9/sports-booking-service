using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Services;

public class CurrentMemberService(IHttpContextAccessor httpContextAccessor, AppDbContext db)
{
    public string GetClerkUserId()
    {
        var clerkUserId = httpContextAccessor.HttpContext?.User.FindFirstValue("sub");
        return clerkUserId ?? throw new InvalidOperationException("Request is missing an authenticated Clerk user id.");
    }

    public Task<Member?> FindMemberAsync(Guid groupId)
    {
        var clerkUserId = GetClerkUserId();
        return db.Members.FirstOrDefaultAsync(m => m.GroupId == groupId && m.ClerkUserId == clerkUserId);
    }
}
