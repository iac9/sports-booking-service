using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.Jobs;
using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Tests;

public class EventRetentionCleanupTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly Guid groupId = Guid.NewGuid();
    private readonly Guid memberId = Guid.NewGuid();

    public EventRetentionCleanupTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Groups.Add(new Group { Id = groupId, Name = "Test Group", Code = "TEST01" });
        db.Members.Add(new Member { Id = memberId, Name = "Alice", GroupId = groupId });
        db.SaveChanges();
    }

    public void Dispose() => connection.Dispose();

    private AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task DeleteExpiredEventsAsync_RemovesOldEventAndAssociatedData_KeepsRecentEvent()
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-EventRetentionCleanup.RetentionMonths);
        var oldEventId = Guid.NewGuid();
        var recentEventId = Guid.NewGuid();

        using (var db = NewContext())
        {
            db.Events.Add(new Event { Id = oldEventId, Title = "Old Game", Date = cutoff.AddDays(-1), GroupId = groupId, CreatedByMemberId = memberId });
            db.Events.Add(new Event { Id = recentEventId, Title = "Recent Game", Date = cutoff.AddDays(1), GroupId = groupId, CreatedByMemberId = memberId });
            db.CourtBookings.Add(new CourtBooking { Id = Guid.NewGuid(), EventId = oldEventId, Court = "1", StartTime = "10:00", EndTime = "11:00" });
            db.Attendees.Add(new Attendee { EventId = oldEventId, MemberId = memberId, JoinedAt = DateTime.UtcNow });
            db.Expenses.Add(new Expense { Id = Guid.NewGuid(), EventId = oldEventId, Description = "Shuttlecocks", Amount = 10m, PaidByMemberId = memberId, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        using (var db = NewContext())
        {
            var deleted = await EventRetentionCleanup.DeleteExpiredEventsAsync(db, cutoff, CancellationToken.None);
            Assert.Equal(1, deleted);
        }

        using var verifyDb = NewContext();
        Assert.False(await verifyDb.Events.AnyAsync(e => e.Id == oldEventId));
        Assert.False(await verifyDb.CourtBookings.AnyAsync(c => c.EventId == oldEventId));
        Assert.False(await verifyDb.Attendees.AnyAsync(a => a.EventId == oldEventId));
        Assert.False(await verifyDb.Expenses.AnyAsync(e => e.EventId == oldEventId));
        Assert.True(await verifyDb.Events.AnyAsync(e => e.Id == recentEventId));
    }
}
