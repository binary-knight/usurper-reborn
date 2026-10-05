'use strict';
// The /api/balance routes of ssh-proxy.js run on a read-only database holding old rows
// (written before 1.2.6, every 1.2.6 column NULL) and 1.2.6 rows (floor_actual set).
// Covers auth on every route, the constant-time token compare, the time windows, the
// changed rate definitions, NULLs passed through as null, the Difficulty and NPC routes
// and the query plans of the queries the page refreshes.
const test = require('node:test');
const assert = require('node:assert');
const nodeCrypto = require('node:crypto');
const { makeDb, insertFight, insertNpc, tallyOf, loadApi, planOf, proxySource, slice } = require('./balance-api-harness');

// Old rows: 50 to 100 hours ago, no 1.2.6 columns. 1.2.6 rows: the last 48 hours.
function fillMixed(w) {
  insertFight(w, { player_name: 'OldOne', outcome: 'victory', rounds: 1, ago: '-100 hours' });
  insertFight(w, { player_name: 'OldOne', outcome: 'death', rounds: 2, monster_name: 'Old Rat', monster_level: 4, damage_taken: 30, ago: '-90 hours' });
  insertFight(w, { player_name: 'OldTwo', player_class: 'Mage', outcome: 'fled', ago: '-80 hours' });
  insertFight(w, { player_name: 'OldTwo', player_class: 'Mage', outcome: 'victory', rounds: 0, is_boss: 1, monster_name: 'Old Boss', ago: '-60 hours' });
  // 1.2.6
  insertFight(w, { player_name: 'NewOne', outcome: 'victory', rounds: 1, ago: '-48 hours', tally: tallyOf({ floor_actual: 3, difficulty: 1 }) });
  insertFight(w, { player_name: 'NewOne', outcome: 'death', rounds: 4, monster_name: 'Cave Bat', monster_level: 9, damage_taken: 40, ago: '-30 hours',
    tally: tallyOf({ floor_actual: 7, difficulty: 1, dmg_to_player_basic: 40, dmg_to_player_ability: 100, dmg_to_player_spell: 50, dmg_to_player_dot: 10, player_hp_end: 0, first_actor: 1 }) });
  insertFight(w, { player_name: 'NewTwo', player_class: 'Mage', outcome: 'victory', rounds: 6, is_boss: 1, monster_name: 'Bat King', ago: '-20 hours',
    tally: tallyOf({ floor_actual: 7, difficulty: 2, dmg_to_team: 30, party_size: 3 }) });
  insertFight(w, { player_name: 'NewTwo', player_class: 'Mage', outcome: 'fled', ago: '-10 hours', tally: tallyOf({ floor_actual: 0, difficulty: 2 }) });
  // a 1.2.6 death row of a fight never entered (mental collapse): every 1.2.6 column NULL
  insertFight(w, { player_name: 'NewOne', outcome: 'death', rounds: 0, monster_name: null, ago: '-5 hours' });
  insertFight(w, { player_name: 'NewOne', outcome: 'victory', rounds: 2, ago: '-2 hours', tally: tallyOf({ floor_actual: 12, difficulty: 1, player_hp_end: null }) });
}

async function withApi(fill, fn, options) {
  const f = makeDb(fill, options);
  try {
    return await fn(loadApi({ db: f.db }), f);
  } finally {
    f.close();
  }
}

const GET_ROUTES = [...new Set([...slice(proxySource(), '// --- Balance Dashboard API ---', 'async function handleDashRequest')
  .matchAll(/method === 'GET' && url === '\/api\/balance\/([\w-]+)'/g)].map((m) => m[1]))];

// ---------------------------------------------------------------- auth

test('every balance GET route answers 401 without a valid token and 403 while the default password is active', async () => {
  assert.ok(GET_ROUTES.includes('difficulty') && GET_ROUTES.includes('onboarding'), GET_ROUTES.join(','));
  assert.strictEqual(GET_ROUTES.length, 12, GET_ROUTES.join(','));
  await withApi(fillMixed, async (a) => {
    for (const route of GET_ROUTES) {
      const u = '/api/balance/' + route;
      assert.strictEqual((await a.call(u, { headers: {} })).status, 401, route + ' without a header');
      assert.strictEqual((await a.call(u, { token: '' })).status, 401, route + ' with an empty token');
      const forged = a.token.slice(0, -1) + (a.token.endsWith('0') ? '1' : '0');
      assert.strictEqual((await a.call(u, { token: forged })).status, 401, route + ' with a forged signature');
      assert.strictEqual((await a.call(u)).status, 200, route + ' with the token');
    }
    a.state.locked = true;
    for (const route of GET_ROUTES) {
      const r = await a.call('/api/balance/' + route);
      assert.strictEqual(r.status, 403, route);
      assert.match(r.body.error, /default password/);
    }
  });
});

