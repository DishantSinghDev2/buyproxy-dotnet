using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BuyProxy.Models;

namespace BuyProxy;

/// <summary>Options for <see cref="BuyProxyClient"/>.</summary>
public sealed class BuyProxyOptions
{
    /// <summary><c>bp_key_…</c> (personal) or <c>bp_live_…</c> (reseller). Defaults to the <c>BUYPROXY_API_KEY</c> environment variable.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Default <c>https://www.buyproxy.org</c>.</summary>
    public string BaseUrl { get; set; } = BuyProxyClient.DefaultBaseUrl;

    /// <summary>Per-request timeout. Default 30 s.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Retries for rate limits and transient failures. Default 2.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Bring your own <see cref="HttpClient"/> (e.g. from <c>IHttpClientFactory</c>).</summary>
    public HttpClient? HttpClient { get; set; }
}

/// <summary>
/// BuyProxy API client. Thread-safe; register one instance (or use <c>IHttpClientFactory</c>) and share it.
/// <code>
/// var bp = new BuyProxyClient(new BuyProxyOptions { ApiKey = Environment.GetEnvironmentVariable("BUYPROXY_API_KEY") });
/// var result = await bp.Plans.GenerateAsync(planId, new GenerateParams { Country = "US", Count = 5 });
/// using var http = result.Proxies[0].CreateHttpClient();
/// </code>
/// </summary>
public sealed class BuyProxyClient : IDisposable
{
    public const string Version = "1.0.0";
    public const string DefaultBaseUrl = "https://www.buyproxy.org";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string? _apiKey;
    private readonly string _baseUrl;
    private readonly TimeSpan _timeout;
    private readonly int _maxRetries;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public ProductsApi Products { get; }
    public AuthApi Auth { get; }
    public AccountApi Account { get; }
    public BalancesApi Balances { get; }
    public PlansApi Plans { get; }
    public TopupApi Topup { get; }
    public TransactionsApi Transactions { get; }
    public ResellerApi Reseller { get; }

    public BuyProxyClient(BuyProxyOptions? options = null)
    {
        options ??= new BuyProxyOptions();
        var key = options.ApiKey ?? Environment.GetEnvironmentVariable("BUYPROXY_API_KEY");
        _apiKey = string.IsNullOrEmpty(key) ? null : key;
        _baseUrl = options.BaseUrl.TrimEnd('/');
        _timeout = options.Timeout;
        _maxRetries = options.MaxRetries;
        _ownsHttp = options.HttpClient is null;
        _http = options.HttpClient ?? new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        Products = new ProductsApi(this);
        Auth = new AuthApi(this);
        Account = new AccountApi(this);
        Balances = new BalancesApi(this);
        Plans = new PlansApi(this);
        Topup = new TopupApi(this);
        Transactions = new TransactionsApi(this);
        Reseller = new ResellerApi(this);
    }

    public BuyProxyClient(string apiKey) : this(new BuyProxyOptions { ApiKey = apiKey }) { }

    /// <summary>"reseller" for bp_live_ keys, "personal" for bp_key_ keys, null without a key.</summary>
    public string? KeyKind => _apiKey is null ? null : _apiKey.StartsWith("bp_live_", StringComparison.Ordinal) ? "reseller" : "personal";

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    internal sealed record Req(
        HttpMethod Method,
        string Path,
        IReadOnlyDictionary<string, object?>? Query = null,
        IReadOnlyDictionary<string, object?>? Body = null,
        IReadOnlyDictionary<string, string>? Headers = null,
        bool Public = false,
        bool Retry5xx = false);

    internal async Task<JsonElement> SendAsync(Req r, CancellationToken ct)
    {
        if (!r.Public && _apiKey is null)
        {
            throw new AuthenticationException(
                "No API key — set BuyProxyOptions.ApiKey or BUYPROXY_API_KEY. Create one at https://www.buyproxy.org/dashboard/api-keys", 401, null);
        }

        var query = r.Query is null ? "" : string.Join("&", r.Query.Where(kv => kv.Value is not null)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture)!)}"));
        var url = _baseUrl + r.Path + (query.Length > 0 ? "?" + query : "");
        var payload = r.Body is null
            ? null
            : JsonSerializer.Serialize(r.Body.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value), Json);

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(r.Method, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd($"buyproxy-dotnet/{Version}");
            if (_apiKey is not null) request.Headers.Add("X-API-Key", _apiKey);
            if (r.Headers is not null) foreach (var (k, v) in r.Headers) request.Headers.Add(k, v);
            if (payload is not null) request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_timeout);

            HttpResponseMessage response;
            string text;
            try
            {
                response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
            {
                if (r.Retry5xx && attempt < _maxRetries)
                {
                    await Task.Delay(Backoff(attempt), ct).ConfigureAwait(false);
                    continue;
                }
                var timedOut = e is OperationCanceledException;
                throw new ConnectionException(timedOut ? $"Request timed out after {_timeout.TotalSeconds}s" : $"Could not reach BuyProxy: {e.Message}", e);
            }

            var status = (int)response.StatusCode;
            response.Dispose();

            if (status is >= 200 and < 300)
            {
                if (text.Length == 0) return default;
                try
                {
                    return JsonDocument.Parse(text).RootElement.Clone();
                }
                catch (JsonException e)
                {
                    throw new BuyProxyException($"BuyProxy returned a non-JSON response (HTTP {status})", status, text, e);
                }
            }

            double? retryAfter = null;
            if (response.Headers.TryGetValues("Retry-After", out var values) &&
                double.TryParse(values.First(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s >= 0)
            {
                retryAfter = Math.Min(s, 30);
            }

            TimeSpan? wait = null;
            if (attempt < _maxRetries)
            {
                if (status == 429) wait = retryAfter is { } ra ? TimeSpan.FromSeconds(ra) : Backoff(attempt);
                else if (r.Retry5xx && status is 502 or 503 or 504) wait = Backoff(attempt);
            }
            if (wait is { } w)
            {
                await Task.Delay(w, ct).ConfigureAwait(false);
                continue;
            }

            string? errorText = null;
            try
            {
                var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                    errorText = e.GetString();
            }
            catch (JsonException)
            {
                // keep the generic message
            }
            throw BuyProxyException.FromResponse(status, text, errorText, retryAfter);
        }
    }

    internal T Read<T>(JsonElement e) =>
        e.Deserialize<T>(Json) ?? throw new BuyProxyException("Unexpected empty response from BuyProxy");

    internal static string Seg(string s) => Uri.EscapeDataString(s);

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(500 * Math.Pow(2, attempt), 8000) * (0.75 + Random.Shared.NextDouble() * 0.5));
}
