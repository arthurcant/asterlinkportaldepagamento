const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), 'utf8');

test('plan repository loads display-only speed from the database', () => {
    const repository = read('Data', 'PlanRepository.cs');

    assert.equal((repository.match(/speed_mbps/g) || []).length >= 4, true);
    assert.match(repository, /SpeedMbps = reader\.GetInt32\("speed_mbps"\)/);
});

test('fresh database contains the four approved plans and their speeds', () => {
    const schema = read('scriptsDB', '004_create_internet_plans.sql');

    assert.match(schema, /speed_mbps INT UNSIGNED NOT NULL/);
    assert.match(schema, /\('Básico', 10, 30, 5\.00, FALSE, TRUE, 1\)/);
    assert.match(schema, /\('Essencial', 20, 60, 10\.00, FALSE, TRUE, 2\)/);
    assert.match(schema, /\('Plus', 30, 120, 15\.00, FALSE, TRUE, 3\)/);
    assert.match(schema, /\('Premium', 50, 240, 20\.00, TRUE, TRUE, 4\)/);
});

test('existing database migration is idempotent and replaces the active catalog', () => {
    const migrationPath = path.join(root, 'scriptsDB', '008_update_internet_plans.sql');

    assert.equal(fs.existsSync(migrationPath), true, '008_update_internet_plans.sql must exist');

    const migration = fs.readFileSync(migrationPath, 'utf8');

    assert.match(migration, /information_schema\.columns/i);
    assert.match(migration, /ADD COLUMN speed_mbps INT UNSIGNED NULL/i);
    assert.match(migration, /UPDATE internet_plans\s+SET\s+is_active = FALSE/i);
    assert.match(migration, /\('Premium', 50, 240, 20\.00, TRUE, TRUE, 4\)/);
});

test('dashboard keeps the top bar and implements the Figma selection flow', () => {
    const view = read('Views', 'Account', 'Dashboard.cshtml');
    const styles = read('wwwroot', 'css', 'plans.css');

    assert.match(view, /class="top-bar"/);
    assert.match(view, /type="radio"/);
    assert.match(view, /@plan\.SpeedMbps Mbps/);
    assert.match(view, /data-checkout-url/);
    assert.match(view, /Continuar com o plano selecionado/);
    assert.match(styles, /\.plans-panel/);
    assert.match(styles, /\.plan-radio:checked \+ \.plan-card/);
});

test('plan speed remains outside RouterOS provisioning', () => {
    const router = read('Services', 'RouterOsHotspot.cs');
    const order = read('Models', 'NetworkOrder.cs');

    assert.doesNotMatch(router, /SpeedMbps|speed_mbps/);
    assert.doesNotMatch(order, /SpeedMbps|speed_mbps/);
});

test('all Figma plan icons are stored locally', () => {
    for (const asset of ['clock.svg', 'crown.svg', 'shield.svg', 'speed.svg', 'support.svg', 'arrow.svg']) {
        const assetPath = path.join(root, 'wwwroot', 'images', 'plans', 'figma', asset);

        assert.equal(fs.existsSync(assetPath), true, `${asset} must exist`);
        assert.equal(fs.statSync(assetPath).size > 0, true, `${asset} must not be empty`);
    }
});
