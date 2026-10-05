'use strict';
// balance.html: the page script must run to the end (tabs, refresh timer, saved-token
// login), every table cell from /api/balance is escaped by makeTable, a server error is
// shown as the server's own text, the refresh loads only the overview and the open tab,
// every card and table states its window, and a NULL from the server renders blank.
const test = require('node:test');
const assert = require('node:assert');
const { HOSTILE, loadPage, assertSafe } = require('./harness');

const FROM = '2026-10-04 10:00:00';

function balanceData(v) {
  const win = { key: 'since126', label: v, from: FROM, clipped: false };
  const fight = { created_at: null, player_name: v, player_class: v, player_level: v, outcome: v, monster_name: v, monster_level: v, rounds: v, damage_dealt: v, damage_taken: v, xp_gained: v, gold_gained: v, player_str: v, player_weap_pow: v, player_max_hp: v, monster_max_hp: v, monster_str: v, monster_def: v, is_boss: 1, floor_actual: v, dmg_to_player_basic: v, dmg_to_player_ability: v, dmg_to_player_spell: v, dmg_to_player_dot: v, dmg_to_team: v };
  const band = { band: 1, boss: 1, floor_from: 1, floor_to: 5, fights: v, players: v, win_pct: v, death_pct: v, flee_pct: v, avg_rounds: v, one_round_win_pct: v, hp_lost_pct: v, dmg_to_player_basic: v, dmg_to_player_ability: v, dmg_to_player_spell: v, dmg_to_player_dot: v, dmg_to_team: v, dmg_by_player: v, dmg_by_team: v, heal_player: v, potions_used: v, abilities_used: v, spells_used: v, party_size: v, encounter_size: v, monster_first_pct: v, teammates_lost: v };
  return {
    overview: { window: win, totalCombats: v, winRate: v, deathRate: v, fleeRate: v, avgRounds: v, avgDamage: v, players: v, oneHitKills: v },
    'class-performance': { window: win, rows: [] },
    'xp-economy': { window: win, rows: [] },
    'death-hotspots': { window: win, byFloor: [], noFloorDeaths: v, byMonster: [{ monster_name: v, min_level: v, max_level: 9, deaths: v, avg_player_level: 1, avg_basic_hits: 1, avg_damage_taken: 1 }] },
    recent: { window: win, rows: [fight, Object.assign({}, fight, { outcome: 'death' })] },
    'one-hit-kills': { window: win, rows: [fight] },
    'boss-fights': { window: win, rows: [fight] },
    'player-activity': { window: win, rows: [{ player_name: v, player_class: v, max_level: v, total_combats: 0, wins: v, deaths: v, total_xp: 1, total_gold: 1, last_combat: null }] },
    suspects: { window: win, rows: [{ player_name: v, player_class: v, max_level: v, total: v, win_pct: v, max_damage: 1, avg_damage: 1, avg_xp: 1, one_hit_kills: v }] },
    difficulty: { window: win, filters: { class: null, difficulty: null }, bands: [band, Object.assign({}, band, { band: 0, boss: 0 })], noFloorFights: v, classes: [v, 'Mage'], difficulties: [1] },
    'npc-behavior': {
      window: { label: 'Last 30 d' },
      overview: { total: 1, uniqueNpcs: 1, last24h: 1, deaths: 1, levelUps: 1 },
      actionDist: [], outcomeDist: [],
      classPerf: [{ npc_class: v, npcs_seen: v, actions: 1, deaths: v, death_pct: 10, net_gold: 1, net_xp: 1, max_level: v }],
      dungeonBreakdown: [{ action: 'team_dungeon', outcome: v, total: 1, pct: v }],
      wealthBoard: [{ npc_name: v, npc_class: v, level: v, actions: 1, net_gold: 1, net_xp: 1 }],
      recentDeaths: [{ npc_name: v, npc_level: v, npc_class: v, action: v, location_before: v, hp_before: v, created_at: null }],
      recent: [{ npc_name: v, npc_level: v, npc_class: v, action: v, location_before: v, location_after: v, outcome: v, gold_delta: 5, xp_delta: -5, hp_before: v, hp_after: v, is_ai_driven: 1, created_at: null }],
    },
    onboarding: {
      tableExists: true,
      last7d: { cohortSize: 2, stages: { account_created: 2, character_created: 1, reached_town: 1, first_kill: 0, second_login: 0 } },
      last30d: { cohortSize: 4, stages: { account_created: 4, character_created: 3, reached_town: 2, first_kill: 1, second_login: 1 } },
      byConnectionType7d: [{ ctype: v, created: 2, killed: 1 }],
    },
  };
}

