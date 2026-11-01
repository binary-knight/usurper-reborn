'use strict';
// web/play/index.html: the top bar links to the game's Discord server beside Home and Wiki, in a new tab.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { WEB, readPage, parseHtml, elements } = require('./harness');

const LANGS = ['en', 'es', 'fr', 'hu', 'it'];
const INVITE = 'https://discord.gg/EZhwgDT6Ta';

test('the play page top bar links to the Discord server in a new tab', () => {
  const links = elements(parseHtml(readPage('play/index.html'))).filter((e) => e.tag === 'a' && e.attrs['data-i18n'] === 'play.discord');
  assert.strictEqual(links.length, 1);
  assert.strictEqual(links[0].attrs.href, INVITE);
  assert.strictEqual(links[0].attrs.target, '_blank');
  assert.strictEqual(links[0].attrs.rel, 'noopener');
});

test('the play page uses the same invite as the landing page', () => {
  const landing = readPage('index.html').match(/https:\/\/discord\.gg\/[A-Za-z0-9]+/g) || [];
  assert.ok(landing.length > 0);
  assert.deepStrictEqual([...new Set(landing)], [INVITE]);
});

test('the Discord link label is in all five languages', () => {
  for (const l of LANGS) {
    const strings = JSON.parse(fs.readFileSync(path.join(WEB, 'lang', l + '.json'), 'utf8'));
    assert.strictEqual(strings['play.discord'], 'Discord', l);
  }
});
