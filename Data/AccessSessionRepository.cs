using asterlinkportaldepagamento.Models;
using MySqlConnector;

namespace asterlinkportaldepagamento.Data;

public sealed class AccessSessionRepository(IConfiguration configuration) : IAccessSessionRepository
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("A conexão 'DefaultConnection' não foi configurada.");

    public async Task<long> CreateForPaymentAsync(
        long userId,
        InternetPlan plan,
        string paymentId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO access_sessions (user_id, plan_id, mercado_pago_payment_id, started_at, expires_at)
            VALUES (@userId, @planId, @paymentId, @startedAt, @expiresAt)
            ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id);
            """;
        var startedAt = DateTimeOffset.UtcNow;
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.Add("@userId", MySqlDbType.Int64).Value = userId;
        command.Parameters.Add("@planId", MySqlDbType.Int32).Value = plan.Id;
        command.Parameters.Add("@paymentId", MySqlDbType.VarChar).Value = paymentId;
        command.Parameters.Add("@startedAt", MySqlDbType.DateTime).Value = startedAt.UtcDateTime;
        command.Parameters.Add("@expiresAt", MySqlDbType.DateTime).Value = startedAt.AddMinutes(plan.DurationMinutes).UtcDateTime;
        await command.ExecuteNonQueryAsync(cancellationToken);

        return command.LastInsertedId;
    }

    public async Task<AccessSession?> GetByIdForUserAsync(long sessionId, long userId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT s.id, s.user_id, s.expires_at, s.mercado_pago_payment_id, p.name
            FROM access_sessions s INNER JOIN internet_plans p ON p.id = s.plan_id
            WHERE s.id = @sessionId AND s.user_id = @userId LIMIT 1;
            """;
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.Add("@sessionId", MySqlDbType.Int64).Value = sessionId;
        command.Parameters.Add("@userId", MySqlDbType.Int64).Value = userId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AccessSession
        {
            Id = reader.GetInt64("id"),
            UserId = reader.GetInt64("user_id"),
            PlanName = reader.GetString("name"),
            ExpiresAt = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime("expires_at"), DateTimeKind.Utc)),
            MercadoPagoPaymentId = reader.GetString("mercado_pago_payment_id")
        };
    }
}
