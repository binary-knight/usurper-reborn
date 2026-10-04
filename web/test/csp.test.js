'use strict';
// The Content-Security-Policy line in the nginx template, checked from the files:
//  1. the header is Content-Security-Policy-Report-Only, set with "always", in one snippet file
//     that the site template includes inside its main server block;
//  2. it names no report endpoint (nothing is sent anywhere);
//  3. no enforcing Content-Security-Policy header exists in the template, the snippet, the node
//     server or a page meta tag (report-only must block nothing);
//  4. every external host that the pages and the wiki stylesheet load from is allowed by the
//     directive that governs it.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.join(__dirname, '..', '..');
const read = (...p) => fs.readFileSync(path.join(ROOT, ...p), 'utf8');
const TEMPLATE = read('scripts-server', 'nginx-usurper.conf');
const SNIPPET = read('scripts-server', 'nginx-csp.conf');
const PAGES = fs.readdirSync(path.join(ROOT, 'web')).filter((f) => f.endsWith('.html'));

const code = (text) => text.split('\n').filter((l) => !/^\s*#/.test(l)).join('\n');

// The add_header directives of a config text, comments removed.
function headers(text) {
  return [...code(text).matchAll(/^\s*add_header\s+(\S+)\s+"([^"]*)"\s*(always)?\s*;/gm)]
    .map((m) => ({ name: m[1], value: m[2], always: m[3] === 'always' }));
}

function policy() {
  const h = headers(SNIPPET);
  assert.strictEqual(h.length, 1, 'the snippet holds exactly one add_header line');
  const map = {};
  for (const part of h[0].value.split(';')) {
    const [name, ...sources] = part.trim().split(/\s+/);
    if (name) map[name] = sources;
  }
  return map;
}

test('the header is Content-Security-Policy-Report-Only and applies to every response', () => {
  const h = headers(SNIPPET);
  assert.strictEqual(h.length, 1);
  assert.strictEqual(h[0].name, 'Content-Security-Policy-Report-Only');
  assert.strictEqual(h[0].always, true, 'add_header needs "always" to cover error responses');
});

test('the site template includes the snippet inside its main server block', () => {
  const lines = TEMPLATE.split('\n');
  const inc = lines.findIndex((l) => /^\s*include\s+\/etc\/nginx\/snippets\/usurper-csp\.conf\s*;/.test(l));
  assert.ok(inc >= 0, 'the template must include usurper-csp.conf');
  const mainStart = lines.findIndex((l, i) => /^server \{/.test(l) && /default_server/.test(lines[i + 1] || ''));
  assert.ok(mainStart >= 0, 'main server block not found');
  const mainEnd = lines.findIndex((l, i) => i > mainStart && l === '}');
  assert.ok(inc > mainStart && inc < mainEnd, 'the include sits in the main server block');
  const install = lines.filter((l) => /^#/.test(l)).join('\n');
  assert.match(install, /cp nginx-csp\.conf \/etc\/nginx\/snippets\/usurper-csp\.conf/, 'install comment copies the snippet');
});

test('no location block sets its own add_header (it would drop the server-level one)', () => {
  assert.strictEqual(headers(TEMPLATE).length, 0);
});

test('the policy names no report-uri', () => {
  assert.doesNotMatch(SNIPPET + TEMPLATE, /report-uri/i);
});

test('the policy names no report-to', () => {
  assert.doesNotMatch(SNIPPET + TEMPLATE, /report-to/i);
});

test('no enforcing Content-Security-Policy header in the template or the snippet', () => {
  for (const [name, text] of [['template', TEMPLATE], ['snippet', SNIPPET]]) {
    assert.doesNotMatch(code(text), /Content-Security-Policy(?!-Report-Only)/i, `${name} holds an enforcing header`);
  }
});

test('the node server and the pages set no enforcing policy either', () => {
  assert.doesNotMatch(read('web', 'ssh-proxy.js'), /Content-Security-Policy/i);
  for (const page of PAGES) {
    assert.doesNotMatch(read('web', page), /http-equiv\s*=\s*["']?Content-Security-Policy/i, `${page} has a CSP meta tag`);
  }
});

const hostOf = (u) => new URL(u).origin;
const allowed = (list, origin) => list.includes(origin);

test('every external script, stylesheet, frame and image host is allowed where it is used', () => {
  const p = policy();
  const seen = { script: new Set(), style: new Set(), frame: new Set() };
  for (const page of PAGES) {
    const html = read('web', page);
    for (const m of html.matchAll(/<script\b[^>]*\bsrc="(https?:[^"]+)"/g)) seen.script.add(hostOf(m[1]));
    for (const m of html.matchAll(/<link\b[^>]*\brel="stylesheet"[^>]*\bhref="(https?:[^"]+)"/g)) seen.style.add(hostOf(m[1]));
    for (const m of html.matchAll(/<link\b[^>]*\bhref="(https?:[^"]+)"[^>]*\brel="stylesheet"/g)) seen.style.add(hostOf(m[1]));
    for (const m of html.matchAll(/<iframe\b[^>]*\bsrc="(https?:[^"]+)"/g)) seen.frame.add(hostOf(m[1]));
  }
  // the wiki stylesheet imports its fonts stylesheet
  for (const m of read('tools', 'wiki-build', 'wiki.css').matchAll(/@import\s+url\("(https?:[^"]+)"\)/g)) seen.style.add(hostOf(m[1]));

  assert.ok(seen.script.size >= 1 && seen.style.size >= 2 && seen.frame.size >= 2, 'the scan must find the known sources');
  for (const o of seen.script) assert.ok(allowed(p['script-src'], o), `script-src lacks ${o}`);
  for (const o of seen.style) assert.ok(allowed(p['style-src'], o), `style-src lacks ${o}`);
  for (const o of seen.frame) assert.ok(allowed(p['frame-src'], o), `frame-src lacks ${o}`);

  // fonts come from the stylesheet host's companion, sponsor avatars from GitHub, the favicon is data:
  assert.ok(allowed(p['font-src'], 'https://fonts.gstatic.com'));
  assert.ok(allowed(p['img-src'], 'https://avatars.githubusercontent.com'));
  assert.ok(p['img-src'].includes('data:'));
  // the terminal websocket and the fetch and event stream calls
  assert.ok(p['connect-src'].includes("'self'"));
  assert.ok(allowed(p['connect-src'], 'wss://usurper-reborn.net'));
  assert.ok(allowed(p['connect-src'], 'wss://play.usurper-reborn.net'));
  // inline script and style stay allowed while the pages carry them
  assert.ok(p['script-src'].includes("'unsafe-inline'"));
  assert.ok(p['style-src'].includes("'unsafe-inline'"));
  assert.deepStrictEqual(p['object-src'], ["'none'"]);
});
