'use strict';
// balance.html: the page script must run to the end (tabs, refresh timer, saved-token
// login), and every table cell from /api/balance is escaped by makeTable.
const test = require('node:test');
const assert = require('node:assert');
const { HOSTILE, loadPage, assertSafe } = require('./harness');

function balanceData(v) {
  const fight = { created_at: null, player_name: v, player_class: v, player_level: v, outcome: v, monster_name: v, monster_level: v, rounds: v, damage_dealt: v, damage_taken: v, xp_gained: v, gold_gained: v, player_str: v, player_weap_pow: v, player_max_hp: v, monster_max_hp: v, monster_str: v, monster_def: v, is_boss: 1 };
  return {
    overview: { totalCombats: v, winRate: v, deaths: v, fled: v, avgRounds: v, avgDamage: v, activePlayers24h: v, oneHitKills: v, oneHitDeaths: v },
    'class-performance': [],
    'xp-economy': [],
    'death-hotspots': { byFloor: [], byMonster: [{ monster_name: v, monster_level: v, deaths: v, avg_player_level: 1, avg_damage_taken: 1 }] },
    recent: [fight, Object.assign({}, fight, { outcome: 'death' })],
    'one-hit-kills': [fight],
    'boss-fights': [fight],
    'player-activity': [{ player_name: v, player_class: v, max_level: v, total_combats: 0, wins: v, deaths: v, total_xp: 1, total_gold: 1, last_combat: null }],
    suspects: [{ player_name: v, player_class: v, max_level: v, total: v, win_pct: v, max_damage: 1, avg_damage: 1, avg_xp: 1, one_hit_kills: v }],
    'npc-behavior': {
      overview: { total: 1, uniqueNpcs: 1, last24h: 1, deaths: 1, levelUps: 1 },
      actionDist: [], outcomeDist: [],
      classPerf: [{ npc_class: v, living_npcs: v, actions: 1, deaths: v, death_pct: 10, net_gold: 1, net_xp: 1, max_level: v }],
      dungeonBreakdown: [{ outcome: v, total: 1, pct: v }],
      wealthBoard: [{ npc_name: v, npc_class: v, level: v, actions: 1, net_gold: 1, net_xp: 1 }],
      recentDeaths: [{ npc_name: v, npc_level: v, npc_class: v, action: v, location_before: v, hp_before: v, created_at: null }],
      recent: [{ npc_name: v, npc_level: v, npc_class: v, action: v, location_before: v, location_after: v, outcome: v, gold_delta: 5, xp_delta: 5, hp_before: v, hp_after: v, is_ai_driven: 1, created_at: null }],
    },
  };
}

async function page(v, storage) {
  const data = balanceData(v);
  return loadPage('balance.html', {
    storage,
    fetch: (url) => {
      const key = url.replace('/api/balance/', '');
      return key in data ? data[key] : {};
    },
  });
}

const TABLES = ['overview-cards', 'recent-table', 'onehit-table', 'bosses-table', 'deaths-table', 'players-table',
  'suspects-table', 'npc-overview-cards', 'npc-class-table', 'npc-dungeon-table', 'npc-wealth-table', 'npc-deaths-table', 'npc-recent-table'];

test('balance page script runs to the end and every tab switches', async () => {
  const p = await page('Alice');
  assert.deepStrictEqual(p.loaded, ['escape.js']);
  const tabs = p.doc.querySelectorAll('.tab');
  assert.strictEqual(tabs.length, 7);
  for (const tab of tabs) {
    p.click(tab);
    const name = tab.dataset.tab;
    const active = p.doc.querySelectorAll('.tab-content').filter((c) => c.classList.contains('active')).map((c) => c.id);
    assert.deepStrictEqual(active, ['tab-' + name], 'after clicking ' + name);
    assert.deepStrictEqual(p.doc.querySelectorAll('.tab').filter((t) => t.classList.contains('active')).map((t) => t.dataset.tab), [name]);
  }
  // no login token: the login screen is shown by the init block at the end of the script
  assert.strictEqual(p.el('login-screen').style.display, 'flex');
});

test('balance markup closes every div it opens (stray </div> from 339ee5a is gone)', async () => {
  const fs = require('node:fs');
  const html = fs.readFileSync(require('node:path').join(__dirname, '..', 'balance.html'), 'utf8');
  const body = html.slice(html.indexOf('<body'), html.indexOf('<script>', html.indexOf('<body')));
  const opens = (body.match(/<div[\s>]/g) || []).length;
  const closes = (body.match(/<\/div>/g) || []).length;
  assert.strictEqual(closes, opens);
});

test('sink balance makeTable (832) and cards: every cell from /api/balance is escaped', async () => {
  const h = await page(HOSTILE, { balance_token: 'tok' });
  const b = await page('Alice', { balance_token: 'tok' });
  await h.settle();
  await b.settle();
  // the saved token logs in and refreshAll fills the tables
  assert.strictEqual(h.el('dashboard').style.display, 'block');
  assert.ok(String(h.el('last-refresh').textContent).startsWith('Updated'), h.el('last-refresh').textContent);
  for (const id of TABLES) {
    const html = h.el(id).innerHTML;
    assert.ok(html.length > 0, id + ' rendered');
    assertSafe(assert, html, b.el(id).innerHTML, id);
  }
  assert.ok(h.el('recent-table').textContent.includes(HOSTILE));
  // markup the page builds itself still renders as markup
  assert.ok(h.el('onehit-table').querySelector('.tag-boss'));
  assert.ok(h.el('recent-table').querySelector('.tag-death'));
  assert.ok(h.el('npc-recent-table').querySelector('.tag'));
});
