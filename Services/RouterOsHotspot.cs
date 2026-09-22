using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using asterlinkportaldepagamento.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace asterlinkportaldepagamento.Services;

public sealed class RouterOsHotspot(
    IHttpClientFactory clients,
    IOptions<NetworkOptions> configured,
    IDataProtectionProvider protection)
{
    private NetworkOptions Options => configured.Value;

    private readonly IDataProtector secrets = protection.CreateProtector("AsterLink.Hotspot.Password.v1");

    public string ProtectPassword(string password) => secrets.Protect(password);

    public string Password(NetworkOrder order) => secrets.Unprotect(order.ProtectedPassword);

    private async Task<JsonElement> RequestAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        Options.Validate();

        using var request = new HttpRequestMessage(method, Options.RestUrl.TrimEnd('/') + "/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Options.Username + ":" + Options.Password)));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await clients.CreateClient("RouterOS").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct);

        using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "[]" : text);

        return json.RootElement.Clone();
    }

    public async Task<DeviceContext> CaptureAsync(IPAddress? source, CancellationToken ct)
    {
        Options.Validate();

        if (source?.IsIPv4MappedToIPv6 == true)
        {
            source = source.MapToIPv4();
        }

        if (source is null || source.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork

            || !System.Net.IPNetwork.TryParse(Options.ClientSubnet, out var subnet)

            || !subnet.Contains(source))
        {
            throw new InvalidOperationException("Conecte-se à rede de visitantes para identificar este dispositivo.");
        }

        var address = source.ToString();
        var hosts = await RequestAsync(HttpMethod.Get, "ip/hotspot/host?address=" + Uri.EscapeDataString(address), null, ct);
        var matching = hosts.EnumerateArray().Where(x => Value(x, "address") == address
            && Value(x, "server") == Options.HotspotServer).ToArray();

        if (matching.Length != 1 || !ValidMac(Value(matching[0], "mac-address")))
        {
            throw new InvalidOperationException("O dispositivo não foi identificado de forma única no HotSpot.");
        }

        return new DeviceContext(
            Value(matching[0], "mac-address").ToUpperInvariant(),
            address,
            Options.GatewayId,
            DateTimeOffset.UtcNow.AddHours(2));
    }

    public async Task EnsureUserAsync(NetworkOrder order, CancellationToken ct)
    {
        if (order.Gateway != Options.GatewayId || order.Minutes <= 0 || !ValidMac(order.Mac))
        {
            throw new InvalidOperationException("Pedido não pertence ao gateway ou possui limites inválidos.");
        }

        var profiles = await RequestAsync(
            HttpMethod.Get,
            "ip/hotspot/user/profile?name=" + Uri.EscapeDataString(Options.UserProfile),
            null,
            ct);

        if (profiles.GetArrayLength() != 1 || Value(profiles[0], "shared-users") != "1")
        {
            throw new InvalidOperationException("Configure um perfil HotSpot com shared-users=1.");
        }

        var users = await UsersAsync(order, ct);

        if (users.GetArrayLength() > 0)
        {
            if (users.GetArrayLength() != 1 || Value(users[0], "mac-address") != order.Mac

                || Value(users[0], "server") != Options.HotspotServer

                || Value(users[0], "profile") != Options.UserProfile

                || ParseDuration(Value(users[0], "limit-uptime")) != TimeSpan.FromMinutes(order.Minutes)

                || Value(users[0], "disabled") == "true")
            {
                throw new InvalidOperationException("Usuário existente diverge do pedido; requer revisão.");
            }

            return; // Never reset counters or recreate a provisioned purchase.
        }

        if (order.AccessStatus == "ready")
        {
            throw new InvalidOperationException("Usuário provisionado foi removido; requer revisão.");
        }

        await RequestAsync(
            HttpMethod.Put,
            "ip/hotspot/user",
            new Dictionary<string, string>
        {
            ["name"] = order.Username,
            ["password"] = Password(order),
            ["mac-address"] = order.Mac,
            ["server"] = Options.HotspotServer,
            ["profile"] = Options.UserProfile,
            ["limit-uptime"] = order.Minutes.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m",
            ["comment"] = "AsterLink order " + order.Id
        },
            ct);
    }

    private Task<JsonElement> UsersAsync(NetworkOrder order, CancellationToken ct)
    => RequestAsync(HttpMethod.Get, "ip/hotspot/user?name=" + Uri.EscapeDataString(order.Username), null, ct);

    public async Task<(bool Active, bool Expired)> StatusAsync(NetworkOrder order, CancellationToken ct)
    {
        var users = await UsersAsync(order, ct);

        if (users.GetArrayLength() != 1)
        {
            return (false, true);
        }

        var expired = Value(users[0], "disabled") == "true"

            || ParseDuration(Value(users[0], "uptime")) >= TimeSpan.FromMinutes(order.Minutes);
        var active = await RequestAsync(HttpMethod.Get, "ip/hotspot/active?user=" + Uri.EscapeDataString(order.Username), null, ct);

        return (!expired

            && active.EnumerateArray().Any(x => Value(x, "mac-address") == order.Mac && Value(x, "server") == Options.HotspotServer), expired);
    }

    public async Task RevokeAsync(NetworkOrder order, CancellationToken ct)
    {
        var users = await UsersAsync(order, ct);

        foreach (var user in users.EnumerateArray())
        {
            await RequestAsync(
                HttpMethod.Patch,
                "ip/hotspot/user/" + Uri.EscapeDataString(Value(user, ".id")),
                new
            {
                disabled = "true"
            },
                ct);
        }

        var active = await RequestAsync(HttpMethod.Get, "ip/hotspot/active?user=" + Uri.EscapeDataString(order.Username), null, ct);

        foreach (var session in active.EnumerateArray())
        {
            await RequestAsync(HttpMethod.Delete, "ip/hotspot/active/" + Uri.EscapeDataString(Value(session, ".id")), null, ct);
        }
    }

    public static string Value(JsonElement item, string name) => item.TryGetProperty(name, out var value) ? value.ToString() : "";

    public static bool ValidMac(string mac) => System.Text.RegularExpressions.Regex.IsMatch(mac, "^(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$")

        && mac != "00:00:00:00:00:00"

        && (Convert.ToByte(mac[..2], 16) & 1) == 0;

    public static TimeSpan ParseDuration(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("Duração ausente no RouterOS.");
        }

        if (TimeSpan.TryParseExact(
            text,
            new[]
        {
            @"h\:mm\:ss",
            @"hh\:mm\:ss",
            @"d\.hh\:mm\:ss"
        },
            System.Globalization.CultureInfo.InvariantCulture,
            out var clock))
        {
            return clock;
        }

        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"(\d+)(w|d|h|m|s)");

        if (string.Concat(matches.Select(x => x.Value)) != text)
        {
            throw new FormatException("Duração desconhecida do RouterOS.");
        }

        double seconds = 0;

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            seconds += double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * (match.Groups[2].Value switch
            {
                "w" => 604800,
                "d" => 86400,
                "h" => 3600,
                "m" => 60,
                _ => 1
            });
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
