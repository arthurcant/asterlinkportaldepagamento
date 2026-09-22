namespace asterlinkportaldepagamento.Services;

public sealed class NetworkOptions
{
    public bool Enabled { get; set; }

    public string GatewayId { get; set; } = "arena-lab";

    public string RestUrl { get; set; } = "";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public string HotspotServer { get; set; } = "";

    public string UserProfile { get; set; } = "";

    public string LoginUrl { get; set; } = "";

    public string ClientSubnet { get; set; } = "";

    public string WebhookUrl { get; set; } = "";

    public string CollectorId { get; set; } = "";

    public bool LiveMode { get; set; }

    public void Validate()
    {
        if (!Enabled)
        {
            throw new InvalidOperationException("Integração de rede desativada.");
        }

        foreach (var url in new[]
        {
            RestUrl,
            LoginUrl,
            WebhookUrl
        })
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https"

                || !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidOperationException("Configure URLs HTTPS válidas para a rede e o webhook.");
            }
        }

        if (new Uri(RestUrl).AbsolutePath.TrimEnd('/') != "/rest")
        {
            throw new InvalidOperationException("RestUrl deve terminar em /rest/.");
        }

        if (new[]
        {
            GatewayId,
            Username,
            Password,
            HotspotServer,
            UserProfile,
            ClientSubnet,
            CollectorId
        }.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Configuração da integração de rede incompleta.");
        }
    }
}
