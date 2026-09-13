namespace SportsBookingService.Api.DTOs;

public record MemberResponse(Guid Id, string Name, bool IsGuest = false, Guid? AddedByMemberId = null);

public record GroupResponse(
    Guid Id,
    string Name,
    string Code,
    List<MemberResponse> Members,
    List<EventDetailResponse> Events,
    List<LocationResponse> Locations,
    Guid? PrimaryLocationId,
    Guid? CurrentMemberId,
    Guid? AdminMemberId,
    bool AutoAcceptBookings = false,
    decimal? AutoAcceptMaxCost = null
);

public record GroupSummaryResponse(
    Guid Id,
    string Name,
    string Code,
    int MemberCount
);

public record LocationResponse(Guid Id, string Name, string Address, string? WebsiteUrl);

public record CourtBookingResponse(Guid Id, string Court, string StartTime, string EndTime);

public record ExpenseResponse(
    Guid Id,
    string Description,
    decimal Amount,
    MemberResponse PaidBy,
    string CreatedAt
);

public record BalanceResponse(MemberResponse Member, decimal Balance);

public record DebtResponse(
    Guid EventId,
    string EventTitle,
    string Date,
    MemberResponse FromMember,
    decimal Amount,
    bool Settled,
    string? SettledAt
);

public record CostBreakdownResponse(
    decimal TotalCost,
    decimal CostPerAttendee,
    List<BalanceResponse> Balances
);

public record EventSummaryResponse(
    Guid Id,
    string Title,
    string Date,
    int AttendeeCount,
    int? MaxAttendees,
    string? LocationName
);

public record EventDetailResponse(
    Guid Id,
    string Title,
    string Date,
    List<CourtBookingResponse> CourtBookings,
    Guid? LocationId,
    string? LocationName,
    int? MaxAttendees,
    List<MemberResponse> Attendees,
    List<MemberResponse> Waitlist,
    List<ExpenseResponse> Expenses,
    MemberResponse CreatedBy,
    CostBreakdownResponse CostBreakdown
);

public record PendingBookingResponse(
    Guid Id,
    string Status,
    string RawEmailFrom,
    string RawEmailSubject,
    string RawEmailText,
    string ReceivedAt,
    string? ProviderHint,
    string? ExtractedVenueName,
    string? ExtractedDate,
    string? ExtractedStartTime,
    string? ExtractedEndTime,
    decimal? ExtractedCost,
    string? ExtractedCurrency,
    string? ExtractedBookedByName,
    double? ExtractionConfidence,
    string? ExtractionError,
    Guid? MatchedLocationId,
    Guid? MatchedEventId,
    Guid? MatchedPayerMemberId,
    Guid? CreatedEventId,
    Guid? ReviewedByMemberId,
    string? ReviewedAt,
    bool AutoAccepted
);

public record InboundEmailAddressResponse(string Address);
