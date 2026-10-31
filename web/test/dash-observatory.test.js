'use strict';
// The Observatory endpoints (web/dash-observatory.js and its routes in ssh-proxy.js) are public. Each
// sends an allow-list, never a login key (username, seller, buyer, world_boss_damage.player_name),
// and a player's display name only as a boss leader or a fallen hero. The fixture database is a temp
// file built from the game's own CREATE TABLE text and migration columns, so the queries follow the
// real schema and indexes.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const Database = require('better-sqlite3');
const { publicNpcs } = require('../dash-npcs');
const O = require('../dash-observatory');
const { schemaSql, slice, proxySource, planOf } = require('./balance-api-harness');

const ROOT = path.join(__dirname, '..', '..');
const read = (...p) => fs.readFileSync(path.join(ROOT, ...p), 'utf8');

// ---------------------------------------------------------------- schema from the C#

function fixtureSchema() {
  const backend = read('Scripts', 'Systems', 'SqlSaveBackend.cs');
  const boss = read('Scripts', 'Systems', 'SqlSaveBackend.WorldBoss.cs');
  const table = (name) => {
    const m = backend.match(new RegExp('CREATE TABLE IF NOT EXISTS ' + name + ' \\([\\s\\S]*?\\n\\s*\\);'));
    if (!m) throw new Error('table not found in SqlSaveBackend.cs: ' + name);
    return m[0];
  };
  const indexes = (tbl) => backend.match(new RegExp('CREATE INDEX IF NOT EXISTS \\w+\\s+ON ' + tbl + '\\([^;]*\\);', 'g')) || [];
  const columns = (arrayName) => {
    const m = boss.match(new RegExp(arrayName + ' =\\s*\\{([\\s\\S]*?)\\};'));
    if (!m) throw new Error('column list not found: ' + arrayName);
    return [...m[1].matchAll(/"([^"]+)"/g)].map((x) => x[1]);
  };
  const bossCols = columns('WorldBossColumns');
  const damageCols = columns('WorldBossDamageColumns');
  assert.ok(damageCols.includes("is_npc INTEGER DEFAULT 0") && damageCols.includes("display_name TEXT DEFAULT ''"));
  const sql = [schemaSql().sql];
  for (const t of ['players', 'world_state', 'auction_listings', 'world_bosses', 'world_boss_damage', 'fallen_legacy']) {
    sql.push(table(t), ...indexes(t));
  }
  sql.push(...bossCols.map((c) => `ALTER TABLE world_bosses ADD COLUMN ${c};`));
  sql.push(...damageCols.map((c) => `ALTER TABLE world_boss_damage ADD COLUMN ${c};`));
  return sql.join('\n');
}

// ---------------------------------------------------------------- the planted world

// Logins differ from display names. "Torvin Ash" is a player who took an NPC's name.
const PLAYERS = [
  { username: 'acct_hroth', display: 'HrothgarZq' },
  { username: 'acct_mira', display: 'MirabelXq' },
  { username: 'acct_torv', display: 'Torvin Ash' },
  { username: 'acct_xyq', display: 'Xy' },
  { username: 'acct_boss_ww', display: 'Bosshitter Ww' },
  { username: 'acct_fallen_qq', display: 'Fallenhero Qq' },
];
const PLAYER_NAMES = new Set(PLAYERS.flatMap((p) => [p.username.toLowerCase(), p.display.toLowerCase()]));
const LOGINS = PLAYERS.map((p) => p.username);
// Display names that are not also an NPC's name.
const DISPLAYS = PLAYERS.map((p) => p.display).filter((d) => d !== 'Torvin Ash' && d !== 'Xy');
// Values that are planted in fields no endpoint may send.
const HIDDEN = ['acct_seller_zz', 'acct_buyer_zz', 'ITEMJSON_SECRET', '987654', 'HEIR_SECRET', '55555', 'affair', 'mord-guid'];

const NPCS = [
  {
    id: 'elara-guid', name: 'Elara Vane', level: 41, class: 4, race: 2, npcFaction: 2, team: 'The Wave Guild',
    location: 'Temple', gold: 900, bankGold: 12000, isDead: false, isPermaDead: false, isAgedDeath: false, isKing: false,
    isTeamLeader: true, spouseName: 'HrothgarZq', age: 33,
    currentGoals: [{ name: 'Look for Marriage Partner', isActive: true }, { name: 'Court HrothgarZq', isActive: true }],
    memories: [{ description: 'Suspect HrothgarZq of an affair', involvedCharacter: 'HrothgarZq' }],
    relationships: { 'Sir Galahad': -100, HrothgarZq: 95, 'Torvin Ash': 40, 'mord-guid': 12, acct_hroth: -99 },
  },
  {
    id: 'galahad-guid', name: 'Sir Galahad', level: 60, class: 7, race: 0, npcFaction: 0, team: "HrothgarZq's Raiders",
    location: 'Castle', gold: 50, bankGold: 0, isKing: true,
    currentGoals: [{ name: 'Look for Marriage Partner', isActive: false }],
    relationships: { 'Elara Vane': -40, 'Mord Necro': 80, 'Torvin Ash': -90 },
  },
  {
    id: 'mord-guid', name: 'Mord Necro', level: 50, class: 6, race: 0, npcFaction: 1, team: 'The Wave Guild', gold: 'lots',
    isDead: true, isPermaDead: true,
    currentGoals: [{ name: 'Look for Marriage Partner soon', isActive: true }],
    relationships: { 'Sir Galahad': 70, 'Mord Necro': 100 },
  },
  { id: 'torvin-guid', name: 'Torvin Ash', level: 20, class: 10, race: 4, npcFaction: -1, isDead: true, isAgedDeath: true,
    relationships: { 'Elara Vane': -10 } },
];

