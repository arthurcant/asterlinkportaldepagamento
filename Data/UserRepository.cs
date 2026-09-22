using asterlinkportaldepagamento.Models;
using MySqlConnector;

namespace asterlinkportaldepagamento.Data;

public sealed class UserRepository(IConfiguration configuration) : IUserRepository
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("A conexão 'DefaultConnection' não foi configurada.");

    public async Task<User?> FindByLoginAsync(string login, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, name, username, email, password_hash FROM users
            WHERE (email = @login OR username = @login) AND is_active = 1 LIMIT 1;
            """;
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.Add("@login", MySqlDbType.VarChar).Value = NormalizeEmail(login);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new User
        {
            Id = reader.GetInt64("id"),
            Name = reader.GetString("name"),
            Username = reader.GetString("username"),
            Email = reader.GetString("email"),
            PasswordHash = reader.GetString("password_hash")
        };
    }

    public async Task<long> CreateAsync(
        string name,
        string username,
        string email,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        const string sql = "INSERT INTO users (name, username, email, password_hash) VALUES (@name, @username, @email, @passwordHash);";
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.Add("@name", MySqlDbType.VarChar).Value = name.Trim();
        command.Parameters.Add("@username", MySqlDbType.VarChar).Value = username.Trim().ToLowerInvariant();
        command.Parameters.Add("@email", MySqlDbType.VarChar).Value = NormalizeEmail(email);
        command.Parameters.Add("@passwordHash", MySqlDbType.VarChar).Value = passwordHash;
        await command.ExecuteNonQueryAsync(cancellationToken);

        return command.LastInsertedId;
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
