using System.Text.Json;
using asterlinkportaldepagamento.Models;
using MySqlConnector;

namespace asterlinkportaldepagamento.Data;

public sealed class NetworkOrderRepository(IConfiguration configuration) : INetworkOrderStore
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Conexão não configurada.");

    public async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new MySqlConnection(ConnectionString);

        try
        {
            await connection.OpenAsync(ct);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    // The dedicated connection owns the advisory lock; disposal releases it on every exit path.
    public async Task<IAsyncDisposable> LockAsync(string id, CancellationToken ct)
    {
        var connection = await OpenAsync(ct);

        try
        {
            await using var command = new MySqlCommand("SELECT GET_LOCK(@key, 10)", connection);
            command.Parameters.AddWithValue("@key", "aster:" + id);

            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 1)
            {
                throw new TimeoutException("Pedido ocupado.");
            }

            return new OrderLock(connection, "aster:" + id);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class OrderLock(MySqlConnection connection, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new MySqlCommand("SELECT RELEASE_LOCK(@key)", connection);
                command.Parameters.AddWithValue("@key", key);
                await command.ExecuteScalarAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }

    public async Task<NetworkOrder?> GetAsync(string id, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new MySqlCommand("SELECT document FROM network_orders WHERE id=@id", connection);
        command.Parameters.AddWithValue("@id", id);
        var json = await command.ExecuteScalarAsync(ct) as string;

        return json is null ? null : JsonSerializer.Deserialize<NetworkOrder>(json);
    }

    // Call only while holding the order lock. Indexed identity is immutable after insertion.
    public async Task SaveAsync(NetworkOrder order, CancellationToken ct, bool enqueue = false)
    {
        await using var connection = await OpenAsync(ct);
        var exists = await GetAsync(order.Id, ct) is not null;
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new MySqlCommand(
            exists
            ? "UPDATE network_orders SET document=@document,payment_id=@payment WHERE id=@id AND user_id=@user"
            : "INSERT INTO network_orders(id,user_id,payment_id,document) VALUES(@id,@user,@payment,@document)",
            connection);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("@id", order.Id);
        command.Parameters.AddWithValue("@user", order.UserId);
        command.Parameters.AddWithValue("@payment", string.IsNullOrEmpty(order.PaymentId) ? null : order.PaymentId);
        command.Parameters.AddWithValue("@document", JsonSerializer.Serialize(order));
        await command.ExecuteNonQueryAsync(ct);

        if (enqueue && order.PaymentId.Length > 0)
        {
            await using var queued = new MySqlCommand(
                "INSERT INTO payment_notifications(payment_id,revision,due_at) VALUES(@id,1,UTC_TIMESTAMP()) ON DUPLICATE KEY UPDATE revision=revision+1,due_at=UTC_TIMESTAMP()",
                connection,
                transaction);
            queued.Parameters.AddWithValue("@id", order.PaymentId);
            await queued.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task EnqueueAsync(string paymentId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new MySqlCommand(
            """
            INSERT INTO payment_notifications(payment_id,revision,due_at) VALUES(@id,1,UTC_TIMESTAMP())
            ON DUPLICATE KEY UPDATE revision=revision+1,due_at=UTC_TIMESTAMP()
            """,
            connection);
        command.Parameters.AddWithValue("@id", paymentId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<(string Id, long Revision)>> PendingAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new MySqlCommand(
            "SELECT payment_id,revision FROM payment_notifications WHERE due_at<=UTC_TIMESTAMP() ORDER BY due_at LIMIT 20",
            connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<(string, long)>();

        while (await reader.ReadAsync(ct))
        {
            result.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return result;
    }

    public async Task FinishAsync(string id, long revision, bool retry, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new MySqlCommand(
            retry
            ? "UPDATE payment_notifications SET due_at=DATE_ADD(UTC_TIMESTAMP(), INTERVAL 30 SECOND) WHERE payment_id=@id AND revision=@revision"
            : "DELETE FROM payment_notifications WHERE payment_id=@id AND revision=@revision",
            connection);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@revision", revision);
        await command.ExecuteNonQueryAsync(ct);
    }
}
