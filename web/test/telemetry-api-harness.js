'use strict';
// Runs the 1.2.7 telemetry endpoint of web/ssh-proxy.js without starting the server: the
// section between its markers is cut from the file and run in a vm context that has no
// require, no http and no net, so nothing in it can open a socket. Requests are fake
// objects; the clock, the timers and the console are injected; the remote database is a
// temp better-sqlite3 file. The game database, when a test asks for one, is a temp file
// with the real schema (read from the C#), open read-only and writable as the server opens it.
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const net = require('node:net');
const { EventEmitter } = require('node:events');
const Database = require('better-sqlite3');
const { slice, proxySource, schemaSql, insertFight } = require('./balance-api-harness');

const ROOT = path.join(__dirname, '..', '..');
const START = '// --- Remote Telemetry Endpoint (1.2.7) ---';
const END = '// --- End Remote Telemetry Endpoint ---';
const DAY_MS = 24 * 60 * 60 * 1000;
const HOUR_MS = 60 * 60 * 1000;
// A fixed instant: 2026-10-05T12:00:00Z. Day number 20366.
const T0 = Date.UTC(2026, 9, 5, 12, 0, 0);
const ID = '0123456789abcdef0123456789abcdef';
const MARKER_ADDRESS = '203.0.113.77';

function section() { return slice(proxySource(), START, END); }

// The section's names, run in a fresh vm context. game: { db, dbWrite } put in the context under
// the server's names, so a stray reference from the section would reach the game database.
function loadSection(game) {
  const logs = [];
  const capture = (level) => (...args) => logs.push(level + ' ' + args.map(String).join(' '));
  const ctx = {
    path, Buffer,
    console: { log: capture('log'), info: capture('info'), warn: capture('warn'), error: capture('error') },
    db: game ? game.db : null,
    dbWrite: game ? game.dbWrite : null,
  };
  vm.createContext(ctx);
  const names = vm.runInContext(section() + `
;({ createTelemetryEndpoint, telemetryPaths, isTelemetryUrl, telemetryAddress, parseTelemetryBatch,
   makeTelemetryLimiter, isTelemetryJsonType, TELEMETRY_COLUMNS, TELEMETRY_BATCH_COLUMNS, TELEMETRY_URL,
   TELEMETRY_MAP_MAX, TELEMETRY_STOP_CACHE_MS, TELEMETRY_PRUNE_MS, TELEMETRY_BODY_TIMEOUT_MS, TELEMETRY_ERROR_WORDS });`,
  ctx, { filename: 'ssh-proxy.js (telemetry section)' });
  return { names, logs, ctx };
}

// A temp folder; remote: true makes the (empty) remote database file there, as the operator does.
function makeDir(options = {}) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'telemetry-api-'));
  const dbPath = path.join(dir, 'remote_telemetry.db');
  const stopPath = path.join(dir, 'remote_telemetry.stop');
  if (options.remote !== false) new Database(dbPath).close();
  return { dir, dbPath, stopPath, cleanup: () => fs.rmSync(dir, { recursive: true, force: true }) };
}

// A game database file with the real schema and one fight, open as the server opens it.
function makeGame(dir) {
  const file = path.join(dir, 'usurper_online.db');
  const w = new Database(file);
  w.exec(schemaSql().sql);
  insertFight(w, { player_name: 'Tester' });
  w.close();
  const db = new Database(file, { readonly: true, fileMustExist: true });
  const dbWrite = new Database(file, { fileMustExist: true });
  return { file, db, dbWrite, close: () => { db.close(); dbWrite.close(); } };
}

// Everything a test needs: the endpoint over the folder's files with an injected clock,
// timers and console. options.game puts game connections in the context; options.mapMax.
function makeEndpoint(dir, options = {}) {
  const loaded = options.loaded || loadSection(options.game);
  const clock = { t: options.t === undefined ? T0 : options.t };
  const timers = [];
  const intervals = [];
  const opened = [];
  // records every database the section opens, with the options it used
  function TrackedDatabase(file, opts) {
    opened.push({ file, opts });
    return new Database(file, opts);
  }
  const ep = loaded.names.createTelemetryEndpoint({
    dbPath: dir.dbPath,
    stopPath: dir.stopPath,
    Database: TrackedDatabase,
    fs,
    isIP: net.isIP,
    now: () => clock.t,
    setTimeout: (fn, ms) => { const t = { fn, ms, cleared: false }; timers.push(t); return t; },
    clearTimeout: (t) => { if (t) t.cleared = true; },
    setInterval: (fn, ms) => { const t = { fn, ms, cleared: false }; intervals.push(t); return t; },
    clearInterval: (t) => { if (t) t.cleared = true; },
    console: loaded.ctx.console,
    mapMax: options.mapMax,
  });
  return { ep, clock, timers, intervals, opened, logs: loaded.logs, names: loaded.names, loaded };
}

// A fake request. body: string, Buffer or an array of chunks; end: false leaves it open. With no
// peer given, every request comes from a new documentation address, so per address counts only
// meet where a test means them to.
let peerSeq = 0;
function freshPeer() { peerSeq++; return '2001:db8::' + peerSeq.toString(16); }
function fakeReq(opts) {
  const req = new EventEmitter();
  req.url = opts.url === undefined ? '/api/telemetry/v1/combat' : opts.url;
  req.method = opts.method || 'POST';
  req.headers = Object.assign({ 'content-type': 'application/json' }, opts.headers || {});
  for (const k of Object.keys(req.headers)) if (req.headers[k] === null) delete req.headers[k];
  req.socket = { remoteAddress: opts.peer === undefined ? freshPeer() : opts.peer, destroyed: false, destroy() { this.destroyed = true; } };
  req.dataListened = false;
  const on = req.on.bind(req);
  req.on = (ev, fn) => { if (ev === 'data') req.dataListened = true; return on(ev, fn); };
  return req;
}

