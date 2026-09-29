using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using asterlinkportaldepagamento.Models;

namespace asterlinkportaldepagamento.Services;

public sealed class MercadoPagoPayments(IHttpClientFactory clients, IConfiguration configuration)
{
    public async Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        string? payload,
        string? idempotency,
        CancellationToken ct)
    {
        var token = configuration["MercadoPago:AccessToken"];

        if (string.IsNullOrWhiteSpace(token) || token.Contains("TOKEN_AQUI"))
        {
            throw new InvalidOperationException("Configure Mercado Pago.");
        }

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (idempotency is not null)
        {
            request.Headers.Add("X-Idempotency-Key", idempotency);
        }

        if (payload is not null)
        {
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }

        using var response = await clients.CreateClient("MercadoPago").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        return document.RootElement.Clone();
    }

    public Task<JsonElement> GetAsync(string id, CancellationToken ct)
    {
        if (id.Length is 0 or > 80 || !id.All(char.IsAsciiDigit))
        {
            throw new InvalidOperationException("ID de pagamento inválido.");
        }

        return SendAsync(HttpMethod.Get, "v1/payments/" + id, null, null, ct);
    }

    public static bool Matches(JsonElement payment, NetworkOrder order, MercadoPagoOptions options)
    => payment.TryGetProperty("external_reference", out var reference)

        && reference.GetString() == order.Id

        && payment.TryGetProperty("transaction_amount", out var amount)

        && amount.TryGetDecimal(out var price)

        && price == order.Price

        && payment.TryGetProperty("currency_id", out var currency)

        && currency.GetString() == "BRL"

        && payment.TryGetProperty("collector_id", out var collector)

        && collector.ToString() == options.CollectorId

        && payment.TryGetProperty("live_mode", out var live)

        && live.ValueKind is JsonValueKind.True or JsonValueKind.False

        && live.GetBoolean() == options.LiveMode;
}