const ECONOMY = {
  kingName: 'Sir Galahad', treasury: 111, kingTaxPercent: 3, cityTaxPercent: 4, dailyIncome: 700, dailyExpenses: 650,
  cityControlTeam: 'The Wave Guild', cityControlMembers: 2, cityControlLeader: 'Elara Vane', cityControlLeaderIsPlayer: false,
  cityControlLeaderBank: 55555, cityControlPower: 5000,
};
const COURT = {
  kingName: 'Sir Galahad', kingSex: 1, totalReign: 12, coronationDate: '2026-10-19T08:00:00.0000000Z', treasury: 4200,
  kingTaxPercent: 5, cityTaxPercent: 6, designatedHeir: 'HEIR_SECRET', monarchHistory: [{ name: 'HrothgarZq' }],
};

function makeFixture(fill) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'dash-observatory-'));
  const file = path.join(dir, 'usurper_online.db');
  const w = new Database(file);
  w.exec(fixtureSchema());
  const addPlayer = w.prepare("INSERT INTO players (username, display_name, player_data) VALUES (?, ?, '{}')");
  PLAYERS.forEach((p) => addPlayer.run(p.username, p.display));
  const ws = w.prepare('INSERT OR REPLACE INTO world_state (key, value) VALUES (?, ?)');
  ws.run('npcs', JSON.stringify(NPCS));
  ws.run('economy', JSON.stringify(ECONOMY));
  ws.run('royal_court', JSON.stringify(COURT));
  ws.run('god_of_week', JSON.stringify({ week: 42, god: 'Aurelion', standing: 900 }));
  ws.run('world_boss_schedule', JSON.stringify({ definitionId: 'iron_titan', bossLevel: 77, spawnUtc: '2099-01-01T01:00:00Z',
    windowHours: 3, spawnedBossId: 0, medianLevel: 70 }));
  if (fill) fill(w);
  w.close();
  const db = new Database(file, { readonly: true, fileMustExist: true });
  return { db, close: () => { db.close(); fs.rmSync(dir, { recursive: true, force: true }); } };
}

function addBoss(w, { name, level = 60, max = 1000, cur = 400, status = 'active', expires = '+2 hours', started = '-1 hours' }) {
  return w.prepare(`INSERT INTO world_bosses (boss_name, boss_level, max_hp, current_hp, expires_at, started_at, status)
    VALUES (?, ?, ?, ?, datetime('now', ?), datetime('now', ?), ?)`).run(name, level, max, cur, expires, started, status).lastInsertRowid;
}
function addDamage(w, bossId, login, display, dmg, isNpc = 0) {
  w.prepare('INSERT INTO world_boss_damage (boss_id, player_name, damage_dealt, is_npc, display_name) VALUES (?, ?, ?, ?, ?)')
    .run(bossId, login, dmg, isNpc, display);
}
function addLog(w, name, xp, gold, ago = '-1 hours') {
  w.prepare(`INSERT INTO npc_decision_log (npc_name, npc_level, npc_class, action, xp_delta, gold_delta, created_at)
    VALUES (?, 10, 'Warrior', 'dungeon', ?, ?, datetime('now', ?))`).run(name, xp, gold, ago);
}
function addDeath(w, floor, ago = '-1 hours', outcome = 'death', login = 'acct_hroth') {
  w.prepare(`INSERT INTO combat_events (player_name, player_level, player_class, player_max_hp, player_str, player_dex,
    player_weap_pow, player_arm_pow, outcome, dungeon_floor, floor_actual, created_at)
    VALUES (?, 10, 'Warrior', 100, 10, 10, 10, 10, ?, 99, ?, datetime('now', ?))`).run(login, outcome, floor, ago);
}
function addListing(w, item, price, { status = 'active', expires = '+5 hours', listed = '-1 hours' } = {}) {
  w.prepare(`INSERT INTO auction_listings (seller, item_name, item_json, price, listed_at, expires_at, buyer, status)
    VALUES ('acct_seller_zz', ?, '{"secret":"ITEMJSON_SECRET"}', ?, datetime('now', ?), datetime('now', ?), 'acct_buyer_zz', ?)`)
    .run(item, price, listed, expires, status);
}
function addFallen(w, display, killer, ago, level = 30) {
  w.prepare(`INSERT INTO fallen_legacy (username, display_name, level, class_name, killer, heirloom_gold, died_at)
    VALUES ('acct_fallen_qq', ?, ?, 'Paladin', ?, 987654, datetime('now', ?))`).run(display, level, killer, ago);
}

