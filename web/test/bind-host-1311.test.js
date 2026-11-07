'use strict';
// 1.3.11: the web service binds WEB_BIND_HOST (loopback by default; compose sets 0.0.0.0). Reads files and runs the cut helper only.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { slice, proxySource } = require('./balance-api-harness');

const ROOT = path.join(__dirname, '..', '..');
const src = proxySource();
const ctx = {};
vm.createContext(ctx);
const [resolveBindHost] = vm.runInContext(
  slice(src, '// --- Bind host (1.3.11) ---', '// --- End bind host ---') +
    '\n[resolveBindHost]', ctx);

test('bind host is loopback unless WEB_BIND_HOST is set', () => {
  assert.equal(resolveBindHost({}), '127.0.0.1');
  assert.equal(resolveBindHost({ WEB_BIND_HOST: '' }), '127.0.0.1');
  assert.equal(resolveBindHost({ WEB_BIND_HOST: '   ' }), '127.0.0.1');
  assert.equal(resolveBindHost({ WEB_BIND_HOST: '0.0.0.0' }), '0.0.0.0');
  assert.equal(resolveBindHost({ WEB_BIND_HOST: ' 10.1.2.3 ' }), '10.1.2.3');
});

test('listen passes the bind host and the log names the address', () => {
  assert.match(src, /httpServer\.listen\(WS_PORT, WS_BIND_HOST, \(\) => \{/);
  assert.match(src, /listening on \$\{WS_BIND_HOST\}:\$\{WS_PORT\}/);
});

test('compose sets 0.0.0.0 for usurper-web and publishes only nginx', () => {
  const yml = fs.readFileSync(path.join(ROOT, 'docker-compose.yml'), 'utf8');
  const web = yml.split(/^  usurper-web:/m)[1].split(/^  usurper-nginx:/m)[0];
  assert.match(web, /^\s+- WEB_BIND_HOST=0\.0\.0\.0/m);
  assert.doesNotMatch(web, /^\s+ports:/m);
  assert.match(yml.split(/^  usurper-nginx:/m)[1], /"80:80"/);
});

test('the guides name WEB_BIND_HOST', () => {
  for (const f of ['SERVER_DEPLOYMENT.md', 'DOCKER.md']) {
    assert.match(fs.readFileSync(path.join(ROOT, 'DOCS', f), 'utf8'), /WEB_BIND_HOST/);
  }
});
