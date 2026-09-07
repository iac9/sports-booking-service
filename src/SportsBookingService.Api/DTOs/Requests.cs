namespace SportsBookingService.Api.DTOs;

public record CreateGroupRequest(string Name, string CreatorName);
public record RenameGroupRequest(string Name);
public record TransferAdminRequest(Guid MemberId);
public record JoinGroupByCodeRequest(string Code, string MemberName);
public record SetPrimaryLocationRequest(Guid? LocationId);

public record CreateCourtBookingRequest(string Court, string StartTime, string EndTime);

public record CreateEventRequest(
    string Title,
    string Date,
    Guid? LocationId,
    int? MaxAttendees,
    List<CreateCourtBookingRequest> CourtBookings
);

public record UpdateEventRequest(
    string? Title,
    string? Date,
    Guid? LocationId,
    int? MaxAttendees,
    List<CreateCourtBookingRequest>? CourtBookings
);

public record AddExpenseRequest(string Description, decimal Amount, Guid PaidByMemberId);
public record UpdateExpenseRequest(string? Description, decimal? Amount, Guid? PaidByMemberId);
public record AddGuestRequest(string? Name);
public record SettleDebtRequest(Guid EventId, Guid FromMemberId, decimal Amount);
public record CreateLocationRequest(string Name, string Address);
public record UpdateLocationRequest(string? Name, string? Address);
