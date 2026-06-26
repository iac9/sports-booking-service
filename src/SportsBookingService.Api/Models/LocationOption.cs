namespace SportsBookingService.Api.Models;

public class LocationOption
{
    public Guid Id { get; set; }
    public string LocationName { get; set; } = "";
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;
    public ICollection<LocationVote> Votes { get; set; } = [];
}
