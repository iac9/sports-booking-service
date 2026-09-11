using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SportsBookingService.Api.Auth;

/// <summary>
/// HTTP Basic Auth for the inbound-email webhook — the one endpoint outside the global Clerk
/// JWT requirement. Nothing here is provider-specific: it just validates a standard
/// <c>Authorization: Basic</c> header. CloudMailin sets that header explicitly per address
/// (it dropped support for credentials embedded in the target URL).
/// </summary>
public class InboundEmailWebhookAuthFilter(IConfiguration configuration, ILogger<InboundEmailWebhookAuthFilter> logger)
    : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var expectedUsername = configuration["Email:WebhookUsername"];
        var expectedPassword = configuration["Email:WebhookPassword"];

        if (string.IsNullOrEmpty(expectedUsername) || string.IsNullOrEmpty(expectedPassword))
        {
            // Fail closed: an unconfigured webhook must not become an open endpoint.
            logger.LogError("Inbound email webhook credentials are not configured; rejecting request.");
            context.Result = new UnauthorizedResult();
            return Task.CompletedTask;
        }

        if (!TryReadBasicCredentials(context.HttpContext.Request.Headers.Authorization, out var username, out var password))
        {
            logger.LogWarning("Inbound email webhook rejected: missing or malformed Basic credentials.");
            context.Result = new UnauthorizedResult();
            return Task.CompletedTask;
        }

        // Both halves are always compared, and compared in fixed time, so neither the response
        // timing nor the short-circuit order leaks which half was wrong.
        var usernameOk = FixedTimeEquals(username, expectedUsername);
        var passwordOk = FixedTimeEquals(password, expectedPassword);

        if (!usernameOk || !passwordOk)
        {
            logger.LogWarning("Inbound email webhook rejected: invalid credentials.");
            context.Result = new UnauthorizedResult();
        }

        return Task.CompletedTask;
    }

    private static bool TryReadBasicCredentials(string? header, out string username, out string password)
    {
        username = "";
        password = "";

        if (string.IsNullOrWhiteSpace(header)) return false;
        if (!AuthenticationHeaderValue.TryParse(header, out var parsed)) return false;
        if (!string.Equals(parsed.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(parsed.Parameter)) return false;

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parsed.Parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = decoded.IndexOf(':');
        if (separator == -1) return false;

        username = decoded[..separator];
        password = decoded[(separator + 1)..];
        return true;
    }

    private static bool FixedTimeEquals(string actual, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(expected));
}
