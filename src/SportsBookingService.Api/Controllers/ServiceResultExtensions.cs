using Microsoft.AspNetCore.Mvc;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Controllers;

public static class ServiceResultExtensions
{
    public static ActionResult ToActionResult(this ControllerBase controller, ServiceError error)
    {
        if (error.Kind == ServiceErrorKind.Forbidden)
        {
            var logger = controller.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Authorization");
            var clerkUserId = controller.HttpContext.User.FindFirst("sub")?.Value ?? "unknown";
            logger.LogWarning("Authorization failure: Clerk user {ClerkUserId} forbidden on {Method} {Path}",
                clerkUserId, controller.Request.Method, controller.Request.Path);
        }

        return error.Kind switch
        {
            ServiceErrorKind.NotFound => error.Message is null ? controller.NotFound() : controller.NotFound(error.Message),
            ServiceErrorKind.Forbidden => controller.Forbid(),
            ServiceErrorKind.Conflict => error.Message is null ? controller.Conflict() : controller.Conflict(error.Message),
            ServiceErrorKind.BadRequest => error.Message is null ? controller.BadRequest() : controller.BadRequest(error.Message),
            _ => controller.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
