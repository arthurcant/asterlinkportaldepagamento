using System.Text.Json;
using asterlinkportaldepagamento.Models;
using asterlinkportaldepagamento.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

static class HotspotSpeedTests
{
    public static async Task RunAsync(Action<bool, string> check, NetworkOptions options)
    {
        foreach (var (speed, minutes, rate) in new[]
        {
            (10, 30, "10M/10M"),
            (20, 60, "20M/20M"),
            (30, 120, "30M/30M"),
            (50, 240, "50M/50M")
        })
        {
            var transport = new FakeRouter { FailAfterProfileCreate = true };
            var router = new RouterOsHotspot(new FakeFactory(transport), Options.Create(options), new EphemeralDataProtectionProvider());
            // Deserialization also exercises the persisted purchase contract.
            var order = JsonSerializer.Deserialize<NetworkOrder>(JsonSerializer.Serialize(new
            {
                Id = Guid.NewGuid().ToString("N"),
                Address = "192.168.88.10",
                Gateway = "lab",
                Minutes = minutes,
                SpeedMbps = speed,
                EnforceSpeedLimit = true,
                PaymentStatus = "approved"
            }))!;
            order.ProtectedPassword = router.ProtectPassword("test-password");
            transport.RejectProfileCreate = true;
            await RejectAsync(() => router.EnsureUserAsync(order, default), check,
                "profile creation failure prevents unrestricted user creation");
            check(transport.PutCount == 0, "profile must be confirmed before creating user");
            transport.RejectProfileCreate = false;
            await router.EnsureUserAsync(order, default);
            var profileName = transport.User!["profile"];
            check(profileName != "paid" && transport.Profiles[profileName]["rate-limit"] == rate,
                $"{speed} Mbps purchase provisions symmetric RouterOS rate");
            check(transport.Profiles[profileName]["address-list"] == "paid-clients"
                && transport.Profiles["paid"]["rate-limit"] == "",
                "speed profile preserves template without modifying it");
            check(transport.User["limit-uptime"] == minutes + "m", "speed does not change purchased duration");
            transport.User["uptime"] = "1m";
            await router.EnsureUserAsync(order, default);
            check(transport.ProfileCreates == 1 && transport.PutCount == 1 && transport.User["uptime"] == "1m",
                "retries do not duplicate profiles or reset consumed time");
            check(await router.LoginAsync(order, default), "speed-limited purchase can connect");
            transport.Profiles[profileName]["rate-limit"] = ((long)speed * 1_000_000).ToString();
            check((await router.StatusAsync(order, default)).Active,
                "equivalent RouterOS numeric rate is accepted");
            transport.Profiles[profileName]["shared-users"] = "2";
            await RejectAsync(() => router.StatusAsync(order, default), check, "shared profile cannot grant multiple sessions");
            transport.Profiles[profileName]["shared-users"] = "1";
            transport.Profiles[profileName]["rate-limit"] = rate + " 100M/100M";
            await RejectAsync(() => router.StatusAsync(order, default), check, "unexpected burst is rejected");
            transport.Profiles[profileName]["rate-limit"] = "1M/1M";
            await RejectAsync(() => router.StatusAsync(order, default), check, "wrong profile speed is not reported as valid access");
            await RejectAsync(() => router.EnsureUserAsync(order, default), check, "wrong existing profile is not silently overwritten");
            order.SpeedMbps = 0;
            await RejectAsync(() => router.EnsureUserAsync(order, default), check, "zero speed cannot provision unlimited access");
            order.SpeedMbps = speed;
            order.PaymentStatus = "pending";
            await RejectAsync(() => router.EnsureUserAsync(order, default), check, "pending purchase cannot provision speed profile");
        }
    }

    private static async Task RejectAsync(Func<Task> action, Action<bool, string> check, string name)
    {
        try
        {
            await action();
        }
        catch (InvalidOperationException)
        {
            check(true, name);
            return;
        }
        check(false, name);
    }
}