test('an unknown balance path is 404 after auth and 401 before it', async () => {
  await withApi(fillMixed, async (a) => {
    assert.strictEqual((await a.call('/api/balance/nope')).status, 404);
    assert.strictEqual((await a.call('/api/balance/nope', { headers: {} })).status, 401);
  });
});

test('the token signature is compared with crypto.timingSafeEqual on equal-length buffers', async () => {
  const calls = [];
  const spy = Object.assign({}, nodeCrypto, {
    createHmac: nodeCrypto.createHmac,
    timingSafeEqual: (a, b) => { calls.push([a.length, b.length]); return nodeCrypto.timingSafeEqual(a, b); },
  });
  const f = makeDb(fillMixed);
  try {
    const a = loadApi({ db: f.db, crypto: spy });
    assert.strictEqual(a.api.verifyBalanceToken(a.token), true);
    assert.deepStrictEqual(calls, [[64, 64]], 'a valid token goes through timingSafeEqual');
    calls.length = 0;
    const [payload, sig] = a.token.split('.');
    const flipped = sig.slice(0, -1) + (sig.endsWith('a') ? 'b' : 'a');
    assert.strictEqual(a.api.verifyBalanceToken(payload + '.' + flipped), false);
    assert.deepStrictEqual(calls, [[64, 64]], 'a wrong signature of the right length is compared, not short-circuited');
  } finally {
    f.close();
  }
});

test('a signature of another length is refused before the compare, without throwing', () => {
  const a = loadApi({ db: null });
  assert.strictEqual(a.api.balanceSigMatches('abcd', 'abc'), false);
  assert.strictEqual(a.api.balanceSigMatches('abc', 'abcd'), false);
  assert.strictEqual(a.api.balanceSigMatches('abc', ''), false);
  assert.strictEqual(a.api.balanceSigMatches('abc', 'abc'), true);
  const [payload] = a.token.split('.');
  assert.strictEqual(a.api.verifyBalanceToken(payload + '.short'), false);
});

// ---------------------------------------------------------------- windows

test('Since 1.2.6 (the default) counts from the first row with floor_actual, rows on both sides', async () => {
  await withApi(fillMixed, async (a) => {
    for (const u of ['/api/balance/overview', '/api/balance/overview?window=since126']) {
      const r = await a.call(u);
      assert.strictEqual(r.status, 200);
      assert.strictEqual(r.body.window.key, 'since126');
      assert.strictEqual(r.body.window.label, 'Since 1.2.6');
      assert.strictEqual(r.body.window.clipped, false);
      // six 1.2.6 rows (the collapse death without a floor included), none of the four old ones
      assert.strictEqual(r.body.totalCombats, 6);
      assert.strictEqual(r.body.victories, 3);
      assert.strictEqual(r.body.deaths, 2);
      assert.strictEqual(r.body.fled, 1);
      assert.strictEqual(r.body.winRate, 50);
      assert.strictEqual(r.body.deathRate, 33.3);
      assert.strictEqual(r.body.fleeRate, 16.7);
      assert.strictEqual(r.body.players, 2);
      assert.strictEqual(r.body.oneHitKills, 1, 'the old round-1 win is outside the window');
    }
    const recent = await a.call('/api/balance/recent');
    assert.deepStrictEqual(recent.body.rows.map((x) => x.player_name).sort(), ['NewOne', 'NewOne', 'NewOne', 'NewOne', 'NewTwo', 'NewTwo']);
  });
});