const keys = (o) => Object.keys(o).sort();
function assertKeys(o, expected, what) {
  assert.ok(o && typeof o === 'object' && !Array.isArray(o), what + ' is an object');
  assert.deepStrictEqual(keys(o), [...expected].sort(), what + ' key set');
}
// No login key, no hidden field value, and (after the allowed display-name fields are taken out by
// the caller) no player display name.
function assertClean(value, what) {
  const json = JSON.stringify(value);
  for (const s of [...LOGINS, ...HIDDEN, 'acct_', 'player_name', 'username', 'seller', 'buyer', 'heirloom', 'item_json']) {
    assert.ok(!json.includes(s), `${what}: ${s} must not appear`);
  }
  for (const d of DISPLAYS) assert.ok(!json.includes(d), `${what}: display name ${d} must not appear`);
}

// ---------------------------------------------------------------- people, profile, ties

const PEOPLE_KEYS = ['name', 'level', 'class', 'race', 'npcFaction', 'team', 'location', 'isDead', 'isPermaDead',
  'isAgedDeath', 'isKing', 'wealth', 'seeking'];

test('people: the allow-list per NPC, wealth, seeking, and no player in a team name', () => {
  const people = O.buildPeople(NPCS, PLAYER_NAMES);
  assert.strictEqual(people.length, 4);
  people.forEach((p, i) => assertKeys(p, PEOPLE_KEYS, 'people[' + i + ']'));
  assertClean(people, 'people');
  const [elara, galahad, mord, torvin] = people;
  assert.strictEqual(elara.wealth, 12900);
  assert.strictEqual(elara.seeking, true);
  assert.strictEqual(galahad.seeking, false, 'an inactive goal is not seeking');
  assert.strictEqual(mord.seeking, false, 'only the exact goal name counts');
  assert.strictEqual(mord.wealth, 0, 'a non-number gold is 0');
  assert.strictEqual(elara.team, 'The Wave Guild');
  assert.strictEqual(galahad.team, '', 'a team named after a player is blanked');
  assert.strictEqual(torvin.isAgedDeath, true);
  assert.strictEqual(galahad.isKing, true);
});

test('people: no player list means no team names', () => {
  const people = O.buildPeople(NPCS, null);
  assert.ok(people.every((p) => p.team === ''));
  assert.deepStrictEqual(O.buildPeople('garbage', PLAYER_NAMES), []);
  assert.deepStrictEqual(O.buildPeople([null, 5, 'x'], PLAYER_NAMES), []);
});

test('profile: one entry of the public NPC list by name, null for an unknown name', () => {
  const list = publicNpcs(NPCS, PLAYER_NAMES);
  const ob = O.createObservatory({ getDb: () => null, getNpcs: () => NPCS, getPublicNpcs: () => list, getPlayerNames: () => PLAYER_NAMES });
  assert.strictEqual(ob.profile('Elara Vane'), list[0]);
  assert.strictEqual(ob.profile('elara vane'), null);
  assert.strictEqual(ob.profile('HrothgarZq'), null);
  assert.strictEqual(ob.profile('__proto__'), null);
  assert.strictEqual(ob.profile(undefined), null);
  assertClean(ob.profile('Elara Vane'), 'profile');
});

test('ties: the strongest likes and dislikes, NPC to NPC only, each pair once', () => {
  const ties = O.buildTies(publicNpcs(NPCS, PLAYER_NAMES), PLAYER_NAMES);
  assertKeys(ties, ['dislikes', 'likes'], 'ties');
  [...ties.dislikes, ...ties.likes].forEach((t, i) => assertKeys(t, ['from', 'to', 'value'], 'tie ' + i));
  assertClean(ties, 'ties');
  assert.ok(!JSON.stringify(ties).includes('Torvin Ash'), 'an NPC name a player shares is never in a tie');
  assert.deepStrictEqual(O.buildTies(publicNpcs(NPCS, PLAYER_NAMES), null), { dislikes: [], likes: [] }, 'no player list, no ties');
  assert.deepStrictEqual(ties.dislikes, [{ from: 'Elara Vane', to: 'Sir Galahad', value: -100 }]);
  assert.deepStrictEqual(ties.likes, [
    { from: 'Sir Galahad', to: 'Mord Necro', value: 80 },
    { from: 'Elara Vane', to: 'Mord Necro', value: 12 },
  ]);
  assert.ok(ties.likes.every((t) => t.value > 0) && ties.dislikes.every((t) => t.value < 0));
});

test('ties: at most 10 each, strongest first', () => {
  const many = [];
  for (let i = 0; i < 30; i++) many.push({ name: 'N' + i, relationships: { ['N' + ((i + 1) % 30)]: i + 1, ['N' + ((i + 2) % 30)]: -(i + 1) } });
  const ties = O.buildTies(publicNpcs(many, new Set()), new Set());
  assert.strictEqual(ties.likes.length, 10);
  assert.strictEqual(ties.dislikes.length, 10);
  assert.strictEqual(ties.likes[0].value, 30);
  assert.strictEqual(ties.dislikes[0].value, -30);
  assert.deepStrictEqual(O.buildTies(null, new Set()), { dislikes: [], likes: [] });
});

// ---------------------------------------------------------------- realm

