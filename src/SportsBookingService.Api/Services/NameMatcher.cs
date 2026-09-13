namespace SportsBookingService.Api.Services;

/// <summary>
/// Shared name normalisation for matching text extracted from an email against records the
/// group already has — venues (<see cref="LocationMatcher"/>) and people
/// (<see cref="MemberMatcher"/>). One implementation so the two can't drift apart.
/// </summary>
public static class NameMatcher
{
    /// <summary>
    /// Reduces a name to letters and digits only, lowercased, so casing, spacing and
    /// punctuation differences between an email and a saved record don't defeat a match
    /// ("Melbourne Sports-Centre." and "melbourne sports centre" normalise identically).
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
