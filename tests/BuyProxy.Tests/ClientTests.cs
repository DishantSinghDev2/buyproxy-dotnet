using System.Net;
using System.Text;
using BuyProxy;
using Xunit;

namespace BuyProxy.Tests;

/// <summary>Replays queued responses and records every request.</summary>
sealed class FakeHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] queue) : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _queue = new(queue);
    public List<(HttpMethod Method, string Url, string? Body, HttpRequestMessage Request)> Seen { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Seen.Add((request.Method, request.RequestUri!.AbsoluteUri, body, request));
        return _queue.Dequeue()(request);
    }

    public static Func<HttpRequestMessage, HttpResponseMessage> Json(int status, string json, string? retryAfter = null) => _ =>
    {
        var r = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (retryAfter is not null) r.Headers.Add("Retry-After", retryAfter);
        return r;
    };
}

public class ProxyEndpointTests
{
    [Fact]
    public void ParsesKeepingAtAndColonInPassword()
    {
        var line = "user-country-us:p@ss:w0rd@gw.buyproxy.org:6969";
        var p = ProxyEndpoint.Parse(line);
        Assert.Equal(("gw.buyproxy.org", 6969, "user-country-us", "p@ss:w0rd"), (p.Host, p.Port, p.Username, p.Password));
        Assert.Equal(line, p.ToString());
        Assert.Equal("http://user-country-us:p%40ss%3Aw0rd@gw.buyproxy.org:6969", p.Url);
        Assert.Equal("socks5://h.example:1080", ProxyEndpoint.Parse("u:p@h.example:1080", "socks5").Server);
    }

    [Theory]
    [InlineData("")] [InlineData("nope")] [InlineData("u:p@host")] [InlineData("u:p@host:99999")] [InlineData(":p@host:80")] [InlineData("u@host:80")]
    public void RejectsMalformedLines(string bad) => Assert.Throws<FormatException>(() => ProxyEndpoint.Parse(bad));

    [Fact]
    public void WebProxyCarriesCredentials()
    {
        var wp = ProxyEndpoint.Parse("u:p@h.example:8080").ToWebProxy();
        var cred = wp.Credentials!.GetCredential(new Uri("http://h.example:8080"), "Basic")!;
        Assert.Equal(("u", "p"), (cred.UserName, cred.Password));
    }
}

public class ClientTests
{
    private const string Account = """{"email":"a@b.co","name":"A","plan":"pro","planExpiresAt":null,"balanceGb":4.2,"createdAt":"t","brandNew":1}""";

    private static BuyProxyClient Client(FakeHandler h, string key = "bp_key_x") =>
        new(new BuyProxyOptions { ApiKey = key, HttpClient = new HttpClient(h), MaxRetries = 2 });

    [Fact]
    public async Task SendsKeyBodyAndUserAgentAndParses()
    {
        var h = new FakeHandler(FakeHandler.Json(200, """{"lines":["u:p@gw.buyproxy.org:6969"]}"""));
        var r = await Client(h).Plans.GenerateAsync("plan 1", new GenerateParams { Protocol = "socks5", Count = 1 });

        var (_, url, body, req) = h.Seen[0];
        Assert.Equal("https://www.buyproxy.org/v1/plans/plan%201/generate", url);
        Assert.Equal("bp_key_x", req.Headers.GetValues("X-API-Key").Single());
        Assert.StartsWith("buyproxy-dotnet/", req.Headers.UserAgent.ToString());
        Assert.Equal("""{"protocol":"socks5","count":1}""", body);
        Assert.Equal("socks5", r.Proxies[0].Protocol);
    }

    [Fact]
    public async Task ModelsAreTypedAndIgnoreUnknownFields()
    {
        var a = await Client(new FakeHandler(FakeHandler.Json(200, Account))).Account.GetAsync();
        Assert.Equal(4.2, a.BalanceGb);
        Assert.Null(a.PlanExpiresAt);
    }

