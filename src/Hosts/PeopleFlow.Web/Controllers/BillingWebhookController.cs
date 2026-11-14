using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Subscription.Core;

namespace PeopleFlow.Web.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/webhooks/billing")]
public class BillingWebhookController : ControllerBase
{
    private const int MaxWebhookBodyBytes = 262_144;
    private const int MaxSignatureHeaderLength = 512;

    private readonly IBillingWebhookService _webhookService;
    private readonly BillingOptions? _billingOptions;

    public BillingWebhookController(
        IBillingWebhookService webhookService,
        IOptions<BillingOptions>? billingOptions = null)
    {
        _webhookService = webhookService ?? throw new ArgumentNullException(nameof(webhookService));
        _billingOptions = billingOptions?.Value;
    }

    [HttpPost("{provider}")]
    [RequestSizeLimit(MaxWebhookBodyBytes)]
    public async Task<IActionResult> Receive(string provider, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "WEBHOOK_INVALID_REQUEST",
                Detail = "Webhook provider is required."
            });
        }

        var sigHeader = Request.Headers["X-Billing-Signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sigHeader))
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "WEBHOOK_SIGNATURE_MISSING",
                Detail = "Missing X-Billing-Signature header."
            });
        }

        if (!HasValidSignatureFormat(sigHeader))
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "WEBHOOK_SIGNATURE_INVALID",
                Detail = "Malformed X-Billing-Signature header."
            });
        }

        if (_billingOptions != null &&
            (!_billingOptions.WebhookSecrets.TryGetValue(provider.Trim(), out var configuredSecret) ||
             string.IsNullOrWhiteSpace(configuredSecret)))
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "WEBHOOK_PROVIDER_UNCONFIGURED",
                Detail = $"Webhook secret is not configured for provider '{provider}'."
            });
        }

        if (Request.ContentLength > MaxWebhookBodyBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "WEBHOOK_PAYLOAD_TOO_LARGE",
                Detail = "Webhook request payload exceeds the 256 KB limit."
            });
        }

        var (exceeded, rawBody) = await ReadBoundedUtf8BodyAsync(Request.Body, MaxWebhookBodyBytes, cancellationToken);
        if (exceeded)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "WEBHOOK_PAYLOAD_TOO_LARGE",
                Detail = "Webhook request payload exceeds the 256 KB limit."
            });
        }

        var result = await _webhookService.ProcessWebhookAsync(provider, rawBody, sigHeader, cancellationToken);
        if (result.IsFailure)
        {
            var statusCode = result.Error.Code switch
            {
                "WEBHOOK_SIGNATURE_MISSING" or "WEBHOOK_SIGNATURE_INVALID" or "WEBHOOK_TIMESTAMP_EXPIRED" or "WEBHOOK_PROVIDER_UNCONFIGURED"
                    => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status400BadRequest
            };

            return StatusCode(statusCode, new ProblemDetails
            {
                Status = statusCode,
                Title = result.Error.Code,
                Detail = result.Error.Message
            });
        }

        return Ok(new
        {
            eventId = result.Value.EventRecordId,
            status = result.Value.Status.ToString(),
            message = result.Value.Message
        });
    }

    private static bool HasValidSignatureFormat(string signatureHeader)
    {
        if (signatureHeader.Length > MaxSignatureHeaderLength)
        {
            return false;
        }

        var hasTimestamp = false;
        var hasSig = false;
        foreach (var part in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("t=", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(part[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var ts) && ts > 0)
            {
                hasTimestamp = true;
            }
            else if (part.StartsWith("v1=", StringComparison.OrdinalIgnoreCase) && part.Length > 3)
            {
                var hex = part[3..];
                hasSig = hex.Length % 2 == 0 && hex.All(Uri.IsHexDigit);
            }
        }
        return hasTimestamp && hasSig;
    }

    private static async Task<(bool Exceeded, string Body)> ReadBoundedUtf8BodyAsync(
        Stream body,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var totalRead = 0;
        int read;
        while ((read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            totalRead += read;
            if (totalRead > maxBytes)
            {
                return (true, string.Empty);
            }
            ms.Write(buffer, 0, read);
        }

        return (false, Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
    }
}
