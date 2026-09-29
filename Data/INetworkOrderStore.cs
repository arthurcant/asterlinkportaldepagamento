using asterlinkportaldepagamento.Models;

namespace asterlinkportaldepagamento.Data;

public interface INetworkOrderStore
{
    Task<IAsyncDisposable> LockAsync(string id, CancellationToken ct);

    Task<NetworkOrder?> GetAsync(string id, CancellationToken ct);

    Task SaveAsync(NetworkOrder order, CancellationToken ct, bool enqueue = false);
}