const REALM_KEYS = ['ruler', 'treasury', 'taxes', 'daily', 'cityControl', 'god', 'boss', 'bossLeaders'];
function assertRealmShape(r) {
  assertKeys(r, REALM_KEYS, 'realm');
  assertKeys(r.ruler, ['name', 'title', 'reignDays', 'since'], 'ruler');
  assertKeys(r.taxes, ['king', 'city'], 'taxes');
  assertKeys(r.daily, ['income', 'expenses'], 'daily');
  assertKeys(r.cityControl, ['team', 'leader', 'members'], 'cityControl');
  assertKeys(r.god, ['name', 'week', 'rule'], 'god');
  assertKeys(r.boss, ['now', 'next'], 'boss');
  if (r.boss.now) assertKeys(r.boss.now, ['name', 'level', 'hpPct', 'endsAt'], 'boss.now');
  if (r.boss.next) assertKeys(r.boss.next, ['name', 'level', 'startsAt', 'endsAt'], 'boss.next');
  assert.ok(Array.isArray(r.bossLeaders));
  r.bossLeaders.forEach((l, i) => assertKeys(l, ['name', 'damage'], 'bossLeaders[' + i + ']'));
}
// The realm with the allowed display names (boss leaders) taken out.
const realmWithoutLeaders = (r) => Object.assign({}, r, { bossLeaders: r.bossLeaders.map((l) => ({ damage: l.damage })) });

test('realm: ruler, money, city control, god, the coming boss and the last boss leaders', () => {
  let lastBoss;
  const fx = makeFixture((w) => {
    const old = addBoss(w, { name: 'Void Colossus', status: 'defeated', expires: '-20 hours', started: '-30 hours' });
    addDamage(w, old, 'acct_mira', 'MirabelXq', 5);
    lastBoss = addBoss(w, { name: 'The Crimson Wyrm', status: 'withdrawn', expires: '-1 hours', started: '-4 hours' });
    addDamage(w, lastBoss, 'acct_boss_ww', 'Bosshitter Ww', 900);
    addDamage(w, lastBoss, 'acct_hroth', 'HrothgarZq', 300);
    addDamage(w, lastBoss, 'elara vane', 'Elara Vane', 5000, 1);
    addDamage(w, lastBoss, 'acct_mira', '', 4000);
  });
  try {
    const r = O.buildRealm(fx.db, NPCS, PLAYER_NAMES, Date.now());
    assertRealmShape(r);
    assertClean(realmWithoutLeaders(r), 'realm');
    assert.deepStrictEqual(r.ruler, { name: 'Sir Galahad', title: 'King', reignDays: 12, since: '2026-10-19T08:00:00.000Z' });
    assert.strictEqual(r.treasury, 4200, 'royal_court first');
    assert.deepStrictEqual(r.taxes, { king: 5, city: 6 });
    assert.deepStrictEqual(r.daily, { income: 700, expenses: 650 });
    assert.deepStrictEqual(r.cityControl, { team: 'The Wave Guild', leader: 'Elara Vane', members: 2 });
    assert.deepStrictEqual(r.god, { name: 'Aurelion', week: 42, rule: 'followers get 5% more XP' });
    assert.strictEqual(r.boss.now, null);
    assert.deepStrictEqual(r.boss.next, { name: 'The Iron Titan', level: 77, startsAt: '2099-01-01T01:00:00.000Z', endsAt: '2099-01-01T04:00:00.000Z' });
    assert.deepStrictEqual(r.bossLeaders, [{ name: 'Bosshitter Ww', damage: 900 }, { name: 'HrothgarZq', damage: 300 }],
      'the latest boss, display names only, no NPC rows and no rows without a display name');
  } finally { fx.close(); }
});

test('realm: an active boss is now, with its own leaders, top 10', () => {
  const fx = makeFixture((w) => {
    const old = addBoss(w, { name: 'Void Colossus', status: 'defeated', expires: '-20 hours', started: '-30 hours' });
    addDamage(w, old, 'acct_mira', 'MirabelXq', 99999);
    const id = addBoss(w, { name: 'Lich King Vareth', level: 88, max: 3000, cur: 1000 });
    for (let i = 0; i < 12; i++) addDamage(w, id, 'acct_z' + i, 'Hero' + String.fromCharCode(65 + i), 100 + i);
  });
  try {
    const r = O.buildRealm(fx.db, NPCS, PLAYER_NAMES, Date.now());
    assertRealmShape(r);
    assert.strictEqual(r.boss.next, null);
    assert.strictEqual(r.boss.now.name, 'Lich King Vareth');
    assert.strictEqual(r.boss.now.level, 88);
    assert.strictEqual(r.boss.now.hpPct, 33);
    assert.match(r.boss.now.endsAt, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/);
    assert.strictEqual(r.bossLeaders.length, 10);
    assert.deepStrictEqual(r.bossLeaders[0], { name: 'HeroL', damage: 111 });
    assert.ok(!JSON.stringify(r).includes('acct_z'), 'no login key');
    assert.ok(!JSON.stringify(r).includes('MirabelXq'), 'only the current boss');
  } finally { fx.close(); }
});

