using System.Text.Json;
using BuyProxy.Models;
using static BuyProxy.BuyProxyClient;

namespace BuyProxy;

/// <summary>The public catalog.</summary>
public sealed class ProductsApi
{
    private readonly BuyProxyClient _c;
    internal ProductsApi(BuyProxyClient c) => _c = c;

    /// <summary>Every product with its <c>ProductKey</c>, minimum GB and live price. No API key needed.</summary>
    public async Task<Catalog> ListAsync(CancellationToken ct = default) =>
        _c.Read<Catalog>(await _c.SendAsync(new Req(HttpMethod.Get, "/v1/products", Public: true, Retry5xx: true), ct));
}

/// <summary>Create an account without a browser: <c>SignupAsync</c>, then <c>VerifyOtpAsync</c> with the emailed code.</summary>
public sealed class AuthApi
{
    private readonly BuyProxyClient _c;
    internal AuthApi(BuyProxyClient c) => _c = c;

    public async Task<SignupResult> SignupAsync(string email, string password, string? name = null, CancellationToken ct = default) =>
        _c.Read<SignupResult>(await _c.SendAsync(new Req(HttpMethod.Post, "/api/auth/signup", Body: new Dictionary<string, object?> { ["email"] = email, ["password"] = password, ["name"] = name }, Public: true), ct));

    public async Task<VerifyResult> VerifyOtpAsync(string email, string code, CancellationToken ct = default) =>
        _c.Read<VerifyResult>(await _c.SendAsync(new Req(HttpMethod.Post, "/api/auth/verify-otp", Body: new Dictionary<string, object?> { ["email"] = email, ["code"] = code }, Public: true), ct));

    public async Task ResendOtpAsync(string email, CancellationToken ct = default) =>
        await _c.SendAsync(new Req(HttpMethod.Post, "/api/auth/resend-otp", Body: new Dictionary<string, object?> { ["email"] = email }, Public: true), ct);
}

public sealed class AccountApi
{
    private readonly BuyProxyClient _c;
    internal AccountApi(BuyProxyClient c) => _c = c;

    public async Task<Models.Account> GetAsync(CancellationToken ct = default) =>
        _c.Read<Models.Account>(await _c.SendAsync(new Req(HttpMethod.Get, "/v1/account", Retry5xx: true), ct));
}

public sealed class BalancesApi
{
    private readonly BuyProxyClient _c;
    internal BalancesApi(BuyProxyClient c) => _c = c;

    /// <summary>Spendable GB per product, including GB still being added after a payment.</summary>
    public async Task<IReadOnlyList<Balance>> ListAsync(CancellationToken ct = default) =>
        _c.Read<List<Balance>>((await _c.SendAsync(new Req(HttpMethod.Get, "/v1/balances", Retry5xx: true), ct)).GetProperty("balances"));
}

public sealed class PlansApi
{
    private readonly BuyProxyClient _c;
    internal PlansApi(BuyProxyClient c) => _c = c;

    public async Task<IReadOnlyList<Plan>> ListAsync(CancellationToken ct = default) =>
        _c.Read<List<Plan>>((await _c.SendAsync(new Req(HttpMethod.Get, "/v1/plans", Retry5xx: true), ct)).GetProperty("plans"));

    /// <summary>Generate proxy lines against one of your plans. Lines are on-demand — don't store them.</summary>
    public async Task<GeneratedProxies> GenerateAsync(string planId, GenerateParams? parameters = null, CancellationToken ct = default)
    {
        parameters ??= new GenerateParams();
        var r = await _c.SendAsync(new Req(HttpMethod.Post, $"/v1/plans/{Seg(planId)}/generate", Body: parameters.ToBody()), ct);
        return GeneratedProxies.From(_c.Read<List<string>>(r.GetProperty("lines")), parameters.Protocol ?? "http");
    }
}

public sealed class TopupApi
{
    private readonly BuyProxyClient _c;
    internal TopupApi(BuyProxyClient c) => _c = c;

    /// <summary>Stripe checkout — a top-up or a plan subscription. Open <c>PayUrl</c> to pay.</summary>
    public async Task<CardCheckout> CardAsync(CardCheckoutParams parameters, CancellationToken ct = default) =>
        _c.Read<CardCheckout>(await _c.SendAsync(new Req(HttpMethod.Post, "/v1/topup/card", Body: parameters.Body), ct));

    /// <summary>Crypto top-up (pay-as-you-go only). Open <c>Url</c> to pay.</summary>
    public async Task<CryptoCheckout> CryptoAsync(string productKey, int gb, CancellationToken ct = default) =>
        _c.Read<CryptoCheckout>(await _c.SendAsync(new Req(HttpMethod.Post, "/v1/topup/crypto", Body: new Dictionary<string, object?> { ["kind"] = "topup", ["productKey"] = productKey, ["gb"] = gb }), ct));
}

public sealed class TransactionsApi
{
    private readonly BuyProxyClient _c;
    internal TransactionsApi(BuyProxyClient c) => _c = c;

    public async Task<IReadOnlyList<Transaction>> ListAsync(int? limit = null, CancellationToken ct = default) =>
        _c.Read<List<Transaction>>((await _c.SendAsync(new Req(HttpMethod.Get, "/v1/transactions", Query: new Dictionary<string, object?> { ["limit"] = limit }, Retry5xx: true), ct)).GetProperty("transactions"));

    public async Task<Transaction> GetAsync(string id, CancellationToken ct = default) =>
        _c.Read<Transaction>(await _c.SendAsync(new Req(HttpMethod.Get, $"/v1/transactions/{Seg(id)}", Retry5xx: true), ct));

