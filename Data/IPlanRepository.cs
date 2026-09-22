using asterlinkportaldepagamento.Models;

namespace asterlinkportaldepagamento.Data;

public interface IPlanRepository
{
    Task<IReadOnlyList<InternetPlan>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<InternetPlan?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}
