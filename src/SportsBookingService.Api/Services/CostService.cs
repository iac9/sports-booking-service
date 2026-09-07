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

        var guestCounts = attendees
            .Where(a => a.Member.IsGuest && a.Member.AddedByMemberId.HasValue)
            .GroupBy(a => a.Member.AddedByMemberId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var owed = attendees.ToDictionary(a => a.MemberId, _ => 0m);

        // Each expense is split independently: every attendee-unit owes the floored
        // per-unit share, and whatever's left over from the floor (a few cents) is
        // charged to whoever paid that expense, so the shares always add up to the total.
        if (attendeeCount > 0)
        {
            foreach (var expense in expenses)
            {
                var unitShare = Math.Floor(expense.Amount / attendeeCount * 100m) / 100m;
                var remainder = Math.Round(expense.Amount - unitShare * attendeeCount, 2);

                foreach (var a in attendees)
                {
                    if (a.Member.IsGuest) continue;
                    owed[a.MemberId] += unitShare * (1 + guestCounts.GetValueOrDefault(a.MemberId, 0));
                }

                owed[expense.PaidByMemberId] = owed.GetValueOrDefault(expense.PaidByMemberId, 0m) + remainder;
            }
        }

        var balances = attendees
            .OrderBy(a => a.JoinedAt)
            .Select(a =>
            {
                var paid = paidByMember.GetValueOrDefault(a.MemberId, 0m);
                var net = Math.Round(paid - Math.Round(owed.GetValueOrDefault(a.MemberId, 0m), 2), 2);
                return new BalanceResponse(new MemberResponse(a.Member.Id, a.Member.Name), net);
            })
            .ToList();

        return new CostBreakdownResponse(totalCost, costPerAttendee, balances);
    }
}
