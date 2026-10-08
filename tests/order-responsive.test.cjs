const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');
const { pathToFileURL } = require('node:url');

const fixture = path.join(__dirname, 'fixtures', 'order-full-bleed.html');
const browser = process.env.CHROME_PATH || [
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe'
].find(fs.existsSync);

function renderAt(width, height) {
    assert.ok(browser, 'Chrome is required for the responsive layout test');
    const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'asterlink-order-layout-'));

    try {
        const result = spawnSync(browser, [
            '--headless=new',
            '--disable-gpu',
            '--disable-extensions',
            '--no-first-run',
            `--user-data-dir=${profile}`,
            `--window-size=${width},${height}`,
            '--dump-dom',
            pathToFileURL(fixture).href
        ], { encoding: 'utf8' });

        assert.equal(result.status, 0, result.stderr);
        const match = result.stdout.match(/<output id="layout-result">([^<]+)<\/output>/);
        assert.ok(match, 'layout result was not emitted by the browser fixture');
        return JSON.parse(match[1].replaceAll('&quot;', '"'));
    } finally {
        fs.rmSync(profile, { recursive: true, force: true });
    }
}

for (const viewport of [
    { name: 'mobile', width: 500, height: 844 },
    { name: 'tablet', width: 768, height: 1024 }
]) {
    test(`order page fills the ${viewport.name} viewport without white borders`, () => {
        const layout = renderAt(viewport.width, viewport.height);

        assert.equal(layout.pageLeft, 0);
        assert.equal(layout.pageWidth, layout.viewportWidth);
        assert.equal(layout.bodyMarginBottom, '0px');
        assert.equal(layout.bodyPaddingLeft, '0px');
        assert.equal(layout.bodyBackgroundColor, 'rgb(0, 18, 50)');
        assert.equal(layout.horizontalOverflow, false);
    });
}
