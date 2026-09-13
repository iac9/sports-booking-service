using SportsBookingService.Api.Models;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Tests;

public class MemberMatcherTests
{
    private static Member M(string name, bool isGuest = false) =>
        new() { Id = Guid.NewGuid(), Name = name, IsGuest = isGuest };

    [Fact]
    public void Matches_an_exact_name_with_full_confidence()
    {
        var target = M("Alice Smith");
        var match = MemberMatcher.Match("Alice Smith", [M("Bob Jones"), target]);

        Assert.NotNull(match);
        Assert.Equal(target.Id, match.Member.Id);
        Assert.Equal(1.0, match.Confidence);
    }

    [Theory]
    [InlineData("alice smith")]
    [InlineData("ALICE SMITH")]
    [InlineData("  Alice  Smith ")]
    [InlineData("Alice-Smith")]
    public void Normalizes_case_whitespace_and_punctuation(string name)
    {
        var target = M("Alice Smith");
        var match = MemberMatcher.Match(name, [target]);

        Assert.Equal(target.Id, match!.Member.Id);
        Assert.Equal(1.0, match.Confidence);
    }

    [Fact]
    public void Matches_a_first_name_against_a_full_name_at_lower_confidence()
    {
        var target = M("Andrew Christabel");
        var match = MemberMatcher.Match("Andrew", [M("Bob Jones"), target]);

        Assert.NotNull(match);
        Assert.Equal(target.Id, match.Member.Id);
        Assert.True(match.Confidence < 1.0);
    }

    [Fact]
    public void Matches_a_full_name_against_a_member_saved_with_only_a_first_name()
    {
        var target = M("Andrew");
        var match = MemberMatcher.Match("Andrew Christabel", [target]);

        Assert.Equal(target.Id, match!.Member.Id);
    }

    [Fact]
    public void Prefers_an_exact_match_over_a_partial_one()
    {
        var partial = M("Alice Smithson");
        var exact = M("Alice");
        var match = MemberMatcher.Match("Alice", [partial, exact]);

        Assert.Equal(exact.Id, match!.Member.Id);
        Assert.Equal(1.0, match.Confidence);
    }

    // Ambiguity must mean "don't know" rather than a guess: unlike a venue, a wrong answer
    // here bills the wrong person and feeds the debt calculations.
    [Fact]
    public void Returns_null_when_two_members_match_partially()
    {
        var match = MemberMatcher.Match("Alice", [M("Alice Smith"), M("Alice Jones")]);
        Assert.Null(match);
    }

    [Fact]
    public void Returns_null_when_two_members_share_the_same_name_exactly()
    {
        var match = MemberMatcher.Match("Alice", [M("Alice"), M("Alice")]);
        Assert.Null(match);
    }

    [Fact]
    public void Ignores_guests_entirely()
    {
        // A guest is a placeholder created by another member, so it can never be the payer —
        // and if it were considered, it would make an otherwise-clean match ambiguous.
        var real = M("Alice");
        var match = MemberMatcher.Match("Alice", [M("Alice", isGuest: true), real]);

        Assert.Equal(real.Id, match!.Member.Id);
    }

    [Fact]
    public void Returns_null_when_only_a_guest_matches()
    {
        Assert.Null(MemberMatcher.Match("Alice", [M("Alice", isGuest: true)]));
    }

    [Fact]
    public void Returns_null_when_nobody_resembles_the_name()
    {
        Assert.Null(MemberMatcher.Match("Zachary", [M("Alice"), M("Bob")]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Returns_null_for_a_name_with_nothing_to_match_on(string? name)
    {
        Assert.Null(MemberMatcher.Match(name, [M("Alice")]));
    }

    [Fact]
    public void Returns_null_when_the_group_has_no_members()
    {
        Assert.Null(MemberMatcher.Match("Alice", []));
    }
}
