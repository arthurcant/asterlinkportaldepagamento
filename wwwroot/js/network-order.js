(function (root, factory) {
    const orderPage = factory();

    if (typeof module === 'object' && module.exports) {
        module.exports = orderPage;
        return;
    }

    root.AsterLinkOrder = orderPage;
    orderPage.start();
})(typeof window === 'undefined' ? globalThis : window, function () {
    const rejectedPayments = new Set(['rejected', 'cancelled', 'refunded', 'charged_back']);

    const states = {
        waiting_payment: {
            title: 'Aguardando pagamento',
            message: 'Aguardando confirmação do pagamento e preparação do acesso.',
            tone: 'pending',
        },
        provisioning: {
            title: 'Pagamento confirmado!',
            message: 'Pagamento aprovado. Preparando seu acesso na rede.',
            tone: 'pending',
        },
        ready: {
            title: 'Pagamento confirmado!',
            message: 'Conectando seu dispositivo à rede. Você também pode tentar conectar pelo botão.',
            tone: 'pending',
            showConnect: true,
        },
        connected: {
            title: 'Pagamento confirmado!',
            message: 'Seu acesso à internet foi liberado. Aproveite!',
            tone: 'success',
        },
        expired: {
            title: 'Acesso encerrado',
            message: 'O acesso foi esgotado ou está indisponível no roteador.',
            tone: 'expired',
        },
        revoked: {
            title: 'Acesso cancelado',
            message: 'O acesso foi cancelado.',
            tone: 'error',
        },
        superseded: {
            title: 'Plano substituído',
            message: 'Um novo plano foi comprado para este dispositivo. Este acesso foi encerrado.',
            tone: 'expired',
        },
        checking_network: {
            title: 'Pagamento confirmado!',
            message: 'Pagamento registrado. Aguardando comunicação com a rede.',
            tone: 'pending',
        },
    };

    function formatDuration(minutes) {
        if (minutes === 60) return '1 hora';
        if (minutes > 60 && minutes % 60 === 0) return `${minutes / 60} horas`;
        return `${minutes} minutos`;
    }

    function formatExpiration(value) {
        if (!value) return 'Aguardando conexão';

        const instant = new Date(value);
        if (Number.isNaN(instant.getTime())) return 'Aguardando conexão';

        return new Intl.DateTimeFormat('pt-BR', {
            hour: '2-digit',
            minute: '2-digit',
            hour12: false,
            timeZone: 'America/Recife',
        }).format(instant);
    }

    function presentationFor(state) {
        if (rejectedPayments.has(state.payment)) {
            return {
                title: 'Pagamento não confirmado',
                message: 'Pagamento recusado ou cancelado. Nenhum novo acesso será liberado.',
                tone: 'error',
                showConnect: false,
            };
        }

        const presentation = states[state.access] || {
            title: 'Verificando acesso',
            message: 'Verificando pagamento e acesso…',
            tone: 'pending',
        };

        return {
            ...presentation,
            showConnect: presentation.showConnect === true,
        };
    }

    function start() {
        const script = document.currentScript;
        const id = script?.dataset.order;
        const page = document.querySelector('[data-order-page]');
        const title = document.getElementById('order-title');
        const label = document.getElementById('network-state');
        const form = document.getElementById('connect-form');
        const expiration = document.getElementById('order-expiration');
        const duration = document.getElementById('order-duration');

        if (!id || !page || !title || !label || !form || !expiration || !duration) return;

        duration.textContent = formatDuration(Number(duration.dataset.minutes));

        async function refresh() {
            try {
                const response = await fetch(`/checkout/pedido/${id}/estado`, {
                    cache: 'no-store',
                    redirect: 'error',
                });
                if (!response.ok) throw new Error();

                const state = await response.json();
                const presentation = presentationFor(state);
                page.dataset.tone = presentation.tone;
                title.textContent = presentation.title;
                label.textContent = presentation.message;
                form.hidden = !presentation.showConnect;
                expiration.textContent = state.access === 'expired'
                    ? 'Plano expirado'
                    : state.access === 'superseded' ? 'Plano substituído'
                    : formatExpiration(state.expires_at);
            } catch {
                page.dataset.tone = 'pending';
                form.hidden = true;
                label.textContent =
                    'Não foi possível atualizar. O pedido foi preservado; a consulta será repetida.';
            }
            setTimeout(refresh, 5000);
        }

        refresh();
    }

    return { formatDuration, formatExpiration, presentationFor, start };
});
