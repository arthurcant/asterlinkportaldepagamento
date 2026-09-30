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
    [HttpGet("entrada")]
    public async Task<IActionResult> Entry(CancellationToken ct)
    {
        try
        { // antes da alteração do sistema ser todo alterado para usar somente IP e não MAC
            var context = await router.CaptureAsync(HttpContext.Connection.RemoteIpAddress, ct);
            var token = protection.CreateProtector("AsterLink.Device.v1").Protect(JsonSerializer.Serialize(context));
            Response.Cookies.Append(
                "AsterLink.Device",
                token,
                new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromHours(2),
                IsEssential = true
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
            var context = JsonSerializer.Deserialize<DeviceContext>(protection.CreateProtector("AsterLink.Device.v1").Unprotect(token));

            return context?.Expires > DateTimeOffset.UtcNow ? context : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }
}
