using System.Net;
using System.Text.Json;
using asterlinkportaldepagamento.Models;
using asterlinkportaldepagamento.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

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

Check(!RouterOsHotspot.ValidMac("01:00:00:00:00:01"), "reject multicast MAC");

Check(RouterOsHotspot.ValidMac("02:11:22:33:44:55"), "accept locally administered device MAC");

var options = new NetworkOptions
{
    Enabled = true,
    GatewayId = "lab",
    RestUrl = "https://router.example/rest/",
    Username = "service",
    Password = "secret",
    HotspotServer = "guest",
    UserProfile = "paid",
    LoginUrl = "https://guest.example/login",
    ClientSubnet = "192.168.88.0/24",
    WebhookUrl = "https://portal.example/webhooks/mercadopago",
    CollectorId = "123",
    LiveMode = false
};

var order = new NetworkOrder
{
    Id = Guid.NewGuid().ToString("N"),
    UserId = 1,
    Price = 5m,
    Mac = "02:11:22:33:44:55",
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

Check(MercadoPagoPayments.Matches(Payment(), order, options), "payment matches purchase snapshot");

Check(!MercadoPagoPayments.Matches(Payment(amount: 1), order, options), "reject wrong amount");

Check(!MercadoPagoPayments.Matches(Payment(currency: "USD"), order, options), "reject wrong currency");

Check(!MercadoPagoPayments.Matches(Payment(collector: "456"), order, options), "reject wrong recipient");

Check(!MercadoPagoPayments.Matches(Payment(live: true), order, options), "reject environment mismatch");

Check(
    !MercadoPagoPayments.Matches(Payment(reference: "another-order"), order, options),
    "reject another purchase");

var handler = new FakeRouter();

var router = new RouterOsHotspot(new FakeFactory(handler), Options.Create(options), new EphemeralDataProtectionProvider());

order.ProtectedPassword = router.ProtectPassword("random-purchase-password");

await Reject(
    async () =>
{
    await router.CaptureAsync(IPAddress.Parse("8.8.8.8"), default);
},
    "reject origin outside guest subnet");

var device = await router.CaptureAsync(IPAddress.Parse("192.168.88.10"), default);

Check(device.Mac == order.Mac && device.Gateway == "lab", "capture from router host table");

handler.DuplicateHost = true;

await Reject(
    async () =>
{
    await router.CaptureAsync(IPAddress.Parse("192.168.88.10"), default);
},
    "reject ambiguous host table");

handler.DuplicateHost = false;

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
    handler.User!["limit-uptime"] == "60m" && handler.User["mac-address"] == order.Mac,
    "purchase duration and MAC sent to router");

handler.User["uptime"] = "1h";

Check((await router.StatusAsync(order, default)).Expired, "router reports consumed allowance");

handler.User["mac-address"] = "02:00:00:00:00:09";

await Reject(() => router.EnsureUserAsync(order, default), "existing username with different MAC is rejected");

handler.User = null;

order.AccessStatus = "ready";

await Reject(() => router.EnsureUserAsync(order, default), "deleted provisioned user is not recreated");

Check(handler.PutCount == 1, "no new grant after missing provisioned user");

Console.WriteLine($"{count} checks passed. HTTP transport is simulated; no router, database or payment was contacted.");

sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

sealed class FakeRouter : HttpMessageHandler
{
    public Dictionary<string, string>? User;

    public int PutCount;

    public bool FailAfterCreate;

    public bool DuplicateHost;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.Scheme != "https" || request.Headers.Authorization?.Scheme != "Basic")
        {
            throw new Exception("Incorrect RouterOS authentication");
        }

        var path = request.RequestUri.AbsolutePath;

        object result;

        if (path.EndsWith("/host"))
        {
            var host = new Dictionary<string, string>
            {
                ["address"] = "192.168.88.10",
                ["server"] = "guest",
                ["mac-address"] = "02:11:22:33:44:55"
            };
            result = DuplicateHost ? new[]
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
        else if (path.EndsWith("/active"))
        {
            result = Array.Empty<object>();
        }
        else
        {
            throw new Exception("Unexpected request " + request.Method + " " + path);
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result)) };
    }
}
