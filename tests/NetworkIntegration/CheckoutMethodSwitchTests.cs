using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using asterlinkportaldepagamento.Controllers;
using asterlinkportaldepagamento.Data;
using asterlinkportaldepagamento.Models;
using asterlinkportaldepagamento.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class CheckoutMethodSwitchTests
{
    public static async Task RunAsync(Action<bool, string> check, NetworkOptions options, MercadoPagoOptions paymentOptions)
    {
        var invalidPlan = new Fixture(options, paymentOptions, speedMbps: 0);
        invalidPlan.Controller.Request.Headers["X-Checkout-Id"] = Guid.NewGuid().ToString("N");
        var invalidResult = await invalidPlan.Controller.Pay(2, JsonSerializer.SerializeToElement(new
        {
            payment_method_id = "pix"
        }), default);
        check(invalidResult is BadRequestObjectResult && invalidPlan.Handler.Posts == 0,
            "invalid plan speed is rejected before initiating a payment");

        foreach (var type in new[] { "credit_card", "debit_card" })
        {
            var fixture = new Fixture(options, paymentOptions);
            var form = JsonSerializer.SerializeToElement(new { payment_method_id = "visa", selected_payment_method = type, token = "test-card-token", installments = 1 });
            var result = await fixture.Controller.Pay(2, form, default);
            check(result is ConflictObjectResult, $"{type}: old pending Pix is not returned as the card result");
            var response = JsonSerializer.SerializeToElement(((ConflictObjectResult)result).Value);
            var replacement = response.GetProperty("next_checkout_id").GetString()!;
            check(fixture.Handler.Cancellations == 1 && fixture.Handler.Posts == 0,
                $"{type}: Pix cancellation confirmed before any card charge");

            // Replaying the old request must give the same replacement ID.
            var repeated = (ConflictObjectResult)await fixture.Controller.Pay(2, form, default);
            check(JsonSerializer.SerializeToElement(repeated.Value).GetProperty("next_checkout_id").GetString() == replacement,
                $"{type}: replacement checkout ID is stable across retries");

            fixture.Controller.Request.Headers["X-Checkout-Id"] = replacement;
            var paid = (JsonResult)await fixture.Controller.Pay(2, form, default);
            var paidJson = JsonSerializer.SerializeToElement(paid.Value);
            check(paidJson.GetProperty("status").GetString() == "approved"
                && paidJson.GetProperty("payment_method_id").GetString() == "visa"
                && fixture.Handler.Posts == 1 && fixture.Store.Order.PaymentId == "23456"
                && fixture.Store.EnqueuedPayments.Contains("23456"),
                $"{type}: new card payment approved and saved to the replacement order");

            await fixture.Controller.Pay(2, form, default);
            check(fixture.Handler.Posts == 1, $"{type}: repeated submit does not charge the card twice");

            var reconciliation = new PaymentReconciliation(fixture.Store, fixture.Payments, fixture.Router,
                Options.Create(paymentOptions), NullLogger<PaymentReconciliation>.Instance);
            await reconciliation.ProcessAsync("23456", default);
            check(fixture.RouterHandler.Active && fixture.RouterHandler.LoginCount == 1
                && fixture.Store.Order.AccessStatus == "ready", $"{type}: approved replacement provisions and connects through RouterOS");
            check(fixture.Store.Order.EnforceSpeedLimit && fixture.Store.Order.SpeedMbps == 20
                && fixture.RouterHandler.Profiles[fixture.RouterHandler.User!["profile"]]["rate-limit"] == "20M/20M",
                $"{type}: checkout saves plan speed and reconciliation applies it");
        }

        foreach (var scenario in new[] { "approved", "lost-cancel-response", "cancel-refused", "approval-during-cancel", "lookup-failed" })
        {
            var fixture = new Fixture(options, paymentOptions);
            fixture.Handler.Scenario = scenario;
            var form = JsonSerializer.SerializeToElement(new { payment_method_id = "visa", selected_payment_method = "credit_card", token = "test-token" });
            var result = await fixture.Controller.Pay(2, form, default);
            var json = JsonSerializer.SerializeToElement(ObjectResultOrJson(result));
            if (scenario is "approved" or "approval-during-cancel")
            {
                check(result is JsonResult && json.GetProperty("status").GetString() == "approved",
                    scenario + ": follows approved Pix without a second charge");
            }
            else if (scenario == "lost-cancel-response")
            {
                check(result is ConflictObjectResult && json.GetProperty("code").GetString() == "payment_method_changed",
                    "lost cancellation response is reconciled before authorizing replacement");
            }
            else
            {
                check(result is ObjectResult && !json.TryGetProperty("next_checkout_id", out _),
                    scenario + ": uncertainty does not authorize a new charge");
            }
            check(fixture.Handler.Posts == 0, scenario + ": no card payment created prematurely");
        }
    }

    private static object? ObjectResultOrJson(IActionResult result)
    {
        return result is JsonResult json ? json.Value : ((ObjectResult)result).Value;
    }

    private sealed class Fixture
    {
        public SwitchHandler Handler { get; }
        public FakeRouter RouterHandler { get; } = new();
        public MemoryOrderStore Store { get; }
        public MercadoPagoPayments Payments { get; }
        public RouterOsHotspot Router { get; }
        public CheckoutController Controller { get; }

        public Fixture(NetworkOptions options, MercadoPagoOptions paymentOptions, int speedMbps = 20)
        {
            var protection = new EphemeralDataProtectionProvider();
            var old = new NetworkOrder
            {
                Id = Guid.NewGuid().ToString("N"), UserId = 1, PlanId = 2, PlanName = "Test",
                Price = 5m, Minutes = 60, Address = "192.168.88.10", Gateway = "lab", PaymentId = "12345"
            };
            Store = new MemoryOrderStore(old);
            Handler = new SwitchHandler(old.Id);
            var factory = new FakeFactory(RouterHandler, Handler);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MercadoPago:AccessToken"] = "test-token",
                ["MercadoPago:WebhookSecret"] = "test-secret"
            }).Build();
            Payments = new MercadoPagoPayments(factory, config);
            Router = new RouterOsHotspot(factory, Options.Create(options), protection);
            var context = new DefaultHttpContext();
            context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Email, "visitor@example.com")
            }, "test"));
            context.Connection.RemoteIpAddress = IPAddress.Parse(old.Address);
            context.Request.Headers["X-Checkout-Id"] = old.Id;
            var token = protection.CreateProtector("AsterLink.Device.IPv4.v2")
                .Protect(JsonSerializer.Serialize(new DeviceContext(old.Address, old.Gateway, DateTimeOffset.UtcNow.AddHours(1))));
            context.Request.Headers.Cookie = "AsterLink.Device=" + token;
            Controller = new CheckoutController(new TestPlans(speedMbps), null!, Store, Payments, Router, protection,
                Options.Create(options), Options.Create(paymentOptions), config)
            {
                ControllerContext = new ControllerContext { HttpContext = context },
                Url = new TestUrls()
            };
        }
    }

    private sealed class TestPlans(int speedMbps) : IPlanRepository
    {
        public Task<InternetPlan?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<InternetPlan?>(new InternetPlan
            {
                Id = id,
                Name = "Test",
                Price = 5m,
                DurationMinutes = 60,
                SpeedMbps = speedMbps
            });
        }

        public Task<IReadOnlyList<InternetPlan>> GetActiveAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestUrls : IUrlHelper
    {
        public ActionContext ActionContext => new();
        public string? Action(UrlActionContext actionContext) => "/checkout/pedido/" + actionContext.Values!.GetType().GetProperty("id")!.GetValue(actionContext.Values);
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => throw new NotSupportedException();
        public string? RouteUrl(UrlRouteContext routeContext) => throw new NotSupportedException();
    }

    private sealed class SwitchHandler(string oldReference) : HttpMessageHandler
    {
        public string Scenario = "normal";
        public int Cancellations;
        public int Posts;
        private string oldStatus = "pending";
        private string newReference = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Scenario == "approved") oldStatus = "approved";
            if (request.Method == HttpMethod.Put)
            {
                Cancellations++;
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
                if (body.GetProperty("status").GetString() != "cancelled") throw new Exception("Invalid cancellation");
                oldStatus = Scenario == "approval-during-cancel" ? "approved" : Scenario == "cancel-refused" ? "pending" : "cancelled";
                if (Scenario == "lost-cancel-response") throw new HttpRequestException("Lost response");
            }
            if (Scenario == "lookup-failed" && Cancellations > 0 && request.Method == HttpMethod.Get)
            {
                throw new HttpRequestException("Lookup failed");
            }
            if (request.Method == HttpMethod.Post)
            {
                Posts++;
                var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
                newReference = payload.GetProperty("external_reference").GetString()!;
                if (payload.GetProperty("payment_method_id").GetString() != "visa"
                    || payload.GetProperty("token").GetString() != "test-card-token") throw new Exception("Wrong card payload");
            }
            var card = request.Method == HttpMethod.Post || request.RequestUri!.AbsolutePath.EndsWith("/23456");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    id = card ? 23456 : 12345, external_reference = card ? newReference : oldReference,
                    transaction_amount = 5m, currency_id = "BRL", collector_id = 123, live_mode = false,
                    status = card ? "approved" : oldStatus, payment_method_id = card ? "visa" : "pix"
                })
            };
        }
    }
}
