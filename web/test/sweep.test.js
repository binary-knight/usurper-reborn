'use strict';
// Sweep over every web/*.html page so the inline-handler class of bug cannot return.
// Rules, checked on the code of every inline <script> block:
//  1. no HTML event-handler attribute (onclick=, onerror=, ...) appears in any string
//     the script builds; data goes in data-* attributes and a delegated listener runs it;
//  2. no setAttribute('on...') and no javascript: URL;
//  3. no page keeps a private escapeHtml or esc; pages that escape load /escape.js
//     before their own scripts.
// Static handlers written directly in the page markup (outside <script>) carry no
// server data and are allowed.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { WEB, pageScripts } = require('./harness');

const PAGES = fs.readdirSync(WEB).filter((f) => f.endsWith('.html')).sort();

// An event-handler attribute written inside a JS string or template:
// onclick=" onclick=' onclick=\' onclick=` onclick=${ (no space before =, as in markup).
const HANDLER_IN_STRING = /(^|[\s"'`<\\])on[a-z]+=\s*(["'`]|\\["']|\$\{)/gm;

function scriptFindings(code) {
  const found = [];
  const lines = code.split('\n');
  lines.forEach((line, i) => {
    if (HANDLER_IN_STRING.test(line)) found.push(`line ${i + 1}: inline handler in a built string: ${line.trim()}`);
    HANDLER_IN_STRING.lastIndex = 0;
    if (/setAttribute\(\s*['"`]on/i.test(line)) found.push(`line ${i + 1}: setAttribute of an event handler: ${line.trim()}`);
    if (/javascript:/i.test(line)) found.push(`line ${i + 1}: javascript: URL: ${line.trim()}`);
    if (/function\s+(escapeHtml|esc)\s*\(|(const|let|var)\s+(escapeHtml|esc)\s*=/.test(line)) found.push(`line ${i + 1}: private escape helper: ${line.trim()}`);
  });
  return found;
}

test('sweep finds the web pages', () => {
  for (const p of ['index.html', 'steam.html', 'dashboard.html', 'admin.html', 'balance.html']) assert.ok(PAGES.includes(p), p);
});

for (const page of PAGES) {
  test(`sweep ${page}: no inline handler, javascript: URL or private escape helper in page scripts`, () => {
    const html = fs.readFileSync(path.join(WEB, page), 'utf8');
    const scripts = pageScripts(html);
    const findings = [];
    scripts.forEach((s, n) => { if (s.code !== undefined) for (const f of scriptFindings(s.code)) findings.push(`script ${n + 1} ${f}`); });
    assert.deepStrictEqual(findings, []);
  });

  test(`sweep ${page}: escapes with the shared helper loaded before its own scripts`, () => {
    const html = fs.readFileSync(path.join(WEB, page), 'utf8');
    const scripts = pageScripts(html);
    const usesEscape = scripts.some((s) => s.code !== undefined && /escapeHtml\(|safeHttpsUrl\(/.test(s.code));
    if (!usesEscape) return;
    const helperAt = scripts.findIndex((s) => s.src === '/escape.js');
    const firstInline = scripts.findIndex((s) => s.code !== undefined);
    assert.ok(helperAt !== -1, 'loads /escape.js');
    assert.ok(helperAt < firstInline, '/escape.js comes before the inline scripts');
  });
}

test('sweep rule catches a handler built by concatenation or interpolation', () => {
  for (const bad of [
    `html += '<div onclick="pick(\\'' + name + '\\')">';`,
    "html += `<b onclick='go(${x})'>`;",
    "el.innerHTML = '<img src=x onerror=\\'a()\\'>';",
    "html += `<b onmouseover=${handler}>`;",
    "el.setAttribute('onclick', 'x()');",
    'function escapeHtml(s) { return s; }',
  ]) {
    assert.ok(scriptFindings(bad).length > 0, bad);
  }
  for (const ok of [
    "feedSource.onopen = function() {};",
    "ws.onmessage = (event) => {};",
    "const onlineDot = g.isOnline ? 'x' : '';",
    "html += '<div data-action=\"select-npc\" data-npc=\"' + escapeHtml(n) + '\">';",
  ]) {
    assert.deepStrictEqual(scriptFindings(ok), [], ok);
  }
});
