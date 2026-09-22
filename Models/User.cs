namespace asterlinkportaldepagamento.Models;

public sealed class User
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Username { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string PasswordHash { get; init; } = string.Empty;
}
