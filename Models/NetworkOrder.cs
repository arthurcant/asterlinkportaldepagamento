namespace asterlinkportaldepagamento.Models;

public sealed class NetworkOrder
{
    public string Id { get; set; } = "";

    public long UserId { get; set; }

    public int PlanId { get; set; }

    public string PlanName { get; set; } = "";

    public decimal Price { get; set; }

    public int Minutes { get; set; }

    public string Mac { get; set; } = "";

    public string Address { get; set; } = "";

    public string Gateway { get; set; } = "";

    public string PaymentId { get; set; } = "";

    public string PaymentStatus { get; set; } = "pending";

    public string AccessStatus { get; set; } = "waiting_payment";

    public string ProtectedPassword { get; set; } = "";

    public string ProtectedPayload { get; set; } = "";

    public string Username => "aster-" + Id;
}

public sealed record DeviceContext(string Mac, string Address, string Gateway, DateTimeOffset Expires);

public sealed record HotspotLoginViewModel(string LoginUrl, string Username, string Password);
