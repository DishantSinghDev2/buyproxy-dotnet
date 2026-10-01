# BuyProxy .NET SDK — Residential Proxy API Client for C#

[![NuGet](https://img.shields.io/nuget/v/BuyProxy?logo=nuget&logoColor=white)](https://www.nuget.org/packages/BuyProxy)
[![downloads](https://img.shields.io/nuget/dt/BuyProxy)](https://www.nuget.org/packages/BuyProxy)
[![CI](https://github.com/DishantSinghDev2/buyproxy-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/DishantSinghDev2/buyproxy-dotnet/actions/workflows/ci.yml)
[![license](https://img.shields.io/badge/license-MIT-green)](LICENSE)

The official **.NET / C# SDK** for [BuyProxy](https://www.buyproxy.org): generate **residential, mobile, ISP and datacenter proxies**, check bandwidth, top up by card or crypto, and manage **reseller** customers. Async-first, nullable-annotated, no dependencies — works with **ASP.NET Core**, **Playwright for .NET** and plain `HttpClient`.

**Keywords:** residential proxies · rotating proxies · mobile proxies · ISP proxies · datacenter proxies · SOCKS5 · proxy API · web scraping · proxy reseller

## Features

- Async API with `CancellationToken` on every call
- Immutable `record` models with nullable reference types
- Automatic retries with back-off for rate limits and transient errors
- `ProxyEndpoint.CreateHttpClient()` returns an `HttpClient` already routed through the proxy

## Installation

```bash
dotnet add package BuyProxy
```

.NET 6 and later (`net6.0`, `net8.0`). No NuGet dependencies.

## Quick start

```csharp
using BuyProxy;

var bp = new BuyProxyClient(Environment.GetEnvironmentVariable("BUYPROXY_API_KEY")!); // or new BuyProxyClient() — reads BUYPROXY_API_KEY

var plan = (await bp.Plans.ListAsync())[0];
var result = await bp.Plans.GenerateAsync(plan.Id, new GenerateParams { Country = "US", City = "Austin", Count = 5 });

Console.WriteLine(result.Proxies[0].Url); // http://user-country-us-city-austin:pass@gw.buyproxy.org:6969
```

Set `StickyMinutes = 10` to keep the same IP for ten minutes, or `Protocol = "socks5"` for SOCKS5.

## Using the proxies with your stack

### HttpClient

```csharp
using var http = result.Proxies[0].CreateHttpClient();
Console.WriteLine(await http.GetStringAsync("https://api.ipify.org"));
```

### ASP.NET Core / dependency injection

```csharp
builder.Services.AddSingleton(_ => new BuyProxyClient(new BuyProxyOptions
{
    ApiKey = builder.Configuration["BuyProxy:ApiKey"],
}));
```

### Playwright for .NET

```csharp
var p = result.Proxies[0];
await using var browser = await playwright.Chromium.LaunchAsync(new()
{
    Proxy = new Microsoft.Playwright.Proxy { Server = p.Server, Username = p.Username, Password = p.Password },
});
```

### curl & shell

```csharp
foreach (var (k, v) in result.Proxies[0].Env()) Console.WriteLine($"export {k}='{v}'");
```

## Buy bandwidth (card & crypto)

```csharp
// Buy 10 GB of Core Residential by card — send the customer to PayUrl
var checkout = await bp.Topup.CardAsync(CardCheckoutParams.Topup("residential", 10));
Console.WriteLine(checkout.PayUrl);

// Or subscribe to a plan (monthly or yearly)
await bp.Topup.CardAsync(CardCheckoutParams.Plan("pro", "year"));

// Crypto (pay-as-you-go top-ups only)
await bp.Topup.CryptoAsync("residential", 50);

// Wait for the payment to settle — GB appears on bp.Balances.ListAsync() right after
var tx = await bp.Transactions.WaitAsync(checkout.TransactionId!);
Console.WriteLine(tx.Status); // "succeeded"
```

Card payments run through Stripe Checkout; crypto is available for pay-as-you-go top-ups.

## Reseller API

Build a proxy product on top of BuyProxy: buy bandwidth in bulk into a **reseller pool**, create isolated **customers**, and allocate GB to them. Needs a `bp_live_` key on a Business plan or higher.

```csharp
var reseller = new BuyProxyClient("bp_live_…");

await reseller.Reseller.Pool.TopupCardAsync("residential", 500);                 // buy into your pool
var customer = await reseller.Reseller.Customers.CreateAsync("Acme Inc");        // isolated sub-account

await reseller.Reseller.Customers.AllocateAsync(customer.Username, "residential", 20, idempotencyKey: "order-1042");

var detail = await reseller.Reseller.Customers.GetAsync(customer.Username);
var result = await reseller.Reseller.Customers.GenerateAsync(customer.Username, detail.Plans[0].Id, new GenerateParams { Country = "DE" });

await reseller.Reseller.Customers.UpdateAsync(customer.Username, status: "suspended");
```

## Error handling

```csharp
try
{
    await bp.Plans.GenerateAsync(planId);
}
catch (InsufficientBandwidthException)
{
    Console.WriteLine("Out of GB — top up first");
}
catch (RateLimitException e)
{
    Console.WriteLine($"Slow down; retry in {e.RetryAfterSeconds}s");
}
catch (AuthenticationException)
{
    Console.WriteLine("Check your API key");
}
```
Every exception derives from `BuyProxyException` and exposes `Status` and `Body`.

## Configuration

```csharp
new BuyProxyClient(new BuyProxyOptions
{
    ApiKey = "bp_key_…",                    // default: BUYPROXY_API_KEY
    BaseUrl = "https://www.buyproxy.org",
    Timeout = TimeSpan.FromSeconds(30),
    MaxRetries = 2,                          // 429s always; 5xx only for safe requests
    HttpClient = httpClientFromFactory,      // bring your own HttpClient
});
```

## API overview

| Capability | Call |
|---|---|
| Catalog & prices | `bp.Products.ListAsync()` |
| Account | `bp.Account.GetAsync()` |
| Spendable GB per product | `bp.Balances.ListAsync()` |
| Plans | `bp.Plans.ListAsync()` |
| Generate proxies | `bp.Plans.GenerateAsync(planId, params)` |
| Card checkout | `bp.Topup.CardAsync(params)` |
| Crypto checkout | `bp.Topup.CryptoAsync(product, gb)` |
| Payments | `bp.Transactions.ListAsync/GetAsync/WaitAsync` |
| Create an account | `bp.Auth.SignupAsync/VerifyOtpAsync/ResendOtpAsync` |
| Reseller summary | `bp.Reseller.MeAsync()` |
| Customers | `bp.Reseller.Customers.ListAsync/CreateAsync/GetAsync/UpdateAsync/UsageAsync/AllocateAsync/GenerateAsync` |
| Reseller pool | `bp.Reseller.Pool.GetAsync/TopupCardAsync/TopupCryptoAsync` |

Full endpoint reference: <https://www.buyproxy.org/docs/api-reference/introduction> · OpenAPI spec: <https://www.buyproxy.org/openapi.yaml>

## FAQ

**How do I get an API key?**
Create a free account at [buyproxy.org](https://www.buyproxy.org/signup), then open **Dashboard → API keys**. Personal keys start with `bp_key_`; reseller keys (Business plan and above) start with `bp_live_`. You can create the *account* from code (`signup` + `verify otp`), but keys are issued from the dashboard.

**What's the difference between a personal and a reseller key?**
A personal key (`bp_key_…`) manages your own bandwidth: balances, plans, generating proxies, top-ups and payments. A reseller key (`bp_live_…`) manages *your customers*: create isolated sub-accounts, buy bandwidth into a pool, allocate it, suspend or reactivate, and generate proxies on their behalf.

**Which proxy types are available?**
Core Residential, Premium Residential, ISP Rotating, Mobile, Datacenter IPv4 and IPv6 — call the catalog endpoint for the live list and per-GB prices (from **$1.99/GB**).

**Do generated proxies expire, and when is bandwidth used?**
Generating a proxy is free and instant; bandwidth is only consumed when traffic flows through it. Lines are on-demand — generate fresh ones rather than storing them.

**What are the rate limits?**
60 requests per minute per API key. The SDK honours `Retry-After` and retries rate-limited requests automatically.

**Is it free to use the SDK?**
Yes — the SDK is MIT-licensed. You pay only for the proxy bandwidth you buy.

## Other BuyProxy SDKs

[Node.js](https://github.com/DishantSinghDev2/buyproxy-node) · [Python](https://github.com/DishantSinghDev2/buyproxy-python) · [Go](https://github.com/DishantSinghDev2/buyproxy-go) · [PHP](https://github.com/DishantSinghDev2/buyproxy-php) · [Ruby](https://github.com/DishantSinghDev2/buyproxy-ruby) · [Java](https://github.com/DishantSinghDev2/buyproxy-java) · [.NET](https://github.com/DishantSinghDev2/buyproxy-dotnet) · [Rust](https://github.com/DishantSinghDev2/buyproxy-rust) · [CLI](https://github.com/DishantSinghDev2/buyproxy-cli)

All SDKs are checked against the same request log, so behaviour is identical across languages — see [buyproxy-sdks](https://github.com/DishantSinghDev2/buyproxy-sdks).

## Development

```bash
dotnet build -c Release
dotnet test
```

Issues and pull requests are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). Security reports: [SECURITY.md](SECURITY.md).

## Links

- Website: <https://www.buyproxy.org>
- Documentation: <https://www.buyproxy.org/docs/sdks>
- Pricing: <https://www.buyproxy.org/pricing>
- Support: <support@buyproxy.org>

## License

[MIT](LICENSE) © BuyProxy
