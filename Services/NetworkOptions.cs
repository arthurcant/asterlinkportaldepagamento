using System.Net;
using System.Net.Sockets;

namespace asterlinkportaldepagamento.Services;

public sealed class NetworkOptions
{
    public bool Enabled { get; set; }

    public string GatewayId { get; set; } = "";

    public string RestUrl { get; set; } = "";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public string HotspotServer { get; set; } = "";

    public string UserProfile { get; set; } = "";

    public string ClientSubnet { get; set; } = "";

    public int RequestTimeoutSeconds { get; set; } = 15;

    public bool AllowInsecureHttpForDevelopment { get; set; }

    public void Validate()
    {
        if (!Enabled)
        {
            throw new InvalidOperationException("Integração REST do RouterOS desativada.");
        }

        if (!Uri.TryCreate(RestUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && AllowInsecureHttpForDevelopment))
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath.TrimEnd('/') != "/rest")
        {
            throw new InvalidOperationException("Configure Network:RestUrl terminado em /rest/. HTTP exige permissão explícita em desenvolvimento.");
        }

        if (new[] { GatewayId, Username, Password, HotspotServer, UserProfile }.Any(string.IsNullOrWhiteSpace)
            || Username.Contains(':'))
        {
            throw new InvalidOperationException("Preencha GatewayId, Username, Password, HotspotServer e UserProfile em Network.");
        }

        if (!IPNetwork.TryParse(ClientSubnet, out var subnet)
            || subnet.BaseAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidOperationException("Network:ClientSubnet deve conter a rede IPv4/CIDR real dos clientes HotSpot.");
        }

        if (RequestTimeoutSeconds is < 1 or > 60)
        {
            throw new InvalidOperationException("Network:RequestTimeoutSeconds deve estar entre 1 e 60.");
        }
    }
}
