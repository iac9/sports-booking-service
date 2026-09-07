using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Services;

public class DebtService(AppDbContext db, CostService costService, CurrentMemberService currentMemberService)
{
    public async Task<ServiceResult<List<DebtResponse>>> GetOwedToMemberAsync(Guid groupId, Guid memberId)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == groupId))
            return ServiceResult<List<DebtResponse>>.Fail(ServiceErrorKind.NotFound);

        var caller = await currentMemberService.FindMemberAsync(groupId);
        if (caller is null)
            return ServiceResult<List<DebtResponse>>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        var events = await db.Events
            .Where(e => e.GroupId == groupId)
            .Include(e => e.Attendees).ThenInclude(a => a.Member)
            .Include(e => e.Expenses).ThenInclude(ex => ex.PaidBy)
            .Include(e => e.Settlements)
            .OrderByDescending(e => e.Date)
            .ToListAsync();

        var debts = new List<DebtResponse>();
        foreach (var ev in events)
        {
            var breakdown = costService.Calculate(ev.Attendees, ev.Expenses);
            var transactions = DebtSimplifier.Simplify(breakdown.Balances);

            foreach (var tx in transactions)
            {
                if (tx.To.Id != memberId) continue;

                // Once settled, a debt is gone for good — excluded here rather than flagged,
                // so it disappears from view for both sides of the transaction.
                var isSettled = ev.Settlements.Any(s => s.FromMemberId == tx.From.Id && s.ToMemberId == memberId);
                if (isSettled) continue;

                debts.Add(new DebtResponse(
                    ev.Id,
                    ev.Title,
                    ev.Date.ToString("yyyy-MM-dd"),
                    tx.From,
                    tx.Amount,
                    false,
                    null
                ));
            }
        }

        return ServiceResult<List<DebtResponse>>.Success(debts);
    }

    public async Task<ServiceResult<DebtResponse>> SettleAsync(Guid groupId, Guid toMemberId, SettleDebtRequest request)
    {
        var caller = await currentMemberService.FindMemberAsync(groupId);
        if (caller is null)
            return ServiceResult<DebtResponse>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");
        if (caller.Id != toMemberId)
            return ServiceResult<DebtResponse>.Fail(ServiceErrorKind.Forbidden);

        var ev = await db.Events
            .Include(e => e.Attendees).ThenInclude(a => a.Member)
            .Include(e => e.Expenses).ThenInclude(ex => ex.PaidBy)
            .Include(e => e.Settlements)
            .FirstOrDefaultAsync(e => e.Id == request.EventId && e.GroupId == groupId);
        if (ev is null) return ServiceResult<DebtResponse>.Fail(ServiceErrorKind.NotFound);

        var fromMember = await db.Members.FirstOrDefaultAsync(m => m.Id == request.FromMemberId && m.GroupId == groupId);
        if (fromMember is null) return ServiceResult<DebtResponse>.Fail(ServiceErrorKind.BadRequest, "Member not found in group.");

        var settlement = ev.Settlements.FirstOrDefault(s => s.FromMemberId == request.FromMemberId && s.ToMemberId == toMemberId);
        if (settlement is null)
        {
            // Never trust a client-supplied amount here — settling permanently deletes the debt,
            // so the amount must be recomputed from current state, the same way GetOwedToMemberAsync does.
            var breakdown = costService.Calculate(ev.Attendees, ev.Expenses);
            var debt = DebtSimplifier.Simplify(breakdown.Balances)
                .FirstOrDefault(tx => tx.From.Id == request.FromMemberId && tx.To.Id == toMemberId);
            if (debt is null)
                return ServiceResult<DebtResponse>.Fail(ServiceErrorKind.BadRequest, "No outstanding debt between these members for this event.");

            settlement = new Settlement
            {
                Id = Guid.NewGuid(),
                EventId = ev.Id,
                FromMemberId = request.FromMemberId,
                ToMemberId = toMemberId,
                Amount = debt.Amount,
                SettledAt = DateTime.UtcNow,
            };
            db.Settlements.Add(settlement);
            await db.SaveChangesAsync();
        }

        return ServiceResult<DebtResponse>.Success(new DebtResponse(
            ev.Id, ev.Title, ev.Date.ToString("yyyy-MM-dd"),
            new MemberResponse(fromMember.Id, fromMember.Name),
            settlement.Amount, true, settlement.SettledAt.ToString("o")
        ));
    }
}
