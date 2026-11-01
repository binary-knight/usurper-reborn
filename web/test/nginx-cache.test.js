'use strict';
// The Cache-Control rule in the nginx template, checked from the files (2.0): pages, styles, scripts and
// language files get "no-cache" so a returning browser revalidates them after a deploy; everything else gets
// an empty value (no header). The header is set once, at server level beside the CSP snippet, so no location
// drops either header. The tests read files only; nothing touches the network.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.join(__dirname, '..', '..');
const read = (...p) => fs.readFileSync(path.join(ROOT, ...p), 'utf8');
const TEMPLATE = read('scripts-server', 'nginx-usurper.conf');
const SNIPPET = read('scripts-server', 'nginx-cache.conf');
const MAP = read('scripts-server', 'nginx-cache-map.conf');
const code = (text) => text.split('\n').filter((l) => !/^\s*#/.test(l) && l.trim() !== '').join('\n');

function mainBlock(lines) {
  const start = lines.findIndex((l, i) => /^server \{/.test(l) && /default_server/.test(lines[i + 1] || ''));
  const end = lines.findIndex((l, i) => i > start && l === '}');
  return [start, end];
}

test('the main server block includes the cache snippet next to the CSP snippet', () => {
  const lines = TEMPLATE.split('\n');
  const [start, end] = mainBlock(lines);
  assert.ok(start >= 0 && end > start, 'main server block not found');
  const csp = lines.findIndex((l) => /^\s*include\s+\/etc\/nginx\/snippets\/usurper-csp\.conf\s*;$/.test(l));
  const cache = lines.findIndex((l) => /^\s*include\s+\/etc\/nginx\/snippets\/usurper-cache\.conf\s*;$/.test(l));
  assert.ok(csp > start && csp < end, 'the CSP include sits in the main server block');
  assert.ok(cache > start && cache < end, 'the cache include sits in the main server block');
  assert.strictEqual(lines.filter((l) => /usurper-cache\.conf\s*;/.test(l) && !/^\s*#/.test(l)).length, 1);
});

test('no location sets its own add_header, so both server level headers reach every location', () => {
  assert.doesNotMatch(code(TEMPLATE), /^\s*add_header\b/m);
});

test('the snippet sets Cache-Control once, from the map', () => {
  assert.strictEqual(code(SNIPPET), 'add_header Cache-Control $usurper_cache_control;');
});

test('the map revalidates pages, styles, scripts and language files and leaves the rest alone', () => {
  assert.strictEqual(code(MAP), [
    'map $uri $usurper_cache_control {',
    '    default "";',
    '    ~\\.(?:html|css|js|json)$ "no-cache";',
    '    ~/$ "no-cache";',
    '}',
  ].join('\n'));
});

test('the install comments copy both files where the includes expect them', () => {
  const comments = TEMPLATE.split('\n').filter((l) => /^#/.test(l)).join('\n');
  assert.match(comments, /sudo cp nginx-cache\.conf \/etc\/nginx\/snippets\/usurper-cache\.conf/);
  assert.match(comments, /sudo cp nginx-cache-map\.conf \/etc\/nginx\/conf\.d\/usurper-cache-map\.conf/);
  assert.match(SNIPPET, /^# Install: sudo cp nginx-cache\.conf \/etc\/nginx\/snippets\/usurper-cache\.conf$/m);
  assert.match(MAP, /^# Install: sudo cp nginx-cache-map\.conf \/etc\/nginx\/conf\.d\/usurper-cache-map\.conf$/m);
});

test('the server deployment guide installs both files', () => {
  const guide = read('DOCS', 'SERVER_DEPLOYMENT.md');
  assert.match(guide, /^sudo cp scripts-server\/nginx-cache\.conf \/etc\/nginx\/snippets\/usurper-cache\.conf$/m);
  assert.match(guide, /^sudo cp scripts-server\/nginx-cache-map\.conf \/etc\/nginx\/conf\.d\/usurper-cache-map\.conf$/m);
});
