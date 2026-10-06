'use strict';
// Runs the /api/balance routes of web/ssh-proxy.js without starting the server: the
// balance section, sendJson and the token functions are cut from the file by their
// markers and run in a vm context against a better-sqlite3 database opened read-only,
// as the server opens it. The schema is read from the game's C# (SqlSaveBackend), so
// the tests follow the real table, the 1.2.6 columns and the real indexes.
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const nodeCrypto = require('node:crypto');
const Database = require('better-sqlite3');

const WEB = path.join(__dirname, '..');
const ROOT = path.join(WEB, '..');

function slice(text, startMarker, endMarker) {
  const a = text.indexOf(startMarker);
  if (a === -1) throw new Error('marker not found: ' + startMarker);
  const b = text.indexOf(endMarker, a);
  if (b === -1) throw new Error('end marker not found: ' + endMarker);
  return text.slice(a, b);
}

// ---------------------------------------------------------------- schema from the C#

function schemaSql() {
  const backend = fs.readFileSync(path.join(ROOT, 'Scripts', 'Systems', 'SqlSaveBackend.cs'), 'utf8');
  const combat = fs.readFileSync(path.join(ROOT, 'Scripts', 'Systems', 'SqlSaveBackend.CombatEvents.cs'), 'utf8');
  const table = (name) => {
    const m = backend.match(new RegExp('CREATE TABLE IF NOT EXISTS ' + name + ' \\([\\s\\S]*?\\n\\s*\\);'));
    if (!m) throw new Error('table not found in SqlSaveBackend.cs: ' + name);
    return m[0];
  };
  const indexes = (src, tbl) => src.match(new RegExp('CREATE INDEX IF NOT EXISTS \\w+ ON ' + tbl + '\\([^;]*\\);', 'g')) || [];
  const tally = [...combat.matchAll(/\("(\w+)", t => t\.\w+\)/g)].map((m) => m[1]);
  if (tally.length !== 18) throw new Error('expected the 18 tally columns, found ' + tally.length);
  const sql = [
    table('combat_events'),
    ...indexes(backend, 'combat_events'),
    ...tally.map((c) => `ALTER TABLE combat_events ADD COLUMN ${c} INTEGER;`),
    ...indexes(combat, 'combat_events'),
    table('npc_decision_log'),
    ...indexes(backend, 'npc_decision_log'),
    table('onboarding_events'),
    ...indexes(backend, 'onboarding_events'),
  ];
  return { sql: sql.join('\n'), tally, indexNames: sql.join('\n').match(/idx_ce_\w+/g) };
}

// Creates a database file, fills it with fill(db), and returns it opened read-only.
function makeDb(fill, options = {}) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'balance-api-'));
  const file = path.join(dir, 'usurper_online.db');
  const w = new Database(file);
  let sql = schemaSql().sql;
  for (const name of options.dropIndexes || []) sql = sql.replace(new RegExp('CREATE INDEX IF NOT EXISTS ' + name + ' [^;]*;'), '');
  w.exec(sql);
  if (fill) fill(w);
  w.close();
  const db = new Database(file, { readonly: true, fileMustExist: true });
  return { db, file, close: () => { db.close(); fs.rmSync(dir, { recursive: true, force: true }); } };
}

const COMBAT_COLS = ['player_name', 'player_level', 'player_class', 'player_max_hp', 'player_str', 'player_dex',
  'player_weap_pow', 'player_arm_pow', 'monster_name', 'monster_level', 'monster_max_hp', 'monster_str', 'monster_def',
  'is_boss', 'outcome', 'rounds', 'damage_dealt', 'damage_taken', 'xp_gained', 'gold_gained', 'dungeon_floor',
  'monster_count', 'has_teammates'];

// Inserts one combat row. ago is an SQLite modifier ('-3 hours'); tally (or null for an old
// row) holds the 1.2.6 columns by name; anything not given takes a plain default.
function insertFight(w, row) {
  const base = {
    player_name: 'Tester', player_level: 10, player_class: 'Warrior', player_max_hp: 200, player_str: 20,
    player_dex: 20, player_weap_pow: 10, player_arm_pow: 10, monster_name: 'Rat', monster_level: 5,
    monster_max_hp: 50, monster_str: 5, monster_def: 5, is_boss: 0, outcome: 'victory', rounds: 3,
    damage_dealt: 60, damage_taken: 10, xp_gained: 100, gold_gained: 20, dungeon_floor: 5, monster_count: 1,
    has_teammates: 0,
  };
  const r = Object.assign(base, row);
  const tally = r.tally || {};
  const cols = COMBAT_COLS.concat(Object.keys(tally), ['created_at']);
  const vals = COMBAT_COLS.map((c) => r[c]).concat(Object.values(tally));
  const ph = COMBAT_COLS.map(() => '?').concat(Object.keys(tally).map(() => '?'), ["datetime('now', ?)"]);
  w.prepare(`INSERT INTO combat_events (${cols.join(', ')}) VALUES (${ph.join(', ')})`).run(...vals, r.ago || '-1 hours');
}

// A full set of 1.2.6 values; override any of them.
function tallyOf(over) {
  return Object.assign({
    floor_actual: 3, difficulty: 1, party_size: 1, encounter_size: 1, first_actor: 0,
    dmg_to_player_basic: 10, dmg_to_player_ability: 0, dmg_to_player_spell: 0, dmg_to_player_dot: 0,
    dmg_to_team: 0, dmg_by_player: 60, dmg_by_team: 0, heal_player: 0, potions_used: 0,
    abilities_used: 0, spells_used: 0, teammates_lost: 0, player_hp_end: 190,
  }, over || {});
}

