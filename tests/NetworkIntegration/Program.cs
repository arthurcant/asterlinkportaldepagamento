using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using asterlinkportaldepagamento.Data;
using asterlinkportaldepagamento.Models;
using asterlinkportaldepagamento.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

var count = 0;

void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new Exception("FAIL: " + name);
    }
    count++;
    Console.WriteLine("PASS: " + name);
}

async Task Reject(Func<Task> action, string name)
{
    try
    {
        await action();
    }
    catch (InvalidOperationException)
    {
        Check(true, name);
        return;
    }

    throw new Exception("FAIL: " + name);
}

const string signature = "ts=1704908010,v1=9b0ea51d5a2b37aff293dac1dfa29ab394589ffa41b7ddb742a6617857fb6ad9";

Check(
    MercadoPagoSignature.Verify(signature, "test-request", "12345", "test-secret"),
    "valid signed query ID");

Check(
    !MercadoPagoSignature.Verify(signature, "test-request", "12346", "test-secret"),
    "tampered payment ID");

Check(
    !MercadoPagoSignature.Verify(signature, "other-request", "12345", "test-secret"),
    "tampered request ID");

Check(!MercadoPagoSignature.Verify(signature, "test-request", "12345", "other-secret"), "wrong secret");

Check(
    !MercadoPagoSignature.Verify(signature + ",ts=1704908010", "test-request", "12345", "test-secret"),
    "ambiguous timestamp");

Check(
    !MercadoPagoSignature.Verify("ts=1,v1=" + new string('é', 64), "r", "12345", "s"),
    "malformed multibyte signature");

Check(!MercadoPagoSignature.Verify("", "r", "12345", "s"), "missing signature");

Check(RouterOsHotspot.ParseDuration("1h30m") == TimeSpan.FromMinutes(90), "RouterOS units");

Check(RouterOsHotspot.ParseDuration("01:00:00") == TimeSpan.FromHours(1), "RouterOS clock duration");

var options = new NetworkOptions
{
    Enabled = true,
    GatewayId = "lab",
    RestUrl = "https://router.example/rest/",
    Username = "service",
    Password = "secret",
    HotspotServer = "guest",
    UserProfile = "paid",
    ClientSubnet = "192.168.88.0/24"
};

var paymentOptions = new MercadoPagoOptions
{
    WebhookUrl = "https://portal.example/webhooks/mercadopago",
    CollectorId = "123",
    LiveMode = false
};

var order = new NetworkOrder
{
    Id = Guid.NewGuid().ToString("N"),
    UserId = 1,
    Price = 5m,
    Address = "192.168.88.10",
    Minutes = 60,
    Gateway = "lab"
};

JsonElement Payment(
    decimal amount = 5m,
    string currency = "BRL",
    string collector = "123",
    bool live = false,
    string? reference = null)
=> JsonSerializer.SerializeToElement(new
{
    external_reference = reference ?? order.Id,
    transaction_amount = amount,
    currency_id = currency,
    collector_id = collector,
    live_mode = live
});

Check(MercadoPagoPayments.Matches(Payment(), order, paymentOptions), "payment matches purchase snapshot");

Check(!MercadoPagoPayments.Matches(Payment(amount: 1), order, paymentOptions), "reject wrong amount");

Check(!MercadoPagoPayments.Matches(Payment(currency: "USD"), order, paymentOptions), "reject wrong currency");

Check(!MercadoPagoPayments.Matches(Payment(collector: "456"), order, paymentOptions), "reject wrong recipient");

Check(!MercadoPagoPayments.Matches(Payment(live: true), order, paymentOptions), "reject environment mismatch");

Check(
    !MercadoPagoPayments.Matches(Payment(reference: "another-order"), order, paymentOptions),
    "reject another purchase");

var handler = new FakeRouter();

options.Validate();
Check(true, "RouterOS settings do not require a payment webhook or browser login URL");
options.RestUrl = "http://router.example/rest/";
await Reject(() =>
{
    options.Validate();
    return Task.CompletedTask;
}, "HTTP requires explicit opt-in");
options.AllowInsecureHttpForDevelopment = true;
options.Validate();
Check(true, "explicit development HTTP is accepted");
options.RestUrl = "https://router.example/rest/";
options.AllowInsecureHttpForDevelopment = false;
options.ClientSubnet = "invalid";
await Reject(() =>
{
    options.Validate();
    return Task.CompletedTask;
}, "invalid client subnet fails before charging");
options.ClientSubnet = "192.168.88.0/24";

