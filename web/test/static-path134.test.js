'use strict';
// 1.3.4: the static handler's path rule (staticFilePath in ssh-proxy.js), run from the file's own source against the
// real web folder: a folder's index.html for the account pages, the old page and asset paths unchanged, and nothing
// outside the web folder (plain, encoded or double-dot forms).
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { slice, proxySource } = require('./balance-api-harness');

const WEB = path.join(__dirname, '..');
const code = slice(proxySource(), '// --- Static path (1.3.4) ---', '// --- End static path ---');
const ctx = { path, String, decodeURIComponent };
vm.createContext(ctx);
const staticFilePath = vm.runInContext(code + '\nstaticFilePath', ctx);
const at = (url) => staticFilePath(WEB, url, fs);

test('the account pages resolve to their folder index.html (the Docker stack serves them through node)', () => {
  for (const page of ['forgot', 'reset', 'verify']) {
    assert.equal(at(`/account/${page}`), path.join(WEB, 'account', page, 'index.html'), page);
    assert.equal(at(`/account/${page}?x=1`), path.join(WEB, 'account', page, 'index.html'), page + ' with a query');
    assert.ok(fs.existsSync(at(`/account/${page}`)), page);
  }
});

test('the old paths stay as they were: / is index.html, a page name gets .html, assets keep their name', () => {
  assert.equal(at('/'), path.join(WEB, 'index.html'));
  assert.equal(at('/admin'), path.join(WEB, 'admin.html'));
  assert.equal(at('/lang/fr.json'), path.join(WEB, 'lang', 'fr.json'));
  assert.equal(at('/account/account.js'), path.join(WEB, 'account', 'account.js'));
  assert.equal(at('/no-such-page'), path.join(WEB, 'no-such-page.html'), 'a missing page still resolves inside (the caller answers 404)');
});

test('nothing outside the web folder: double dots, encoded forms, a sibling folder, NUL and backslash give null', () => {
  for (const url of ['/../README.md', '/account/../../PRIVACY.md', '/%2e%2e/README.md', '/%2E%2E%2FREADME.md',
    '/..%2fREADME.md', '/account/%2e%2e/%2e%2e/PRIVACY', '/%00', '/a%00b.html', '/..\\README.md', '/%5c..%5cREADME.md',
    '/%E0%A4%A', '/%', `/../${path.basename(WEB)}2/x.html`]) {
    assert.equal(at(url), null, url);
  }
});
