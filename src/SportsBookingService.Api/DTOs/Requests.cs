using System.ComponentModel.DataAnnotations;

namespace SportsBookingService.Api.DTOs;

public record CreateGroupRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required, StringLength(100, MinimumLength = 1)] string CreatorName
);

public record RenameGroupRequest([Required, StringLength(100, MinimumLength = 1)] string Name);

public record TransferAdminRequest(Guid MemberId);

public record JoinGroupByCodeRequest(
    [Required, StringLength(6, MinimumLength = 6)] string Code,
    [Required, StringLength(100, MinimumLength = 1)] string MemberName
);

public record SetPrimaryLocationRequest(Guid? LocationId);

public record CreateCourtBookingRequest(
    [Required, StringLength(50, MinimumLength = 1)] string Court,
    [Required, StringLength(20, MinimumLength = 1)] string StartTime,
    [Required, StringLength(20, MinimumLength = 1)] string EndTime
);

public record CreateEventRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [Required, RegularExpression(@"^\d{4}-\d{2}-\d{2}$")] string Date,
    Guid? LocationId,
    [Range(1, 1000)] int? MaxAttendees,
    List<CreateCourtBookingRequest> CourtBookings
);

public record UpdateEventRequest(
    [StringLength(200, MinimumLength = 1)] string? Title,
    [RegularExpression(@"^\d{4}-\d{2}-\d{2}$")] string? Date,
    Guid? LocationId,
    [Range(1, 1000)] int? MaxAttendees,
    List<CreateCourtBookingRequest>? CourtBookings
);

public record AddExpenseRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Description,
    [Range(0.01, 100000)] decimal Amount,
    Guid PaidByMemberId
);

public record UpdateExpenseRequest(
    [StringLength(200, MinimumLength = 1)] string? Description,
    [Range(0.01, 100000)] decimal? Amount,
    Guid? PaidByMemberId
);

public record AddGuestRequest([StringLength(100, MinimumLength = 1)] string? Name);

public record SettleDebtRequest(Guid EventId, Guid FromMemberId);

public record CreateLocationRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required, StringLength(300, MinimumLength = 1)] string Address,
    [RegularExpression(@"^https?://\S+$", ErrorMessage = "Website URL must start with http:// or https://"), StringLength(2048)] string? WebsiteUrl
);

public record UpdateLocationRequest(
    [StringLength(100, MinimumLength = 1)] string? Name,
    [StringLength(300, MinimumLength = 1)] string? Address,
    [RegularExpression(@"^https?://\S+$", ErrorMessage = "Website URL must start with http:// or https://"), StringLength(2048)] string? WebsiteUrl
);

// ── Email ingestion ───────────────────────────────────────────────────────────

/// <summary>
/// CloudMailin's JSON POST format. Only the fields we actually read are modelled;
/// CloudMailin sends more (attachments, spam scores, reply_plain) which is ignored.
/// See https://docs.cloudmailin.com/http_post_formats/json/
/// </summary>
public record CloudMailinInboundWebhookPayload(
    CloudMailinEnvelope? Envelope,
    CloudMailinHeaders? Headers,
    string? Plain,
    string? Html
);

public record CloudMailinEnvelope(string? To, string? From, List<string>? Recipients);

public record CloudMailinHeaders(string? From, string? Subject);

public record ConfirmPendingBookingRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [Required, RegularExpression(@"^\d{4}-\d{2}-\d{2}$")] string Date,
    Guid? LocationId,
    [Range(1, 1000)] int? MaxAttendees,
    List<CreateCourtBookingRequest> CourtBookings,
    [Range(0.01, 100000)] decimal? ExpenseAmount,
    [StringLength(200, MinimumLength = 1)] string? ExpenseDescription
);

public record SetBookingAutomationRequest(
    bool AutoAcceptBookings,
    [Range(0.01, 100000)] decimal? AutoAcceptMaxCost
);
