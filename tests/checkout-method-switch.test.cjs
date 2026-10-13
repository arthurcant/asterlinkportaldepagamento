const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

function checkout(responses) {
    const elements = new Map();
    const storage = new Map([['aster-checkout:visitor@example.com:2', 'old-pix-order']]);
    const requests = [];
    const navigations = [];
    let settings;
    const context = {
        window: {
            asterCheckout: { payerEmail: 'visitor@example.com', planId: 2, publicKey: 'test', amount: 10 },
            location: { assign: (url) => navigations.push(url) },
            addEventListener() {},
        },
        document: {
            getElementById(id) {
                if (!elements.has(id)) {
                    elements.set(id, { hidden: true, classList: { toggle() {} }, setAttribute() {}, addEventListener() {} });
                }
                return elements.get(id);
            },
            querySelector: () => ({ value: 'csrf' }),
        },
        sessionStorage: {
            getItem: (key) => storage.get(key),
            setItem: (key, value) => storage.set(key, value),
            removeItem: (key) => storage.delete(key),
        },
        crypto: { randomUUID: () => 'unexpected-random-id' },
        MercadoPago: function () {
            return { bricks: () => ({ create: async (_type, _id, options) => { settings = options; return {}; } }) };
        },
        fetch: async (url, options) => {
            requests.push({ url, ...options });
            const response = responses.shift();
            if (response instanceof Error) throw response;
            return { ok: response.status === 200, status: response.status, json: async () => response.body };
        },
        console,
        setTimeout() {},
    };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/checkout.js'), 'utf8'), context);
    return { submit: (type) => settings.callbacks.onSubmit({ selectedPaymentMethod: type, formData: { payment_method_id: 'visa', token: 'card-token' } }), requests, navigations, elements, storage };
}

for (const type of ['credit_card', 'debit_card']) {
    test(`returning from Pix to ${type} submits the card using the server-authorized replacement`, async () => {
        const app = checkout([
            { status: 409, body: { code: 'payment_method_changed', next_checkout_id: 'replacement-order' } },
            { status: 200, body: { status: 'approved', order_url: '/checkout/pedido/card-order' } },
        ]);
        await app.submit(type);
        assert.equal(app.requests.length, 2);
        assert.equal(app.requests[0].headers['X-Checkout-Id'], 'old-pix-order');
        assert.equal(app.requests[1].headers['X-Checkout-Id'], 'replacement-order');
        assert.equal(JSON.parse(app.requests[1].body).token, 'card-token');
        assert.deepEqual(app.navigations, ['/checkout/pedido/card-order']);
        assert.equal(app.elements.get('pix-payment').hidden, true);
    });
}

test('uncertain payment response keeps the same checkout ID for retry', async () => {
    const app = checkout([{ status: 502, body: { message: 'Could not confirm' } }]);
    await assert.rejects(app.submit('credit_card'));
    assert.equal(app.requests.length, 1);
    assert.equal(app.storage.get('aster-checkout:visitor@example.com:2'), 'old-pix-order');
    assert.deepEqual(app.navigations, []);
});

test('lost card response preserves the replacement ID rather than returning to old Pix', async () => {
    const app = checkout([
        { status: 409, body: { code: 'payment_method_changed', next_checkout_id: 'replacement-order' } },
        new Error('Connection lost'),
    ]);
    await assert.rejects(app.submit('credit_card'));
    assert.equal(app.storage.get('aster-checkout:visitor@example.com:2'), 'replacement-order');
    assert.equal(app.requests.length, 2);
});

for (const method of ['pix', 'visa']) {
    test(`QR code is displayed only for a pending Pix, received method=${method}`, async () => {
        const app = checkout([{ status: 200, body: {
            status: 'pending', payment_method_id: method,
            point_of_interaction: { transaction_data: { qr_code: 'pix-code', qr_code_base64: 'image' } },
        } }]);
        await app.submit(method === 'pix' ? 'bank_transfer' : 'credit_card');
        assert.equal(app.elements.get('pix-payment').hidden, method !== 'pix');
    });
}

test('replacement retries are bounded to avoid automatic charge loops', async () => {
    const body = { code: 'payment_method_changed', next_checkout_id: 'replacement-order' };
    const app = checkout([{ status: 409, body }, { status: 409, body }]);
    await assert.rejects(app.submit('credit_card'));
    assert.equal(app.requests.length, 2);
});