function insertNpc(w, row) {
  const r = Object.assign({ npc_name: 'Npc', npc_level: 5, npc_class: 'Warrior', action: 'dungeon', location_before: 'Inn',
    location_after: 'Dungeon', outcome: 'won', gold_delta: 0, xp_delta: 0, hp_before: 50, hp_after: 50, is_ai_driven: 0, ago: '-1 hours' }, row);
  w.prepare(`INSERT INTO npc_decision_log (npc_name, npc_level, npc_class, action, location_before, location_after, outcome,
    gold_delta, xp_delta, hp_before, hp_after, is_ai_driven, created_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?, datetime('now', ?))`)
    .run(r.npc_name, r.npc_level, r.npc_class, r.action, r.location_before, r.location_after, r.outcome,
      r.gold_delta, r.xp_delta, r.hp_before, r.hp_after, r.is_ai_driven, r.ago);
}

// ---------------------------------------------------------------- the routes

function proxySource() { return fs.readFileSync(path.join(WEB, 'ssh-proxy.js'), 'utf8'); }

// Loads the balance routes. options.db is the database (or null), options.locked makes the
// default-password lock active, options.crypto replaces node:crypto (for a spy).
// 1.2.7: options.endpoint stands for the server's telemetryEndpoint (the remote views call its
// reader()); by default a stand-in with no remote file. Every reader() call is counted in
// remoteCalls and every statement on the handle it returns is recorded in remotePrepared.
// options.now fixes Date.now() inside the section (the remote windows count UTC days).
function loadApi(options = {}) {
  const src = proxySource();
  const code = [
    slice(src, 'function sendJson(', '// --- Bug Report Proxy ---'),
    slice(src, '// 1.2.6: the token signature is compared in constant time', '// --- Admin Dashboard Auth'),
    slice(src, '// --- Balance Dashboard API ---', 'async function handleDashRequest'),
    ';({ handleBalanceRequest, createBalanceToken, verifyBalanceToken, balanceSigMatches, balanceWindow,' +
      ' BALANCE_SOURCES, BALANCE_CLASS_NAMES, BALANCE_REMOTE_HELD, BALANCE_REMOTE_ROW_CEILING, BALANCE_REMOTE_SQL,' +
      ' BALANCE_REMOTE_WINDOW_MS, BALANCE_WINDOWS, BALANCE_UNVERIFIED });',
  ].join('\n');
  const prepared = [];
  const realDb = options.db === undefined ? null : options.db;
  // records every statement and its arguments, so a test can EXPLAIN what a route ran
  const db = realDb && {
    prepare(sql) {
      const st = realDb.prepare(sql);
      const wrap = (fn) => (...args) => { prepared.push({ sql, args }); return st[fn](...args); };
      return { get: wrap('get'), all: wrap('all'), run: wrap('run') };
    },
  };
  const state = { locked: !!options.locked };
  const remotePrepared = [];
  const remoteCalls = [];
  const endpoint = options.endpoint || { reader: () => null };
  const telemetryEndpoint = {
    reader() {
      const h = endpoint.reader();
      remoteCalls.push(h);
      return h && {
        prepare(sql) {
          const st = h.prepare(sql);
          return { all: (...args) => { remotePrepared.push({ sql, args }); return st.all(...args); } };
        },
      };
    },
  };
  let SectionDate = Date;
  if (options.now !== undefined) {
    const fixed = options.now;
    SectionDate = class extends Date { static now() { return fixed; } };
  }
  const ctx = {
    db,
    telemetryEndpoint,
    crypto: options.crypto || nodeCrypto,
    Buffer, URL, JSON, Math, Object, Number, String, Date: SectionDate,
    console: { log() {}, warn() {}, info() {}, error() {} },
    BALANCE_USER: 'admin',
    BALANCE_DEFAULT_PASS: 'changeme',
    BALANCE_SECRET: 'test-secret',
    BALANCE_TOKEN_TTL: 60 * 60 * 1000,
    readBody: async () => ({}),
    checkRateLimit: () => true,
    recordFailedLogin() {},
    clearLoginAttempts() {},
    verifyBalancePassword: () => false,
    setBalancePasswordHash: () => true,
    hashPassword: (p) => p,
    isDefaultCredentialActive: () => state.locked,
  };
  vm.createContext(ctx);
  const api = vm.runInContext(code, ctx, { filename: 'ssh-proxy.js (balance section)' });
  const token = api.createBalanceToken();
  async function call(url, opts = {}) {
    const req = {
      url, method: opts.method || 'GET',
      headers: opts.headers || { authorization: 'Bearer ' + (opts.token === undefined ? token : opts.token) },
      socket: { remoteAddress: '127.0.0.1' },
    };
    const out = { status: 0, headers: {}, body: '' };
    const res = {
      setHeader(k, v) { out.headers[k.toLowerCase()] = v; },
      writeHead(s) { out.status = s; },
      end(b) { out.body = b === undefined ? '' : b; },
      headersSent: false,
    };
    await api.handleBalanceRequest(req, res);
    return { status: out.status, headers: out.headers, body: out.body ? JSON.parse(out.body) : null };
  }
  return { api, call, token, prepared, remotePrepared, remoteCalls, state, ctx };
}

// EXPLAIN QUERY PLAN of a recorded statement, as plan lines.
function planOf(db, sql, args) {
  return db.prepare('EXPLAIN QUERY PLAN ' + sql).all(...args).map((r) => r.detail);
}

module.exports = { schemaSql, makeDb, insertFight, insertNpc, tallyOf, loadApi, planOf, slice, proxySource };
