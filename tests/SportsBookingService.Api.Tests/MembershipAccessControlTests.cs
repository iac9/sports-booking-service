using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SportsBookingService.Api.Controllers;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Tests;

// Verifies the A01 fix: every endpoint below now rejects callers who aren't a member
// of the group the resource belongs to, not just callers who are merely authenticated.
public class MembershipAccessControlTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly Guid groupId = Guid.NewGuid();
    private readonly Guid aliceId = Guid.NewGuid();
    private readonly Guid otherGroupId = Guid.NewGuid();
    private readonly Guid malloryId = Guid.NewGuid();
    private const string AliceClerkId = "clerk-alice";
    private const string MalloryClerkId = "clerk-mallory";

    public MembershipAccessControlTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Groups.Add(new Group { Id = groupId, Name = "Alice's Group", Code = "ALICE1" });
        db.Members.Add(new Member { Id = aliceId, Name = "Alice", GroupId = groupId, ClerkUserId = AliceClerkId });

        // Mallory is a member of a *different* group, not this one.
        db.Groups.Add(new Group { Id = otherGroupId, Name = "Mallory's Group", Code = "MALORY" });
        db.Members.Add(new Member { Id = malloryId, Name = "Mallory", GroupId = otherGroupId, ClerkUserId = MalloryClerkId });
        db.SaveChanges();
    }

    public void Dispose() => connection.Dispose();

    private AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        return new AppDbContext(options);
    }

    private static CurrentMemberService NewCurrentMemberService(AppDbContext db, string clerkUserId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", clerkUserId)])),
            },
        };
        return new CurrentMemberService(httpContextAccessor, db, NullLogger<CurrentMemberService>.Instance);
    }

    private EventService NewEventService(AppDbContext db, string clerkUserId)
    {
        var currentMemberService = NewCurrentMemberService(db, clerkUserId);
        var eventMapper = new EventMapper(db, new CostService(), currentMemberService);
        return new EventService(db, eventMapper, currentMemberService);
    }

    private GroupsController NewGroupsController(AppDbContext db, string clerkUserId)
    {
        var currentMemberService = NewCurrentMemberService(db, clerkUserId);
        var eventMapper = new EventMapper(db, new CostService(), currentMemberService);
        var groupService = new GroupService(db, eventMapper, currentMemberService, NullLogger<GroupService>.Instance);
        return new GroupsController(db, eventMapper, groupService, currentMemberService, NullLogger<GroupsController>.Instance);
    }

    private LocationsController NewLocationsController(AppDbContext db, string clerkUserId) =>
        new(db, NewCurrentMemberService(db, clerkUserId));

    private EventsController NewEventsController(AppDbContext db, string clerkUserId)
    {
        var currentMemberService = NewCurrentMemberService(db, clerkUserId);
        var eventMapper = new EventMapper(db, new CostService(), currentMemberService);
        var eventService = NewEventService(db, clerkUserId);
        return new EventsController(db, eventMapper, eventService, currentMemberService);
    }

    private DebtService NewDebtService(AppDbContext db, string clerkUserId) =>
        new(db, new CostService(), NewCurrentMemberService(db, clerkUserId));

    private async Task<Guid> CreateEventAsync()
    {
        using var db = NewContext();
        var result = await NewEventService(db, AliceClerkId).CreateAsync(groupId, new CreateEventRequest(
            "Badminton", "2026-06-21", null, null, []));
        return result.Value!.Id;
    }

    // ── GroupsController.GetByCode ──────────────────────────────────────────────

    [Fact]
    public async Task GetByCode_Member_Succeeds()
    {
        using var db = NewContext();
        var result = await NewGroupsController(db, AliceClerkId).GetByCode("ALICE1");
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetByCode_NonMember_ReturnsNotFound()
    {
        using var db = NewContext();
        var result = await NewGroupsController(db, MalloryClerkId).GetByCode("ALICE1");
        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── GroupsController.GetMembers ──────────────────────────────────────────────

    [Fact]
    public async Task GetMembers_Member_Succeeds()
    {
        using var db = NewContext();
        var result = await NewGroupsController(db, AliceClerkId).GetMembers(groupId);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetMembers_NonMember_ReturnsNotFound()
    {
        using var db = NewContext();
        var result = await NewGroupsController(db, MalloryClerkId).GetMembers(groupId);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── GroupService.SetPrimaryLocationAsync ────────────────────────────────────

    [Fact]
    public async Task SetPrimaryLocationAsync_NonMember_ReturnsBadRequest()
    {
        using var db = NewContext();
        var currentMemberService = NewCurrentMemberService(db, MalloryClerkId);
        var eventMapper = new EventMapper(db, new CostService(), currentMemberService);
        var groupService = new GroupService(db, eventMapper, currentMemberService, NullLogger<GroupService>.Instance);

        var result = await groupService.SetPrimaryLocationAsync(groupId, new SetPrimaryLocationRequest(null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorKind.BadRequest, result.Error!.Kind);
    }

    // ── LocationsController ──────────────────────────────────────────────────────

    [Fact]
    public async Task Locations_List_NonMember_ReturnsBadRequest()
    {
        using var db = NewContext();
        var result = await NewLocationsController(db, MalloryClerkId).List(groupId);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task Locations_Create_NonMember_ReturnsBadRequest()
    {
        using var db = NewContext();
        var result = await NewLocationsController(db, MalloryClerkId).Create(groupId, new CreateLocationRequest("Court A", "123 Main St"));
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Locations_Update_NonMember_ReturnsBadRequest()
    {
        Guid locationId;
        using (var db = NewContext())
        {
            var created = await NewLocationsController(db, AliceClerkId).Create(groupId, new CreateLocationRequest("Court A", "123 Main St"));
            locationId = ((LocationResponse)((CreatedAtActionResult)created.Result!).Value!).Id;
        }

        using var db2 = NewContext();
        var result = await NewLocationsController(db2, MalloryClerkId).Update(groupId, locationId, new UpdateLocationRequest("Court B", null));
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Locations_Delete_NonMember_ReturnsBadRequest()
    {
        Guid locationId;
        using (var db = NewContext())
        {
            var created = await NewLocationsController(db, AliceClerkId).Create(groupId, new CreateLocationRequest("Court A", "123 Main St"));
            locationId = ((LocationResponse)((CreatedAtActionResult)created.Result!).Value!).Id;
        }

        using var db2 = NewContext();
        var result = await NewLocationsController(db2, MalloryClerkId).Delete(groupId, locationId);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    // ── EventsController.List / Get ──────────────────────────────────────────────

    [Fact]
    public async Task Events_List_NonMember_ReturnsBadRequest()
    {
        using var db = NewContext();
        var result = await NewEventsController(db, MalloryClerkId).List(groupId);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Events_Get_NonMember_ReturnsBadRequest()
    {
        var eventId = await CreateEventAsync();

        using var db = NewContext();
        var result = await NewEventsController(db, MalloryClerkId).Get(groupId, eventId);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Events_Get_Member_Succeeds()
    {
        var eventId = await CreateEventAsync();

        using var db = NewContext();
        var result = await NewEventsController(db, AliceClerkId).Get(groupId, eventId);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    // ── EventService.UpdateAsync / DeleteAsync ──────────────────────────────────

    [Fact]
    public async Task UpdateAsync_NonMember_ReturnsBadRequest()
    {
        var eventId = await CreateEventAsync();

        using var db = NewContext();
        var result = await NewEventService(db, MalloryClerkId).UpdateAsync(groupId, eventId,
            new UpdateEventRequest("Renamed", null, null, null, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorKind.BadRequest, result.Error!.Kind);
    }

    [Fact]
    public async Task DeleteAsync_NonMember_ReturnsBadRequest()
    {
        var eventId = await CreateEventAsync();

        using var db = NewContext();
        var result = await NewEventService(db, MalloryClerkId).DeleteAsync(groupId, eventId);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorKind.BadRequest, result.Error!.Kind);
    }

    // ── EventService.AddExpenseAsync ────────────────────────────────────────────

    [Fact]
    public async Task AddExpenseAsync_NonMember_ReturnsBadRequest()
    {
        var eventId = await CreateEventAsync();

        using var db = NewContext();
        var result = await NewEventService(db, MalloryClerkId).AddExpenseAsync(groupId, eventId,
            new AddExpenseRequest("Shuttlecocks", 10m, aliceId));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorKind.BadRequest, result.Error!.Kind);
    }

    // ── DebtService.GetOwedToMemberAsync ────────────────────────────────────────

    [Fact]
    public async Task GetOwedToMemberAsync_NonMember_ReturnsBadRequest()
    {
        using var db = NewContext();
        var result = await NewDebtService(db, MalloryClerkId).GetOwedToMemberAsync(groupId, aliceId);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorKind.BadRequest, result.Error!.Kind);
    }

    [Fact]
    public async Task GetOwedToMemberAsync_Member_Succeeds()
    {
        using var db = NewContext();
        var result = await NewDebtService(db, AliceClerkId).GetOwedToMemberAsync(groupId, aliceId);

        Assert.True(result.IsSuccess);
    }
}
