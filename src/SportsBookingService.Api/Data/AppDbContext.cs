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
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<PendingBooking> PendingBookings => Set<PendingBooking>();

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

        modelBuilder.Entity<Group>()
            .HasIndex(g => g.InboundEmailToken)
            .IsUnique();

        modelBuilder.Entity<Member>()
            .HasIndex(m => new { m.GroupId, m.ClerkUserId })
            .IsUnique()
            .HasFilter("\"ClerkUserId\" IS NOT NULL");

        modelBuilder.Entity<Settlement>()
            .HasOne(s => s.FromMember)
            .WithMany()
            .HasForeignKey(s => s.FromMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Settlement>()
            .HasOne(s => s.ToMember)
            .WithMany()
            .HasForeignKey(s => s.ToMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Settlement>()
            .HasIndex(s => new { s.EventId, s.FromMemberId, s.ToMemberId })
            .IsUnique();

        modelBuilder.Entity<PendingBooking>()
            .HasOne(p => p.Group)
            .WithMany(g => g.PendingBookings)
            .HasForeignKey(p => p.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // A deleted location/event must not take the review-queue row with it — the raw email
        // is still worth showing, just without the match.
        modelBuilder.Entity<PendingBooking>()
            .HasOne(p => p.MatchedLocation)
            .WithMany()
            .HasForeignKey(p => p.MatchedLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<PendingBooking>()
            .HasOne(p => p.MatchedEvent)
            .WithMany()
            .HasForeignKey(p => p.MatchedEventId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<PendingBooking>()
            .HasOne(p => p.CreatedEvent)
            .WithMany()
            .HasForeignKey(p => p.CreatedEventId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<PendingBooking>()
            .HasOne(p => p.ReviewedByMember)
            .WithMany()
            .HasForeignKey(p => p.ReviewedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PendingBooking>()
            .HasIndex(p => new { p.GroupId, p.Status });
    }
}