function endpoint(url) {
  return url.replace('/api/balance/', '').split('?')[0];
}

async function page(v, storage, override) {
  const data = balanceData(v);
  return loadPage('balance.html', {
    storage,
    fetch: (url) => {
      if (override) { const r = override(url); if (r !== undefined) return r; }
      const key = endpoint(url);
      return key in data ? data[key] : {};
    },
  });
}

function tabEl(p, name) {
  return p.doc.querySelectorAll('.tab').find((t) => t.dataset.tab === name);
}

const TABS = ['difficulty', 'recent', 'onehit', 'bosses', 'deaths', 'players', 'suspects', 'npc', 'onboarding'];
const TABLES = ['overview-cards', 'difficulty-table', 'diff-class', 'recent-table', 'onehit-table', 'bosses-table', 'deaths-table',
  'players-table', 'suspects-table', 'npc-overview-cards', 'npc-class-table', 'npc-dungeon-table', 'npc-wealth-table',
  'npc-deaths-table', 'npc-recent-table', 'onboarding-table'];

test('balance page script runs to the end and every tab switches, Difficulty first', async () => {
  const p = await page('Alice');
  assert.deepStrictEqual(p.loaded, ['escape.js']);
  const tabs = p.doc.querySelectorAll('.tab');
  assert.deepStrictEqual(tabs.map((t) => t.dataset.tab), TABS);
  assert.deepStrictEqual(p.doc.querySelectorAll('.tab-content').filter((c) => c.classList.contains('active')).map((c) => c.id), ['tab-difficulty']);
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

test('the classes the NPC tab uses have rules', () => {
  const html = require('./harness').readPage('balance.html');
  const style = html.slice(html.indexOf('<style>'), html.indexOf('</style>'));
  assert.match(style, /\n\s*\.grid\s*\{/);
  assert.match(style, /\.grid\.two-col\s*\{/);
});

test('sink balance makeTable and cards: every cell from /api/balance is escaped, on every tab', async () => {
  const h = await page(HOSTILE, { balance_token: 'tok' });
  const b = await page('Alice', { balance_token: 'tok' });
  for (const p of [h, b]) {
    await p.settle();
    assert.strictEqual(p.el('dashboard').style.display, 'block');
    for (const name of TABS) { p.click(tabEl(p, name)); await p.settle(); }
    assert.deepStrictEqual(p.errors, []);
  }
  assert.ok(String(h.el('last-refresh').textContent).startsWith('Updated'), h.el('last-refresh').textContent);
  for (const id of TABLES) {
    const html = h.el(id).innerHTML;
    assert.ok(html.length > 0, id + ' rendered');
    assertSafe(assert, html, b.el(id).innerHTML, id);
  }
  assert.ok(h.el('recent-table').textContent.includes(HOSTILE));
  assert.ok(h.el('overview-cards').textContent.includes(HOSTILE + ', from'), 'the window label is shown, as text');
  // markup the page builds itself still renders as markup
  assert.ok(h.el('onehit-table').querySelector('.tag-boss'));
  assert.ok(h.el('difficulty-table').querySelector('.tag-boss'));
  assert.ok(h.el('recent-table').querySelector('.tag-death'));
  assert.ok(h.el('npc-recent-table').querySelector('.tag'));
  assert.ok(h.el('npc-dungeon-table').querySelector('.tag'));
});

test('a server error is shown as the server\'s own text, not a crash in a loader', async () => {
  const lock = 'Dashboard locked: the default password is still active. Change it first (POST /api/balance/change-password).';
  for (const [status, message] of [[403, lock], [500, 'no such column: boom'], [503, 'Database not available']]) {
    const p = await page('Alice', { balance_token: 'tok' }, (url) => ({ status, body: { error: message } }));
    await p.settle();
    assert.strictEqual(p.el('dashboard').style.display, 'block', 'a locked or failing server still shows the dashboard');
    assert.strictEqual(p.el('api-error').style.display, 'block', String(status));
    assert.strictEqual(p.el('api-error').textContent, 'Server error: ' + message);
    assert.strictEqual(p.el('last-refresh').textContent, 'Error: ' + message);
    assert.deepStrictEqual(p.errors, []);
  }
  // a good refresh clears it
  let fail = true;
  const p = await page('Alice', { balance_token: 'tok' }, (url) => (fail ? { status: 500, body: { error: 'boom' } } : undefined));
  await p.settle();
  assert.strictEqual(p.el('api-error').style.display, 'block');
  fail = false;
  await p.run('refreshVisible()');
  assert.strictEqual(p.el('api-error').style.display, 'none');
});

test('a 401 logs out', async () => {
  const p = await page('Alice', { balance_token: 'tok' }, () => ({ status: 401, body: { error: 'Unauthorized' } }));
  await p.settle();
  assert.strictEqual(p.el('login-screen').style.display, 'flex');
  assert.strictEqual(p.storage.balance_token, undefined);
});

test('every 30 s the overview and the open tab refresh, nothing else; the NPC tab only while open', async () => {
  const p = await page('Alice', { balance_token: 'tok' });
  await p.settle();
  assert.deepStrictEqual(p.calls.interval.map((i) => i.ms), [30000]);
  const tick = async () => {
    p.calls.fetch.length = 0;
    p.calls.interval[0].fn();
    await p.settle();
    return p.calls.fetch.map((c) => endpoint(c.url)).sort();
  };
  const overview = ['class-performance', 'death-hotspots', 'overview', 'xp-economy'];
  assert.deepStrictEqual(await tick(), overview.concat(['difficulty']).sort());
  p.calls.fetch.length = 0;
  p.click(tabEl(p, 'npc'));
  await p.settle();
  assert.deepStrictEqual(p.calls.fetch.map((c) => endpoint(c.url)), ['npc-behavior'], 'opening a tab loads it');
  assert.deepStrictEqual(await tick(), overview.concat(['npc-behavior']).sort());
  p.click(tabEl(p, 'recent'));
  await p.settle();
  assert.deepStrictEqual(await tick(), overview.concat(['recent']).sort());
  p.click(tabEl(p, 'deaths'));
  await p.settle();
  assert.deepStrictEqual(await tick(), overview, 'the deaths tab shares the overview endpoint');
  // logged out: the timer does nothing
  p.run('doLogout()');
  assert.deepStrictEqual(await tick(), []);
});

test('the window filter goes to every combat view; Since 1.2.6 is the default', async () => {
  const p = await page('Alice', { balance_token: 'tok' });
  await p.settle();
  const combat = p.calls.fetch.map((c) => c.url).filter((u) => !/npc-behavior|onboarding/.test(u));
  assert.ok(combat.length >= 5);
  for (const u of combat) assert.match(u, /[?&]window=since126(&|$)/, u);
  p.calls.fetch.length = 0;
  const sel = p.el('window-select');
  sel.value = '7d';
  p.doc.dispatch(sel, 'change');
  await p.settle();
  const urls = p.calls.fetch.map((c) => c.url);
  assert.deepStrictEqual(urls.map(endpoint).sort(), ['class-performance', 'death-hotspots', 'difficulty', 'overview', 'xp-economy']);
  for (const u of urls) assert.match(u, /[?&]window=7d(&|$)/, u);
});

test('difficulty filters go to the server and the class list comes from it', async () => {
  const p = await page('Alice', { balance_token: 'tok' });
  await p.settle();
  const opts = p.el('diff-class').querySelectorAll('option').map((o) => [o.getAttribute('value'), o.textContent]);
  assert.deepStrictEqual(opts, [['', 'All classes'], ['Alice', 'Alice'], ['Mage', 'Mage']]);
  p.calls.fetch.length = 0;
  p.el('diff-class').value = 'Mage';
  p.doc.dispatch(p.el('diff-class'), 'change');
  await p.settle();
  assert.deepStrictEqual(p.calls.fetch.map((c) => c.url), ['/api/balance/difficulty?class=Mage&window=since126']);
  p.calls.fetch.length = 0;
  p.el('diff-level').value = '2';
  p.doc.dispatch(p.el('diff-level'), 'change');
  await p.settle();
  assert.deepStrictEqual(p.calls.fetch.map((c) => c.url), ['/api/balance/difficulty?class=Mage&difficulty=2&window=since126']);
  assert.match(p.el('difficulty-table').textContent, /Mage, Hard/);
});

function cells(p, id) {
  return p.el(id).querySelectorAll('tbody tr').map((tr) => tr.querySelectorAll('td').map((td) => td.textContent));
}

function headers(p, id) {
  return p.el(id).querySelectorAll('th').map((th) => th.textContent);
}

test('a NULL from the server renders blank, never 0', async () => {
  const nulls = {
    difficulty: { window: { key: 'since126', label: 'Since 1.2.6', from: FROM, clipped: false }, bands: [{ band: 2, boss: 0, floor_from: 6, floor_to: 10, fights: 3, players: 1, win_pct: 0, death_pct: 100, flee_pct: 0, avg_rounds: 2, one_round_win_pct: 0, hp_lost_pct: null, dmg_to_player_basic: null, dmg_to_player_ability: null, dmg_to_player_spell: null, dmg_to_player_dot: null, dmg_to_team: null, dmg_by_player: null, dmg_by_team: null, heal_player: null, potions_used: null, abilities_used: null, spells_used: null, party_size: null, encounter_size: null, monster_first_pct: null, teammates_lost: null }], noFloorFights: 7, classes: [], difficulties: [] },
    'boss-fights': { window: { key: '30d', label: 'Last 30 d', from: FROM, clipped: false }, rows: [{ created_at: FROM, player_name: 'A', player_class: 'Mage', player_level: 3, outcome: 'victory', monster_name: 'B', monster_level: 4, floor_actual: null, rounds: 2, damage_dealt: 9, damage_taken: 5, dmg_to_player_basic: null, dmg_to_player_ability: null, dmg_to_player_spell: null, dmg_to_player_dot: null, dmg_to_team: null }] },
    'death-hotspots': { window: { key: '30d', label: 'Last 30 d', from: FROM, clipped: false }, byFloor: [], noFloorDeaths: 4, byMonster: [{ monster_name: 'Old Rat', min_level: 4, max_level: 6, deaths: 1, avg_player_level: 3, avg_basic_hits: 30, avg_damage_taken: null }] },
    overview: { window: { key: 'since126', label: 'Since 1.2.6', from: null, clipped: false }, totalCombats: 0, winRate: null, deathRate: null, fleeRate: null, avgRounds: null, avgDamage: null, players: 0, oneHitKills: 0 },
  };
  const p = await page('Alice', { balance_token: 'tok' }, (url) => nulls[endpoint(url)]);
  await p.settle();
  for (const name of ['bosses', 'deaths']) { p.click(tabEl(p, name)); await p.settle(); }
  const d = cells(p, 'difficulty-table')[0];
  const dh = headers(p, 'difficulty-table');
  assert.strictEqual(d[dh.indexOf('Death %')], '100%');
  for (const h of ['HP Lost % Max', 'Taken Basic', 'Taken Ability', 'Taken Spell', 'Taken DoT', 'To Team', 'By Player', 'By Team',
    'Heals', 'Potions', 'Abilities', 'Spells', 'Party', 'Encounter', 'Monster First %', 'Teammates Lost']) {
    assert.strictEqual(d[dh.indexOf(h)], '', h);
  }
  assert.match(p.el('difficulty-table').textContent, /No floor recorded \(rows before 1\.2\.6, or no fight entered\): 7 fights/);
  const bRow = cells(p, 'bosses-table')[0];
  const bh = headers(p, 'bosses-table');
  assert.strictEqual(bRow[bh.indexOf('Basic Hits')], '5');
  for (const h of ['Floor', 'Taken Basic', 'Taken Ability', 'Taken Spell', 'Taken DoT', 'To Team']) assert.strictEqual(bRow[bh.indexOf(h)], '', h);
  const m = cells(p, 'deaths-table')[0];
  const mh = headers(p, 'deaths-table');
  assert.deepStrictEqual([m[mh.indexOf('Levels')], m[mh.indexOf('Avg Basic Hits')], m[mh.indexOf('Avg Dmg Taken')]], ['4-6', '30', '']);
  assert.match(p.el('deathFloorChart-nofloor').textContent, /: 4 deaths$/);
  const cardVals = p.el('overview-cards').querySelectorAll('.card').map((c) => [c.querySelector('.label').textContent, c.querySelector('.value').textContent]);
  assert.deepStrictEqual(cardVals.filter(([l]) => /Rate|Avg/.test(l)).map(([, v]) => v), ['', '', '', '', '']);
});

test('every card and table states its window', async () => {
  const p = await page('Alice', { balance_token: 'tok' });
  await p.settle();
  for (const name of TABS) { p.click(tabEl(p, name)); await p.settle(); }
  const cardsEl = p.el('overview-cards').querySelectorAll('.card');
  assert.strictEqual(cardsEl.length, 8);
  for (const c of cardsEl) assert.match(c.querySelector('.note').textContent, /^Alice, from /);
  for (const id of ['difficulty-table', 'recent-table', 'onehit-table', 'bosses-table', 'deaths-table', 'players-table', 'suspects-table']) {
    assert.match(p.el(id).querySelector('.table-note').textContent, /^Alice, from /, id);
  }
  for (const id of ['classWinChart-window', 'classDmgChart-window', 'xpCurveChart-window', 'deathFloorChart-window']) {
    assert.match(p.el(id).textContent, /^Alice, from /, id);
  }
  for (const c of p.el('npc-overview-cards').querySelectorAll('.card')) assert.ok(c.querySelector('.note').textContent.length > 0);
  assert.match(p.el('onboarding-table').textContent, /Cohorts: 2 accounts in the last 7 d, 4 in the last 30 d/);
});

test('NPC tab: blank for a missing outcome, negative deltas shown, NPCs seen in 30 days', async () => {
  const p = await page('Alice', { balance_token: 'tok' }, (url) => (endpoint(url) === 'npc-behavior' ? {
    overview: { total: 4, uniqueNpcs: 2, last24h: 4, deaths: 1, levelUps: 0 },
    actionDist: [], outcomeDist: [],
    classPerf: [{ npc_class: 'Mage', npcs_seen: 2, actions: 4, deaths: 1, death_pct: 25, net_gold: 5, net_xp: 0, max_level: 3 }],
    dungeonBreakdown: [{ action: 'dungeon', outcome: null, total: 1, pct: 50 }, { action: 'team_dungeon', outcome: 'won', total: 1, pct: 100 }],
    wealthBoard: [], recentDeaths: [],
    recent: [{ npc_name: 'N', npc_level: 1, npc_class: 'Mage', action: 'dungeon', location_before: 'a', location_after: 'b', outcome: null, gold_delta: 0, xp_delta: -7, hp_before: 1, hp_after: 1, is_ai_driven: 0, created_at: null }],
  } : undefined));
  await p.settle();
  p.click(tabEl(p, 'npc'));
  await p.settle();
  assert.deepStrictEqual(cells(p, 'npc-dungeon-table'), [['Solo', '', '1', '50%'], ['Team', 'won', '1', '100%']]);
  // a missing outcome is an empty cell, not an empty coloured tag
  const outcomeCells = p.el('npc-dungeon-table').querySelectorAll('tbody tr').map((tr) => tr.querySelectorAll('td')[1]);
  assert.deepStrictEqual(outcomeCells.map((td) => !!td.querySelector('.tag')), [false, true]);
  assert.strictEqual(p.el('npc-recent-table').querySelectorAll('tbody tr')[0].querySelectorAll('td')[headers(p, 'npc-recent-table').indexOf('Outcome')].querySelector('.tag'), null);
  const r = cells(p, 'npc-recent-table')[0];
  const rh = headers(p, 'npc-recent-table');
  assert.strictEqual(r[rh.indexOf('Outcome')], '');
  assert.strictEqual(r[rh.indexOf('XP Δ')], '-7');
  assert.ok(!p.el('npc-dungeon-table').textContent.includes('null'));
  assert.ok(headers(p, 'npc-class-table').includes('NPCs Seen (30 d)'));
  const labels = p.el('npc-overview-cards').querySelectorAll('.label').map((l) => l.textContent);
  assert.ok(labels.includes('NPCs Seen'));
});

test('onboarding tab: the funnel and the no-table message', async () => {
  const p = await page('Alice', { balance_token: 'tok' });
  await p.settle();
  p.click(tabEl(p, 'onboarding'));
  await p.settle();
  assert.deepStrictEqual(cells(p, 'onboarding-table')[1], ['Character created', '1 (50.0%)', '3 (75.0%)']);
  const q = await page('Alice', { balance_token: 'tok' }, (url) => (endpoint(url) === 'onboarding' ? { tableExists: false, message: 'onboarding_events table not yet created' } : undefined));
  await q.settle();
  q.click(tabEl(q, 'onboarding'));
  await q.settle();
  assert.strictEqual(q.el('onboarding-table').textContent, 'onboarding_events table not yet created');
});

test('the page against the real routes: every tab renders, nothing fails, old rows show blanks', async () => {
  const { makeDb, insertFight, tallyOf, loadApi } = require('./balance-api-harness');
  const f = makeDb((w) => {
    insertFight(w, { player_name: 'OldOne', outcome: 'victory', ago: '-95 hours' });
    insertFight(w, { player_name: 'OldOne', outcome: 'death', is_boss: 1, monster_name: 'Old Boss', ago: '-90 hours' });
    insertFight(w, { player_name: 'NewOne', outcome: 'victory', rounds: 1, ago: '-40 hours', tally: tallyOf({ floor_actual: 3 }) });
    insertFight(w, { player_name: 'NewOne', outcome: 'death', is_boss: 1, monster_name: 'New Boss', ago: '-20 hours', tally: tallyOf({ floor_actual: 8, dmg_to_team: 12 }) });
  });
  try {
    const a = loadApi({ db: f.db });
    const p = await loadPage('balance.html', {
      storage: { balance_token: a.token },
      fetch: async (url, init) => a.call(url, { token: String(init.headers.Authorization).slice(7) }),
    });
    await p.settle();
    for (const name of TABS) { p.click(tabEl(p, name)); await p.settle(); }
    assert.deepStrictEqual(p.errors, []);
    assert.strictEqual(p.el('api-error').style.display, 'none');
    assert.ok(String(p.el('last-refresh').textContent).startsWith('Updated'), p.el('last-refresh').textContent);
    // Since 1.2.6: the old boss death is outside the window
    assert.deepStrictEqual(cells(p, 'bosses-table').map((r) => r[headers(p, 'bosses-table').indexOf('Boss')]), ['New Boss']);
    assert.strictEqual(cells(p, 'bosses-table')[0][headers(p, 'bosses-table').indexOf('To Team')], '12');
    assert.deepStrictEqual(cells(p, 'difficulty-table').map((r) => r.slice(0, 3)), [['1-5', '', '1'], ['6-10', 'BOSS', '1']]);
    // 30 d: the old boss row is in, its 1.2.6 cells blank
    const sel = p.el('window-select');
    sel.value = '30d';
    p.doc.dispatch(sel, 'change');
    p.click(tabEl(p, 'bosses'));
    await p.settle();
    const bh = headers(p, 'bosses-table');
    const old = cells(p, 'bosses-table').find((r) => r[bh.indexOf('Boss')] === 'Old Boss');
    for (const h of ['Floor', 'Taken Basic', 'Taken Ability', 'Taken Spell', 'Taken DoT', 'To Team']) assert.strictEqual(old[bh.indexOf(h)], '', h);
    assert.strictEqual(old[bh.indexOf('Basic Hits')], '10');
    assert.match(p.el('deathFloorChart-nofloor').textContent, /: 1 deaths$/);
  } finally {
    f.close();
  }
});
