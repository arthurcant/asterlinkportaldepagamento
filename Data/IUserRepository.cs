using asterlinkportaldepagamento.Models;

namespace asterlinkportaldepagamento.Data;

public interface IUserRepository
{
    Task<User?> FindByLoginAsync(string login, CancellationToken cancellationToken = default);

    Task<long> CreateAsync(
        string name,
        string username,
        string email,
        string passwordHash,
        CancellationToken cancellationToken = default);
}
