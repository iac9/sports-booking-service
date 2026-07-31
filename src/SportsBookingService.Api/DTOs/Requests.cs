namespace SportsBookingService.Api.DTOs;

public record CreateGroupRequest(string Name, string CreatorName);
public record RenameGroupRequest(string Name);
public record TransferAdminRequest(Guid MemberId);
public record JoinGroupRequest(string Name);
public record JoinGroupByCodeRequest(string Code, string MemberName);
public record SetPrimaryLocationRequest(Guid? LocationId);

public record CreateCourtBookingRequest(string Court, string StartTime, string EndTime);
public record CreateLocationOptionRequest(string LocationName);
public record UpdateLocationOptionRequest(Guid? Id, string LocationName);

public record CreateEventRequest(
    string Title,
    string Date,
    Guid? LocationId,
    int MaxAttendees,
    string Status,
    List<CreateCourtBookingRequest> CourtBookings,
    List<CreateLocationOptionRequest>? LocationOptions
);

public record UpdateEventRequest(
    string? Title,
    string? Date,
    Guid? LocationId,
    int? MaxAttendees,
    List<CreateCourtBookingRequest>? CourtBookings,
    List<UpdateLocationOptionRequest>? LocationOptions
);

public record AddExpenseRequest(string Description, decimal Amount, Guid PaidByMemberId);
public record CreateLocationRequest(string Name, string Address);
public record UpdateLocationRequest(string? Name, string? Address);
