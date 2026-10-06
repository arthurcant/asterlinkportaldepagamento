const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');

test('registration form collects CPF with mobile-friendly constraints', () => {
    const view = fs.readFileSync(path.join(root, 'Views', 'Account', 'Index.cshtml'), 'utf8');

    assert.match(view, /asp-for="Register\.Cpf"/);
    assert.match(view, /inputmode="numeric"/);
    assert.match(view, /autocomplete="off"/);
    assert.match(view, /maxlength="14"/);
});

test('fresh database schema stores one normalized CPF per user', () => {
    const schema = fs.readFileSync(path.join(root, 'scriptsDB', '002_create_users.sql'), 'utf8');
    const seed = fs.readFileSync(path.join(root, 'scriptsDB', '003_create_test_user.sql'), 'utf8');

    assert.match(schema, /cpf CHAR\(11\) NOT NULL/);
    assert.match(schema, /UNIQUE KEY ux_users_cpf \(cpf\)/);
    assert.match(seed, /users \(name, username, email, cpf, password_hash, is_active\)/);
});

test('existing database migration adds nullable CPF and a unique index idempotently', () => {
    const migrationPath = path.join(root, 'scriptsDB', '007_add_user_cpf.sql');

    assert.equal(fs.existsSync(migrationPath), true, '007_add_user_cpf.sql must exist');

    const migration = fs.readFileSync(migrationPath, 'utf8');

    assert.match(migration, /information_schema\.columns/i);
    assert.match(migration, /ADD COLUMN cpf CHAR\(11\) NULL/i);
    assert.match(migration, /information_schema\.statistics/i);
    assert.match(migration, /ADD UNIQUE KEY ux_users_cpf \(cpf\)/i);
});
