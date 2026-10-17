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

// 1.3.4: the live counters never show a 0 nobody measured. Before data, on an error answer and on a failed
// request the hero line stays hidden; a missing count shows "--"; a real 0 still shows as 0.
async function heroState(answer) {
  const p = await loadPage('index.html', {
    fetch: (url) => (url === '/api/stats' ? answer() : url === '/api/sponsors' ? { sponsors: [] } : {}),
  });
  await p.settle();
  return {
    p,
    online: p.el('hl-online').hidden,
    world: p.el('hl-world').hidden,
    marr: p.el('hl-marr').textContent,
    kids: p.el('hl-kids').textContent,
    kills: p.el('hl-kills').textContent,
  };
}

test('index.html hero line: hidden before data and with "--" in place of 0', async () => {
  const p = await loadPage('index.html', { fetch: () => new Promise(() => {}) });
  await p.settle();
  assert.strictEqual(p.el('hl-online').hidden, true);
  assert.strictEqual(p.el('hl-world').hidden, true);
  for (const id of ['hl-online-n', 'hl-marr', 'hl-kids', 'hl-kills']) assert.strictEqual(p.el(id).textContent, '--', id);
});

test('index.html hero line: the server error answer (empty stats) shows no counts', async () => {
  const s = await heroState(() => ({ error: 'Stats query failed', online: [], stats: {}, highlights: {}, news: [], npcActivities: [] }));
  assert.strictEqual(s.world, true, 'world line hidden');
  assert.strictEqual(s.online, true, 'online line hidden');
});

test('index.html hero line: a failed request shows no counts', async () => {
  const s = await heroState(() => { throw new Error('network'); });
  assert.strictEqual(s.world, true);
  assert.strictEqual(s.online, true);
  assert.ok(!/>0</.test(s.p.el('stats-content').innerHTML), 'board shows no zero');
});

test('index.html hero line: an HTTP error status shows no counts', async () => {
  const s = await heroState(() => ({ status: 502, body: { stats: { marriages: 0, children: 0, totalKills: 0 } } }));
  assert.strictEqual(s.world, true);
});

test('index.html hero line: a real 0 still shows as 0, a missing count as "--"', async () => {
  const s = await heroState(() => ({ online: [], onlineCount: 0, stats: { marriages: 0, children: 12, totalPlayers: 3 }, highlights: {}, news: [] }));
  assert.strictEqual(s.world, false, 'world line shown');
  assert.strictEqual(s.marr, '0');
  assert.strictEqual(s.kids, '12');
  assert.strictEqual(s.kills, '--');
});

for (const pageName of PAGES) {
  test(`${pageName} stats board: a missing count shows "--", a real 0 shows 0`, async () => {
    const p = await loadPage(pageName, {
      fetch: (url) => (url === '/api/stats' ? { online: [], onlineCount: 0, stats: { totalPlayers: 0, marriages: 2 }, highlights: {}, news: [], npcActivities: [] } : url === '/api/sponsors' ? { sponsors: [] } : {}),
    });
    await p.settle();
    const cards = p.el('stats-content').querySelector('#stats-upper').innerHTML;
    assert.ok(/stat-value">0</.test(cards), 'real 0 for total players');
    assert.ok(/stat-value">--</.test(cards), '"--" for a missing count');
    assert.ok(/stat-value">2 \/ --</.test(cards), 'marriages real, children missing');
  });
}
