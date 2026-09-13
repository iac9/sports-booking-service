using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Tests;

/// <summary>
/// Auto-accept changes how inbound email is handled for the whole group, so it's gated the same
/// way as renaming or deleting: admin only.
/// </summary>
public class BookingAutomationSettingsTests : IDisposable
{
    private const string AdminClerkId = "clerk-alice";
    private const string MemberClerkId = "clerk-bob";
    private const string OutsiderClerkId = "clerk-mallory";

    private readonly SqliteConnection connection;
    private readonly Guid groupId = Guid.NewGuid();
    private readonly Guid adminId = Guid.NewGuid();

    public BookingAutomationSettingsTests()
    {
        connection = TestServices.OpenSharedConnection();

        using var db = TestServices.NewContext(connection);
        db.Database.EnsureCreated();
        db.Groups.Add(new Group { Id = groupId, Name = "Badminton", Code = "BAD001" });
        db.Members.Add(new Member { Id = adminId, Name = "Alice", GroupId = groupId, ClerkUserId = AdminClerkId });
        db.Members.Add(new Member { Id = Guid.NewGuid(), Name = "Bob", GroupId = groupId, ClerkUserId = MemberClerkId });

        var otherGroupId = Guid.NewGuid();
        db.Groups.Add(new Group { Id = otherGroupId, Name = "Other", Code = "OTHER1" });
        db.Members.Add(new Member { Id = Guid.NewGuid(), Name = "Mallory", GroupId = otherGroupId, ClerkUserId = OutsiderClerkId });
        db.SaveChanges();

        // Group.AdminMemberId -> Member and Member.GroupId -> Group form a cycle EF can't order
        // in one save, so the admin is assigned once both rows exist.
        db.Groups.Single(g => g.Id == groupId).AdminMemberId = adminId;
        db.SaveChanges();
    }

    public void Dispose() => connection.Dispose();

    private static GroupService NewGroupService(AppDbContext db, string clerkUserId)
    {
        var currentMemberService = TestServices.NewCurrentMemberService(db, clerkUserId);
        var eventMapper = new EventMapper(db, new CostService(), currentMemberService);
        return new GroupService(db, eventMapper, currentMemberService, NullLogger<GroupService>.Instance);
    }

    [Fact]
    public async Task Admin_can_enable_auto_accept_with_a_ceiling()
    {
        using (var db = TestServices.NewContext(connection))
        {
            var result = await NewGroupService(db, AdminClerkId)
                .SetBookingAutomationAsync(groupId, new SetBookingAutomationRequest(true, 150m));
            Assert.True(result.IsSuccess);
        }

        using var verify = TestServices.NewContext(connection);
        var group = await verify.Groups.SingleAsync(g => g.Id == groupId);
        Assert.True(group.AutoAcceptBookings);
        Assert.Equal(150m, group.AutoAcceptMaxCost);
    }

    [Fact]
    public async Task Admin_can_turn_auto_accept_back_off()
    {
        using (var db = TestServices.NewContext(connection))
            await NewGroupService(db, AdminClerkId).SetBookingAutomationAsync(groupId, new SetBookingAutomationRequest(true, 150m));

        using (var db = TestServices.NewContext(connection))
        {
            var result = await NewGroupService(db, AdminClerkId)
                .SetBookingAutomationAsync(groupId, new SetBookingAutomationRequest(false, null));
            Assert.True(result.IsSuccess);
        }

        using var verify = TestServices.NewContext(connection);
        Assert.False((await verify.Groups.SingleAsync(g => g.Id == groupId)).AutoAcceptBookings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Enabling_without_a_positive_ceiling_is_rejected(int? maxCost)
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewGroupService(db, AdminClerkId)
            .SetBookingAutomationAsync(groupId, new SetBookingAutomationRequest(true, maxCost));

        Assert.Equal(ServiceErrorKind.BadRequest, result.Error!.Kind);
        Assert.False((await db.Groups.SingleAsync(g => g.Id == groupId)).AutoAcceptBookings);
    }

    [Fact]
    public async Task A_non_admin_member_is_forbidden()
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewGroupService(db, MemberClerkId)
            .SetBookingAutomationAsync(groupId, new SetBookingAutomationRequest(true, 150m));

        Assert.Equal(ServiceErrorKind.Forbidden, result.Error!.Kind);
        Assert.False((await db.Groups.SingleAsync(g => g.Id == groupId)).AutoAcceptBookings);
    }

    [Fact]
    public async Task Someone_from_another_group_is_forbidden()
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewGroupService(db, OutsiderClerkId)
            .SetBookingAutomationAsync(groupId, new SetBookingAutomationRequest(true, 150m));

        Assert.Equal(ServiceErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task An_unknown_group_is_not_found()
    {
        using var db = TestServices.NewContext(connection);
        var result = await NewGroupService(db, AdminClerkId)
            .SetBookingAutomationAsync(Guid.NewGuid(), new SetBookingAutomationRequest(true, 150m));

        Assert.Equal(ServiceErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task Event_creation_works_without_an_authenticated_caller()
    {
        // What the anonymous inbound-email webhook relies on: the explicit-member overload,
        // since resolving a caller from the JWT would throw on an unauthenticated request.
        using var db = TestServices.NewContext(connection);
        var admin = await db.Members.SingleAsync(m => m.Id == adminId);

        var currentMemberService = TestServices.NewCurrentMemberService(db, "clerk-nobody");
        var eventService = TestServices.NewEventService(db, currentMemberService);

        var result = await eventService.CreateAsAsync(groupId, new CreateEventRequest(
            "Badminton", "2026-12-01", null, null, []), admin);

        Assert.True(result.IsSuccess);
        Assert.Equal(adminId, (await db.Events.SingleAsync()).CreatedByMemberId);
    }
}
