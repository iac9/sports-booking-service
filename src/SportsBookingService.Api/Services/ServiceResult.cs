namespace SportsBookingService.Api.Services;

public enum ServiceErrorKind
{
    NotFound,
    Forbidden,
    Conflict,
    BadRequest,
}

public record ServiceError(ServiceErrorKind Kind, string? Message = null);

public class ServiceResult
{
    public ServiceError? Error { get; private init; }
    public bool IsSuccess => Error is null;

    public static ServiceResult Success() => new();
    public static ServiceResult Fail(ServiceErrorKind kind, string? message = null) => new() { Error = new ServiceError(kind, message) };
}

public class ServiceResult<T>
{
    public T? Value { get; private init; }
    public ServiceError? Error { get; private init; }
    public bool IsSuccess => Error is null;

    public static ServiceResult<T> Success(T value) => new() { Value = value };
    public static ServiceResult<T> Fail(ServiceErrorKind kind, string? message = null) => new() { Error = new ServiceError(kind, message) };
}
