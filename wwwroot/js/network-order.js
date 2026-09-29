(() => {
    const id = document.currentScript.dataset.order;
    const label = document.getElementById('network-state');
    const form = document.getElementById('connect-form');
    const messages = {
        waiting_payment: 'Aguardando confirmação do pagamento e preparação do acesso.',
        provisioning: 'Pagamento aprovado. Preparando seu acesso na rede.',
        ready: 'Pagamento aprovado. Conectando seu dispositivo à rede. Você também pode tentar conectar pelo botão.',
        connected: 'Dispositivo conectado. O roteador controla o tempo restante.',
        expired: 'O acesso foi esgotado ou está indisponível no roteador.',
        revoked: 'O acesso foi cancelado.',
        checking_network: 'Pagamento registrado. Aguardando comunicação com a rede.',
    };
    async function refresh() {
        try {
            const response = await fetch(`/checkout/pedido/${id}/estado`, {
                cache: 'no-store',
                redirect: 'error',
            });
            if (!response.ok) throw new Error();
            const state = await response.json();
            form.hidden = state.access !== 'ready';
            label.textContent = ['rejected', 'cancelled', 'refunded', 'charged_back'].includes(
                state.payment,
            )
                ? 'Pagamento recusado ou cancelado. Nenhum novo acesso será liberado.'
                : messages[state.access] || 'Verificando acesso…';
        } catch {
            form.hidden = true;
            label.textContent =
                'Não foi possível atualizar. O pedido foi preservado; a consulta será repetida.';
        }
        setTimeout(refresh, 5000);
    }
    refresh();
})();