    /// <summary>Poll until the payment settles (<c>succeeded</c> or <c>failed</c>).</summary>
    public async Task<Transaction> WaitAsync(string id, TimeSpan? interval = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var gap = interval ?? TimeSpan.FromSeconds(3);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(10));
        while (true)
        {
            var tx = await GetAsync(id, ct).ConfigureAwait(false);
            if (tx.Status != "pending") return tx;
            if (DateTime.UtcNow + gap > deadline) throw new BuyProxyException($"Payment {id} is still pending after waiting");
            await Task.Delay(gap, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>Reseller API — needs a <c>bp_live_</c> key on a Business plan or higher.</summary>
public sealed class ResellerApi
{
    private readonly BuyProxyClient _c;

    public CustomersApi Customers { get; }
    public PoolApi Pool { get; }

    internal ResellerApi(BuyProxyClient c)
    {
        _c = c;
        Customers = new CustomersApi(c);
        Pool = new PoolApi(c);
    }

    public async Task<ResellerSummary> MeAsync(CancellationToken ct = default) =>
        _c.Read<ResellerSummary>(await _c.SendAsync(new Req(HttpMethod.Get, "/v1/me", Retry5xx: true), ct));
}

public sealed class CustomersApi
{
    private readonly BuyProxyClient _c;
    internal CustomersApi(BuyProxyClient c) => _c = c;

    /// <param name="status">"active", "suspended", or null for all.</param>
    public async Task<IReadOnlyList<Customer>> ListAsync(string? status = null, CancellationToken ct = default) =>
        _c.Read<List<Customer>>((await _c.SendAsync(new Req(HttpMethod.Get, "/v1/customers", Query: new Dictionary<string, object?> { ["status"] = status }, Retry5xx: true), ct)).GetProperty("customers"));

    /// <summary>Creates an isolated sub-account with no bandwidth — fund it with <see cref="AllocateAsync"/>.</summary>
    public async Task<Customer> CreateAsync(string label, CancellationToken ct = default) =>
        _c.Read<Customer>(await _c.SendAsync(new Req(HttpMethod.Post, "/v1/customers", Body: new Dictionary<string, object?> { ["label"] = label }), ct));

    public async Task<CustomerDetail> GetAsync(string username, CancellationToken ct = default) =>
        _c.Read<CustomerDetail>(await _c.SendAsync(new Req(HttpMethod.Get, $"/v1/customers/{Seg(username)}", Retry5xx: true), ct));

    /// <summary>Rename and/or suspend ("suspended") / reactivate ("active"); pass null to leave a field unchanged.</summary>
    public async Task<Customer> UpdateAsync(string username, string? label = null, string? status = null, CancellationToken ct = default) =>
        _c.Read<Customer>(await _c.SendAsync(new Req(new HttpMethod("PATCH"), $"/v1/customers/{Seg(username)}", Body: new Dictionary<string, object?> { ["label"] = label, ["status"] = status }), ct));

    public async Task<CustomerUsage> UsageAsync(string username, CancellationToken ct = default) =>
        _c.Read<CustomerUsage>(await _c.SendAsync(new Req(HttpMethod.Get, $"/v1/customers/{Seg(username)}/usage", Retry5xx: true), ct));

    /// <summary>Moves GB from your pool onto a customer. Pass <paramref name="idempotencyKey"/> to make retries safe.</summary>
    public async Task<Allocation> AllocateAsync(string username, string productKey, int gb, string? idempotencyKey = null, CancellationToken ct = default) =>
        _c.Read<Allocation>(await _c.SendAsync(new Req(
            HttpMethod.Post, $"/v1/customers/{Seg(username)}/allocate",
            Body: new Dictionary<string, object?> { ["productKey"] = productKey, ["gb"] = gb },
            Headers: idempotencyKey is null ? null : new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey },
            Retry5xx: idempotencyKey is not null), ct));

    public async Task<GeneratedProxies> GenerateAsync(string username, string planId, GenerateParams? parameters = null, CancellationToken ct = default)
    {
        parameters ??= new GenerateParams();
        var r = await _c.SendAsync(new Req(HttpMethod.Post, $"/v1/customers/{Seg(username)}/generate", Body: parameters.ToBody(planId)), ct);
        return GeneratedProxies.From(_c.Read<List<string>>(r.GetProperty("lines")), parameters.Protocol ?? "http");
    }
}

public sealed class PoolApi
{
    private readonly BuyProxyClient _c;
    internal PoolApi(BuyProxyClient c) => _c = c;

    /// <summary>Your unallocated bandwidth, per product.</summary>
    public async Task<IReadOnlyList<PoolEntry>> GetAsync(CancellationToken ct = default) =>
        _c.Read<List<PoolEntry>>((await _c.SendAsync(new Req(HttpMethod.Get, "/v1/pool", Retry5xx: true), ct)).GetProperty("pool"));

    public async Task<CardCheckout> TopupCardAsync(string productKey, int gb, CancellationToken ct = default) =>
        _c.Read<CardCheckout>(await _c.SendAsync(new Req(HttpMethod.Post, "/v1/pool/topup/card", Body: new Dictionary<string, object?> { ["productKey"] = productKey, ["gb"] = gb }), ct));

    public async Task<CryptoCheckout> TopupCryptoAsync(string productKey, int gb, CancellationToken ct = default) =>
        _c.Read<CryptoCheckout>(await _c.SendAsync(new Req(HttpMethod.Post, "/v1/pool/topup/crypto", Body: new Dictionary<string, object?> { ["productKey"] = productKey, ["gb"] = gb }), ct));
}