var router = new RouterOsHotspot(new FakeFactory(handler), Options.Create(options), new EphemeralDataProtectionProvider());

order.ProtectedPassword = router.ProtectPassword("random-purchase-password");

await Reject(
    async () =>
{
    await router.CaptureAsync(IPAddress.Parse("8.8.8.8"), default);
},
    "reject origin outside guest subnet");

var device = await router.CaptureAsync(IPAddress.Parse("192.168.88.10"), default);

Check(device.Address == "192.168.88.10" && device.Gateway == "lab",
    "capture IPv4 from host table without a hardware address");

Check((await router.CaptureAsync(IPAddress.Parse("::ffff:192.168.88.10"), default)).Address == "192.168.88.10",
    "normalize IPv4 mapped by ASP.NET without enabling native IPv6");
await Reject(() => router.CaptureAsync(IPAddress.Parse("2001:db8::10"), default), "reject native IPv6");
await Reject(() => router.CaptureAsync(null, default), "reject missing source IP");
handler.HostPresent = false;
await Reject(() => router.CaptureAsync(IPAddress.Parse("192.168.88.10"), default), "reject IP absent from HotSpot");
handler.HostPresent = true;
handler.HostServer = "another-server";
await Reject(() => router.CaptureAsync(IPAddress.Parse("192.168.88.10"), default), "reject IP on another HotSpot server");
handler.HostServer = "guest";
Check(handler.HostQueries.All(query => query.Contains("address=192.168.88.10")
        && query.Contains(".proplist=") && !query.Contains("mac-address")),
    "host requests use an IPv4 filter and explicit fields only");

handler.DuplicateHost = true;

await Reject(
    async () =>
{
    await router.CaptureAsync(IPAddress.Parse("192.168.88.10"), default);
},
    "reject ambiguous host table");

handler.DuplicateHost = false;

await Reject(
    () => router.EnsureUserAsync(order, default),
    "pending payment cannot provision a HotSpot user");

Check(handler.PutCount == 0, "no network grant before payment approval");

order.PaymentStatus = "approved";

foreach (var invalidAddress in new[] { "", "not-an-ip", "2001:db8::10", "192.168.99.10" })
{
    order.Address = invalidAddress;
    await Reject(() => router.EnsureUserAsync(order, default), "invalid purchase IP cannot provision: " + invalidAddress);
    await Reject(() => router.LoginAsync(order, default), "invalid purchase IP cannot log in: " + invalidAddress);
}
order.Address = "192.168.88.10";

handler.FailAfterCreate = true;

try
{
    await router.EnsureUserAsync(order, default);

    throw new Exception("Expected simulated timeout");
}
catch (HttpRequestException)
{
    Check(handler.PutCount == 1, "router created user before lost response");
}

await router.EnsureUserAsync(order, default);

Check(handler.PutCount == 1, "retry reconciles existing user without resetting time");

Check(
    handler.User!["limit-uptime"] == "60m" && !handler.User.ContainsKey("mac-address"),
    "purchase duration sent without a hardware address");

Check(await router.LoginAsync(order, default), "approved purchase connects through REST");
Check(
    handler.LoginBody!["ip"] == "192.168.88.10"
        && !handler.LoginBody.ContainsKey("mac-address")
        && handler.LoginBody["user"] == order.Username
        && handler.LoginBody["password"] == "random-purchase-password"
        && !handler.LoginBody.ContainsKey("server"),
    "login sends only IP and purchase credentials");
Check(await router.LoginAsync(order, default) && handler.LoginCount == 1,
    "repeated login keeps an existing session and its remaining time");

order.PaymentStatus = "pending";
await Reject(() => router.LoginAsync(order, default), "pending payment cannot log in");
order.PaymentStatus = "approved";
order.AccessStatus = "revoked";
await Reject(() => router.LoginAsync(order, default), "revoked purchase cannot log in");
order.AccessStatus = "provisioning";

