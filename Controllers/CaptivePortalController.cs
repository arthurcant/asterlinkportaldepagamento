using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using asterlinkportaldepagamento.Models;
using asterlinkportaldepagamento.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace asterlinkportaldepagamento.Controllers;

[Route("rede")]
public sealed class CaptivePortalController(RouterOsHotspot router, IDataProtectionProvider protection) : Controller
{
    private const string DeviceProtectionPurpose = "AsterLink.Device.IPv4.v2";

    [HttpGet("entrada")]
    public async Task<IActionResult> Entry(CancellationToken ct)
    {
        try
        {
            var context = await router.CaptureAsync(HttpContext.Connection.RemoteIpAddress, ct);
            var token = protection.CreateProtector(DeviceProtectionPurpose).Protect(JsonSerializer.Serialize(context));
            Response.Cookies.Append(
                "AsterLink.Device",
                token,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromHours(2),
                    IsEssential = true,
                    Path = "/"
                });

            return Redirect("/conta");
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    public static DeviceContext? Read(HttpRequest request, IDataProtectionProvider protection)
    {
        if (!request.Cookies.TryGetValue("AsterLink.Device", out var token))
        {
            return null;
        }

        try
        {
            var context = JsonSerializer.Deserialize<DeviceContext>(protection.CreateProtector(DeviceProtectionPurpose).Unprotect(token));

            if (context is null || context.Expires <= DateTimeOffset.UtcNow
                || string.IsNullOrWhiteSpace(context.Gateway)
                || !IPAddress.TryParse(context.Address, out var address)
                || address.AddressFamily != AddressFamily.InterNetwork
                || address.ToString() != context.Address)
            {
                return null;
            }

            return context;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }
}
