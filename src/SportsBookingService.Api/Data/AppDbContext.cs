using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<CourtBooking> CourtBookings => Set<CourtBooking>();
    public DbSet<Attendee> Attendees => Set<Attendee>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<Expense> Expenses => Set<Expense>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Attendee>().HasKey(a => new { a.EventId, a.MemberId });
        modelBuilder.Entity<WaitlistEntry>().HasKey(w => new { w.EventId, w.MemberId });

        modelBuilder.Entity<Event>()
            .HasOne(e => e.CreatedBy)
            .WithMany()
            .HasForeignKey(e => e.CreatedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Expense>()
            .HasOne(e => e.PaidBy)
            .WithMany()
            .HasForeignKey(e => e.PaidByMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Group>()
            .HasOne(g => g.AdminMember)
            .WithMany()
            .HasForeignKey(g => g.AdminMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Group>()
            .HasIndex(g => g.Code)
            .IsUnique();

        modelBuilder.Entity<Member>()
            .HasIndex(m => new { m.GroupId, m.ClerkUserId })
            .IsUnique()
            .HasFilter("\"ClerkUserId\" IS NOT NULL");
    }
}
