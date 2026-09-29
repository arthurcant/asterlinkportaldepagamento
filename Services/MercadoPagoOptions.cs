namespace asterlinkportaldepagamento.Services;

public sealed class MercadoPagoOptions
{
    public string WebhookUrl { get; set; } = "";

    public string CollectorId { get; set; } = "";

    public bool LiveMode { get; set; }

    public void Validate()
    {
        if (!Uri.TryCreate(WebhookUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("Preencha MercadoPago:WebhookUrl com a URL HTTPS pública do webhook.");
        }

        if (string.IsNullOrWhiteSpace(CollectorId) || !CollectorId.All(char.IsAsciiDigit))
        {
            throw new InvalidOperationException("Preencha MercadoPago:CollectorId com o ID real da conta recebedora.");
        }
    }
}
