using System.Net;

namespace BuyProxy;

/// <summary>One generated proxy. The API returns <c>username:password@host:port</c> lines.</summary>
public sealed record ProxyEndpoint(string Host, int Port, string Username, string Password, string Protocol = "http")
{
    /// <summary>Parse a <c>username:password@host:port</c> line ('@' and ':' are allowed inside the password).</summary>
    public static ProxyEndpoint Parse(string line, string protocol = "http")
    {
        var bad = new FormatException($"Not a proxy line (expected username:password@host:port): \"{line}\"");
        var at = line.LastIndexOf('@');
        if (at < 0) throw bad;
        var credentials = line[..at];
        var address = line[(at + 1)..];
        var colon = credentials.IndexOf(':');
        var lastColon = address.LastIndexOf(':');
        if (colon < 1 || lastColon < 1) throw bad;
        if (!int.TryParse(address[(lastColon + 1)..], out var port) || port is < 1 or > 65535) throw bad;
        return new ProxyEndpoint(address[..lastColon], port, credentials[..colon], credentials[(colon + 1)..], protocol);
    }

    /// <summary>The <c>username:password@host:port</c> form the API uses.</summary>
    public override string ToString() => $"{Username}:{Password}@{Host}:{Port}";

    /// <summary><c>http://host:port</c> — no credentials.</summary>
    public string Server => $"{Protocol}://{Host}:{Port}";

    /// <summary>Full URL with percent-encoded credentials, ready for any HTTP client.</summary>
    public string Url => $"{Protocol}://{Uri.EscapeDataString(Username)}:{Uri.EscapeDataString(Password)}@{Host}:{Port}";

    /// <summary>A <see cref="WebProxy"/> with credentials, for <c>HttpClientHandler.Proxy</c>.</summary>
    public WebProxy ToWebProxy() => new(new Uri(Server)) { Credentials = new NetworkCredential(Username, Password) };

    /// <summary>An <see cref="HttpClient"/> that sends every request through this proxy (HTTP or SOCKS5).</summary>
    public HttpClient CreateHttpClient() => new(new HttpClientHandler { Proxy = ToWebProxy(), UseProxy = true });

    /// <summary>Environment variables understood by curl, wget, git and most CLIs.</summary>
    public IReadOnlyDictionary<string, string> Env() =>
        new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" }.ToDictionary(k => k, _ => Url);
}

/// <summary>The result of a generate call: raw lines plus parsed proxies.</summary>
public sealed record GeneratedProxies(IReadOnlyList<string> Lines, IReadOnlyList<ProxyEndpoint> Proxies)
{
    internal static GeneratedProxies From(IReadOnlyList<string> lines, string protocol) =>
        new(lines, lines.Select(l => ProxyEndpoint.Parse(l, protocol)).ToList());
}