test('the 24 h, 7 d and 30 d windows count by time, old rows included; an unknown window is refused', async () => {
  await withApi(fillMixed, async (a) => {
    const total = async (w) => (await a.call('/api/balance/overview?window=' + w)).body;
    assert.strictEqual((await total('24h')).totalCombats, 4);
    assert.strictEqual((await total('24h')).window.label, 'Last 24 h');
    assert.strictEqual((await total('7d')).totalCombats, 10);
    assert.strictEqual((await total('30d')).totalCombats, 10);
    assert.strictEqual((await total('30d')).oneHitKills, 2, 'round-1 victories only: not the round-0 win');
    for (const bad of ['1y', 'since125', '', '24h;DROP', '__proto__', 'constructor']) {
      const r = await a.call('/api/balance/overview?window=' + encodeURIComponent(bad));
      if (bad === '') { assert.strictEqual(r.status, 200); continue; }
      assert.strictEqual(r.status, 400, bad);
      assert.match(r.body.error, /Unknown window/);
    }
    for (const route of ['recent', 'difficulty', 'boss-fights', 'death-hotspots', 'player-activity', 'suspects', 'xp-economy', 'class-performance', 'one-hit-kills']) {
      assert.strictEqual((await a.call(`/api/balance/${route}?window=forever`)).status, 400, route);
    }
  });
});

test('Since 1.2.6 is empty, not everything, before the first 1.2.6 row', async () => {
  await withApi((w) => {
    insertFight(w, { outcome: 'victory', ago: '-3 hours' });
    insertFight(w, { outcome: 'death', ago: '-2 hours' });
  }, async (a) => {
    const r = await a.call('/api/balance/overview');
    assert.strictEqual(r.body.window.from, null);
    assert.strictEqual(r.body.totalCombats, 0);
    assert.strictEqual(r.body.winRate, null);
    assert.strictEqual(r.body.avgRounds, null);
    assert.strictEqual((await a.call('/api/balance/overview?window=24h')).body.totalCombats, 2);
  });
});

test('no window starts before the oldest kept victory or fled row, so deaths are not over-counted', async () => {
  // as after the 15000-row cap: non-deaths only from 10 days back, deaths from 20 days back
  await withApi((w) => {
    insertFight(w, { outcome: 'death', ago: '-20 days', tally: tallyOf({ floor_actual: 2 }) });
    insertFight(w, { outcome: 'death', ago: '-15 days', tally: tallyOf({ floor_actual: 2 }) });
    insertFight(w, { outcome: 'victory', ago: '-10 days', tally: tallyOf({ floor_actual: 2 }) });
    insertFight(w, { outcome: 'death', ago: '-5 days', tally: tallyOf({ floor_actual: 2 }) });
  }, async (a) => {
    for (const w of ['30d', 'since126']) {
      const r = await a.call('/api/balance/overview?window=' + w);
      assert.strictEqual(r.body.window.clipped, true, w);
      assert.strictEqual(r.body.totalCombats, 2, w);
      assert.strictEqual(r.body.deaths, 1, w);
      assert.strictEqual(r.body.winRate, 50, w);
    }
    const r7 = await a.call('/api/balance/overview?window=7d');
    assert.strictEqual(r7.body.window.clipped, false);
    assert.strictEqual(r7.body.totalCombats, 1);
  });
});

// ---------------------------------------------------------------- combat views

test('one-hit kills are victories in round 1 only: no deaths, no round 0', async () => {
  await withApi((w) => {
    insertFight(w, { player_name: 'A', outcome: 'victory', rounds: 1, tally: tallyOf() });
    insertFight(w, { player_name: 'B', outcome: 'death', rounds: 1, tally: tallyOf() });
    insertFight(w, { player_name: 'C', outcome: 'victory', rounds: 0, tally: tallyOf() });
    insertFight(w, { player_name: 'D', outcome: 'victory', rounds: 2, tally: tallyOf() });
  }, async (a) => {
    const r = await a.call('/api/balance/one-hit-kills');
    assert.deepStrictEqual(r.body.rows.map((x) => x.player_name), ['A']);
    assert.strictEqual((await a.call('/api/balance/overview')).body.oneHitKills, 1);
  });
});

