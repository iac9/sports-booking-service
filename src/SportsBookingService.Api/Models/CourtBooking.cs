namespace SportsBookingService.Api.Models;

public class CourtBooking
{
    public Guid Id { get; set; }
    public string Court { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;
}
