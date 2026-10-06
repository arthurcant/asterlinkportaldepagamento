const test = require('node:test');
const assert = require('node:assert/strict');

const baseUrl = process.env.AUTH_TEST_BASE_URL || 'http://127.0.0.1:7263';

for (const mode of ['login', 'register']) {
    test(`auth ${mode} keeps its form in the new portal layout`, async () => {
        const response = await fetch(`${baseUrl}/conta?mode=${mode}`, { redirect: 'manual' });
        assert.equal(response.status, 200);
        const html = await response.text();
        const visibleText = html.replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ');

        assert.match(visibleText, /Conecte-se ao Wi-Fi Ásterlink/);
        assert.match(visibleText, /Rápido/);
        assert.match(visibleText, /Seguro/);
        assert.match(visibleText, /Estável/);
        assert.doesNotMatch(visibleText, /Banner de Patrocínio/);
        assert.match(html, /src="\/images\/auth\/VERSÃO 6 LOGO DA ÁSTER.svg"/);
        assert.match(html, mode === 'login' ? /action="\/conta\/entrar"/ : /action="\/conta\/cadastrar"/);
        assert.match(html, /name="__RequestVerificationToken"/);

        for (const field of mode === 'login'
            ? ['Login.Identifier', 'Login.Password']
            : ['Register.Name', 'Register.Username', 'Register.Email', 'Register.Cpf', 'Register.Password', 'Register.ConfirmPassword']) {
            assert.ok(html.includes(`name="${field}"`), `${field} must remain in the form`);
        }
    });
}
