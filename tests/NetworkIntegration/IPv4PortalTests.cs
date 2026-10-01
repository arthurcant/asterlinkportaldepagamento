using System.Net;
using System.Text.Json;
using asterlinkportaldepagamento.Controllers;
using asterlinkportaldepagamento.Models;
using asterlinkportaldepagamento.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

internal static class IPv4PortalTests
{
    public static async Task RunAsync(Action<bool, string> check, NetworkOptions options, MercadoPagoOptions paymentOptions)
    {
        var protection = new EphemeralDataProtectionProvider();
        var handler = new FakeRouter();
        var factory = new FakeFactory(handler);
        var router = new RouterOsHotspot(factory, Options.Create(options), protection);

        var legacyToken = protection.CreateProtector("AsterLink.Device.v1").Protect(JsonSerializer.Serialize(new
        {
            Mac = "02:11:22:33:44:55",
            Address = "192.168.88.10",
            Gateway = "lab",
            Expires = DateTimeOffset.UtcNow.AddHours(1)
        }));
        check(CaptivePortalController.Read(RequestWithCookie(legacyToken), protection) is null,
            "old identity cookies require a new entry before checkout");

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Path = "/rede/entrada";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.88.10");
        var controller = new CaptivePortalController(router, protection)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
        check(await controller.Entry(default) is RedirectResult { Url: "/conta" },
            "IPv4-only entry retains the account redirect");

        var cookie = SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.ToString());
        var token = cookie.Value.ToString();
        var device = CaptivePortalController.Read(RequestWithCookie(token), protection);
        check(device is { Address: "192.168.88.10", Gateway: "lab" },
            "entry issues a readable IPv4 cookie for checkout");
        check(cookie.Path == "/" && cookie.HttpOnly && cookie.Secure
            && cookie.SameSite == Microsoft.Net.Http.Headers.SameSiteMode.Lax
            && cookie.MaxAge == TimeSpan.FromHours(2),
            "IPv4 cookie preserves path, security and two-hour lifetime");
        using var document = JsonDocument.Parse(protection.CreateProtector("AsterLink.Device.IPv4.v2").Unprotect(token));
        check(!document.RootElement.TryGetProperty("Mac", out _), "new device cookie does not store a hardware address");

        foreach (var sample in new[]
        {
            new { Address = "192.168.88.10", Gateway = "lab", Expires = DateTimeOffset.UtcNow.AddMinutes(-1) },
            new { Address = "2001:db8::10", Gateway = "lab", Expires = DateTimeOffset.UtcNow.AddHours(1) },
            new { Address = "", Gateway = "lab", Expires = DateTimeOffset.UtcNow.AddHours(1) },
            new { Address = "192.168.88.10", Gateway = "", Expires = DateTimeOffset.UtcNow.AddHours(1) }
        })
        {
            var invalidToken = protection.CreateProtector("AsterLink.Device.IPv4.v2").Protect(JsonSerializer.Serialize(sample));
            check(CaptivePortalController.Read(RequestWithCookie(invalidToken), protection) is null,
                "expired or invalid IPv4 identity cookie is refused: " + sample.Address);
        }
        check(CaptivePortalController.Read(RequestWithCookie("invalid-token"), protection) is null,
            "tampered cookie is refused");
        check(CaptivePortalController.Read(new DefaultHttpContext().Request, protection) is null,
            "missing cookie is refused");

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MercadoPago:WebhookSecret"] = "test-secret"
        }).Build();
        // These scenarios must stop before persistence/payment; null dependencies fail if that boundary is crossed.
        var checkout = new CheckoutController(
            null!, null!, null!, new MercadoPagoPayments(factory, configuration), router,
            protection, Options.Create(options), Options.Create(paymentOptions), configuration);
        var request = RequestWithCookie(token);
        checkout.ControllerContext = new ControllerContext { HttpContext = request.HttpContext };
        request.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.88.11");
        handler.HostAddress = "192.168.88.11";
        var changed = await checkout.Pay(2, JsonSerializer.SerializeToElement(new
        {
        }), default) as BadRequestObjectResult;
        check(changed is not null
            && JsonSerializer.Serialize(changed.Value).Contains("IPv4", StringComparison.Ordinal),
            "checkout refuses a cookie issued to a different IP before charging");

        handler.HostAddress = "192.168.88.10";
        request.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.168.88.10");
        var same = await checkout.Pay(2, JsonSerializer.SerializeToElement(new
        {
        }), default) as BadRequestObjectResult;
        check(same is not null
            && JsonSerializer.Serialize(same.Value).Contains("Identificador", StringComparison.Ordinal),
            "same IPv4 passes device validation and proceeds to checkout ID validation");

        handler.HostPresent = false;
        var absentContext = new DefaultHttpContext();
        absentContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.88.10");
        controller.ControllerContext = new ControllerContext { HttpContext = absentContext };
        check(await controller.Entry(default) is BadRequestObjectResult
            && !absentContext.Response.Headers.ContainsKey("Set-Cookie"),
            "entry never creates an identity cookie for an absent HotSpot IP");
    }

    private static HttpRequest RequestWithCookie(string token)
    {
        var request = new DefaultHttpContext().Request;
        request.Scheme = "https";
        request.Path = "/checkout/2";
        request.Headers.Cookie = "AsterLink.Device=" + token;
        return request;
    }
}
