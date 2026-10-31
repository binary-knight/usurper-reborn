'use strict';
// The public data behind the Observatory page: /api/dash/people, profile, ties, realm, movers,
// depths, market and fallen. Each builder reads through the read-only db handle it is given and
// returns an allow-list: the fields the page draws and nothing else. Login keys (usernames, sellers,
// buyers, world_boss_damage.player_name) are never selected. A name of another character is sent
// only when it is an NPC that no player shares (the guards in dash-npcs.js); player display names
// appear only as the realm's boss leaders and the fallen heroes' names. A missing table, a missing
// column or an unreadable world_state row gives empty values, never a throw.
const { buildGuards, str, num, bool, arr, obj } = require('./dash-npcs');

// WorldBossData.cs definition id to boss name (pinned by a test against the C#).
const BOSS_NAMES = Object.freeze({
  abyssal_leviathan: 'The Abyssal Leviathan',
  void_colossus: 'Void Colossus',
  shadowlord_malachar: 'Shadowlord Malachar',
  crimson_wyrm: 'The Crimson Wyrm',
  lich_king_vareth: 'Lich King Vareth',
  iron_titan: 'The Iron Titan',
  dread_serpent_nidhogg: 'Dread Serpent Nidhogg',
  nameless_horror: 'The Nameless Horror',
});

// GameConfig.GodWeeklyXpBonusPct is 5.
const GOD_RULE = 'followers get 5% more XP';
const MARRIAGE_GOAL = 'Look for Marriage Partner';
const DAYS = 7;

// Cache periods in milliseconds.
const TTL = Object.freeze({
  people: 10000, profile: 10000, ties: 10000, realm: 60000,
  movers: 600000, depths: 600000, market: 60000, fallen: 60000,
});

const noop = () => {};
const finite = (...vals) => { for (const v of vals) if (typeof v === 'number' && isFinite(v)) return v; return 0; };

// An SQLite datetime('now') text (UTC) as ISO 8601, or '' when it is not one.
function sqlToIso(v) {
  const m = /^(\d{4}-\d{2}-\d{2})[ T](\d{2}:\d{2}:\d{2})$/.exec(str(v));
  return m ? `${m[1]}T${m[2]}Z` : '';
}

// A C# DateTime from System.Text.Json (UTC, with or without the Z) as milliseconds, or NaN.
function parseUtc(v) {
  const s = str(v);
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(s)) return NaN;
  const zoned = /(Z|[+-]\d{2}:\d{2})$/.test(s) ? s : s + 'Z';
  return Date.parse(zoned);
}

const isoOf = (ms) => (isFinite(ms) ? new Date(ms).toISOString() : '');

// A world_state value as an object, {} when missing, unreadable or not an object.
function readWorldState(db, key, log) {
  try {
    const row = db.prepare('SELECT value FROM world_state WHERE key = ?').get(key);
    if (!row || typeof row.value !== 'string') return {};
    return obj(JSON.parse(row.value));
  } catch (err) {
    log(`world_state ${key}: ${err.message}`);
    return {};
  }
}

function query(db, log, what, fn, fallback) {
  try { return fn(); } catch (err) { log(`${what}: ${err.message}`); return fallback; }
}

// ---------------------------------------------------------------- people, profile, ties

function buildPeople(npcs, playerNames) {
  const { list, guards: g } = buildGuards(npcs, playerNames);
  return list.map((n) => ({
    name: str(n.name),
    level: num(n.level),
    class: num(n.class),
    race: num(n.race),
    npcFaction: typeof n.npcFaction === 'number' ? n.npcFaction : -1,
    // Players name teams, and recruited NPCs carry the name.
    team: g.mentionsPlayer(n.team) ? '' : str(n.team),
    location: str(n.location),
    isDead: bool(n.isDead),
    isPermaDead: bool(n.isPermaDead),
    isAgedDeath: bool(n.isAgedDeath),
    isKing: bool(n.isKing),
    wealth: num(n.gold) + num(n.bankGold),
    seeking: arr(n.currentGoals).some((x) => x && typeof x === 'object' && x.isActive === true && x.name === MARRIAGE_GOAL),
  }));
}

