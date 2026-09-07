using SportsBookingService.Api.DTOs;

namespace SportsBookingService.Api.Services;

public static class DebtSimplifier
{
    private const decimal Epsilon = 0.005m;

    public record Transaction(MemberResponse From, MemberResponse To, decimal Amount);

    // Greedy match: pair the largest creditor with the largest debtor, repeatedly,
    // until every net balance is settled. Not globally minimal, but simple and deterministic.
    public static List<Transaction> Simplify(List<BalanceResponse> balances)
    {
        var creditors = balances
            .Where(b => b.Balance > Epsilon)
            .Select(b => (Member: b.Member, Amount: b.Balance))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var debtors = balances
            .Where(b => b.Balance < -Epsilon)
            .Select(b => (Member: b.Member, Amount: -b.Balance))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var transactions = new List<Transaction>();
        var ci = 0;
        var di = 0;

        while (ci < creditors.Count && di < debtors.Count)
        {
            var (creditor, creditAmount) = creditors[ci];
            var (debtor, debtAmount) = debtors[di];
            var amount = Math.Min(creditAmount, debtAmount);

            if (amount > Epsilon)
                transactions.Add(new Transaction(debtor, creditor, Math.Round(amount, 2)));

            creditors[ci] = (creditor, creditAmount - amount);
            debtors[di] = (debtor, debtAmount - amount);

            if (creditors[ci].Amount <= Epsilon) ci++;
            if (debtors[di].Amount <= Epsilon) di++;
        }

        return transactions;
    }
}
