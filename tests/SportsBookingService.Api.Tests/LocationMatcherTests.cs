using SportsBookingService.Api.Models;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Tests;

public class LocationMatcherTests
{
    private static Location Loc(string name) => new() { Id = Guid.NewGuid(), Name = name, Address = "" };

    [Fact]
    public void Matches_exact_name_with_full_confidence()
    {
        var target = Loc("Melbourne Sports Centre");
        var match = LocationMatcher.Match("Melbourne Sports Centre", [Loc("Other Venue"), target]);

        Assert.NotNull(match);
        Assert.Equal(target.Id, match.Location.Id);
        Assert.Equal(1.0, match.Confidence);
    }

    [Theory]
    [InlineData("melbourne sports centre")]
    [InlineData("MELBOURNE SPORTS CENTRE")]
    [InlineData("  Melbourne  Sports Centre ")]
    [InlineData("Melbourne Sports-Centre")]
    [InlineData("Melbourne Sports Centre.")]
    public void Normalizes_case_whitespace_and_punctuation_before_matching(string venueName)
    {
        var target = Loc("Melbourne Sports Centre");
        var match = LocationMatcher.Match(venueName, [target]);

        Assert.NotNull(match);
        Assert.Equal(target.Id, match.Location.Id);
        Assert.Equal(1.0, match.Confidence);
    }

    [Fact]
    public void Falls_back_to_partial_match_at_lower_confidence()
    {
        var target = Loc("Melbourne Sports");
        var match = LocationMatcher.Match("Melbourne Sports Centre", [Loc("Sydney Courts"), target]);

        Assert.NotNull(match);
        Assert.Equal(target.Id, match.Location.Id);
        Assert.True(match.Confidence < 1.0);
    }

    [Fact]
    public void Prefers_an_exact_match_over_a_partial_one()
    {
        var partial = Loc("Melbourne");
        var exact = Loc("Melbourne Sports Centre");
        var match = LocationMatcher.Match("Melbourne Sports Centre", [partial, exact]);

        Assert.Equal(exact.Id, match!.Location.Id);
    }

    [Fact]
    public void Prefers_the_longest_partial_match()
    {
        var vague = Loc("Melbourne");
        var specific = Loc("Melbourne Sports");
        var match = LocationMatcher.Match("Melbourne Sports Centre", [vague, specific]);

        Assert.Equal(specific.Id, match!.Location.Id);
    }

    [Fact]
    public void Returns_null_when_nothing_resembles_the_venue()
    {
        Assert.Null(LocationMatcher.Match("Brisbane Badminton Hall", [Loc("Melbourne Sports Centre")]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Returns_null_for_a_venue_name_with_nothing_to_match_on(string? venueName)
    {
        Assert.Null(LocationMatcher.Match(venueName, [Loc("Melbourne Sports Centre")]));
    }

    [Fact]
    public void Returns_null_when_the_group_has_no_locations()
    {
        Assert.Null(LocationMatcher.Match("Melbourne Sports Centre", []));
    }
}
