using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using asterlinkportaldepagamento.Data;
using asterlinkportaldepagamento.Models;
using asterlinkportaldepagamento.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

internal static class PlanReplacementTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
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
        var handler = new RouterHandler();
        var router = new RouterOsHotspot(new RouterFactory(handler), Options.Create(options),
            new EphemeralDataProtectionProvider());
        var oldOrder = CreateOrder(router, "192.168.88.10", 1, 10);
        var newOrder = CreateOrder(router, "192.168.88.10", 1, 30);
        handler.AddUser(oldOrder);
        handler.AddUser(newOrder);
        handler.ActiveUser = oldOrder.Username;
        var store = new OrderStore(oldOrder, newOrder);
        var activator = new NetworkAccessActivator(router, store);

        newOrder.PaymentStatus = "pending";
        try
        {
            await activator.ActivateAsync(newOrder, default);
            throw new Exception("Expected payment rejection");
        }
        catch (InvalidOperationException)
        {
            check(handler.ActiveUser == oldOrder.Username && handler.RemovedUsers == 0,
                "unapproved replacement cannot change an existing HotSpot session");
        }
        newOrder.PaymentStatus = "approved";

        check(await activator.ActivateAsync(newOrder, default),
            "approved replacement activates the new purchase");
        check(handler.ActiveUser == newOrder.Username && handler.RemovedUsers == 1
            && !handler.Users.ContainsKey(oldOrder.Username),
            "replacement disconnects and deletes only the previous paid HotSpot user");
        check((await store.GetAsync(oldOrder.Id, default))?.AccessStatus == "superseded",
            "previous order is marked superseded and cannot be re-provisioned");
        check(handler.Events.IndexOf("delete-active") < handler.Events.IndexOf("login"),
            "previous session is closed before the new login");

        check(await activator.ActivateAsync(newOrder, default) && handler.LoginCount == 1
            && handler.RemovedUsers == 1,
            "repeated activation does not renew time or remove another user");

        var retryHandler = new RouterHandler { FailAfterDisconnect = true };
        var retryRouter = new RouterOsHotspot(new RouterFactory(retryHandler), Options.Create(options),
            new EphemeralDataProtectionProvider());
        var retryOld = CreateOrder(retryRouter, "192.168.88.10", 3, 10);
        var retryNew = CreateOrder(retryRouter, "192.168.88.10", 3, 30);
        retryHandler.AddUser(retryOld);
        retryHandler.AddUser(retryNew);
        retryHandler.ActiveUser = retryOld.Username;
        var retryStore = new OrderStore(retryOld, retryNew);
        var retryActivator = new NetworkAccessActivator(retryRouter, retryStore);
        try
        {
            await retryActivator.ActivateAsync(retryNew, default);
            throw new Exception("Expected lost RouterOS response");
        }
        catch (HttpRequestException)
        {
            check((await retryStore.GetAsync(retryOld.Id, default))?.AccessStatus == "superseded"
                && (await retryStore.GetAsync(retryNew.Id, default))?.SupersededOrderId == retryOld.Id,
                "interrupted replacement persists enough state for a safe retry");
        }
        var persistedRetry = (await retryStore.GetAsync(retryNew.Id, default))!;
        var refundedPrevious = (await retryStore.GetAsync(retryOld.Id, default))!;
        refundedPrevious.PaymentStatus = "refunded";
        refundedPrevious.AccessStatus = "revoked";
        await retryStore.SaveAsync(refundedPrevious, default);
        check(await retryActivator.ActivateAsync(persistedRetry, default)
            && retryHandler.ActiveUser == retryNew.Username
            && retryHandler.RemovedUsers == 1,
            "retry finishes cleanup after an old refund without creating a second allowance");

        var outsider = CreateOrder(router, "192.168.88.10", 2, 20);
        handler.AddUser(outsider);
        handler.ActiveUser = outsider.Username;
        var thirdOrder = CreateOrder(router, "192.168.88.10", 1, 50);
        handler.AddUser(thirdOrder);
        await store.SaveAsync(outsider, default);
        await store.SaveAsync(thirdOrder, default);
        try
        {
            await activator.ActivateAsync(thirdOrder, default);
            throw new Exception("Expected ownership rejection");
        }
        catch (InvalidOperationException)
        {
            check(handler.ActiveUser == outsider.Username && handler.RemovedUsers == 1,
                "an active session owned by another application account is never removed");
        }
    }

    private static NetworkOrder CreateOrder(RouterOsHotspot router, string address, long userId, int speed)
    {
        return new NetworkOrder
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            Address = address,
            Gateway = "lab",
            Minutes = speed == 10 ? 30 : speed == 30 ? 120 : 60,
            SpeedMbps = speed,
            EnforceSpeedLimit = true,
            PaymentStatus = "approved",
            AccessStatus = "ready",
            ProtectedPassword = router.ProtectPassword("purchase-secret")
        };
    }

    private sealed class OrderStore(params NetworkOrder[] orders) : INetworkOrderStore
    {
        private readonly Dictionary<string, NetworkOrder> values = orders.ToDictionary(x => x.Id, Clone);

        public Task<IAsyncDisposable> LockAsync(string id, CancellationToken ct)
            => Task.FromResult<IAsyncDisposable>(new Gate());

        public Task<NetworkOrder?> GetAsync(string id, CancellationToken ct)
            => Task.FromResult(values.TryGetValue(id, out var value) ? Clone(value) : null);

        public Task SaveAsync(NetworkOrder order, CancellationToken ct, bool enqueue = false)
        {
            values[order.Id] = Clone(order);
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(string paymentId, CancellationToken ct) => Task.CompletedTask;

        private static NetworkOrder Clone(NetworkOrder order)
            => JsonSerializer.Deserialize<NetworkOrder>(JsonSerializer.Serialize(order))!;

        private sealed class Gate : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class RouterFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RouterHandler : HttpMessageHandler
    {
        public Dictionary<string, Dictionary<string, string>> Users { get; } = [];
        public string? ActiveUser;
        public int RemovedUsers;
        public int LoginCount;
        public bool FailAfterDisconnect;
        public List<string> Events { get; } = [];

        public void AddUser(NetworkOrder order)
        {
            Users[order.Username] = new Dictionary<string, string>
            {
                [".id"] = "*" + (Users.Count + 1),
                ["name"] = order.Username,
                ["server"] = "guest",
                ["profile"] = "paid-aster-v1-" + order.SpeedMbps + "m",
                ["comment"] = "AsterLink order " + order.Id + " IPv4 " + order.Address,
                ["limit-uptime"] = order.Minutes + "m",
                ["uptime"] = "0s",
                ["disabled"] = "false"
            };
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
            object result;
            if (path.EndsWith("/host"))
            {
                return Json(new[] { new Dictionary<string, string>
                {
                    ["address"] = "192.168.88.10", ["server"] = "guest",
                    ["to-address"] = "192.168.88.200"
                } });
            }
            if (path.EndsWith("/profile"))
            {
                var name = query["name"].ToString();
                result = new[] { new Dictionary<string, string>
                {
                    ["name"] = name,
                    ["shared-users"] = "1",
                    ["rate-limit"] = name.EndsWith("10m") ? "10M/10M"
                        : name.EndsWith("20m") ? "20M/20M"
                        : name.EndsWith("30m") ? "30M/30M" : "50M/50M"
                } };
            }
            else if (path.EndsWith("/user"))
            {
                var name = query["name"].ToString();
                result = Users.TryGetValue(name, out var user)
                    ? new[] { user } : Array.Empty<Dictionary<string, string>>();
            }
            else if (path.EndsWith("/active"))
            {
                var user = query.TryGetValue("user", out var queriedUser) ? queriedUser.ToString() : "";
                var address = query.TryGetValue("address", out var queriedAddress) ? queriedAddress.ToString() : "";
                result = ActiveUser is not null
                    && (user.Length == 0 || user == ActiveUser)
                    && (address.Length == 0 || address == "192.168.88.200")
                    ? new[] { new Dictionary<string, string>
                    {
                        [".id"] = "*9", ["user"] = ActiveUser,
                        ["address"] = "192.168.88.200", ["server"] = "guest",
                        ["session-time-left"] = "20m"
                    } }
                    : Array.Empty<Dictionary<string, string>>();
            }
            else if (request.Method == HttpMethod.Patch && path.Contains("/user/*"))
            {
                var user = Users.Values.Single(x => x[".id"] == path.Split('/').Last());
                user["disabled"] = "true";
                result = user;
            }
            else if (request.Method == HttpMethod.Delete && path.Contains("/active/*"))
            {
                Events.Add("delete-active");
                ActiveUser = null;
                if (FailAfterDisconnect)
                {
                    FailAfterDisconnect = false;
                    throw new HttpRequestException("Simulated lost disconnect response");
                }
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            else if (request.Method == HttpMethod.Delete && path.Contains("/user/*"))
            {
                var user = Users.Single(x => x.Value[".id"] == path.Split('/').Last());
                Users.Remove(user.Key);
                RemovedUsers++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            else if (request.Method == HttpMethod.Post && path.EndsWith("/active/login"))
            {
                if (ActiveUser is not null)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = JsonContent.Create(new { detail = "failure: IP is already logged in" })
                    };
                }
                var body = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    await request.Content!.ReadAsStringAsync(cancellationToken))!;
                Events.Add("login");
                ActiveUser = body["user"];
                LoginCount++;
                result = Array.Empty<object>();
            }
            else
            {
                throw new Exception("Unexpected RouterOS request: " + request.Method + " " + path);
            }
            return Json(result);
        }

        private static HttpResponseMessage Json(object value)
            => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
