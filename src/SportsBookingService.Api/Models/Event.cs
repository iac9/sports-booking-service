namespace SportsBookingService.Api.Models;

public class Event
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public DateOnly Date { get; set; }
    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }
    public int MaxAttendees { get; set; }
    public Guid GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public Guid CreatedByMemberId { get; set; }
    public Member CreatedBy { get; set; } = null!;
    public ICollection<CourtBooking> CourtBookings { get; set; } = [];
    public ICollection<Attendee> Attendees { get; set; } = [];
    public ICollection<WaitlistEntry> Waitlist { get; set; } = [];
    public ICollection<Expense> Expenses { get; set; } = [];
}
