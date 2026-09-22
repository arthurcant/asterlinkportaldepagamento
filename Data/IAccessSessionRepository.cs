using asterlinkportaldepagamento.Models;

namespace asterlinkportaldepagamento.Data;

public interface IAccessSessionRepository
{
    Task<long> CreateForPaymentAsync(
        long userId,
        InternetPlan plan,
        string paymentId,
        CancellationToken cancellationToken = default);

    Task<AccessSession?> GetByIdForUserAsync(long sessionId, long userId, CancellationToken cancellationToken = default);
}
