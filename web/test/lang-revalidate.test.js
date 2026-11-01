'use strict';
// The language files keep their names across deploys and nginx sends them with Last-Modified and ETag only,
// so a browser may reuse an old copy without asking. Every page that fetches one asks the server to revalidate
// it (cache: 'no-cache', a 304 when unchanged), so new keys reach players on their next load.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { WEB, loadPage } = require('./harness');

const langCalls = (p) => p.calls.fetch.filter((c) => /^\/?lang\/[a-z]{2}\.json$/.test(c.url));

test('play page revalidates the saved language file', async () => {
  const p = await loadPage('play/index.html', { storage: { lang: 'fr' }, fetch: () => ({}) });
  const calls = langCalls(p);
  assert.strictEqual(calls.length, 1);
  assert.match(calls[0].url, /^\/lang\/fr\.json$/);
  assert.strictEqual(calls[0].init && calls[0].init.cache, 'no-cache');
});

test('play page revalidates the English file', async () => {
  const p = await loadPage('play/index.html', { fetch: () => ({}) });
  const calls = langCalls(p);
  assert.strictEqual(calls.length, 1);
  assert.match(calls[0].url, /^\/lang\/en\.json$/);
  assert.strictEqual(calls[0].init && calls[0].init.cache, 'no-cache');
});

test('landing page revalidates the saved language file and the English file', async () => {
  for (const [saved, file] of [['hu', /^lang\/hu\.json$/], [null, /^lang\/en\.json$/]]) {
    const p = await loadPage('index.html', { storage: saved ? { lang: saved } : {}, fetch: () => ({}) });
    const calls = langCalls(p);
    assert.strictEqual(calls.length, 1, String(saved));
    assert.match(calls[0].url, file);
    assert.strictEqual(calls[0].init && calls[0].init.cache, 'no-cache', String(saved));
  }
});

test('account pages revalidate the language file', () => {
  const js = fs.readFileSync(path.join(WEB, 'account', 'account.js'), 'utf8');
  const fetches = js.match(/fetch\('\/lang\/' \+ lang \+ '\.json', \{[^}]*\}\)/g) || [];
  assert.strictEqual(fetches.length, 1);
  assert.match(fetches[0], /cache: 'no-cache'/);
  assert.match(fetches[0], /credentials: 'omit'/);
});
