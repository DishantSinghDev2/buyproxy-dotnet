namespace BuyProxy;

/// <summary>Controls proxy generation. Unset (null) fields use the API's defaults.</summary>
public sealed record GenerateParams
{
    /// <summary>"http" (default) or "socks5".</summary>
    public string? Protocol { get; init; }

    /// <summary>1–1000, default 1.</summary>
    public int? Count { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code, e.g. "US".</summary>
    public string? Country { get; init; }

    public string? State { get; init; }
    public string? City { get; init; }
    public string? Isp { get; init; }

    /// <summary>Keep the same IP this long; leave null for a fresh IP per request (rotating).</summary>
    public int? StickyMinutes { get; init; }

    internal Dictionary<string, object?> ToBody(string? planId = null) => new()
    {
        ["planId"] = planId,
        ["protocol"] = Protocol,
        ["count"] = Count,
        ["country"] = Country,
        ["state"] = State,
        ["city"] = City,
        ["isp"] = Isp,
        ["stickyMinutes"] = StickyMinutes,
    };
}

/// <summary>A card checkout request: a pay-as-you-go top-up or a plan subscription.</summary>
public sealed class CardCheckoutParams
{
    internal Dictionary<string, object?> Body { get; }

    private CardCheckoutParams(Dictionary<string, object?> body) => Body = body;

    /// <summary>Buy <paramref name="gb"/> GB of one product (see <c>Products.ListAsync</c> for keys and minimums).</summary>
    public static CardCheckoutParams Topup(string productKey, int gb) =>
        new(new() { ["kind"] = "topup", ["productKey"] = productKey, ["gb"] = gb });

    /// <summary>Subscribe to "pro", "business" or "advanced". <paramref name="interval"/> is "month" (default) or "year".</summary>
    public static CardCheckoutParams Plan(string plan, string? interval = null) =>
        new(new() { ["kind"] = "plan", ["plan"] = plan, ["interval"] = interval });
}
