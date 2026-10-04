'use strict';
// index.html and steam.html: GitHub sponsor cards and the live stats board.
const test = require('node:test');
const assert = require('node:assert');
const { HOSTILE, HOSTILE_URLS, loadPage, assertSafe, elements } = require('./harness');

const PAGES = ['index.html', 'steam.html'];

async function sponsorsHtml(pageName, sponsors) {
  const p = await loadPage(pageName, {
    fetch: (url) => (url === '/api/sponsors' ? { sponsors } : url === '/api/stats' ? { error: 'x' } : url.startsWith('lang/') ? {} : []),
  });
  await p.settle();
  return { p, html: p.el('sponsors-grid').innerHTML };
}

function sponsor(v, url, avatar) {
  return { login: v, name: v, tier: v, url, avatarUrl: avatar };
}

for (const pageName of PAGES) {
  test(`${pageName} loads escape.js and has no private escapeHtml`, async () => {
    const p = await loadPage(pageName, { fetch: () => ({}) });
    assert.deepStrictEqual(p.loaded, ['escape.js']);
    assert.strictEqual(p.run('escapeHtml'), p.run('UsurperEscape.escapeHtml'));
  });

  test(`sink ${pageName} sponsors (name, login, tier as text and attribute)`, async () => {
    const ok = 'https://github.com/someone';
    const av = 'https://avatars.githubusercontent.com/u/1?v=4';
    const h = await sponsorsHtml(pageName, [sponsor(HOSTILE, ok, av)]);
    const b = await sponsorsHtml(pageName, [sponsor('Alice', ok, av)]);
    assertSafe(assert, h.html, b.html, pageName + ' sponsors');
    const a = elements(h.html).find((e) => e.tag === 'a');
    assert.strictEqual(a.attrs.href, ok);
    assert.strictEqual(a.attrs.title, HOSTILE);
    const img = elements(h.html).find((e) => e.tag === 'img');
    assert.strictEqual(img.attrs.src, av);
    assert.strictEqual(img.attrs.alt, HOSTILE);
    assert.ok(h.p.el('sponsors-grid').textContent.includes(HOSTILE));
  });

  test(`sink ${pageName} sponsor URLs: https only, javascript: and data: never become href or src`, async () => {
    for (const bad of HOSTILE_URLS) {
      const h = await sponsorsHtml(pageName, [sponsor('Alice', bad, bad)]);
      const els = elements(h.html);
      assert.strictEqual(els.filter((e) => e.tag === 'a').length, 0, `${bad}: rendered as a link`);
      assert.strictEqual(els.filter((e) => e.tag === 'img').length, 0, `${bad}: rendered as an image`);
      assert.ok(els.every((e) => e.attrs.href === undefined && e.attrs.src === undefined), bad);
      assert.ok(h.p.el('sponsors-grid').textContent.includes('Alice'), 'name still shown');
      assertSafe(assert, h.html, undefined, bad);
    }
  });

  test(`sink ${pageName} stats board: levels, ranks and counts from /api/stats are escaped`, async () => {
    async function statsHtml(v) {
      const data = {
        onlineCount: v,
        online: [{ name: v, className: v, level: v, location: v, connectionType: v, isImmortal: false },
                 { name: v, isImmortal: true, divineName: v, godLevel: v, location: v }],
        stats: { totalPlayers: v, totalKills: v, avgLevel: v, deepestFloor: v, totalGold: v, marriages: v, children: v, permadeadNpcs: v, agedDeathNpcs: v },
        highlights: { topPlayer: { name: v, level: v, className: v }, king: v, popularClass: v, mostWanted: { name: v, murderWeight: v, level: v, className: v } },
        immortals: [{ divineName: v, godTitle: v, godLevel: v, godAlignment: v, believers: v, isOnline: true }],
        leaderboard: [{ rank: v, name: v, className: v, level: v, experience: v, isOnline: true }],
        pvpLeaderboard: [{ rank: v, name: v, className: v, level: v, wins: v, losses: v, goldStolen: v }],
        npcActivities: [{ message: v, time: '' }],
        news: [{ message: v, time: '' }],
      };
      const p = await loadPage(pageName, { fetch: (url) => (url === '/api/stats' ? data : url === '/api/sponsors' ? { sponsors: [] } : {}) });
      await p.settle();
      return p.el('stats-content').querySelector('#stats-upper').innerHTML + p.el('stats-content').querySelector('#stats-feeds').innerHTML;
    }
    const h = await statsHtml(HOSTILE);
    const b = await statsHtml('Alice');
    assert.ok(h.length > 500, 'stats rendered');
    assertSafe(assert, h, b, pageName + ' stats');
  });
}
