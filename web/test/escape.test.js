'use strict';
const test = require('node:test');
const assert = require('node:assert');
const { escapeHtml, safeHttpsUrl } = require('../escape.js');
const { HOSTILE, HOSTILE_URLS, parseHtml, elements, decodeEntities } = require('./harness');

test('escapeHtml escapes & < > " \' and backslash', () => {
  assert.strictEqual(escapeHtml(`&<>"'\\`), '&amp;&lt;&gt;&quot;&#39;&#92;');
  assert.strictEqual(escapeHtml('plain text'), 'plain text');
});

test('escapeHtml converts non-strings and keeps zero', () => {
  assert.strictEqual(escapeHtml(null), '');
  assert.strictEqual(escapeHtml(undefined), '');
  assert.strictEqual(escapeHtml(0), '0');
  assert.strictEqual(escapeHtml(42), '42');
  assert.strictEqual(escapeHtml(false), 'false');
});

test('escaped hostile text round-trips in text and in both attribute quote styles', () => {
  const e = escapeHtml(HOSTILE);
  for (const html of [`<p>${e}</p>`, `<p title="${e}">x</p>`, `<p title='${e}'>x</p>`]) {
    const els = elements(html);
    assert.deepStrictEqual(els.map((x) => x.tag), ['p'], html);
    const p = els[0];
    if (p.attrs.title !== undefined) assert.strictEqual(p.attrs.title, HOSTILE);
    else assert.strictEqual(p.children[0].text, HOSTILE);
  }
  // no character that can end a JS string or an attribute survives
  assert.ok(!/['"<>\\]/.test(e), e);
  assert.strictEqual(decodeEntities(e), HOSTILE);
});

test('safeHttpsUrl accepts https URLs only', () => {
  assert.strictEqual(safeHttpsUrl('https://github.com/someone'), 'https://github.com/someone');
  assert.strictEqual(safeHttpsUrl('https://avatars.githubusercontent.com/u/1?v=4'), 'https://avatars.githubusercontent.com/u/1?v=4');
  for (const bad of HOSTILE_URLS) assert.strictEqual(safeHttpsUrl(bad), null, bad);
  for (const bad of [null, undefined, '', 42, {}, 'not a url']) assert.strictEqual(safeHttpsUrl(bad), null, String(bad));
});

test('parser sanity: an unescaped value does inject markup', () => {
  // Guards the harness itself: the oracle must see injection when it is there.
  const els = elements(`<p title="${HOSTILE}">x</p>`);
  assert.ok(els.some((x) => x.tag === 'img'));
  assert.ok(parseHtml('<a href="x">y</a>').children[0].attrs.href === 'x');
});