// The public list (dash-npcs publicNpcs) by name; the first entry of a name wins.
function profileMap(publicList) {
  const map = new Map();
  arr(publicList).forEach((n) => { if (n && typeof n.name === 'string' && n.name && !map.has(n.name)) map.set(n.name, n); });
  return map;
}

// The 10 strongest dislikes and likes among NPCs. publicNpcs has already kept only NPC to NPC ties
// with no player name as the target; an NPC whose name a player shares is left out as the source
// too, and with no player list there are no ties. A pair is listed once in each list, in the
// direction that holds the extreme.
function buildTies(publicList, playerNames) {
  const likes = new Map();
  const dislikes = new Map();
  if (!playerNames) return { dislikes: [], likes: [] };
  arr(publicList).forEach((n) => {
    if (!n || typeof n.name !== 'string' || !n.name || playerNames.has(n.name.toLowerCase())) return;
    Object.entries(obj(n.relationships)).forEach(([to, value]) => {
      if (to === n.name || typeof value !== 'number' || !isFinite(value) || value === 0) return;
      const pair = [n.name, to].sort().join('\u0000');
      const target = value > 0 ? likes : dislikes;
      const prev = target.get(pair);
      if (!prev || Math.abs(value) > Math.abs(prev.value)) target.set(pair, { from: n.name, to, value });
    });
  });
  const order = (a, b) => Math.abs(b.value) - Math.abs(a.value) || a.from.localeCompare(b.from) || a.to.localeCompare(b.to);
  return {
    dislikes: [...dislikes.values()].sort(order).slice(0, 10),
    likes: [...likes.values()].sort(order).slice(0, 10),
  };
}

// ---------------------------------------------------------------- realm

function buildRealm(db, npcs, playerNames, nowMs, log = noop) {
  const { guards: g } = buildGuards(npcs, playerNames);
  const court = db ? readWorldState(db, 'royal_court', log) : {};
  const econ = db ? readWorldState(db, 'economy', log) : {};
  const week = db ? readWorldState(db, 'god_of_week', log) : {};
  const sched = db ? readWorldState(db, 'world_boss_schedule', log) : {};

  let rulerName = str(court.kingName) || str(econ.kingName);
  if (rulerName === 'None') rulerName = '';
  const ruler = {
    name: rulerName,
    // King.cs: Female (2) is Queen; any other value is King.
    title: rulerName ? (court.kingSex === 2 ? 'Queen' : 'King') : '',
    reignDays: num(court.totalReign),
    since: isoOf(parseUtc(court.coronationDate)),
  };

  // The leader is named only when the game marks it as not a player and it is an NPC no player shares.
  const leaderRaw = econ.cityControlLeaderIsPlayer ? '' : str(econ.cityControlLeader);
  const cityControl = {
    team: g.mentionsPlayer(econ.cityControlTeam) ? '' : str(econ.cityControlTeam),
    leader: g.isSafeNpc(leaderRaw) ? g.resolve(leaderRaw) : '',
    members: num(econ.cityControlMembers),
  };

  const godName = str(week.god);
  const god = { name: godName, week: num(week.week), rule: godName ? GOD_RULE : '' };

  const active = db ? query(db, log, 'world_bosses active', () => db.prepare(
    `SELECT id, boss_name, boss_level, max_hp, current_hp, expires_at FROM world_bosses
     WHERE status = 'active' AND expires_at > datetime('now') ORDER BY started_at DESC, id DESC LIMIT 1`).get(), null) : null;

  let now = null;
  let next = null;
  if (active) {
    const max = num(active.max_hp);
    now = {
      name: str(active.boss_name),
      level: num(active.boss_level),
      hpPct: max > 0 ? Math.max(0, Math.min(100, Math.round((num(active.current_hp) * 100) / max))) : 0,
      endsAt: sqlToIso(active.expires_at),
    };
  } else if (num(sched.spawnedBossId) === 0 && typeof sched.definitionId === 'string' && sched.definitionId) {
    const start = parseUtc(sched.spawnUtc);
    const end = start + Math.max(0, num(sched.windowHours)) * 3600000;
    if (isFinite(start) && end > nowMs) {
      next = {
        name: Object.prototype.hasOwnProperty.call(BOSS_NAMES, sched.definitionId) ? BOSS_NAMES[sched.definitionId] : '',
        level: num(sched.bossLevel),
        startsAt: isoOf(start),
        endsAt: isoOf(end),
      };
    }
  }

  // The current boss, else the latest one. Display names only; NPC rows and rows without one are left out.
  let bossLeaders = [];
  if (db) {
    const bossId = active ? active.id : query(db, log, 'world_bosses latest', () => {
      const r = db.prepare('SELECT id FROM world_bosses ORDER BY started_at DESC, id DESC LIMIT 1').get();
      return r ? r.id : null;
    }, null);
    if (bossId !== null && bossId !== undefined) {
      bossLeaders = query(db, log, 'world_boss_damage', () => db.prepare(
        `SELECT display_name, damage_dealt FROM world_boss_damage
         WHERE boss_id = ? AND COALESCE(is_npc, 0) = 0 AND COALESCE(display_name, '') != ''
         ORDER BY damage_dealt DESC LIMIT 10`).all(bossId), [])
        .map((r) => ({ name: str(r.display_name), damage: num(r.damage_dealt) }));
    }
  }

  return {
    ruler,
    treasury: finite(court.treasury, econ.treasury),
    taxes: { king: finite(court.kingTaxPercent, econ.kingTaxPercent), city: finite(court.cityTaxPercent, econ.cityTaxPercent) },
    daily: { income: num(econ.dailyIncome), expenses: num(econ.dailyExpenses) },
    cityControl,
    god,
    boss: { now, next },
    bossLeaders,
  };
}