test('death hotspots: each floor_actual its own point (dungeon_floor is the monster level), monster level range', async () => {
  await withApi((w) => {
    insertFight(w, { outcome: 'death', monster_name: 'Imp', monster_level: 4, dungeon_floor: 4, tally: tallyOf({ floor_actual: 3 }) });
    insertFight(w, { outcome: 'death', monster_name: 'Imp', monster_level: 4, dungeon_floor: 4, tally: tallyOf({ floor_actual: 9 }) });
    insertFight(w, { outcome: 'death', monster_name: 'Imp', monster_level: 6, dungeon_floor: 6, tally: tallyOf({ floor_actual: 9 }) });
  }, async (a) => {
    const r = (await a.call('/api/balance/death-hotspots')).body;
    assert.deepStrictEqual(r.byFloor.map((x) => [x.floor_actual, x.deaths]), [[3, 1], [9, 2]]);
    assert.deepStrictEqual(r.byMonster.map((x) => [x.monster_name, x.min_level, x.max_level, x.deaths]), [['Imp', 4, 6, 3]]);
  });
});

test('death hotspots: floors from floor_actual, old deaths on the no-floor line, damage split null on old rows', async () => {
  await withApi(fillMixed, async (a) => {
    const r = (await a.call('/api/balance/death-hotspots?window=30d')).body;
    assert.deepStrictEqual(r.byFloor.map((x) => [x.floor_actual, x.deaths]), [[7, 1]]);
    assert.strictEqual(r.noFloorDeaths, 2, 'the old death and the collapse death');
    const old = r.byMonster.find((x) => x.monster_name === 'Old Rat');
    assert.strictEqual(old.avg_basic_hits, 30);
    assert.strictEqual(old.avg_damage_taken, null, 'NULL stays null, never 0');
    const bat = r.byMonster.find((x) => x.monster_name === 'Cave Bat');
    assert.strictEqual(bat.avg_basic_hits, 40);
    assert.strictEqual(bat.avg_damage_taken, 200);
    assert.strictEqual(bat.min_level, 9);
    assert.strictEqual(bat.max_level, 9);
  });
});

test('boss fights carry the damage split, null on old rows', async () => {
  await withApi(fillMixed, async (a) => {
    const rows = (await a.call('/api/balance/boss-fights?window=30d')).body.rows;
    const oldBoss = rows.find((x) => x.monster_name === 'Old Boss');
    const newBoss = rows.find((x) => x.monster_name === 'Bat King');
    for (const c of ['floor_actual', 'dmg_to_player_basic', 'dmg_to_player_ability', 'dmg_to_player_spell', 'dmg_to_player_dot', 'dmg_to_team']) {
      assert.strictEqual(oldBoss[c], null, c);
    }
    assert.strictEqual(newBoss.dmg_to_team, 30);
    assert.strictEqual(newBoss.floor_actual, 7);
  });
});

test('player activity and suspects take the class from the latest row', async () => {
  await withApi((w) => {
    for (let i = 0; i < 6; i++) insertFight(w, { player_name: 'Switcher', player_class: 'Cleric', ago: `-${20 - i} hours`, tally: tallyOf() });
    for (let i = 0; i < 6; i++) insertFight(w, { player_name: 'Switcher', player_class: 'Warrior', ago: `-${10 - i} hours`, tally: tallyOf() });
    // the newest row is a Mage again
    insertFight(w, { player_name: 'Switcher', player_class: 'Mage', ago: '-1 hours', tally: tallyOf() });
  }, async (a) => {
    const p = (await a.call('/api/balance/player-activity')).body.rows;
    assert.deepStrictEqual(p.map((x) => [x.player_name, x.player_class, x.total_combats]), [['Switcher', 'Mage', 13]]);
    const s = (await a.call('/api/balance/suspects')).body.rows;
    assert.deepStrictEqual(s.map((x) => [x.player_name, x.player_class, x.total]), [['Switcher', 'Mage', 13]]);
  });
});

test('class performance and xp economy count only the window', async () => {
  await withApi(fillMixed, async (a) => {
    const c = (await a.call('/api/balance/class-performance')).body;
    assert.deepStrictEqual(c.rows.map((x) => [x.player_class, x.total, x.wins]), [['Warrior', 4, 2], ['Mage', 2, 1]]);
    assert.strictEqual(c.window.key, 'since126');
    const x = (await a.call('/api/balance/xp-economy?window=30d')).body;
    assert.strictEqual(x.rows[0].combats, 5);
  });
});

// ---------------------------------------------------------------- difficulty

