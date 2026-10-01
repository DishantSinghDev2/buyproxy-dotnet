using System.Net;

namespace BuyProxy;

/// <summary>Base class for every exception thrown by the SDK.</summary>
public class BuyProxyException : Exception
{
    /// <summary>HTTP status, or null when the request never got a response.</summary>
    public int? Status { get; }

    /// <summary>Raw response body, when there was one.</summary>
    public string? Body { get; }

    public BuyProxyException(string message, int? status = null, string? body = null, Exception? inner = null)
        : base(status is null ? message : $"{message} (HTTP {status})", inner)
    {
        Status = status;
        Body = body;
    }

    internal static BuyProxyException FromResponse(int status, string? body, string? errorText, double? retryAfter)
    {
        var message = errorText ?? "BuyProxy API error";
        return status switch
        {
            400 => new ValidationException(message, status, body),
            401 => new AuthenticationException(message, status, body),
            402 => new InsufficientBandwidthException(message, status, body),
            403 => new PermissionDeniedException(message, status, body),
            404 => new NotFoundException(message, status, body),
            429 => new RateLimitException(message, body, retryAfter),
            >= 500 => new ServerException(message, status, body),
            _ => new BuyProxyException(message, status, body),
        };
    }
}

/// <summary>400 — a parameter was missing or invalid.</summary>
public sealed class ValidationException(string message, int status, string? body) : BuyProxyException(message, status, body);

/// <summary>401 — the API key is missing or invalid.</summary>
public sealed class AuthenticationException(string message, int status, string? body) : BuyProxyException(message, status, body);

/// <summary>402 — empty reseller pool, or a plan with no GB left.</summary>
public sealed class InsufficientBandwidthException(string message, int status, string? body) : BuyProxyException(message, status, body);

/// <summary>403 — suspended account or customer, or the plan doesn't include this endpoint.</summary>
public sealed class PermissionDeniedException(string message, int status, string? body) : BuyProxyException(message, status, body);

/// <summary>404 — the plan, customer or transaction doesn't exist on this account.</summary>
public sealed class NotFoundException(string message, int status, string? body) : BuyProxyException(message, status, body);

/// <summary>5xx — BuyProxy or an upstream provider is unavailable; usually safe to retry.</summary>
public sealed class ServerException(string message, int status, string? body) : BuyProxyException(message, status, body);

/// <summary>429 — more than 60 requests/minute on this key.</summary>
public sealed class RateLimitException(string message, string? body, double? retryAfterSeconds) : BuyProxyException(message, 429, body)
{
    /// <summary>Seconds the API asked you to wait, when it said.</summary>
    public double? RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>The request never got a response (DNS, reset, timeout, offline).</summary>
public sealed class ConnectionException(string message, Exception inner) : BuyProxyException(message, null, null, inner);
