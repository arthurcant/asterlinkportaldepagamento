namespace asterlinkportaldepagamento.Models;

public sealed class CheckoutViewModel
{
    public required InternetPlan Plan { get; init; }

    public string PublicKey { get; init; } = string.Empty;

    public string PayerEmail { get; init; } = string.Empty;
}
