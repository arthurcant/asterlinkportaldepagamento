namespace asterlinkportaldepagamento.Models;

public sealed class InternetPlan
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int DurationMinutes { get; init; }

    public decimal Price { get; init; }

    public bool IsPopular { get; init; }
}
