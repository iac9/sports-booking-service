namespace SportsBookingService.Api.DTOs;

public record MemberResponse(Guid Id, string Name);

public record GroupResponse(
    Guid Id,
    string Name,
    string Code,
    List<MemberResponse> Members,
    List<EventDetailResponse> Events,
    List<LocationResponse> Locations,
    Guid? PrimaryLocationId
);

public record LocationResponse(Guid Id, string Name, string Address);

public record CourtBookingResponse(Guid Id, string Court, string StartTime, string EndTime);

public record LocationOptionResponse(Guid Id, string LocationName, List<MemberResponse> Votes);

public record ExpenseResponse(
    Guid Id,
    string Description,
    decimal Amount,
    MemberResponse PaidBy,
    string CreatedAt
);

public record BalanceResponse(MemberResponse Member, decimal Balance);

public record CostBreakdownResponse(
    decimal TotalCost,
    decimal CostPerAttendee,
    List<BalanceResponse> Balances
);

public record EventSummaryResponse(
    Guid Id,
    string Title,
    string Date,
    string Status,
    int AttendeeCount,
    int MaxAttendees,
    string? LocationName
);

public record EventDetailResponse(
    Guid Id,
    string Title,
    string Date,
    List<CourtBookingResponse> CourtBookings,
    Guid? LocationId,
    string? LocationName,
    int MaxAttendees,
    List<MemberResponse> Attendees,
    List<MemberResponse> Waitlist,
    List<ExpenseResponse> Expenses,
    MemberResponse CreatedBy,
    string Status,
    List<LocationOptionResponse>? LocationOptions,
    CostBreakdownResponse CostBreakdown
);
