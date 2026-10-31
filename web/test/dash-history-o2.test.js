'use strict';
// O2: GET /api/dash/history. The section between its markers in web/ssh-proxy.js is cut out and run in a vm
// context with an injected clock and a temp game database made with the table's real DDL, read from the C#
// (SqlSaveBackend.cs), and opened read only as the server opens it.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const Database = require('better-sqlite3');
const { slice, proxySource } = require('./balance-api-harness');

const ROOT = path.join(__dirname, '..', '..');
const START = '// --- Dashboard History (O2) ---';
const END = '// --- End Dashboard History ---';
const HOUR = 60 * 60 * 1000;
// 2026-10-31T14:20:00Z, twenty minutes into the hour
const NOW = Date.UTC(2026, 9, 31, 14, 20, 0);
const COLUMNS = ['taken_at', 'npcs_alive', 'npcs_dead', 'npcs_married', 'children', 'treasury',
  'players_online', 'births', 'deaths', 'marriages'];

// values made in the vm context have its prototypes; the JSON round trip is what the reply sends anyway
const plain = (v) => JSON.parse(JSON.stringify(v));
const key = (ms) => new Date(ms).toISOString().slice(0, 13) + ':00:00Z';

function tableDdl() {
  const src = fs.readFileSync(path.join(ROOT, 'Scripts', 'Systems', 'SqlSaveBackend.cs'), 'utf8');
  const m = src.match(/CREATE TABLE IF NOT EXISTS world_snapshots \([\s\S]*?\n\s*\);/);
  if (!m) throw new Error('world_snapshots not found in SqlSaveBackend.cs');
  return m[0];
}

// A game database file with the table (unless table: false) and a row for each of the hours before NOW
// (hours: 800 by default), plus one row in the future; returned open read only.
function makeDb(options = {}) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'dash-history-'));
  const file = path.join(dir, 'usurper_online.db');
  const w = new Database(file);
  if (options.table !== false) {
    w.exec(tableDdl());
    const put = w.prepare('INSERT INTO world_snapshots VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)');
    const hours = options.hours === undefined ? 800 : options.hours;
    w.transaction(() => {
      for (let i = 0; i < hours; i++) put.run(key(NOW - i * HOUR), 100 + i, 5, 20, 7, 1000 * i, 2, 1, 0, 1);
      put.run(key(NOW + HOUR), 0, 0, 0, 0, 0, 0, 0, 0, 0);
    })();
  }
  w.close();
  const db = new Database(file, { readonly: true, fileMustExist: true });
  return {
    db, file,
    write(sql, ...args) { const x = new Database(file); try { x.prepare(sql).run(...args); } finally { x.close(); } },
    cleanup() { db.close(); fs.rmSync(dir, { recursive: true, force: true }); },
  };
}

// The section in a fresh context: db is the database handed to getDb (or null), clock.t the time.
function load(db) {
  const ctx = { URL, console };
  vm.createContext(ctx);
  const names = vm.runInContext(slice(proxySource(), START, END) + `
;({ createDashHistory, dashHistoryHours, dashHistoryHourKey, DASH_HISTORY_HOURS, DASH_HISTORY_CACHE_MS,
   DASH_HISTORY_COLUMNS, DASH_HISTORY_SQL });`, ctx, { filename: 'ssh-proxy.js (dashboard history section)' });
  const clock = { t: NOW };
  const sent = [];
  const prepared = [];
  const handle = db && {
    prepare(sql) { prepared.push(sql); return db.prepare(sql); },
  };
  const h = names.createDashHistory({
    getDb: () => handle,
    now: () => clock.t,
    sendJson: (res, status, data) => sent.push({ res, status, data }),
  });
  return { names, h, clock, sent, prepared };
}

