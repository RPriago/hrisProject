using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PeopleFlow.Modules.Tenancy.Contracts;

namespace PeopleFlow.Web.Security;

public sealed class ApiClientAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiClient";
    private const string RateLimitedItemKey = "ApiClientRateLimited";

    private readonly IApiClientAuthenticator _authenticator;

    public ApiClientAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiClientAuthenticator authenticator)
        : base(options, logger, encoder)
    {
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!TryReadCredentials(out var clientId, out var secret))
        {
            return AuthenticateResult.NoResult();
        }

        var result = await _authenticator.AuthenticateAsync(clientId, secret, Context.RequestAborted);
        if (!result.IsSuccess)
        {
            if (result.Error.Code == "API_CLIENT_RATE_LIMITED")
            {
                Context.Items[RateLimitedItemKey] = true;
            }

            return AuthenticateResult.Fail(result.Error.Message);
        }

        var info = result.Value;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, info.ClientRecordId.ToString()),
            new("sub", info.ClientRecordId.ToString()),
            new("client_id", info.ClientId),
            new("tenant_id", info.TenantId.ToString()),
            new("active_tenant_id", info.TenantId.ToString()),
            new("auth_mode", "api_client")
        };

        foreach (var scope in info.AllowedScopes)
        {
            claims.Add(new Claim("api_scope", scope));
        }

        if (info.ScopedLegalEntityIds is not null)
        {
            foreach (var legalEntityId in info.ScopedLegalEntityIds)
            {
                claims.Add(new Claim("scoped_legal_entity_id", legalEntityId.ToString()));
            }
        }

        if (info.ScopedLocationIds is not null)
        {
            foreach (var locationId in info.ScopedLocationIds)
            {
                claims.Add(new Claim("scoped_location_id", locationId.ToString()));
            }
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var isRateLimited = Context.Items.ContainsKey(RateLimitedItemKey);
        var status = isRateLimited ? StatusCodes.Status429TooManyRequests : StatusCodes.Status401Unauthorized;
        var code = isRateLimited ? "API_CLIENT_RATE_LIMITED" : "INVALID_API_CLIENT";
        var detail = isRateLimited
            ? "API client per-minute rate limit exceeded."
            : "Valid API client credentials are required.";

        Response.StatusCode = status;
        Response.ContentType = "application/problem+json";
        await Response.WriteAsync(JsonSerializer.Serialize(new ProblemDetails
        {
            Status = status,
            Title = code,
            Detail = detail
        }));
    }

    private bool TryReadCredentials(out string clientId, out string secret)
    {
        clientId = string.Empty;
        secret = string.Empty;

        var raw = Request.Headers["X-API-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
        {
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (authHeader is not null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                raw = authHeader["Bearer ".Length..].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith("pfi_", StringComparison.Ordinal))
        {
            return false;
        }

        var sep = raw.IndexOf(':');
        if (sep <= 4 || sep >= raw.Length - 1)
        {
            return false;
        }

        clientId = raw[..sep].Trim();
        secret = raw[(sep + 1)..].Trim();
        return clientId.Length > 0 && secret.Length > 0;
    }
}
