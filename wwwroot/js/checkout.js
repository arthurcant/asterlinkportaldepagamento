(() => {
    const config = window.asterCheckout;
    const message = document.getElementById('checkout-message');
    const pixPanel = document.getElementById('pix-payment');
    const pixImage = document.getElementById('pix-qr-code');
    const pixCode = document.getElementById('pix-copy-code');
    const copyButton = document.getElementById('copy-pix-code');
    const ticketLink = document.getElementById('pix-ticket-link');
    const paymentContainer = document.getElementById('paymentBrick_container');
    const storageKey = `aster-checkout:${config.payerEmail}:${config.planId}`;
    let checkoutId = sessionStorage.getItem(storageKey) || crypto.randomUUID();
    sessionStorage.setItem(storageKey, checkoutId);
    let tracking = false;
    let submitError = false;
    const trackOrder = (url) => {
        if (tracking) return;
        tracking = true;
        const poll = async () => {
            try {
                const response = await fetch(`${url}/estado`, {
                    cache: 'no-store',
                    redirect: 'error',
                });
                if (!response.ok) throw new Error();
                const state = await response.json();
                if (
                    state.payment === 'approved' ||
                    ['ready', 'connected', 'revoked', 'expired'].includes(state.access)
                ) {
                    sessionStorage.removeItem(storageKey);
                    window.location.assign(url);
                    return;
                }
                if (['rejected', 'cancelled', 'refunded', 'charged_back'].includes(state.payment)) {
                    sessionStorage.removeItem(storageKey);
                    window.location.assign(url);
                    return;
                }
            } catch {
                show('Pedido preservado. Tentando atualizar a confirmação do pagamento.');
            }
            setTimeout(poll, 5000);
        };
        poll();
    };
    let paymentBrickController = null;
    let recreatingBrick = false;
    const show = (text, error = false) => {
        message.hidden = false;
        message.classList.toggle('error', error);
        message.textContent = text;
    };

    const showPix = (data) => {
        const transaction = data?.point_of_interaction?.transaction_data;
        if (!transaction?.qr_code || !transaction?.qr_code_base64) return false;
        pixImage.src = `data:image/png;base64,${transaction.qr_code_base64}`;
        pixCode.value = transaction.qr_code;
        ticketLink.hidden = !transaction.ticket_url;
        if (transaction.ticket_url) ticketLink.href = transaction.ticket_url;
        pixPanel.hidden = false;
        document.getElementById('paymentBrick_container').hidden = true;
        show('Pagamento PIX gerado. Aguarde a confirmação após pagar.', false);
        return true;
    };

    copyButton.addEventListener('click', async () => {
        try {
            await navigator.clipboard.writeText(pixCode.value);
            copyButton.textContent = 'Código PIX copiado!';
            setTimeout(() => {
                copyButton.textContent = 'Copiar código PIX';
            }, 2500);
        } catch {
            pixCode.focus();
            pixCode.select();
            document.execCommand('copy');
            copyButton.textContent = 'Código PIX copiado!';
            setTimeout(() => {
                copyButton.textContent = 'Copiar código PIX';
            }, 2500);
        }
    });

    const resetPaymentForm = async () => {
        if (recreatingBrick) return;
        recreatingBrick = true;
        try {
            if (paymentBrickController) {
                await paymentBrickController.unmount();
                paymentBrickController = null;
            }
            paymentContainer.hidden = false;
            paymentContainer.replaceChildren();
            await createPaymentBrick();
        } catch (error) {
            console.error(error);
            show('Não foi possível carregar novamente o formulário de pagamento.', true);
        } finally {
            recreatingBrick = false;
        }
    };

    const createPaymentBrick = async () => {
        const bricks = new MercadoPago(config.publicKey, { locale: 'pt-BR' }).bricks();
        const settings = {
            initialization: { amount: config.amount, payer: { email: config.payerEmail } },
            customization: {
                paymentMethods: { bankTransfer: ['pix'], creditCard: 'all', debitCard: 'all' },
                visual: { style: { theme: 'dark' } },
            },
            callbacks: {
                onReady: () => paymentContainer.setAttribute('aria-busy', 'false'),
                onSubmit: ({ selectedPaymentMethod, formData }) => {
                    submitError = false;
                    paymentContainer.setAttribute('aria-busy', 'true');
                    return fetch(`/checkout/${config.planId}/pagar`, {
                        method: 'POST',
                        redirect: 'error',
                        headers: {
                            'Content-Type': 'application/json',
                            'X-CSRF-TOKEN': document.querySelector(
                                'input[name="__RequestVerificationToken"]',
                            ).value,
                            'X-Checkout-Id': checkoutId,
                        },
                        body: JSON.stringify({
                            ...formData,
                            selected_payment_method: selectedPaymentMethod,
                        }),
                    })
                        .then(async (response) => {
                            const data = await response.json().catch(() => {
                                throw new Error(
                                    'O servidor não retornou uma resposta de pagamento válida. Verifique sua sessão e tente novamente.',
                                );
                            });
                            if (!response.ok)
                                throw new Error(data.message || 'Pagamento não autorizado.');
                            if (data.status === 'approved' && data.order_url) {
                                sessionStorage.removeItem(storageKey);
                                window.location.assign(data.order_url);
                                return data;
                            }
                            if (
                                data.order_url &&
                                !['rejected', 'cancelled', 'charged_back'].includes(data.status)
                            )
                                trackOrder(data.order_url);
                            if (data.status === 'pending' && showPix(data)) return data;
                            if (['rejected', 'cancelled', 'charged_back'].includes(data.status)) {
                                checkoutId = crypto.randomUUID();
                                sessionStorage.setItem(storageKey, checkoutId);
                                show(
                                    'O cartão não foi aprovado. Confira os dados e tente novamente.',
                                    true,
                                );
                                await resetPaymentForm();
                                return data;
                            }
                            show(`Pagamento recebido com status: ${data.status || 'pendente'}.`);
                            return data;
                        })
                        .catch(async (error) => {
                            submitError = true;
                            paymentContainer.setAttribute('aria-busy', 'false');
                            show(
                                error.message || 'Não foi possível confirmar o pagamento. Tente novamente com o mesmo pedido.',
                                true,
                            );
                            throw error;
                        });
                },
                onError: (error) => {
                    console.error(error);
                    paymentContainer.setAttribute('aria-busy', 'false');
                    if (!submitError) {
                        show('Não foi possível carregar ou processar o pagamento.', true);
                    }
                },
            },
        };
        paymentBrickController = await bricks.create('payment', 'paymentBrick_container', settings);
        window.paymentBrickController = paymentBrickController;
    };

    if (!config.publicKey || config.publicKey.includes('PUBLIC_KEY_AQUI')) {
        show('Configure a Public Key do Mercado Pago no appsettings.json.', true);
        return;
    }
    createPaymentBrick().catch((error) => {
        console.error(error);
        show('Não foi possível carregar o formulário de pagamento.', true);
    });
    window.addEventListener('beforeunload', () => paymentBrickController?.unmount());
})();
