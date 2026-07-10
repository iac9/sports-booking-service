using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace SportsBookingService.Api.Auth;

/// <summary>
/// Clerk exposes a JWKS document at {FrontendApiUrl}/.well-known/jwks.json but does not
/// document OIDC discovery (/.well-known/openid-configuration), so this retriever fetches
/// the JWKS directly and wraps it in an OpenIdConnectConfiguration for JwtBearer's
/// ConfigurationManager to consume (with its usual caching/refresh behavior).
/// </summary>
public class ClerkJwksConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var jwksJson = await retriever.GetDocumentAsync(address, cancel);
        var keySet = new JsonWebKeySet(jwksJson);

        var configuration = new OpenIdConnectConfiguration();
        foreach (var key in keySet.GetSigningKeys())
        {
            configuration.SigningKeys.Add(key);
        }

        return configuration;
    }
}