test('realm: the city leader is named only when it is an NPC no player shares', () => {
  const fx = makeFixture();
  try {
    const run = (econ) => {
      const npcs = NPCS;
      const db = {
        prepare(sql) {
          const st = fx.db.prepare(sql);
          return { get: (...a) => (sql.includes('world_state') && a[0] === 'economy' ? { value: JSON.stringify(Object.assign({}, ECONOMY, econ)) } : st.get(...a)), all: (...a) => st.all(...a) };
        },
      };
      return O.buildRealm(db, npcs, PLAYER_NAMES, Date.now()).cityControl;
    };
    assert.strictEqual(run({}).leader, 'Elara Vane');
    assert.strictEqual(run({ cityControlLeaderIsPlayer: true }).leader, '', 'the game marks the leader as a player');
    assert.strictEqual(run({ cityControlLeader: 'HrothgarZq' }).leader, '', 'a player display name');
    assert.strictEqual(run({ cityControlLeader: 'acct_hroth' }).leader, '', 'a login');
    assert.strictEqual(run({ cityControlLeader: 'Torvin Ash' }).leader, '', 'an NPC name a player shares');
    assert.strictEqual(run({ cityControlLeader: 'Nobody Known' }).leader, '', 'not an NPC');
    assert.strictEqual(run({ cityControlLeader: 'elara-guid' }).leader, 'Elara Vane', 'an NPC id resolves to the name');
    assert.strictEqual(run({ cityControlTeam: 'Hammers of MirabelXq' }).team, '', 'a team named after a player');
    assert.strictEqual(run({ cityControlTeam: 'Xy Crew' }).team, '', 'a short player name, whole word');
    assert.strictEqual(run({ cityControlTeam: 'Xyloph Crew' }).team, 'Xyloph Crew', 'part of a word is not a name');
  } finally { fx.close(); }
  const r = O.buildRealm(null, NPCS, null, Date.now());
  assert.strictEqual(r.cityControl.team, '');
  assert.strictEqual(r.cityControl.leader, '');
});

test('realm: a Queen, the economy fallback and a past schedule', () => {
  const fx = makeFixture((w) => {
    const ws = w.prepare('INSERT OR REPLACE INTO world_state (key, value) VALUES (?, ?)');
    ws.run('royal_court', JSON.stringify({ kingName: 'Elara Vane', kingSex: 2 }));
    ws.run('world_boss_schedule', JSON.stringify({ definitionId: 'not_a_boss', bossLevel: 5, spawnUtc: '2000-01-01T01:00:00', windowHours: 3, spawnedBossId: 0 }));
  });
  try {
    const r = O.buildRealm(fx.db, NPCS, PLAYER_NAMES, Date.now());
    assert.strictEqual(r.ruler.title, 'Queen');
    assert.strictEqual(r.treasury, 111, 'economy when royal_court has none');
    assert.deepStrictEqual(r.taxes, { king: 3, city: 4 });
    assert.strictEqual(r.boss.next, null, 'a window already over is not next');
    const later = O.buildRealm(fx.db, NPCS, PLAYER_NAMES, Date.parse('1999-12-31T00:00:00Z'));
    assert.deepStrictEqual(later.boss.next, { name: '', level: 5, startsAt: '2000-01-01T01:00:00.000Z', endsAt: '2000-01-01T04:00:00.000Z' },
      'an unknown definition has no name; a time without a zone is UTC');
  } finally { fx.close(); }
});

test('realm: malformed world_state rows give empty values, never a throw', () => {
  for (const bad of ['{', '[]', 'null', '42', '"text"', '{"kingName":5,"treasury":"x","kingSex":"2","totalReign":null}', '']) {
    const fx = makeFixture((w) => {
      const ws = w.prepare('INSERT OR REPLACE INTO world_state (key, value) VALUES (?, ?)');
      for (const k of ['royal_court', 'economy', 'god_of_week', 'world_boss_schedule']) ws.run(k, bad);
    });
    try {
      const r = O.buildRealm(fx.db, NPCS, PLAYER_NAMES, Date.now());
      assertRealmShape(r);
      assert.deepStrictEqual(r.ruler, { name: '', title: '', reignDays: 0, since: '' }, bad);
      assert.strictEqual(r.treasury, 0, bad);
      assert.deepStrictEqual(r.taxes, { king: 0, city: 0 }, bad);
      assert.deepStrictEqual(r.daily, { income: 0, expenses: 0 }, bad);
      assert.deepStrictEqual(r.cityControl, { team: '', leader: '', members: 0 }, bad);
      assert.deepStrictEqual(r.god, { name: '', week: 0, rule: '' }, bad);
      assert.deepStrictEqual(r.boss, { now: null, next: null }, bad);
      assert.deepStrictEqual(r.bossLeaders, [], bad);
    } finally { fx.close(); }
  }
  // No database, and a database without the tables.
  assertRealmShape(O.buildRealm(null, null, null, Date.now()));
  const empty = new Database(':memory:');
  try { assertRealmShape(O.buildRealm(empty, NPCS, PLAYER_NAMES, Date.now())); } finally { empty.close(); }
});

// ---------------------------------------------------------------- movers, depths, market, fallen

