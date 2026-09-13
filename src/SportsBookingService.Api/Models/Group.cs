namespace SportsBookingService.Api.Models;

public class Group
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";

    /// <summary>
    /// The plus-address suffix identifying this group on the app's single shared inbound
    /// mailbox (e.g. <c>inbox+{token}@cloudmailin.net</c>). This token is the only spam gate
    /// on the webhook, so it is longer and higher-entropy than the share-friendly join <see cref="Code"/>.
    /// </summary>
    public string? InboundEmailToken { get; set; }

    /// <summary>
    /// Opt-in: when set, a forwarded booking that satisfies every check in
    /// <c>EmailIngestionService</c> becomes a real <see cref="Event"/> with no human review.
    /// Off by default, and admin-only to change, since it alters behaviour for the whole group.
    /// </summary>
    public bool AutoAcceptBookings { get; set; }

    /// <summary>
    /// Ceiling on an auto-accepted booking's cost. Required for auto-accept to fire at all —
    /// a null here disables it even when <see cref="AutoAcceptBookings"/> is true, so a
    /// half-configured group fails closed rather than accepting any amount.
    /// </summary>
    public decimal? AutoAcceptMaxCost { get; set; }

    public Guid? PrimaryLocationId { get; set; }
    public Guid? AdminMemberId { get; set; }
    public Member? AdminMember { get; set; }
    public ICollection<Member> Members { get; set; } = [];
    public ICollection<Event> Events { get; set; } = [];
    public ICollection<Location> Locations { get; set; } = [];
    public ICollection<PendingBooking> PendingBookings { get; set; } = [];
}
