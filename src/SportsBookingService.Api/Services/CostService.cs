using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Services;

public class CostService
{
    public CostBreakdownResponse Calculate(ICollection<Attendee> attendees, ICollection<Expense> expenses)
    {
        var totalCost = expenses.Sum(e => e.Amount);
        var attendeeCount = attendees.Count;
        var costPerAttendee = attendeeCount > 0
            ? Math.Ceiling(totalCost / attendeeCount * 100m) / 100m
            : 0m;

        var paidByMember = expenses
            .GroupBy(e => e.PaidByMemberId)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        var balances = attendees
            .OrderBy(a => a.JoinedAt)
            .Select(a =>
            {
                var paid = paidByMember.GetValueOrDefault(a.MemberId, 0m);
                var net = Math.Round(paid - costPerAttendee, 2);
                return new BalanceResponse(new MemberResponse(a.Member.Id, a.Member.Name), net);
            })
            .ToList();

        return new CostBreakdownResponse(totalCost, costPerAttendee, balances);
    }
}