handler.Active = false;
handler.HostPresent = false;
Check(!await router.LoginAsync(order, default) && handler.LoginCount == 1,
    "offline device does not trigger login against a stale address");
handler.HostPresent = true;
handler.DuplicateHost = true;
await Reject(() => router.LoginAsync(order, default), "ambiguous IPv4 cannot log in");
handler.DuplicateHost = false;
handler.HostAddress = "192.168.99.10";
Check(!await router.LoginAsync(order, default), "different host address cannot receive the purchase");
handler.HostAddress = "192.168.88.10";

order.Address = "192.168.88.99";
await Reject(() => router.LoginAsync(order, default), "a purchase cannot be rebound to another IPv4");
order.Address = "192.168.88.10";

handler.TranslatedAddress = "192.168.88.200";
Check(await router.LoginAsync(order, default) && handler.LoginBody!["ip"] == "192.168.88.200",
    "login uses the translated IPv4 of the original host when HotSpot assigns one");
Check((await router.StatusAsync(order, default)).Active, "translated IPv4 session is recognized");
handler.ActiveAddress = "192.168.88.99";
Check(!(await router.StatusAsync(order, default)).Active, "another IPv4 session is not reported as connected");
await Reject(() => router.LoginAsync(order, default), "conflicting active session cannot trigger a new login");
handler.ActiveAddress = null;
handler.Active = false;
handler.TranslatedAddress = "192.168.99.200";
await Reject(() => router.LoginAsync(order, default), "translated address outside client subnet is refused");
handler.TranslatedAddress = null;

handler.SuppressActivation = true;
Check(!await router.LoginAsync(order, default), "HTTP success is not reported as connected without active session");
handler.SuppressActivation = false;
handler.FailAfterLogin = true;
try
{
    await router.LoginAsync(order, default);
    throw new Exception("Expected lost login response");
}
catch (HttpRequestException)
{
    Check(true, "lost login response remains retryable");
}
var loginCount = handler.LoginCount;
Check(await router.LoginAsync(order, default) && handler.LoginCount == loginCount,
    "retry reconciles active session after lost login response");

handler.User["uptime"] = "1h";

Check((await router.StatusAsync(order, default)).Expired, "router reports consumed allowance");

Check(!await router.LoginAsync(order, default) && handler.LoginCount == loginCount,
    "exhausted allowance never receives a new login");

handler.User["uptime"] = "30m";
await router.RevokeAsync(order, default);
Check(handler.User["disabled"] == "true" && !handler.Active,
    "refund disables the user and removes the active session");
Check(handler.RevokePaths.SequenceEqual(new[] { "/rest/ip/hotspot/user/*1", "/rest/ip/hotspot/active/*2" }),
    "RouterOS record IDs retain the literal asterisk for PATCH and DELETE");
handler.User["disabled"] = "false";

handler.User["comment"] = "AsterLink order " + order.Id;
await Reject(() => router.EnsureUserAsync(order, default), "legacy user requires review without resetting counters");

handler.User["comment"] = "AsterLink order " + order.Id + " IPv4 192.168.88.99";

await Reject(() => router.EnsureUserAsync(order, default), "existing username bound to another IPv4 is rejected");

handler.User = null;

order.AccessStatus = "ready";

await Reject(() => router.EnsureUserAsync(order, default), "deleted provisioned user is not recreated");

Check(handler.PutCount == 1, "no new grant after missing provisioned user");

// Exercise the actual reconciliation service; only HTTP and persistence are replaced.
var purchase = new NetworkOrder
{
    Id = Guid.NewGuid().ToString("N"),
    UserId = 1,
    Price = 5m,
    Minutes = 90,
    Address = "192.168.88.10",
    Gateway = "lab"
};
var paymentHandler = new FakePayment(purchase.Id);
var networkHandler = new FakeRouter();
var factory = new FakeFactory(networkHandler, paymentHandler);
var networkService = new RouterOsHotspot(factory, Options.Create(options), new EphemeralDataProtectionProvider());
purchase.ProtectedPassword = networkService.ProtectPassword("purchase-secret");
var store = new MemoryOrderStore(purchase);
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["MercadoPago:AccessToken"] = "test-only-token"
}).Build();
var reconciliation = new PaymentReconciliation(
    store,
    new MercadoPagoPayments(factory, configuration),
    networkService,
    Options.Create(paymentOptions),
    NullLogger<PaymentReconciliation>.Instance);