test('difficulty: 5-floor bands from floor_actual, bosses in their own row, the rest on the no-floor line', async () => {
  await withApi(fillMixed, async (a) => {
    const r = (await a.call('/api/balance/difficulty')).body;
    assert.deepStrictEqual(r.bands.map((b) => [b.band, b.boss, b.floor_from, b.floor_to, b.fights]),
      [[0, 0, 0, 0, 1], [1, 0, 1, 5, 1], [2, 0, 6, 10, 1], [2, 1, 6, 10, 1], [3, 0, 11, 15, 1]]);
    assert.strictEqual(r.noFloorFights, 1, 'the collapse death');
    assert.deepStrictEqual(r.classes, ['Mage', 'Warrior']);
    assert.deepStrictEqual(r.difficulties, [1, 2]);
    const death = r.bands.find((b) => b.band === 2 && b.boss === 0);
    assert.strictEqual(death.death_pct, 100);
    assert.strictEqual(death.hp_lost_pct, 100, '(40 + 100 + 50 + 10) of 200 max HP');
    assert.strictEqual(death.dmg_to_player_ability, 100);
    assert.strictEqual(death.monster_first_pct, 100);
    assert.strictEqual(death.players, 1);
    const first = r.bands.find((b) => b.band === 1);
    assert.strictEqual(first.one_round_win_pct, 100);
    // 30 d: the old rows have no floor, so they only add to the no-floor line
    const r30 = (await a.call('/api/balance/difficulty?window=30d')).body;
    assert.strictEqual(r30.noFloorFights, 5);
    assert.strictEqual(r30.bands.reduce((n, b) => n + b.fights, 0), 5);
  });
});

test('difficulty: class and difficulty filters, and junk filters refused', async () => {
  await withApi(fillMixed, async (a) => {
    const mage = (await a.call('/api/balance/difficulty?class=Mage')).body;
    assert.deepStrictEqual(mage.bands.map((b) => [b.band, b.boss]), [[0, 0], [2, 1]]);
    assert.deepStrictEqual(mage.filters, { class: 'Mage', difficulty: null });
    const hard = (await a.call('/api/balance/difficulty?difficulty=1')).body;
    assert.deepStrictEqual(hard.bands.map((b) => [b.band, b.boss]), [[1, 0], [2, 0], [3, 0]]);
    const both = (await a.call('/api/balance/difficulty?class=Mage&difficulty=1')).body;
    assert.deepStrictEqual(both.bands, []);
    for (const q of ['class=%3Cb%3E', 'class=Mage%27--', 'difficulty=4', 'difficulty=-1', 'difficulty=1.5', 'difficulty=x']) {
      const r = await a.call('/api/balance/difficulty?' + q);
      assert.strictEqual(r.status, 400, q);
    }
  });
});

test('difficulty: a 1.2.6 column that is NULL comes back null, never 0', async () => {
  await withApi((w) => {
    insertFight(w, { outcome: 'victory', tally: { floor_actual: 4 } });
  }, async (a) => {
    const b = (await a.call('/api/balance/difficulty')).body.bands[0];
    for (const c of ['hp_lost_pct', 'dmg_to_player_basic', 'dmg_to_team', 'dmg_by_player', 'heal_player', 'potions_used',
      'abilities_used', 'spells_used', 'party_size', 'encounter_size', 'monster_first_pct', 'teammates_lost']) {
      assert.strictEqual(b[c], null, c);
    }
    assert.strictEqual(b.win_pct, 100);
  });
});

// ---------------------------------------------------------------- npc

