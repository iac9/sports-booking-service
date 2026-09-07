namespace SportsBookingService.Api.Models;

public class Settlement
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;
    public Guid FromMemberId { get; set; }
    public Member FromMember { get; set; } = null!;
    public Guid ToMemberId { get; set; }
    public Member ToMember { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateTime SettledAt { get; set; }
}
