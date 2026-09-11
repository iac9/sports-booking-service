using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBookingService.Api.Auth;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Controllers;

/// <summary>
/// The one controller outside the global Clerk JWT requirement: mail providers can't hold a
/// user session. <c>[AllowAnonymous]</c> only lifts the JWT requirement — the Basic Auth
/// filter below still gates every request.
/// </summary>
[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
[ServiceFilter(typeof(InboundEmailWebhookAuthFilter))]
public class WebhooksController(EmailIngestionService ingestionService, ILogger<WebhooksController> logger) : ControllerBase
{
    [HttpPost("email")]
    public async Task<IActionResult> InboundEmail(CloudMailinInboundWebhookPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            await ingestionService.IngestAsync(payload, cancellationToken);
        }
        catch (Exception ex)
        {
            // CloudMailin reads any non-2xx as "retry this email later", which would just
            // replay the same failure. Log it and accept — a genuinely broken ingest surfaces
            // as a Failed row in the review queue, not as a bounced email.
            logger.LogError(ex, "Failed to ingest inbound email");
        }

        return Ok();
    }
}