test('npc behaviour: team runs included, goal rows left out, percentages over the same rows, 30 days', async () => {
  await withApi((w) => {
    insertNpc(w, { npc_name: 'Ann', action: 'dungeon', outcome: 'won' });
    insertNpc(w, { npc_name: 'Ann', action: 'dungeon', outcome: null });
    insertNpc(w, { npc_name: 'Bob', action: 'team_dungeon', outcome: 'died' });
    insertNpc(w, { npc_name: 'Bob', action: 'team_dungeon', outcome: 'won', npc_class: 'Mage', gold_delta: 50 });
    insertNpc(w, { npc_name: 'Cy', action: 'target_steer', outcome: 'goal:Combat' });
    insertNpc(w, { npc_name: 'Cy', action: 'target_steer', outcome: 'goal:Social' });
    insertNpc(w, { npc_name: 'Old', action: 'dungeon', outcome: 'won', ago: '-40 days' });
  }, async (a) => {
    const r = (await a.call('/api/balance/npc-behavior')).body;
    assert.strictEqual(r.window.label, 'Last 30 d');
    assert.strictEqual(r.overview.total, 4);
    assert.strictEqual(r.overview.uniqueNpcs, 2);
    assert.ok(!r.actionDist.some((x) => x.action === 'target_steer'));
    assert.ok(!r.outcomeDist.some((x) => String(x.outcome).startsWith('goal:')));
    assert.strictEqual(r.outcomeDist.reduce((n, x) => n + x.pct, 0), 100);
    assert.deepStrictEqual(r.dungeonBreakdown.map((x) => [x.action, x.outcome, x.total, x.pct]),
      [['dungeon', null, 1, 50], ['dungeon', 'won', 1, 50], ['team_dungeon', 'died', 1, 50], ['team_dungeon', 'won', 1, 50]]);
    assert.deepStrictEqual(r.wealthBoard.map((x) => [x.npc_name, x.npc_class, x.net_gold]), [['Bob', 'Mage', 50]]);
    assert.ok(r.classPerf.every((x) => 'npcs_seen' in x));
  });
});

// ---------------------------------------------------------------- read-only and plans

const REFRESHED = ['overview', 'class-performance', 'xp-economy', 'death-hotspots', 'recent', 'one-hit-kills', 'boss-fights',
  'player-activity', 'suspects', 'difficulty', 'difficulty?class=Warrior&difficulty=1'];

test('every refreshed combat query is read-only and searches an index (no full scan of combat_events)', async () => {
  // enough rows on both sides that a full scan would be a real cost
  await withApi((w) => {
    for (let i = 0; i < 300; i++) insertFight(w, { player_name: 'P' + (i % 20), outcome: ['victory', 'death', 'fled'][i % 3], ago: `-${200 + i} hours` });
    for (let i = 0; i < 300; i++) insertFight(w, { player_name: 'P' + (i % 20), outcome: ['victory', 'death', 'fled'][i % 3], is_boss: i % 9 === 0 ? 1 : 0, ago: `-${1 + (i % 150)} hours`, tally: tallyOf({ floor_actual: i % 40 }) });
  }, async (a, f) => {
    const lines = [];
    for (const route of REFRESHED) {
      for (const w of ['since126', '24h', '7d', '30d']) {
        a.prepared.length = 0;
        const r = await a.call(`/api/balance/${route}${route.includes('?') ? '&' : '?'}window=${w}`);
        assert.strictEqual(r.status, 200, route + ' ' + w);
        for (const p of a.prepared) {
          assert.match(p.sql.trim(), /^SELECT\b/i, 'read-only statement');
          if (!/combat_events/.test(p.sql)) continue;
          const plan = planOf(f.db, p.sql, p.args);
          lines.push(`${route} ${w}: ${plan.join(' | ')}`);
          for (const step of plan) {
            assert.doesNotMatch(step, /^SCAN (combat_events|l)\b/, `${route} ${w}: ${p.sql.replace(/\s+/g, ' ')}\n${plan.join('\n')}`);
          }
          assert.ok(plan.some((s) => /^SEARCH combat_events USING (COVERING )?INDEX idx_ce_(created|floor|outcome|player)\b/.test(s)), plan.join('\n'));
        }
      }
    }
    if (process.env.BALANCE_PLAN_OUT) require('node:fs').writeFileSync(process.env.BALANCE_PLAN_OUT, lines.join('\n') + '\n');
  });
});

test('onboarding route still answers on the read-only database', async () => {
  await withApi((w) => {
    const ins = w.prepare("INSERT INTO onboarding_events (username, event, connection_type, created_at) VALUES (?, ?, ?, datetime('now', '-1 days'))");
    ins.run('u1', 'account_created', 'Web');
    ins.run('u1', 'character_created', 'Web');
    ins.run('u2', 'account_created', null);
  }, async (a) => {
    const r = (await a.call('/api/balance/onboarding')).body;
    assert.strictEqual(r.tableExists, true);
    assert.strictEqual(r.last7d.cohortSize, 2);
    assert.strictEqual(r.last7d.stages.character_created, 1);
    assert.deepStrictEqual(r.byConnectionType7d.map((x) => [x.ctype, x.created]).sort(), [['Unknown', 1], ['Web', 1]]);
  });
});
