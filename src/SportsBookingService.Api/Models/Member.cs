namespace SportsBookingService.Api.Models;

public class Member
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? ClerkUserId { get; set; }
    public Guid GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public bool IsGuest { get; set; } = false;
    public Guid? AddedByMemberId { get; set; }
}
