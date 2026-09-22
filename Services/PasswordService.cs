using System.Security.Cryptography;

namespace asterlinkportaldepagamento.Services;

public sealed class PasswordService : IPasswordService
{
    private const int Iterations = 210_000;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);

        return $"pbkdf2_sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encodedHash)
    {
        try
        {
            var parts = encodedHash.Split('$');

            if (parts.Length != 4 || parts[0] != "pbkdf2_sha256")
            {
                return false;
            }

            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                Convert.FromBase64String(parts[2]),
                int.Parse(parts[1]),
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
