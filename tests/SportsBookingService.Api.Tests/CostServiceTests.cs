using SportsBookingService.Api.Models;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Tests;

public class CostServiceTests
{
    private static Member NewMember(string name, bool isGuest = false, Guid? addedByMemberId = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        GroupId = Guid.NewGuid(),
        IsGuest = isGuest,
        AddedByMemberId = addedByMemberId,
    };

    private static Attendee NewAttendee(Member member, DateTime joinedAt) => new()
    {
        MemberId = member.Id,
        Member = member,
        JoinedAt = joinedAt,
    };

    private static Expense NewExpense(Member paidBy, decimal amount) => new()
    {
        Id = Guid.NewGuid(),
        Description = "Court booking",
        Amount = amount,
        PaidByMemberId = paidBy.Id,
        PaidBy = paidBy,
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public void Calculate_WithNoGuests_SplitsCostEvenly()
    {
        var alice = NewMember("Alice");
        var bob = NewMember("Bob");
        var attendees = new List<Attendee> { NewAttendee(alice, DateTime.UtcNow), NewAttendee(bob, DateTime.UtcNow) };
        var expenses = new List<Expense> { NewExpense(alice, 24m) };

        var result = new CostService().Calculate(attendees, expenses);

        Assert.Equal(12m, result.CostPerAttendee);
        var aliceBalance = result.Balances.Single(b => b.Member.Id == alice.Id);
        var bobBalance = result.Balances.Single(b => b.Member.Id == bob.Id);
        Assert.Equal(12m, aliceBalance.Balance); // paid 24, owed 12, net +12
        Assert.Equal(-12m, bobBalance.Balance); // paid 0, owed 12, net -12
    }

    [Fact]
    public void Calculate_WithOneGuest_FoldsGuestShareIntoHost()
    {
        var alice = NewMember("Alice");
        var bob = NewMember("Bob");
        var charlie = NewMember("Charlie");
        var guest = NewMember("Alice's guest", isGuest: true, addedByMemberId: alice.Id);
        var attendees = new List<Attendee>
        {
            NewAttendee(alice, DateTime.UtcNow),
            NewAttendee(bob, DateTime.UtcNow),
            NewAttendee(charlie, DateTime.UtcNow),
            NewAttendee(guest, DateTime.UtcNow),
        };
        var expenses = new List<Expense> { NewExpense(alice, 40m) };

        var result = new CostService().Calculate(attendees, expenses);

        // unit share = 40 / 4 = 10
        Assert.Equal(10m, result.CostPerAttendee);
        var aliceBalance = result.Balances.Single(b => b.Member.Id == alice.Id);
        var bobBalance = result.Balances.Single(b => b.Member.Id == bob.Id);
        var charlieBalance = result.Balances.Single(b => b.Member.Id == charlie.Id);
        var guestBalance = result.Balances.Single(b => b.Member.Id == guest.Id);

        Assert.Equal(20m, aliceBalance.Balance); // paid 40, owed 2x10=20, net +20
        Assert.Equal(-10m, bobBalance.Balance);
        Assert.Equal(-10m, charlieBalance.Balance);
        Assert.Equal(0m, guestBalance.Balance); // owes nothing, paid nothing
    }

    [Fact]
    public void Calculate_WithGuestsUnderDifferentHosts_AllocatesEachToTheirOwnHost()
    {
        var alice = NewMember("Alice");
        var bob = NewMember("Bob");
        var aliceGuest = NewMember("Alice's guest", isGuest: true, addedByMemberId: alice.Id);
        var bobGuest1 = NewMember("Bob's guest 1", isGuest: true, addedByMemberId: bob.Id);
        var bobGuest2 = NewMember("Bob's guest 2", isGuest: true, addedByMemberId: bob.Id);
        var attendees = new List<Attendee>
        {
            NewAttendee(alice, DateTime.UtcNow),
            NewAttendee(bob, DateTime.UtcNow),
            NewAttendee(aliceGuest, DateTime.UtcNow),
            NewAttendee(bobGuest1, DateTime.UtcNow),
            NewAttendee(bobGuest2, DateTime.UtcNow),
        };
        var expenses = new List<Expense> { NewExpense(alice, 50m) };

        var result = new CostService().Calculate(attendees, expenses);

        // unit share = 50 / 5 = 10
        Assert.Equal(10m, result.CostPerAttendee);
        var aliceBalance = result.Balances.Single(b => b.Member.Id == alice.Id);
        var bobBalance = result.Balances.Single(b => b.Member.Id == bob.Id);

        Assert.Equal(30m, aliceBalance.Balance); // paid 50, owed 2x10=20, net +30
        Assert.Equal(-30m, bobBalance.Balance); // paid 0, owed 3x10=30, net -30
        Assert.All(result.Balances.Where(b => b.Member.Id == aliceGuest.Id || b.Member.Id == bobGuest1.Id || b.Member.Id == bobGuest2.Id),
            b => Assert.Equal(0m, b.Balance));
    }

    [Fact]
    public void Calculate_WithUnevenSplit_FloorsShareAndChargesRemainderToPayer()
    {
        var alice = NewMember("Alice");
        var bob = NewMember("Bob");
        var charlie = NewMember("Charlie");
        var attendees = new List<Attendee>
        {
            NewAttendee(alice, DateTime.UtcNow),
            NewAttendee(bob, DateTime.UtcNow),
            NewAttendee(charlie, DateTime.UtcNow),
        };
        var expenses = new List<Expense> { NewExpense(bob, 100m) };

        var result = new CostService().Calculate(attendees, expenses);

        var aliceBalance = result.Balances.Single(b => b.Member.Id == alice.Id);
        var bobBalance = result.Balances.Single(b => b.Member.Id == bob.Id);
        var charlieBalance = result.Balances.Single(b => b.Member.Id == charlie.Id);

        // 100 / 3 = 33.333... -> floored share is 33.33, leftover 0.01 goes to Bob (the payer)
        Assert.Equal(-33.33m, aliceBalance.Balance);
        Assert.Equal(66.66m, bobBalance.Balance); // paid 100, owed 33.34, net +66.66
        Assert.Equal(-33.33m, charlieBalance.Balance);
        Assert.Equal(0m, aliceBalance.Balance + bobBalance.Balance + charlieBalance.Balance);
    }

    [Fact]
    public void Calculate_WithMultipleExpensesByDifferentPayers_AssignsEachRemainderToItsOwnPayer()
    {
        var alice = NewMember("Alice");
        var bob = NewMember("Bob");
        var charlie = NewMember("Charlie");
        var attendees = new List<Attendee>
        {
            NewAttendee(alice, DateTime.UtcNow),
            NewAttendee(bob, DateTime.UtcNow),
            NewAttendee(charlie, DateTime.UtcNow),
        };
        var expenses = new List<Expense> { NewExpense(bob, 10m), NewExpense(alice, 100m) };

        var result = new CostService().Calculate(attendees, expenses);

        var aliceBalance = result.Balances.Single(b => b.Member.Id == alice.Id);
        var bobBalance = result.Balances.Single(b => b.Member.Id == bob.Id);
        var charlieBalance = result.Balances.Single(b => b.Member.Id == charlie.Id);

        // 10/3 -> floor 3.33, leftover 0.01 to Bob; 100/3 -> floor 33.33, leftover 0.01 to Alice
        // Alice owed 3.33 + 33.33 + 0.01 = 36.67, paid 100 -> net +63.33
        // Bob owed 3.33 + 0.01 + 33.33 = 36.67, paid 10 -> net -26.67
        // Charlie owed 3.33 + 33.33 = 36.66, paid 0 -> net -36.66
        Assert.Equal(63.33m, aliceBalance.Balance);
        Assert.Equal(-26.67m, bobBalance.Balance);
        Assert.Equal(-36.66m, charlieBalance.Balance);
        Assert.Equal(0m, aliceBalance.Balance + bobBalance.Balance + charlieBalance.Balance);
    }
}
