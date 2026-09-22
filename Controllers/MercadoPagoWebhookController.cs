using asterlinkportaldepagamento.Data;
using asterlinkportaldepagamento.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace asterlinkportaldepagamento.Controllers;

[ApiController, Route("webhooks/mercadopago")]
public sealed class MercadoPagoWebhookController(
    NetworkOrderRepository orders,
    IConfiguration configuration,
    IOptions<NetworkOptions> options) : ControllerBase
{
    [HttpPost, RequestSizeLimit(32768)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return StatusCode(503);
        }

        var id = Request.Query["data.id"];

        if (id.Count != 1 || id.ToString().Length is 0 or > 80

            || !id.ToString().All(char.IsAsciiDigit))
        {
            return BadRequest();
        }

        if (Request.Headers["x-request-id"].Count != 1 || Request.Headers["x-signature"].Count != 1

            || !MercadoPagoSignature.Verify(
                Request.Headers["x-signature"].ToString(),
                Request.Headers["x-request-id"].ToString(),
                id.ToString(),
                configuration["MercadoPago:WebhookSecret"] ?? ""))
        {
            return Unauthorized();
        }

        await orders.EnqueueAsync(id.ToString(), ct);

        return Ok();
    }
}