// ---------------------------------------------------------------- movers, depths, market, fallen

// The unary plus keeps SQLite from walking a whole name or floor index to save the GROUP BY sort;
// the time range index reads only the last 7 days.
const MOVERS_SQL = `SELECT npc_name, SUM(xp_delta) AS xp, SUM(gold_delta) AS gold FROM npc_decision_log
  WHERE created_at >= datetime('now', '-${DAYS} days') GROUP BY +npc_name`;

// Logged gains over the last 7 days, kept only for names in the public NPC list.
function buildMovers(db, publicNames, log = noop) {
  const names = publicNames instanceof Set ? publicNames : new Set();
  const rows = db ? query(db, log, 'npc_decision_log', () => db.prepare(MOVERS_SQL).all(), []) : [];
  const kept = rows.filter((r) => typeof r.npc_name === 'string' && names.has(r.npc_name));
  const top = (field) => kept
    .filter((r) => num(r[field]) > 0)
    .sort((a, b) => num(b[field]) - num(a[field]) || a.npc_name.localeCompare(b.npc_name))
    .slice(0, 10)
    .map((r) => ({ name: r.npc_name, [field]: num(r[field]) }));
  return { days: DAYS, xp: top('xp'), gold: top('gold') };
}

const DEPTHS_SQL = `SELECT floor_actual AS floor, COUNT(*) AS n FROM combat_events
  WHERE outcome = 'death' AND created_at >= datetime('now', '-${DAYS} days') GROUP BY +floor_actual`;

// Deaths in 10-floor bands over the last 7 days; a death with no floor counts as unknown.
function buildDepths(db, log = noop) {
  const rows = db ? query(db, log, 'combat_events', () => db.prepare(DEPTHS_SQL).all(), []) : [];
  const counts = new Map();
  let unknown = 0;
  let top = 10;
  rows.forEach((r) => {
    const f = r.floor;
    const n = num(r.n);
    if (typeof f !== 'number' || !Number.isInteger(f) || f < 1) { unknown += n; return; }
    const band = Math.floor((f - 1) / 10);
    counts.set(band, (counts.get(band) || 0) + n);
    top = Math.max(top, band + 1);
  });
  const bands = [];
  for (let b = 0; b < top; b++) bands.push({ band: `${b * 10 + 1}-${b * 10 + 10}`, deaths: counts.get(b) || 0 });
  return { days: DAYS, bands, unknown };
}