test('movers: logged gains of the last 7 days, NPC names in the public list only, top 10', () => {
  const fx = makeFixture((w) => {
    addLog(w, 'Elara Vane', 500, 20);
    addLog(w, 'Elara Vane', 250, -100);
    addLog(w, 'Sir Galahad', 100, 900);
    addLog(w, 'Mord Necro', -50, 0);
    addLog(w, 'Sir Galahad', 99999, 99999, '-8 days');
    addLog(w, 'HrothgarZq', 99999, 99999);
    addLog(w, 'acct_hroth', 99999, 99999);
    for (let i = 0; i < 12; i++) addLog(w, 'N' + i, 10 + i, 10 + i);
  });
  try {
    const names = new Set(['Elara Vane', 'Sir Galahad', 'Mord Necro', ...Array.from({ length: 12 }, (_, i) => 'N' + i)]);
    const m = O.buildMovers(fx.db, names);
    assertKeys(m, ['days', 'xp', 'gold'], 'movers');
    m.xp.forEach((x) => assertKeys(x, ['name', 'xp'], 'mover xp'));
    m.gold.forEach((x) => assertKeys(x, ['name', 'gold'], 'mover gold'));
    assertClean(m, 'movers');
    assert.strictEqual(m.days, 7);
    assert.deepStrictEqual(m.xp.slice(0, 2), [{ name: 'Elara Vane', xp: 750 }, { name: 'Sir Galahad', xp: 100 }]);
    assert.deepStrictEqual(m.gold[0], { name: 'Sir Galahad', gold: 900 });
    assert.strictEqual(m.xp.length, 10);
    assert.ok(m.gold.every((g) => g.name !== 'Elara Vane'), 'a net loss is not a gain');
    assert.ok(m.xp.every((g) => g.name !== 'Mord Necro'));
    assert.deepStrictEqual(O.buildMovers(fx.db, null), { days: 7, xp: [], gold: [] });
  } finally { fx.close(); }
});

test('depths: deaths of the last 7 days in 10-floor bands, counts only', () => {
  const fx = makeFixture((w) => {
    addDeath(w, 1); addDeath(w, 10); addDeath(w, 11); addDeath(w, 99); addDeath(w, 100); addDeath(w, 101);
    addDeath(w, null); addDeath(w, 0);
    addDeath(w, 5, '-8 days');
    addDeath(w, 5, '-1 hours', 'victory');
  });
  try {
    const d = O.buildDepths(fx.db);
    assertKeys(d, ['days', 'bands', 'unknown'], 'depths');
    d.bands.forEach((b) => assertKeys(b, ['band', 'deaths'], 'band'));
    assertClean(d, 'depths');
    assert.strictEqual(d.bands.length, 11);
    assert.deepStrictEqual(d.bands[0], { band: '1-10', deaths: 2 });
    assert.deepStrictEqual(d.bands[1], { band: '11-20', deaths: 1 });
    assert.deepStrictEqual(d.bands[9], { band: '91-100', deaths: 2 });
    assert.deepStrictEqual(d.bands[10], { band: '101-110', deaths: 1 });
    assert.strictEqual(d.unknown, 2);
  } finally { fx.close(); }
  assert.strictEqual(O.buildDepths(null).bands.length, 10);
});

test('market: active listings newest first, at most 50, never seller, buyer or item_json', () => {
  const fx = makeFixture((w) => {
    addListing(w, 'Old Sword', 10, { listed: '-3 hours' });
    addListing(w, 'New Shield', 20, { listed: '-1 hours' });
    addListing(w, 'Sold Helm', 30, { status: 'sold' });
    addListing(w, 'Expired Ring', 40, { expires: '-1 hours' });
    for (let i = 0; i < 55; i++) addListing(w, 'Potion ' + i, i, { listed: '-10 hours' });
  });
  try {
    const m = O.buildMarket(fx.db);
    assertKeys(m, ['count', 'listings'], 'market');
    m.listings.forEach((l) => assertKeys(l, ['item', 'price', 'expiresAt'], 'listing'));
    assertClean(m, 'market');
    assert.strictEqual(m.count, 57);
    assert.strictEqual(m.listings.length, 50);
    assert.strictEqual(m.listings[0].item, 'New Shield');
    assert.strictEqual(m.listings[1].item, 'Old Sword');
    assert.ok(!JSON.stringify(m).includes('Sold Helm') && !JSON.stringify(m).includes('Expired Ring'));
  } finally { fx.close(); }
  assert.deepStrictEqual(O.buildMarket(null), { count: 0, listings: [] });
});

