const test = require('node:test');
const assert = require('node:assert/strict');

const orderPage = require('../wwwroot/js/network-order.js');

test('formats purchased plan duration without inventing units', () => {
    assert.equal(orderPage.formatDuration(30), '30 minutos');
    assert.equal(orderPage.formatDuration(60), '1 hora');
    assert.equal(orderPage.formatDuration(120), '2 horas');
});

test('formats the RouterOS expiration instant in Arena Pernambuco time', () => {
    assert.equal(orderPage.formatExpiration('2026-10-07T16:42:00Z'), '13:42');
    assert.equal(orderPage.formatExpiration(null), 'Aguardando conexão');
});

test('keeps connection guidance while adapting the confirmed state', () => {
    const ready = orderPage.presentationFor({ payment: 'approved', access: 'ready' });
    assert.equal(ready.showConnect, true);
    assert.match(ready.message, /Conectando seu dispositivo/);

    const connected = orderPage.presentationFor({ payment: 'approved', access: 'connected' });
    assert.equal(connected.showConnect, false);
    assert.equal(connected.title, 'Pagamento confirmado!');
    assert.match(connected.message, /acesso à internet foi liberado/i);
});

test('does not present rejected payment as a successful connection', () => {
    const rejected = orderPage.presentationFor({ payment: 'rejected', access: 'revoked' });
    assert.equal(rejected.tone, 'error');
    assert.match(rejected.message, /recusado ou cancelado/i);
});

test('shows a replaced plan as ended without another connect button', () => {
    const replaced = orderPage.presentationFor({ payment: 'approved', access: 'superseded' });
    assert.equal(replaced.showConnect, false);
    assert.match(replaced.message, /novo plano/i);
});
