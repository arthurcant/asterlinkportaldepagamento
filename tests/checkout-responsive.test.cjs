const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const root = path.resolve(__dirname, '..');
const css = fs.readFileSync(path.join(root, 'wwwroot', 'css', 'checkout.css'), 'utf8');
const checkout = fs.readFileSync(path.join(root, 'wwwroot', 'js', 'checkout.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'Views', 'Checkout', 'Index.cshtml'), 'utf8');

test('checkout gives the Mercado Pago Brick the full mobile viewport width', () => {
    assert.match(css, /@media\s*\(max-width:\s*480px\)/);
    assert.match(css, /\.checkout-page\s*\{[^}]*padding-inline:\s*0/s);
    assert.match(css, /\.checkout-card\s*\{[^}]*width:\s*100%[^}]*padding-inline:\s*0/s);
    assert.match(css, /#paymentBrick_container\s*\{[^}]*width:\s*100%[^}]*min-width:\s*0[^}]*max-width:\s*100%/s);
});

test('checkout uses the supported Mercado Pago form padding variable', () => {
    assert.match(checkout, /customVariables:\s*\{[^}]*formPadding:\s*'12px'/s);
});

test('checkout follows the shared navy ice and white project palette', () => {
    assert.match(css, /\.checkout-page\s*\{[^}]*var\(--navy\)[^}]*var\(--ice\)/s);
    assert.match(css, /\.checkout-summary\s*\{[^}]*background:\s*var\(--bar\)/s);
    assert.match(css, /\.checkout-card\s*\{[^}]*border:\s*1px solid #cde4f5[^}]*background:\s*#fff/s);
    assert.match(checkout, /theme:\s*'default'/);
    assert.match(checkout, /customVariables:\s*\{[^}]*baseColor:\s*'#087df5'[^}]*formBackgroundColor:\s*'#ffffff'/s);
});

test('Pix presentation is centralized in the checkout stylesheet', () => {
    assert.doesNotMatch(view, /<style>/);
    assert.match(css, /\.pix-payment\s*\{/);
    assert.match(css, /\.copy-pix-button\s*\{/);
});
