namespace SportsBookingService.Api.Models;

public class LocationVote
{
    public Guid LocationOptionId { get; set; }
    public LocationOption LocationOption { get; set; } = null!;
    public Guid MemberId { get; set; }
    public Member Member { get; set; } = null!;
}
