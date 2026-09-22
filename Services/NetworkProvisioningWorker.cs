using asterlinkportaldepagamento.Data;
using Microsoft.Extensions.Options;

namespace asterlinkportaldepagamento.Services;

public sealed class NetworkProvisioningWorker(
    IServiceScopeFactory scopes,
    IOptions<NetworkOptions> options,
    ILogger<NetworkProvisioningWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.Value.Enabled)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var repository = scope.ServiceProvider.GetRequiredService<NetworkOrderRepository>();

                    foreach (var job in await repository.PendingAsync(stoppingToken))
                    {
                        var retry = true;

                        try
                        {
                            retry = await scope.ServiceProvider.GetRequiredService<PaymentReconciliation>().ProcessAsync(job.Id, stoppingToken);
                        }
                        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                        {
                            logger.LogWarning(
                                "Falha ao conciliar pagamento {PaymentId}: {ErrorType}. Nova tentativa pendente.",
                                job.Id,
                                exception.GetType().Name);
                        }

                        await repository.FinishAsync(job.Id, job.Revision, retry, stoppingToken);
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Fila da rede indisponível: {ErrorType}.", exception.GetType().Name);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

public sealed class PaymentReconciliation(
    NetworkOrderRepository orders,
    MercadoPagoPayments payments,
    RouterOsHotspot router,
    IOptions<NetworkOptions> options,
    ILogger<PaymentReconciliation> logger)
{
    public async Task<bool> ProcessAsync(string paymentId, CancellationToken ct)
    {
        var payment = await payments.GetAsync(paymentId, ct);
        var reference = RouterOsHotspot.Value(payment, "external_reference");

        if (!Guid.TryParseExact(reference, "N", out _))
        {
            return false;
        }

        await using var gate = await orders.LockAsync(reference, ct);
        var order = await orders.GetAsync(reference, ct);

        if (order is null)
        {
            return false;
        }

        // Refresh under the lock so an old response cannot overwrite a later refund.
        payment = await payments.GetAsync(paymentId, ct);

        if (RouterOsHotspot.Value(payment, "id") != paymentId

            || !MercadoPagoPayments.Matches(payment, order, options.Value)

            || (order.PaymentId.Length > 0 && order.PaymentId != paymentId))
        {
            logger.LogError("Pagamento {PaymentId} diverge do pedido {OrderId}; liberação recusada.", paymentId, order.Id);

            return false;
        }

        order.PaymentId = paymentId;
        order.PaymentStatus = RouterOsHotspot.Value(payment, "status");
        order.ProtectedPayload = "";

        if (order.PaymentStatus == "approved" && order.AccessStatus != "revoked")
        {
            if (order.AccessStatus != "ready")
            {
                order.AccessStatus = "provisioning";
            }

            await orders.SaveAsync(order, ct);
            await router.EnsureUserAsync(order, ct);
            order.AccessStatus = "ready";
        }
        else if (order.PaymentStatus is "refunded" or "charged_back" or "cancelled")
        {
            await router.RevokeAsync(order, ct);
            order.AccessStatus = "revoked";
        }

        await orders.SaveAsync(order, ct);

        return order.PaymentStatus is "pending" or "in_process" or "authorized";
    }
}
