namespace asterlinkportaldepagamento.Models;

public sealed class AccessSession
{
    public long Id { get; init; }

    public long UserId { get; init; }

    public string PlanName { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; init; }

    public string MercadoPagoPaymentId { get; init; } = string.Empty;
}