test('the section sits in handleDashRequest as a GET route', () => {
  const route = slice(proxySource(), 'async function handleDashRequest', START);
  assert.match(route, /if \(method === 'GET' && dashHistory\.matches\(url\)\) \{\s*dashHistory\.handle\(req, res\);\s*return true;/);
});

test('the columns are the table\'s, in its order, and the C# writes the same ten', () => {
  const { names } = load(null);
  assert.deepStrictEqual([...names.DASH_HISTORY_COLUMNS], COLUMNS);
  const ddlColumns = [...tableDdl().matchAll(/^\s*(\w+) (TEXT|INTEGER)/gm)].map((m) => m[1]);
  assert.deepStrictEqual(ddlColumns, COLUMNS);
});

test('hours: only 24, 168 and 720 are taken; anything else is 24', () => {
  const { names } = load(null);
  const cases = {
    '/api/dash/history?hours=24': 24,
    '/api/dash/history?hours=168': 168,
    '/api/dash/history?hours=720': 720,
    '/api/dash/history': 24,
    '/api/dash/history?': 24,
    '/api/dash/history?hours=': 24,
    '/api/dash/history?hours=0': 24,
    '/api/dash/history?hours=1': 24,
    '/api/dash/history?hours=23': 24,
    '/api/dash/history?hours=48': 24,
    '/api/dash/history?hours=169': 24,
    '/api/dash/history?hours=721': 24,
    '/api/dash/history?hours=8760': 24,
    '/api/dash/history?hours=99999999': 24,
    '/api/dash/history?hours=-168': 24,
    '/api/dash/history?hours=168.0': 24,
    '/api/dash/history?hours=1e3': 24,
    '/api/dash/history?hours=0x2d0': 24,
    '/api/dash/history?hours=%20168': 24,
    '/api/dash/history?hours=abc': 24,
    '/api/dash/history?days=30': 24,
    '/api/dash/history?hours=720&x=1': 720,
  };
  for (const [url, want] of Object.entries(cases)) assert.strictEqual(names.dashHistoryHours(url), want, url);
});

test('only /api/dash/history and its query forms match the route', () => {
  const { h } = load(null);
  for (const u of ['/api/dash/history', '/api/dash/history?hours=168']) assert.strictEqual(h.matches(u), true, u);
  for (const u of ['/api/dash/historyx', '/api/dash/history/', '/api/dash/history/../npcs', '/api/dash/histor', '/api/dash/events']) {
    assert.strictEqual(h.matches(u), false, u);
  }
});

test('each window gives that many rows, oldest first, up to the current hour, the future left out', () => {
  const g = makeDb();
  try {
    const { h } = load(g.db);
    for (const hours of [24, 168, 720]) {
      const rows = h.rows(hours);
      assert.strictEqual(rows.length, hours, `${hours} rows`);
      assert.strictEqual(rows[0].taken_at, key(NOW - (hours - 1) * HOUR), 'the oldest row of the window');
      assert.strictEqual(rows[rows.length - 1].taken_at, key(NOW), 'the current hour, not the future one');
      const ordered = rows.map((r) => r.taken_at);
      assert.deepStrictEqual(ordered, [...ordered].sort(), 'oldest first');
      assert.ok(!ordered.includes(key(NOW - hours * HOUR)), 'the hour before the window is left out');
    }
  } finally { g.cleanup(); }
});

test('a row carries exactly the ten columns with their values', () => {
  const g = makeDb();
  try {
    const { h } = load(g.db);
    const last = h.rows(24).at(-1);
    assert.deepStrictEqual(Object.keys(last), COLUMNS);
    assert.deepStrictEqual(plain(last), { taken_at: key(NOW), npcs_alive: 100, npcs_dead: 5, npcs_married: 20, children: 7,
      treasury: 0, players_online: 2, births: 1, deaths: 0, marriages: 1 });
  } finally { g.cleanup(); }
});

test('the select reads a primary key range on taken_at', () => {
  const g = makeDb();
  try {
    const { names } = load(g.db);
    const plan = g.db.prepare('EXPLAIN QUERY PLAN ' + names.DASH_HISTORY_SQL).all('a', 'b').map((r) => r.detail).join(' | ');
    assert.match(plan, /SEARCH world_snapshots USING INDEX sqlite_autoindex_world_snapshots_1 \(taken_at>\? AND taken_at<\?\)/);
    assert.doesNotMatch(names.DASH_HISTORY_SQL, /\*/);
  } finally { g.cleanup(); }
});

test('the handler replies 200 with the window the query asks for', () => {
  const g = makeDb();
  try {
    const { h, sent } = load(g.db);
    h.handle({ url: '/api/dash/history?hours=168' }, 'r1');
    h.handle({ url: '/api/dash/history?hours=5000' }, 'r2');
    assert.deepStrictEqual(sent.map((s) => [s.res, s.status, s.data.length]), [['r1', 200, 168], ['r2', 200, 24]]);
  } finally { g.cleanup(); }
});

test('a window is cached for 5 minutes, then read again', () => {
  const g = makeDb({ hours: 3 });
  try {
    const { h, clock, prepared, names } = load(g.db);
    assert.strictEqual(names.DASH_HISTORY_CACHE_MS, 5 * 60 * 1000);
    assert.strictEqual(h.rows(24).length, 3);
    g.write('INSERT INTO world_snapshots (taken_at, births) VALUES (?, 9)', key(NOW - 5 * HOUR));
    clock.t = NOW + 5 * 60 * 1000 - 1;
    assert.strictEqual(h.rows(24).length, 3, 'served from the cache');
    assert.strictEqual(prepared.length, 1);
    assert.strictEqual(h.rows(168).length, 4, 'each window has its own entry');
    clock.t = NOW + 5 * 60 * 1000;
    assert.strictEqual(h.rows(24).length, 4, 'read again after 5 minutes');
    assert.strictEqual(prepared.length, 3);
  } finally { g.cleanup(); }
});

test('no database, or one without the table, gives an empty list', () => {
  assert.deepStrictEqual(plain(load(null).h.rows(24)), []);
  const g = makeDb({ table: false });
  try {
    const { h, sent } = load(g.db);
    h.handle({ url: '/api/dash/history?hours=720' }, 'r');
    assert.deepStrictEqual(plain(sent.map((s) => [s.status, s.data])), [[200, []]]);
  } finally { g.cleanup(); }
});
