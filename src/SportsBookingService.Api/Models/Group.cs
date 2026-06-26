namespace SportsBookingService.Api.Models;

public class Group
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public ICollection<Member> Members { get; set; } = [];
    public ICollection<Event> Events { get; set; } = [];
    public ICollection<Location> Locations { get; set; } = [];
}
