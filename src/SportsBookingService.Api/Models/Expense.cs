namespace SportsBookingService.Api.Models;

public class Expense
{
    public Guid Id { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public Guid PaidByMemberId { get; set; }
    public Member PaidBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;
}
