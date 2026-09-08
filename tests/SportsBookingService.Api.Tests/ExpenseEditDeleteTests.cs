using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Tests;

public class ExpenseEditDeleteTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly Guid groupId = Guid.NewGuid();
    private readonly Guid creatorId = Guid.NewGuid();
    private readonly Guid otherMemberId = Guid.NewGuid();
    private const string ClerkUserId = "clerk-alice";

    public ExpenseEditDeleteTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Groups.Add(new Group { Id = groupId, Name = "Test Group", Code = "TEST01" });
        db.Members.Add(new Member { Id = creatorId, Name = "Alice", GroupId = groupId, ClerkUserId = ClerkUserId });
        db.Members.Add(new Member { Id = otherMemberId, Name = "Bob", GroupId = groupId, ClerkUserId = "clerk-bob" });
        db.SaveChanges();
    }

    public void Dispose() => connection.Dispose();

    private AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        return new AppDbContext(options);
    }

    private EventService NewEventService(AppDbContext db)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", ClerkUserId)])),
            },
        };
        var currentMemberService = new CurrentMemberService(httpContextAccessor, db, NullLogger<CurrentMemberService>.Instance);
        var eventMapper = new EventMapper(db, new CostService(), currentMemberService);
        return new EventService(db, eventMapper, currentMemberService);
    }

    private async Task<Guid> CreateEventWithExpenseAsync(decimal amount = 10m)
    {
        Guid eventId;
        using (var db = NewContext())
        {
            var createResult = await NewEventService(db).CreateAsync(groupId, new CreateEventRequest(
                "Badminton", "2026-06-21", null, null, []));
            eventId = createResult.Value!.Id;
        }
        using (var db = NewContext())
        {
            await NewEventService(db).AddExpenseAsync(groupId, eventId, new AddExpenseRequest("Shuttlecocks", amount, creatorId));
        }
        return eventId;
    }

    [Fact]
    public async Task UpdateExpenseAsync_ChangesOnlySuppliedFields()
    {
        var eventId = await CreateEventWithExpenseAsync();
        Guid expenseId;
        using (var db = NewContext())
            expenseId = (await db.Expenses.FirstAsync(e => e.EventId == eventId)).Id;

        using var db2 = NewContext();
        var result = await NewEventService(db2).UpdateExpenseAsync(groupId, eventId, expenseId,
            new UpdateExpenseRequest("Court fee", 15m, null));

        Assert.True(result.IsSuccess);
        Assert.Equal("Court fee", result.Value!.Description);
        Assert.Equal(15m, result.Value.Amount);
        Assert.Equal(creatorId, result.Value.PaidBy.Id);
    }

    [Fact]
    public async Task UpdateExpenseAsync_CanReassignPaidBy()
    {
        var eventId = await CreateEventWithExpenseAsync();
        Guid expenseId;
        using (var db = NewContext())
            expenseId = (await db.Expenses.FirstAsync(e => e.EventId == eventId)).Id;

        using var db2 = NewContext();
        var result = await NewEventService(db2).UpdateExpenseAsync(groupId, eventId, expenseId,
            new UpdateExpenseRequest(null, null, otherMemberId));

        Assert.True(result.IsSuccess);
        Assert.Equal(otherMemberId, result.Value!.PaidBy.Id);
    }

    [Fact]
    public async Task UpdateExpenseAsync_UnknownPaidByMember_ReturnsBadRequest()
    {
        var eventId = await CreateEventWithExpenseAsync();
        Guid expenseId;
        using (var db = NewContext())
            expenseId = (await db.Expenses.FirstAsync(e => e.EventId == eventId)).Id;

        using var db2 = NewContext();
        var result = await NewEventService(db2).UpdateExpenseAsync(groupId, eventId, expenseId,
            new UpdateExpenseRequest(null, null, Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorKind.BadRequest, result.Error!.Kind);
    }

    [Fact]
    public async Task UpdateExpenseAsync_UnknownExpense_ReturnsNotFound()
    {
        var eventId = await CreateEventWithExpenseAsync();

        using var db = NewContext();
        var result = await NewEventService(db).UpdateExpenseAsync(groupId, eventId, Guid.NewGuid(),
            new UpdateExpenseRequest("x", null, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task DeleteExpenseAsync_RemovesExpense()
    {
        var eventId = await CreateEventWithExpenseAsync();
        Guid expenseId;
        using (var db = NewContext())
            expenseId = (await db.Expenses.FirstAsync(e => e.EventId == eventId)).Id;

        using (var db2 = NewContext())
        {
            var result = await NewEventService(db2).DeleteExpenseAsync(groupId, eventId, expenseId);
            Assert.True(result.IsSuccess);
        }

        using var verifyDb = NewContext();
        Assert.False(await verifyDb.Expenses.AnyAsync(e => e.Id == expenseId));
    }

    [Fact]
    public async Task DeleteExpenseAsync_UnknownExpense_ReturnsNotFound()
    {
        var eventId = await CreateEventWithExpenseAsync();

        using var db = NewContext();
        var result = await NewEventService(db).DeleteExpenseAsync(groupId, eventId, Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorKind.NotFound, result.Error!.Kind);
    }
}
