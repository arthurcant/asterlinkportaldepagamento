using System.Security.Cryptography;
using System.Text;
using asterlinkportaldepagamento.Data;
using asterlinkportaldepagamento.Models;

namespace asterlinkportaldepagamento.Services;

// Call with the device advisory lock held before locking the new order.
public sealed class NetworkAccessActivator(RouterOsHotspot router, INetworkOrderStore orders)
{
    public static string DeviceLockId(NetworkOrder order)
    {
        var identity = Encoding.UTF8.GetBytes(order.Gateway + ":" + order.Address);
        return "device:" + Convert.ToHexString(SHA256.HashData(identity))[..32];
    }

    public async Task<bool> ActivateAsync(NetworkOrder order, CancellationToken ct)
    {
        if (order.PaymentStatus != "approved" || order.AccessStatus != "ready")
        {
            throw new InvalidOperationException("Somente um pedido aprovado e provisionado pode substituir o acesso.");
        }

        if (order.SupersededOrderId.Length > 0)
        {
            await CompleteReplacementAsync(order, order.SupersededOrderId, ct);
        }
        else
        {
            var username = await router.ActiveUserForDeviceAsync(order, ct);
            if (username is not null && username != order.Username)
            {
                const string prefix = "aster-";
                var oldId = username.StartsWith(prefix, StringComparison.Ordinal)
                    ? username[prefix.Length..] : "";

                if (!Guid.TryParseExact(oldId, "N", out _))
                {
                    throw new InvalidOperationException("Há uma sessão HotSpot não reconhecida neste IPv4; substituição recusada.");
                }

                await using var oldGate = await orders.LockAsync(oldId, ct);
                var previous = await RequirePreviousAsync(order, oldId, recovering: false, ct);
                var oldStatus = await router.StatusAsync(previous, ct);

                if (!oldStatus.Active)
                {
                    throw new InvalidOperationException("A sessão anterior não corresponde ao pedido registrado; substituição recusada.");
                }

                order.SupersededOrderId = oldId;
                await orders.SaveAsync(order, ct);
                previous.AccessStatus = "superseded";
                await orders.SaveAsync(previous, ct);
                await router.RemoveSupersededUserAsync(previous, ct);
            }
        }

        return await router.LoginAsync(order, ct);
    }

    private async Task CompleteReplacementAsync(NetworkOrder order, string oldId, CancellationToken ct)
    {
        await using var oldGate = await orders.LockAsync(oldId, ct);
        var previous = await RequirePreviousAsync(order, oldId, recovering: true, ct);

        if (previous.AccessStatus == "ready")
        {
            previous.AccessStatus = "superseded";
            await orders.SaveAsync(previous, ct);
        }

        await router.RemoveSupersededUserAsync(previous, ct);
    }

    private async Task<NetworkOrder> RequirePreviousAsync(
        NetworkOrder order,
        string oldId,
        bool recovering,
        CancellationToken ct)
    {
        var previous = await orders.GetAsync(oldId, ct);

        if (previous is null || previous.Id == order.Id
            || previous.Username != "aster-" + oldId
            || previous.UserId != order.UserId
            || previous.Gateway != order.Gateway
            || previous.Address != order.Address
            || (recovering
                ? previous.AccessStatus is not ("ready" or "superseded" or "revoked")
                : previous.AccessStatus != "ready")
            || previous.AccessStatus == "ready" && previous.PaymentStatus != "approved")
        {
            throw new InvalidOperationException("O acesso anterior não pertence a esta conta e dispositivo; substituição recusada.");
        }

        return previous;
    }
}