Check(await reconciliation.ProcessAsync("12345", default)
    && networkHandler.PutCount == 0 && networkHandler.LoginCount == 0,
    "reconciliation retries a pending payment without network access");

paymentHandler.Status = "approved";
paymentHandler.Amount = 1m;
Check(!await reconciliation.ProcessAsync("12345", default) && networkHandler.PutCount == 0,
    "approved payment with wrong amount cannot provision or connect");

paymentHandler.Amount = 5m;
networkHandler.HostPresent = false;
Check(await reconciliation.ProcessAsync("12345", default)
    && store.Order.AccessStatus == "ready" && networkHandler.PutCount == 1 && networkHandler.LoginCount == 0,
    "approval persists the user and retries connection while the device is offline");
Check(networkHandler.User!["limit-uptime"] == "90m", "reconciliation uses the purchased plan duration");

networkHandler.HostPresent = true;
networkHandler.FailAfterLogin = true;
try
{
    await reconciliation.ProcessAsync("12345", default);
    throw new Exception("Expected login transport failure");
}
catch (HttpRequestException)
{
    Check(store.Order.AccessStatus == "ready", "user creation is persisted before attempting login");
}

Check(!await reconciliation.ProcessAsync("12345", default)
    && networkHandler.Active && networkHandler.PutCount == 1 && networkHandler.LoginCount == 1,
    "approved payment automatically connects and reconciles a lost response without new credit");
Check(!await reconciliation.ProcessAsync("12345", default)
    && networkHandler.PutCount == 1 && networkHandler.LoginCount == 1,
    "duplicate approval neither recreates the user nor restarts the session");

networkHandler.User["uptime"] = "1h30m";
Check(!await reconciliation.ProcessAsync("12345", default) && networkHandler.LoginCount == 1,
    "duplicate approval after expiry cannot renew access");

paymentHandler.Status = "refunded";
Check(!await reconciliation.ProcessAsync("12345", default)
    && store.Order.AccessStatus == "revoked" && networkHandler.User["disabled"] == "true" && !networkHandler.Active,
    "confirmed refund revokes the account and disconnects its session");

paymentHandler.Status = "approved";
Check(!await reconciliation.ProcessAsync("12345", default)
    && store.Order.AccessStatus == "revoked" && networkHandler.LoginCount == 1,
    "later approved notification cannot restore a revoked purchase");

await IPv4PortalTests.RunAsync(Check, options, paymentOptions);

Console.WriteLine($"{count} checks passed. HTTP transport is simulated; no router, database or payment was contacted.");

sealed class FakeFactory(HttpMessageHandler handler, HttpMessageHandler? paymentHandler = null) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        return name == "MercadoPago"
            ? new HttpClient(paymentHandler!, disposeHandler: false) { BaseAddress = new Uri("https://api.mercadopago.com/") }
            : new HttpClient(handler, disposeHandler: false);
    }
}

sealed class FakePayment(string reference) : HttpMessageHandler
{
    public string Status = "pending";

    public decimal Amount = 5m;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri!.AbsolutePath != "/v1/payments/12345")
        {
            throw new Exception("Unexpected payment request");
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                id = 12345,
                external_reference = reference,
                transaction_amount = Amount,
                currency_id = "BRL",
                collector_id = 123,
                live_mode = false,
                status = Status
            })
        });
    }
}

sealed class MemoryOrderStore(NetworkOrder order) : INetworkOrderStore
{
    public NetworkOrder Order = order;

    public Task<IAsyncDisposable> LockAsync(string id, CancellationToken ct)
    {
        return Task.FromResult<IAsyncDisposable>(new NoopLock());
    }

    public Task<NetworkOrder?> GetAsync(string id, CancellationToken ct)
    {
        return Task.FromResult(id == Order.Id ? JsonSerializer.Deserialize<NetworkOrder>(JsonSerializer.Serialize(Order)) : null);
    }

