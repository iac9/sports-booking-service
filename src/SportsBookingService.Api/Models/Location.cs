namespace SportsBookingService.Api.Models;

public class Location
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string? WebsiteUrl { get; set; }
    public Guid GroupId { get; set; }
    public Group Group { get; set; } = null!;
}