function fakeRes() {
  const out = { status: 0, headers: {}, body: null, ended: false };
  const res = {
    headersSent: false,
    setHeader(k, v) { out.headers[k.toLowerCase()] = v; },
    getHeader(k) { return out.headers[k.toLowerCase()]; },
    writeHead(s, h) {
      out.status = s;
      if (h) for (const [k, v] of Object.entries(h)) out.headers[k.toLowerCase()] = v;
      res.headersSent = true;
    },
    end(b, cb) { out.body = b === undefined ? '' : String(b); out.ended = true; if (typeof cb === 'function') cb(); },
  };
  return { res, out };
}

// Sends one request through the endpoint and returns what came back.
async function send(t, opts = {}) {
  const req = fakeReq(opts);
  const { res, out } = fakeRes();
  const p = t.ep.handle(req, res);
  if (opts.end !== false) {
    let chunks = opts.body === undefined ? [] : opts.body;
    if (!Array.isArray(chunks)) chunks = [chunks];
    for (const c of chunks) {
      if (req.listenerCount('data') === 0) break;
      req.emit('data', Buffer.isBuffer(c) ? c : Buffer.from(c, 'utf8'));
    }
    if (req.listenerCount('end') > 0) req.emit('end');
  }
  if (opts.beforeAwait) opts.beforeAwait(req, res);
  await p;
  return { status: out.status, headers: out.headers, body: out.body, ended: out.ended, socketDestroyed: req.socket.destroyed, dataListened: req.dataListened, req };
}

// ---------------------------------------------------------------- rows and bodies

function fixture() {
  return JSON.parse(fs.readFileSync(path.join(ROOT, 'Tests', 'Fixtures', 'telemetry-rows.json'), 'utf8'));
}

function goodRow(over) { return Object.assign({}, fixture().good[0], over || {}); }

function batch(over) {
  return Object.assign({ schema: 1, version: [1, 2, 7], source: 1, install_id: ID, rows: [goodRow()] }, over || {});
}

function bodyOf(over) { return JSON.stringify(batch(over)); }

// The C# column table: (key, min, max) in TelemetryRow.Columns, MaxSafe as 2^53 minus 1.
function csharpColumns() {
  const src = fs.readFileSync(path.join(ROOT, 'Scripts', 'Systems', 'TelemetryRow.cs'), 'utf8');
  const block = src.match(/Columns\s*=\s*\{([\s\S]*?)\};/);
  if (!block) throw new Error('TelemetryRow.Columns not found');
  const max = Number(src.match(/MaxSafe\s*=\s*(\d+)L;/)[1]);
  const num = (s) => (s === 'MaxSafe' ? max : Number(s));
  return [...block[1].matchAll(/\("(\w+)",\s*(-?\d+|MaxSafe),\s*(-?\d+|MaxSafe)\)/g)].map((m) => [m[1], num(m[2]), num(m[3])]);
}

function remoteRows(dbPath) {
  const d = new Database(dbPath, { readonly: true, fileMustExist: true });
  try { return d.prepare('SELECT * FROM remote_combat_events ORDER BY rowid').all(); } finally { d.close(); }
}

function remoteCount(dbPath) {
  const d = new Database(dbPath, { readonly: true, fileMustExist: true });
  try {
    const has = d.prepare("SELECT 1 FROM sqlite_master WHERE name = 'remote_combat_events'").get();
    return has ? d.prepare('SELECT COUNT(*) AS n FROM remote_combat_events').get().n : 0;
  } finally { d.close(); }
}

// Inserts count rows for one day straight into the remote table (a test's own writes). With
// seqStart a number, player_hp_end numbers the rows seqStart, seqStart + 1, ...
function fillRemote(dbPath, day, count, seqStart) {
  const d = new Database(dbPath, { fileMustExist: true });
  try {
    const cols = d.prepare('PRAGMA table_info(remote_combat_events)').all().map((c) => c.name);
    const row = Object.assign({ schema: 1, version_major: 1, version_minor: 2, version_patch: 7, source: 1, install_id: ID, received_day: day }, goodRow());
    const st = d.prepare(`INSERT INTO remote_combat_events (${cols.join(', ')}) VALUES (${cols.map(() => '?').join(', ')})`);
    const value = (c, i) => (c === 'player_hp_end' && typeof seqStart === 'number' ? seqStart + i : row[c]);
    d.transaction(() => { for (let i = 0; i < count; i++) st.run(...cols.map((c) => value(c, i))); })();
  } finally { d.close(); }
}

module.exports = {
  START, END, DAY_MS, HOUR_MS, T0, ID, MARKER_ADDRESS,
  section, proxySourceText: proxySource, loadSection, makeDir, makeGame, makeEndpoint, fakeReq, fakeRes, send, freshPeer,
  fixture, goodRow, batch, bodyOf, csharpColumns, remoteRows, remoteCount, fillRemote,
};