    public Task SaveAsync(NetworkOrder value, CancellationToken ct, bool enqueue = false)
    {
        Order = JsonSerializer.Deserialize<NetworkOrder>(JsonSerializer.Serialize(value))!;
        return Task.CompletedTask;
    }

    private sealed class NoopLock : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

sealed class FakeRouter : HttpMessageHandler
{
    public Dictionary<string, string>? User;

    public int PutCount;

    public bool FailAfterCreate;

    public bool DuplicateHost;

    public bool HostPresent = true;

    public string HostAddress = "192.168.88.10";

    public string HostServer = "guest";

    public string? TranslatedAddress;

    public string? ActiveAddress;

    public List<string> HostQueries = [];

    public bool Active;

    public int LoginCount;

    public bool FailAfterLogin;

    public bool SuppressActivation;

    public Dictionary<string, string>? LoginBody;

    public List<string> RevokePaths = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.Scheme != "https"
            || request.Headers.Authorization?.Scheme != "Basic"
            || request.Headers.Authorization.Parameter != "c2VydmljZTpzZWNyZXQ=")
        {
            throw new Exception("Incorrect RouterOS authentication");
        }

        var path = request.RequestUri.AbsolutePath;

        object result;

        if (path.EndsWith("/host"))
        {
            HostQueries.Add(Uri.UnescapeDataString(request.RequestUri.Query));
            var host = new Dictionary<string, string>
            {
                ["address"] = HostAddress,
                [".id"] = "*3",
                ["to-address"] = TranslatedAddress ?? HostAddress,
                ["server"] = HostServer
            };
            result = !HostPresent ? Array.Empty<Dictionary<string, string>>() : DuplicateHost ? new[]
            {
                host,
                host
            } : new[] { host };
        }
        else if (path.EndsWith("/profile"))
        {
            result = new[] { new Dictionary<string, string>
                {
                    ["name"] = "paid",
                    ["shared-users"] = "1"
                } };
        }
        else if (request.Method == HttpMethod.Put && path.EndsWith("/user"))
        {
            PutCount++;
            User = JsonSerializer.Deserialize<Dictionary<string, string>>(await request.Content!.ReadAsStringAsync(cancellationToken))!;

            User[".id"] = "*1";

            User["uptime"] = "0s";

            User["disabled"] = "false";

            if (FailAfterCreate)
            {
                FailAfterCreate = false;

                throw new HttpRequestException("Simulated lost response");
            }

            result = User;
        }
        else if (path.EndsWith("/user"))
        {
            result = User is null ? Array.Empty<Dictionary<string, string>>() : new[] { User };
        }
        else if (request.Method == HttpMethod.Post && path.EndsWith("/active/login"))
        {
            LoginCount++;
            LoginBody = JsonSerializer.Deserialize<Dictionary<string, string>>(
                await request.Content!.ReadAsStringAsync(cancellationToken))!;
            Active = !SuppressActivation;

            if (FailAfterLogin)
            {
                FailAfterLogin = false;
                throw new HttpRequestException("Simulated lost login response");
            }

            result = Array.Empty<object>();
        }
        else if (request.Method == HttpMethod.Patch && path == "/rest/ip/hotspot/user/*1")
        {
            RevokePaths.Add(path);
            var body = JsonSerializer.Deserialize<Dictionary<string, string>>(
                await request.Content!.ReadAsStringAsync(cancellationToken))!;
            User!["disabled"] = body["disabled"];
            result = User;
        }
        else if (request.Method == HttpMethod.Delete && path == "/rest/ip/hotspot/active/*2")
        {
            RevokePaths.Add(path);
            Active = false;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
        else if (path.EndsWith("/active"))
        {
            result = Active ? new[]
            {
                new Dictionary<string, string>
                {
                    [".id"] = "*2",
                    ["user"] = User!["name"],
                    ["address"] = ActiveAddress ?? TranslatedAddress ?? HostAddress,
                    ["server"] = User["server"]
                }
            } : Array.Empty<Dictionary<string, string>>();
        }
        else
        {
            throw new Exception("Unexpected request " + request.Method + " " + path);
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result)) };
    }
}
