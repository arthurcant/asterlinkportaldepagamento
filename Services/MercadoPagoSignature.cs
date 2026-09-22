using System.Security.Cryptography;
using System.Text;

namespace asterlinkportaldepagamento.Services;

public static class MercadoPagoSignature
{
    // Official HMAC manifest uses data.id from the query, not the JSON body.
    public static bool Verify(string signature, string requestId, string dataId, string secret)
    {
        if (new[]
        {
            signature,
            requestId,
            dataId,
            secret
        }.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        if (signature.Length > 1024 || requestId.Length > 200 || dataId.Length > 80)
        {
            return false;
        }

        string? timestamp = null;
        var hashes = new List<string>();

        foreach (var part in signature.Split(','))
        {
            var pair = part.Trim().Split('=', 2);

            if (pair.Length != 2)
            {
                return false;
            }

            if (pair[0] == "ts")
            {
                if (timestamp is not null)
                {
                    return false;
                }

                timestamp = pair[1];
            }

            if (pair[0] == "v1")
            {
                hashes.Add(pair[1]);
            }
        }

        if (!long.TryParse(timestamp, out var ts) || ts <= 0)
        {
            return false;
        }

        var manifest = $"id:{dataId.ToLowerInvariant()};request-id:{requestId};ts:{timestamp};";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(manifest));

        foreach (var hash in hashes)
        {
            if (hash.Length != 64)
            {
                continue;
            }

            try
            {
                if (CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(hash)))
                {
                    return true;
                }
            }
            catch (FormatException)
            {
            }
        }

        return false;
    }
}
