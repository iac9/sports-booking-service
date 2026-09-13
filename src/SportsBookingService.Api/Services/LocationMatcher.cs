using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Services;

public record LocationMatch(Location Location, double Confidence);

/// <summary>
/// Matches an extracted venue name against a group's saved locations. Pure — no DB access —
/// so the matching rules can be unit-tested directly. The confidence it returns is
/// informational: it pre-selects the location dropdown in the review UI and never
/// auto-creates anything.
/// </summary>
public static class LocationMatcher
{
    public static LocationMatch? Match(string? venueName, IEnumerable<Location> locations)
    {
        var needle = NameMatcher.Normalize(venueName);
        if (needle.Length == 0) return null;

        var candidates = locations
            .Select(l => (Location: l, Name: NameMatcher.Normalize(l.Name)))
            .Where(c => c.Name.Length > 0)
            .ToList();

        var exact = candidates.FirstOrDefault(c => c.Name == needle);
        if (exact.Location is not null) return new LocationMatch(exact.Location, 1.0);

        // Partial matches in either direction: "Melbourne Sports Centre" in the email should
        // still find a saved "Melbourne Sports", and vice versa. Longest overlap wins, so a
        // more specific saved location beats a shorter one that also happens to be a substring.
        var partial = candidates
            .Where(c => c.Name.Contains(needle, StringComparison.Ordinal) || needle.Contains(c.Name, StringComparison.Ordinal))
            .OrderByDescending(c => c.Name.Length)
            .FirstOrDefault();

        return partial.Location is null ? null : new LocationMatch(partial.Location, 0.6);
    }
}
