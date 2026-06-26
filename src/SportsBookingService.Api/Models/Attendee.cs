namespace SportsBookingService.Api.Models;

public class Attendee
{
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;
    public Guid MemberId { get; set; }
    public Member Member { get; set; } = null!;
    public DateTime JoinedAt { get; set; }
}
