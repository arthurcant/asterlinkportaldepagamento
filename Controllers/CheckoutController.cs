using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using asterlinkportaldepagamento.Data;
using asterlinkportaldepagamento.Models;
using asterlinkportaldepagamento.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace asterlinkportaldepagamento.Controllers;

[Authorize, Route("checkout")]
public sealed class CheckoutController(
    IPlanRepository plans,
    IAccessSessionRepository sessions,
    NetworkOrderRepository orders,
    MercadoPagoPayments payments,
    RouterOsHotspot router,
    IDataProtectionProvider protection,
    IOptions<NetworkOptions> configured,
    IOptions<MercadoPagoOptions> paymentOptions,
    IConfiguration configuration) : Controller
{
    private long UserId => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private readonly IDataProtector payloadProtector = protection.CreateProtector("AsterLink.PaymentPayload.v1");

    [HttpGet("{planId:int}")]
    public async Task<IActionResult> Index(int planId, CancellationToken ct)
    {
        var plan = await plans.GetByIdAsync(planId, ct);

        return plan is null ? NotFound() : View(new CheckoutViewModel
        {
            Plan = plan,
            PublicKey = configuration["MercadoPago:PublicKey"] ?? "",
            PayerEmail = User.FindFirstValue(ClaimTypes.Email) ?? ""
        });
    }

    [HttpPost("{planId:int}/pagar"), ValidateAntiForgeryToken, RequestSizeLimit(32768)]
    public async Task<IActionResult> Pay(int planId, [FromBody] JsonElement formData, CancellationToken ct)
    {
        if (!configured.Value.Enabled)
        {
            return StatusCode(
                503,
                new
                {
                    message = "O acesso à rede ainda não foi configurado. Nenhuma cobrança foi iniciada."
                });
        }

        try
        {
            configured.Value.Validate();
            paymentOptions.Value.Validate();
        }
        catch (InvalidOperationException exception)
        {
            return StatusCode(503, new
            {
                message = exception.Message
            });
        }

        if (string.IsNullOrWhiteSpace(configuration["MercadoPago:WebhookSecret"]))
        {
            return StatusCode(
                503,
                new
                {
                    message = "Configure a assinatura das notificações antes de iniciar cobranças."
                });
        }

        if (formData.ValueKind != JsonValueKind.Object)
        {
            return BadRequest();
        }

        var device = CaptivePortalController.Read(Request, protection);

        if (device is null)
        {
            return BadRequest(new
            {
                message = "Entre pelo portal da rede em /rede/entrada antes de pagar."
            });
        }

        DeviceContext current;

        try
        {
            current = await router.CaptureAsync(HttpContext.Connection.RemoteIpAddress, ct);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new
            {
                message = exception.Message
            });
        }

        if (device.Address != current.Address || device.Gateway != current.Gateway)
        {
            return BadRequest(new
            {
                message = "O endereço IPv4 ou a rede mudou. Entre novamente pelo portal da rede."
            });
        }

        if (!Guid.TryParse(Request.Headers["X-Checkout-Id"].ToString(), out var checkoutId))
        {
            return BadRequest(new
            {
                message = "Identificador do pedido inválido."
            });
        }

        var id = checkoutId.ToString("N");
        await using var gate = await orders.LockAsync(id, ct);
        var order = await orders.GetAsync(id, ct);

        if (order is not null

            && (order.UserId != UserId || order.PlanId != planId || order.Address != current.Address

                || order.Gateway != current.Gateway))
        {
            return Conflict();
        }

        if (order is null)
        {
            var plan = await plans.GetByIdAsync(planId, ct);

            if (plan is null || plan.Price <= 0 || plan.DurationMinutes <= 0)
            {
                return BadRequest(new
                {
                    message = "Plano inválido."
                });
            }

            var method = RouterOsHotspot.Value(formData, "payment_method_id");
            var type = RouterOsHotspot.Value(formData, "payment_type_id");

            if (string.IsNullOrEmpty(type))
            {
                type = RouterOsHotspot.Value(formData, "selected_payment_method");
            }

            if (method != "pix" && type is not ("credit_card" or "debit_card"))
            {
                return BadRequest(new
                {
                    message = "Use PIX ou cartão."
                });
            }

            var email = User.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrWhiteSpace(email)

                || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            {
                return BadRequest(new
                {
                    message = "E-mail inválido."
                });
            }

            var payer = new Dictionary<string, object?> { ["email"] = email };

            if (formData.TryGetProperty("payer", out var payerInput)

                && payerInput.ValueKind == JsonValueKind.Object

                && payerInput.TryGetProperty("identification", out var identity))
            {
                payer["identification"] = identity.Clone();
            }

            var payload = new Dictionary<string, object?>
            {
                ["transaction_amount"] = plan.Price,
                ["description"] = "Áster Link - " + plan.Name,
                ["external_reference"] = id,
                ["notification_url"] = paymentOptions.Value.WebhookUrl,
                ["payment_method_id"] = method,
                ["payer"] = payer
            };

            foreach (var name in new[]
            {
                "token",
                "installments",
                "issuer_id"
            })
            {
                if (formData.TryGetProperty(name, out var value))
                {
                    payload[name] = value.Clone();
                }
            }

            order = new NetworkOrder
            {
                Id = id,
                UserId = UserId,
                PlanId = planId,
                PlanName = plan.Name,
                Price = plan.Price,
                Minutes = plan.DurationMinutes,
                Address = current.Address,
                Gateway = current.Gateway,
                ProtectedPassword = router.ProtectPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24))),
                ProtectedPayload = payloadProtector.Protect(JsonSerializer.Serialize(payload))
            };
            await orders.SaveAsync(order, ct);
        }

        JsonElement payment;

        try
        {
            payment = order.PaymentId.Length > 0
            ? await payments.GetAsync(order.PaymentId, ct)
            : await payments.SendAsync(HttpMethod.Post, "v1/payments", payloadProtector.Unprotect(order.ProtectedPayload), order.Id, ct);
        }
        catch (HttpRequestException)
        {
            return StatusCode(
                502,
                new
                {
                    message = "Não foi possível confirmar a cobrança. Repita este mesmo pedido; não inicie outra compra."
                });
        }

        var paymentId = RouterOsHotspot.Value(payment, "id");

        if (paymentId.Length == 0 || !paymentId.All(char.IsAsciiDigit)

            || !MercadoPagoPayments.Matches(payment, order, paymentOptions.Value))
        {
            return StatusCode(
                502,
                new
                {
                    message = "A resposta do pagamento exige verificação. Acesso não liberado."
                });
        }

        order.PaymentId = paymentId;
        order.PaymentStatus = RouterOsHotspot.Value(payment, "status");
        order.ProtectedPayload = "";
        await orders.SaveAsync(order, ct, enqueue: true);

        JsonElement? transaction = payment.TryGetProperty("point_of_interaction", out var interaction)

            && interaction.TryGetProperty("transaction_data", out var details) ? details.Clone() : null;

        return Json(new
        {
            id = paymentId,
            status = order.PaymentStatus,
            order_url = Url.Action(nameof(Order), new
            {
                id = order.Id
            }),
            point_of_interaction = new
            {
                transaction_data = transaction
            }
        });
    }

    [HttpGet("pedido/{id}")]
    public async Task<IActionResult> Order(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var order = await OwnedAsync(id, ct);

        return order is null ? NotFound() : View(order);
    }

    [HttpGet("pedido/{id}/estado")]
    public async Task<IActionResult> State(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var order = await OwnedAsync(id, ct);

        if (order is null)
        {
            return NotFound();
        }

        var state = order.AccessStatus;

        if (state == "ready")
        {
            try
            {
                var network = await router.StatusAsync(order, ct);
                state = network.Expired ? "expired" : network.Active ? "connected" : "ready";
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                state = "checking_network";
            }
        }

        return Json(new
        {
            payment = order.PaymentStatus,
            access = state
        });
    }

    [HttpPost("pedido/{id}/conectar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";

        if (!Guid.TryParseExact(id, "N", out _))
        {
            return NotFound();
        }

        await using var gate = await orders.LockAsync(id, ct);
        var order = await OwnedAsync(id, ct);

        if (order is null)
        {
            return NotFound();
        }

        if (order.PaymentStatus != "approved" || order.AccessStatus != "ready")
        {
            return Conflict("Aguarde a liberação do pedido.");
        }

        var device = await router.CaptureAsync(HttpContext.Connection.RemoteIpAddress, ct);

        if (device.Address != order.Address || device.Gateway != order.Gateway)
        {
            return Forbid();
        }

        var payment = await payments.GetAsync(order.PaymentId, ct);

        if (!MercadoPagoPayments.Matches(payment, order, paymentOptions.Value)

            || RouterOsHotspot.Value(payment, "status") != "approved")
        {
            await orders.EnqueueAsync(order.PaymentId, ct);

            return Conflict("O pagamento precisa ser verificado.");
        }

        if ((await router.StatusAsync(order, ct)).Expired)
        {
            return Conflict("O acesso está esgotado ou indisponível.");
        }

        if (!await router.LoginAsync(order, ct))
        {
            return Conflict("Dispositivo ausente ou acesso esgotado. Conecte-se à rede HotSpot e tente novamente.");
        }

        return RedirectToAction(nameof(Order), new
        {
            id = order.Id
        });
    }

    private async Task<NetworkOrder?> OwnedAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            return null;
        }

        var order = await orders.GetAsync(id, ct);

        return order?.UserId == UserId ? order : null;
    }

    [HttpGet("sucesso/{sessionId:long}")]
    public async Task<IActionResult> Success(long sessionId, CancellationToken ct)
    {
        var session = await sessions.GetByIdForUserAsync(sessionId, UserId, ct);

        return session is null ? NotFound() : View(new PaymentSuccessViewModel { Session = session });
    }
}