const MARKET_WHERE = "status = 'active' AND expires_at > datetime('now')";
const MARKET_SQL = `SELECT item_name, price, expires_at FROM auction_listings WHERE ${MARKET_WHERE}
  ORDER BY listed_at DESC, id DESC LIMIT 50`;
const MARKET_COUNT_SQL = `SELECT COUNT(*) AS n FROM auction_listings WHERE ${MARKET_WHERE}`;

// Active listings, newest first. Never the seller, buyer or item_json.
function buildMarket(db, log = noop) {
  if (!db) return { count: 0, listings: [] };
  const listings = query(db, log, 'auction_listings', () => db.prepare(MARKET_SQL).all(), [])
    .map((r) => ({ item: str(r.item_name), price: num(r.price), expiresAt: sqlToIso(r.expires_at) }));
  const count = query(db, log, 'auction_listings count', () => num((db.prepare(MARKET_COUNT_SQL).get() || {}).n), 0);
  return { count, listings };
}

const FALLEN_SQL = 'SELECT display_name, level, class_name, killer, died_at FROM fallen_legacy ORDER BY died_at DESC LIMIT 20';

// The 20 latest fallen heroes. Never the username or heirloom gold; the killer only when no player
// name or login appears in it (a PvP death).
function buildFallen(db, npcs, playerNames, log = noop) {
  if (!db) return [];
  const { guards: g } = buildGuards(npcs, playerNames);
  return query(db, log, 'fallen_legacy', () => db.prepare(FALLEN_SQL).all(), [])
    .map((r) => ({
      name: str(r.display_name),
      level: num(r.level),
      className: str(r.class_name),
      killer: g.mentionsPlayer(r.killer) ? '' : str(r.killer),
      diedAt: sqlToIso(r.died_at),
    }));
}

// ---------------------------------------------------------------- the cached endpoints

// getDb: the read-only handle (or null). getNpcs: the raw NPC blob (10 s cache). getPublicNpcs: the
// dash-npcs public list (10 s cache). getPlayerNames: lower-cased player names and logins, or null
// when they could not be read. now: the clock in milliseconds.
function createObservatory({ getDb, getNpcs, getPublicNpcs, getPlayerNames, now = Date.now, log = noop }) {
  const cache = new Map();
  const cached = (key, build) => {
    const t = now();
    const hit = cache.get(key);
    if (hit && t - hit.at < TTL[key]) return hit.value;
    const value = build(t);
    cache.set(key, { at: t, value });
    return value;
  };
  const publicNames = () => new Set(arr(getPublicNpcs()).map((n) => n && n.name).filter((x) => typeof x === 'string' && x));
  return {
    people: () => cached('people', () => buildPeople(getNpcs(), getPlayerNames())),
    profile: (name) => {
      const map = cached('profile', () => profileMap(getPublicNpcs()));
      return typeof name === 'string' && map.has(name) ? map.get(name) : null;
    },
    ties: () => cached('ties', () => buildTies(getPublicNpcs(), getPlayerNames())),
    realm: () => cached('realm', (t) => buildRealm(getDb(), getNpcs(), getPlayerNames(), t, log)),
    movers: () => cached('movers', () => buildMovers(getDb(), publicNames(), log)),
    depths: () => cached('depths', () => buildDepths(getDb(), log)),
    market: () => cached('market', () => buildMarket(getDb(), log)),
    fallen: () => cached('fallen', () => buildFallen(getDb(), getNpcs(), getPlayerNames(), log)),
  };
}

module.exports = {
  BOSS_NAMES, TTL, GOD_RULE, MARRIAGE_GOAL,
  buildPeople, profileMap, buildTies, buildRealm, buildMovers, buildDepths, buildMarket, buildFallen,
  createObservatory, sqlToIso, parseUtc,
  MOVERS_SQL, DEPTHS_SQL, MARKET_SQL, MARKET_COUNT_SQL, FALLEN_SQL,
};
