using SportsBookingService.Api.Models;

namespace SportsBookingService.Api.Services;

public record MemberMatch(Member Member, double Confidence);

/// <summary>
/// Matches a name read out of a booking email against a group's members, to work out who
/// paid. Pure — no DB access — so the rules can be unit-tested directly.
/// </summary>
/// <remarks>
/// Deliberately stricter than <see cref="LocationMatcher"/>: where two candidates match
/// equally well this returns null instead of picking one. A wrong venue guess only
/// mis-preselects a dropdown someone is about to check, but a wrong payer guess bills the
/// wrong person and feeds the debt calculations, so ambiguity has to mean "don't know".
/// </remarks>
public static class MemberMatcher
{
    public static MemberMatch? Match(string? bookedByName, IEnumerable<Member> members)
    {
        var needle = NameMatcher.Normalize(bookedByName);
        if (needle.Length == 0) return null;

        var candidates = members
            // A guest is a placeholder added by another member, never an account that can pay.
            .Where(m => !m.IsGuest)
            .Select(m => (Member: m, Name: NameMatcher.Normalize(m.Name)))
            .Where(c => c.Name.Length > 0)
            .ToList();

        var exact = candidates.Where(c => c.Name == needle).ToList();
        if (exact.Count == 1) return new MemberMatch(exact[0].Member, 1.0);
        if (exact.Count > 1) return null;

        // "Andrew" in the email should still find "Andrew Christabel", and a full name in the
        // email should find a member saved as just "Andrew".
        var partial = candidates
            .Where(c => c.Name.Contains(needle, StringComparison.Ordinal) || needle.Contains(c.Name, StringComparison.Ordinal))
            .ToList();

        return partial.Count == 1 ? new MemberMatch(partial[0].Member, 0.6) : null;
    }
}
