using asterlinkportaldepagamento.Models;
using MySqlConnector;

namespace asterlinkportaldepagamento.Data;

public sealed class PlanRepository(IConfiguration configuration) : IPlanRepository
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("A conexão 'DefaultConnection' não foi configurada.");

    public async Task<IReadOnlyList<InternetPlan>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT id, name, duration_minutes, price, is_popular FROM internet_plans WHERE is_active = 1 ORDER BY display_order, duration_minutes;";
        var plans = new List<InternetPlan>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            plans.Add(new InternetPlan
            {
                Id = reader.GetInt32("id"),
                Name = reader.GetString("name"),
                DurationMinutes = reader.GetInt32("duration_minutes"),
                Price = reader.GetDecimal("price"),
                IsPopular = reader.GetBoolean("is_popular")
            });
        }

        return plans;
    }

    public async Task<InternetPlan?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT id, name, duration_minutes, price, is_popular FROM internet_plans WHERE id = @id AND is_active = 1 LIMIT 1;";
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.Add("@id", MySqlDbType.Int32).Value = id;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new InternetPlan
        {
            Id = reader.GetInt32("id"),
            Name = reader.GetString("name"),
            DurationMinutes = reader.GetInt32("duration_minutes"),
            Price = reader.GetDecimal("price"),
            IsPopular = reader.GetBoolean("is_popular")
        };
    }
}
