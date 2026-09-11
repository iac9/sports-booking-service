using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SportsBookingService.Api.Auth;

namespace SportsBookingService.Api.Tests;

public class InboundEmailWebhookAuthFilterTests
{
    private const string Username = "cloudmailin";
    private const string Password = "s3cret";

    private static readonly IConfiguration ConfiguredCredentials = TestServices.ConfigurationWith(
        ("Email:WebhookUsername", Username),
        ("Email:WebhookPassword", Password));

    private static async Task<IActionResult?> RunAsync(IConfiguration configuration, string? authorizationHeader)
    {
        var httpContext = new DefaultHttpContext();
        if (authorizationHeader is not null) httpContext.Request.Headers.Authorization = authorizationHeader;

        var context = new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            []);

        await new InboundEmailWebhookAuthFilter(configuration, NullLogger<InboundEmailWebhookAuthFilter>.Instance)
            .OnAuthorizationAsync(context);

        return context.Result;
    }

    private static string Basic(string username, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

    [Fact]
    public async Task Allows_a_request_with_the_configured_credentials()
    {
        Assert.Null(await RunAsync(ConfiguredCredentials, Basic(Username, Password)));
    }

    [Fact]
    public async Task Allows_a_password_that_contains_a_colon()
    {
        var configuration = TestServices.ConfigurationWith(
            ("Email:WebhookUsername", Username),
            ("Email:WebhookPassword", "pass:with:colons"));

        Assert.Null(await RunAsync(configuration, Basic(Username, "pass:with:colons")));
    }

    [Theory]
    [InlineData(null)]                        // no header at all
    [InlineData("")]
    [InlineData("Bearer sometoken")]          // wrong scheme
    [InlineData("Basic")]                     // scheme with no credentials
    [InlineData("Basic not-valid-base64!!")]
    public async Task Rejects_a_missing_or_malformed_header(string? header)
    {
        Assert.IsType<UnauthorizedResult>(await RunAsync(ConfiguredCredentials, header));
    }

    [Fact]
    public async Task Rejects_credentials_with_no_colon_separator()
    {
        var header = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("nocolonhere"));
        Assert.IsType<UnauthorizedResult>(await RunAsync(ConfiguredCredentials, header));
    }

    [Theory]
    [InlineData("wronguser", Password)]
    [InlineData(Username, "wrongpassword")]
    [InlineData("wronguser", "wrongpassword")]
    [InlineData(Username, "")]
    public async Task Rejects_wrong_credentials(string username, string password)
    {
        Assert.IsType<UnauthorizedResult>(await RunAsync(ConfiguredCredentials, Basic(username, password)));
    }

    [Theory]
    [InlineData("Email:WebhookUsername")]
    [InlineData("Email:WebhookPassword")]
    public async Task Fails_closed_when_a_credential_is_not_configured(string configuredKey)
    {
        // Only one half set — an unconfigured webhook must never become an open endpoint.
        var configuration = TestServices.ConfigurationWith((configuredKey, "something"));

        Assert.IsType<UnauthorizedResult>(await RunAsync(configuration, Basic(Username, Password)));
    }

    [Fact]
    public async Task Fails_closed_when_nothing_is_configured()
    {
        Assert.IsType<UnauthorizedResult>(await RunAsync(TestServices.EmptyConfiguration, Basic(Username, Password)));
    }
}