test('fallen: the 20 latest, the killer only when it is not a player', () => {
  const fx = makeFixture((w) => {
    addFallen(w, 'Fallenhero Qq', 'Cave Troll', '-1 hours', 31);
    addFallen(w, 'HrothgarZq', 'MirabelXq', '-2 hours');
    addFallen(w, 'MirabelXq', 'acct_hroth', '-3 hours');
    addFallen(w, 'Fallenhero Qq', 'slain by Xy in the arena', '-4 hours');
    addFallen(w, 'Fallenhero Qq', 'Torvin Ash', '-5 hours');
    addFallen(w, 'Fallenhero Qq', 'Sir Galahad', '-6 hours');
    addFallen(w, 'Fallenhero Qq', 'Xylophage', '-7 hours');
    for (let i = 0; i < 20; i++) addFallen(w, 'Old Hero', 'Rat', '-' + (10 + i) + ' days');
  });
  try {
    const f = O.buildFallen(fx.db, NPCS, PLAYER_NAMES);
    assert.strictEqual(f.length, 20);
    f.forEach((x) => assertKeys(x, ['name', 'level', 'className', 'killer', 'diedAt'], 'fallen'));
    assertClean(f.map((x) => Object.assign({}, x, { name: '' })), 'fallen');
    assert.deepStrictEqual(f[0], { name: 'Fallenhero Qq', level: 31, className: 'Paladin', killer: 'Cave Troll', diedAt: f[0].diedAt });
    assert.match(f[0].diedAt, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/);
    assert.deepStrictEqual(f.slice(1, 7).map((x) => x.killer), ['', '', '', '', 'Sir Galahad', 'Xylophage'],
      'PvP by display name, by login, a short name in a phrase, and an NPC name a player shares are blanked');
    assert.strictEqual(f[1].name, 'HrothgarZq', 'the fallen hero\'s own display name is the one name sent');
    const closed = O.buildFallen(fx.db, NPCS, null);
    assert.ok(closed.every((x) => x.killer === ''), 'no player list means no killers');
  } finally { fx.close(); }
  assert.deepStrictEqual(O.buildFallen(null, NPCS, PLAYER_NAMES), []);
});

test('old database without the tables: every endpoint is empty, none throws', () => {
  const empty = new Database(':memory:');
  try {
    assert.deepStrictEqual(O.buildMovers(empty, new Set(['Elara Vane'])), { days: 7, xp: [], gold: [] });
    assert.strictEqual(O.buildDepths(empty).unknown, 0);
    assert.deepStrictEqual(O.buildMarket(empty), { count: 0, listings: [] });
    assert.deepStrictEqual(O.buildFallen(empty, NPCS, PLAYER_NAMES), []);
  } finally { empty.close(); }
});

test('the queries use the indexes the plan names', () => {
  const fx = makeFixture();
  try {
    const uses = (sql, idx) => {
      const plan = planOf(fx.db, sql, []).join(' | ');
      assert.ok(plan.includes('USING INDEX ' + idx) || plan.includes('USING COVERING INDEX ' + idx), `${idx} in: ${plan}`);
      assert.ok(!/SCAN \w+( \||$)/.test(plan), `no full scan: ${plan}`);
      return plan;
    };
    uses(O.MOVERS_SQL, 'idx_npc_decision_log_created');
    uses(O.MARKET_SQL, 'idx_auction_status');
    uses(O.MARKET_COUNT_SQL, 'idx_auction_status');
    assert.ok(!uses(O.FALLEN_SQL, 'idx_fallen_legacy_died').includes('TEMP B-TREE'), 'the index gives the order, 20 rows read');
    uses(O.DEPTHS_SQL, 'idx_ce_outcome');
  } finally { fx.close(); }
});

// ---------------------------------------------------------------- the cache

test('each endpoint returns the same object within its period and rebuilds after it', () => {
  const fx = makeFixture();
  try {
    let t = 1000000;
    let reads = 0;
    const list = publicNpcs(NPCS, PLAYER_NAMES);
    const ob = O.createObservatory({
      getDb: () => fx.db, getNpcs: () => { reads++; return NPCS; }, getPublicNpcs: () => list,
      getPlayerNames: () => PLAYER_NAMES, now: () => t,
    });
    assert.deepStrictEqual({ ...O.TTL }, { people: 10000, profile: 10000, ties: 10000, realm: 60000,
      movers: 600000, depths: 600000, market: 60000, fallen: 60000 });
    for (const name of ['people', 'ties', 'realm', 'movers', 'depths', 'market', 'fallen']) {
      const start = t;
      const a = ob[name]();
      t = start + O.TTL[name] - 1;
      assert.strictEqual(ob[name](), a, name + ' within its period');
      t = start + O.TTL[name];
      assert.notStrictEqual(ob[name](), a, name + ' after its period');
      t += 1;
    }
    assert.ok(reads > 0);
  } finally { fx.close(); }
});

test('the boss name map equals the Id and Name pairs in WorldBossData.cs', () => {
  const src = read('Scripts', 'Data', 'WorldBossData.cs');
  const pairs = {};
  for (const m of src.matchAll(/^\s*Id = "([^"]+)",\s*\n\s*Name = "([^"]+)",/gm)) pairs[m[1]] = m[2];
  assert.ok(Object.keys(pairs).length >= 8, 'pairs found');
  assert.deepStrictEqual({ ...O.BOSS_NAMES }, pairs);
  assert.match(read('Scripts', 'Core', 'GameConfig.cs'), /GodWeeklyXpBonusPct = 5;/, 'the god rule text says 5%');
});

// ---------------------------------------------------------------- the routes in ssh-proxy.js