    [Fact]
    public async Task StatusesMapToExceptions()
    {
        foreach (var (status, type) in new (int, Type)[] { (401, typeof(AuthenticationException)), (402, typeof(InsufficientBandwidthException)), (404, typeof(NotFoundException)) })
        {
            var ex = await Assert.ThrowsAsync(type, () => Client(new FakeHandler(FakeHandler.Json(status, """{"error":"nope"}"""))).Account.GetAsync());
            Assert.Equal(status, ((BuyProxyException)ex).Status);
            Assert.Contains("nope", ex.Message);
        }
    }

    [Fact]
    public async Task Retries429ThenSucceeds()
    {
        var h = new FakeHandler(FakeHandler.Json(429, """{"error":"slow"}""", "0"), FakeHandler.Json(200, Account));
        Assert.Equal("pro", (await Client(h).Account.GetAsync()).Plan);
        Assert.Equal(2, h.Seen.Count);
    }

    [Fact]
    public async Task RateLimitExceptionAfterRetries()
    {
        var h = new FakeHandler(Enumerable.Repeat(FakeHandler.Json(429, """{"error":"slow"}""", "0"), 3).ToArray());
        await Assert.ThrowsAsync<RateLimitException>(() => Client(h).Account.GetAsync());
        Assert.Equal(3, h.Seen.Count);
    }

    [Fact]
    public async Task PostNotRetriedOn503UnlessIdempotent()
    {
        var h = new FakeHandler(FakeHandler.Json(503, """{"error":"down"}"""));
        await Assert.ThrowsAsync<ServerException>(() => Client(h).Reseller.Customers.AllocateAsync("c", "premium", 1));
        Assert.Single(h.Seen);

        var keyed = new FakeHandler(FakeHandler.Json(503, """{"error":"down"}"""),
            FakeHandler.Json(201, """{"productKey":"premium","productId":"x","gb":1,"alreadyApplied":false,"poolRemainingGb":0}"""));
        var a = await Client(keyed).Reseller.Customers.AllocateAsync("c", "premium", 1, "k1");
        Assert.False(a.AlreadyApplied);
        Assert.Equal("k1", keyed.Seen[1].Request.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal("""{"productKey":"premium","gb":1}""", keyed.Seen[1].Body);
    }

    [Fact]
    public async Task MissingKeyNeverHitsNetworkButCatalogIsPublic()
    {
        Environment.SetEnvironmentVariable("BUYPROXY_API_KEY", null);
        var h = new FakeHandler(FakeHandler.Json(200, """{"products":[],"volumeBonus":{"thresholdGb":100,"rate":0.1},"plans":[]}"""));
        var bp = new BuyProxyClient(new BuyProxyOptions { ApiKey = "", HttpClient = new HttpClient(h) });
        await Assert.ThrowsAsync<AuthenticationException>(() => bp.Account.GetAsync());
        Assert.Equal(100, (await bp.Products.ListAsync()).VolumeBonus.ThresholdGb);
        Assert.Single(h.Seen);
    }

    [Fact]
    public async Task WaitPollsUntilSettled()
    {
        static string Tx(string s) => $$"""{"id":"st_1","kind":"topup","status":"{{s}}","paymentMethod":"card","interval":null,"amountCents":1,"currency":"usd","gb":1,"productKey":null,"plan":null,"resellerPool":false,"createdAt":"t"}""";
        var h = new FakeHandler(FakeHandler.Json(200, Tx("pending")), FakeHandler.Json(200, Tx("succeeded")));
        Assert.Equal("succeeded", (await Client(h).Transactions.WaitAsync("st_1", TimeSpan.FromMilliseconds(5))).Status);
    }

    [Fact]
    public void KeyKind()
    {
        Assert.Equal("reseller", new BuyProxyClient("bp_live_x").KeyKind);
        Assert.Equal("personal", new BuyProxyClient("bp_key_x").KeyKind);
    }
}