// Runs the proxy's player-name getter, public NPC cache, Observatory wiring and dash router in a vm
// against the fixture database.
function loadRoutes(db) {
  const src = proxySource();
  const code = [
    slice(src, 'function sendJson(', '// --- Bug Report Proxy ---'),
    slice(src, '// Lower-cased player character names and login names', '// Read royal court data from world_state'),
    slice(src, 'async function handleDashRequest', '// --- Admin Dashboard API ---'),
    ';({ handleDashRequest, getDashPlayerNames });',
  ].join('\n');
  const npcs = JSON.parse(db.prepare("SELECT value FROM world_state WHERE key = 'npcs'").get().value);
  const ctx = {
    db, publicNpcs, createObservatory: O.createObservatory, URL, JSON, Object, Date, Set, String,
    DASH_NPC_CACHE_TTL: 10000, getDashNpcs: () => npcs, dashSseClients: new Set(),
    getDashSummary: () => ({}), getDashEvents: () => [], getDashEventsHourly: () => [],
    console: { log() {}, warn() {}, info() {}, error() {} },
  };
  vm.createContext(ctx);
  const api = vm.runInContext(code, ctx, { filename: 'ssh-proxy.js (dash section)' });
  async function call(url, method = 'GET') {
    const out = { status: 0, headers: {}, body: '' };
    const res = {
      setHeader(k, v) { out.headers[k.toLowerCase()] = v; }, writeHead(s) { out.status = s; },
      end(b) { out.body = b === undefined ? '' : b; }, headersSent: false,
    };
    const handled = await api.handleDashRequest({ url, method, on() {} }, res);
    return { handled, status: out.status, type: out.headers['content-type'], raw: out.body, body: out.body ? JSON.parse(out.body) : null };
  }
  return { api, call };
}

test('routes: every Observatory endpoint is a public GET with JSON from its allow-list', async () => {
  const fx = makeFixture((w) => {
    const id = addBoss(w, { name: 'Lich King Vareth' });
    addDamage(w, id, 'acct_boss_ww', 'Bosshitter Ww', 70);
    addLog(w, 'Elara Vane', 5, 5);
    addDeath(w, 30);
    addListing(w, 'Fine Blade', 99);
    addFallen(w, 'Fallenhero Qq', 'HrothgarZq', '-1 hours');
  });
  try {
    const { api, call } = loadRoutes(fx.db);
    const names = api.getDashPlayerNames();
    for (const p of PLAYERS) assert.ok(names.has(p.username) && names.has(p.display.toLowerCase()), 'player names read from the db');
    const all = {};
    for (const route of ['people', 'ties', 'realm', 'movers', 'depths', 'market', 'fallen']) {
      const r = await call('/api/dash/' + route);
      assert.strictEqual(r.status, 200, route);
      assert.strictEqual(r.type, 'application/json', route);
      all[route] = r.body;
    }
    assert.strictEqual(all.people.length, 4);
    all.people.forEach((p) => assertKeys(p, PEOPLE_KEYS, 'route people'));
    assertRealmShape(all.realm);
    assert.deepStrictEqual(all.realm.bossLeaders, [{ name: 'Bosshitter Ww', damage: 70 }]);
    assert.strictEqual(all.fallen[0].killer, '', 'a PvP killer is blanked through the route');
    const clean = Object.assign({}, all, {
      realm: realmWithoutLeaders(all.realm),
      fallen: all.fallen.map((x) => Object.assign({}, x, { name: '' })),
    });
    assertClean(clean, 'routes');
    const q = await call('/api/dash/people?x=1');
    assert.strictEqual(q.status, 200, 'a query string does not change the route');
    const post = await call('/api/dash/people', 'POST');
    assert.strictEqual(post.handled, false, 'GET only');
  } finally { fx.close(); }
});

test('routes: profile by name, 404 JSON for an unknown or malformed name', async () => {
  const fx = makeFixture();
  try {
    const { call } = loadRoutes(fx.db);
    const ok = await call('/api/dash/profile?name=Elara%20Vane');
    assert.strictEqual(ok.status, 200);
    assert.strictEqual(ok.body.name, 'Elara Vane');
    assert.deepStrictEqual(ok.body, publicNpcs(NPCS, PLAYER_NAMES)[0]);
    assertClean(ok.body, 'profile route');
    const plus = await call('/api/dash/profile?name=Elara+Vane');
    assert.strictEqual(plus.status, 200);
    for (const url of ['/api/dash/profile?name=HrothgarZq', '/api/dash/profile?name=acct_hroth', '/api/dash/profile',
      '/api/dash/profile?name=', '/api/dash/profile?name=%E0%A4%A', '/api/dash/profile?name=%', '/api/dash/profile?name=__proto__',
      '/api/dash/profile?name=' + 'a'.repeat(500)]) {
      const r = await call(url);
      assert.strictEqual(r.status, 404, url);
      assert.strictEqual(r.type, 'application/json', url);
      assert.deepStrictEqual(r.body, { error: 'Not found' }, url);
    }
  } finally { fx.close(); }
});

test('routes: the player names are read once per NPC cache period and fail closed', () => {
  const fx = makeFixture();
  try {
    let prepares = 0;
    const counting = { prepare: (sql) => { if (sql.includes('FROM players')) prepares++; return fx.db.prepare(sql); } };
    const { api } = loadRoutes(counting);
    const a = api.getDashPlayerNames();
    assert.strictEqual(api.getDashPlayerNames(), a);
    assert.strictEqual(prepares, 1);
  } finally { fx.close(); }
  const broken = { prepare: (sql) => { if (sql.includes('FROM players')) throw new Error('locked'); return { get: () => ({ value: JSON.stringify(NPCS) }) }; } };
  const { api } = loadRoutes(broken);
  assert.strictEqual(api.getDashPlayerNames(), null);
});
