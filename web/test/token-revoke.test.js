'use strict';
// 1.2.8: the web admin routes that end an account's remember-me tokens (login_tokens rows).
//  - reset-password, ban and delete each remove only that account's rows (matched without case);
//    another account's rows stay.
//  - a database with no login_tokens table (game server not yet on 1.2.8) still completes each route.
//  - no token secret reaches a log line or a response.
// The three routes are cut from web/ssh-proxy.js by their markers and run in a vm context against a
// writable better-sqlite3 file. Nothing touches the network.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const Database = require('better-sqlite3');
const { slice, proxySource } = require('./balance-api-harness');

const ROOT = path.join(__dirname, '..', '..');
const SECRET = 'verifier-secret-0123456789abcdef';

function backendSql(re, what) {
  const backend = fs.readFileSync(path.join(ROOT, 'Scripts', 'Systems', 'SqlSaveBackend.cs'), 'utf8');
  const m = backend.match(re);
  if (!m) throw new Error(what + ' not found in SqlSaveBackend.cs');
  return m[0];
}

function load(withTable) {
  const src = proxySource();
  const route = (name, start, end) =>
    `async function ${name}(method, url, playerUsername, playerAction, req, res) {\n${slice(src, start, end)}\n  return false;\n}`;
  const code = [
    route('resetRoute', '  // POST /api/admin/players/:username/reset-password', '  // DELETE /api/admin/players/:username\n'),
    route('banRoute', '  // POST /api/admin/players/:username/ban', '  // POST /api/admin/players/:username/unban'),
    route('deleteRoute', '  // DELETE /api/admin/players/:username\n', '  // POST /api/admin/commands'),
    ';({ resetRoute, banRoute, deleteRoute });',
  ].join('\n');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'token-revoke-'));
  const dbWrite = new Database(path.join(dir, 'usurper_online.db'));
  dbWrite.exec(backendSql(/CREATE TABLE IF NOT EXISTS players \([\s\S]*?\n\s*\);/, 'players table'));
  dbWrite.exec('CREATE TABLE sleeping_players (username TEXT); CREATE TABLE online_players (username TEXT, ip_address TEXT);' +
    ' CREATE TABLE pvp_log (username TEXT); CREATE TABLE messages (from_player TEXT, to_player TEXT);' +
    ' CREATE TABLE banned_ips (ip_address TEXT PRIMARY KEY, reason TEXT, banned_at TEXT, banned_by TEXT, associated_username TEXT);' +
    ' CREATE TABLE admin_commands (id INTEGER PRIMARY KEY AUTOINCREMENT, command TEXT, target_username TEXT, args TEXT, created_by TEXT, status TEXT, result TEXT, executed_at TEXT);' +
    ' CREATE TABLE wizard_log (wizard_name TEXT, action TEXT, target TEXT, details TEXT, created_at TEXT);');
  if (withTable) dbWrite.exec(backendSql(/CREATE TABLE IF NOT EXISTS login_tokens \([\s\S]*?\n\s*\);/, 'login_tokens table'));
  const logs = [];
  const log = (...a) => logs.push(a.join(' '));
  let body = {};
  const ctx = {
    db: dbWrite, dbWrite,
    hashGamePassword: (p) => 'hashed:' + p,
    process: { env: {} }, Buffer, JSON, String, Object, Date, setTimeout,
    console: { log, warn: log, error: log, info: log },
    readBody: async () => body,
    sendJson(res, status, data) { res.status = status; res.data = data; },
  };
  vm.createContext(ctx);
  const api = vm.runInContext(code, ctx, { filename: 'ssh-proxy.js (token routes)' });
  const seed = () => {
    dbWrite.prepare("INSERT INTO players (username, display_name, password_hash, player_data) VALUES ('target', 'Target', 'old', '{}')").run();
    dbWrite.prepare("INSERT INTO players (username, display_name, password_hash, player_data) VALUES ('bystander', 'Bystander', 'old', '{}')").run();
    if (!withTable) return;
    const ins = dbWrite.prepare("INSERT INTO login_tokens (selector, verifier_hash, username, pw_stamp, created_at, expires_at, client_label) VALUES (?, ?, ?, 's', 'c', 'e', 'l')");
    ins.run('sel1', SECRET, 'target'); ins.run('sel2', SECRET, 'TARGET'); ins.run('sel3', SECRET, 'bystander');
  };
  async function run(fn, method, action, b) {
    body = b || {};
    const res = {};
    const handled = await api[fn](method, '/x', 'Target', action, {}, res);
    return { handled, status: res.status, data: res.data };
  }
  const tokens = () => dbWrite.prepare('SELECT selector FROM login_tokens ORDER BY selector').all().map(r => r.selector);
  const close = () => { dbWrite.close(); fs.rmSync(dir, { recursive: true, force: true }); };
  return { dbWrite, logs, seed, run, tokens, close };
}

const ROUTES = [
  ['reset-password', 'resetRoute', 'POST', '/reset-password', { newPassword: 'new secret' }],
  ['ban', 'banRoute', 'POST', '/ban', { reason: 'test', banIp: false }],
  ['delete', 'deleteRoute', 'DELETE', '', {}],
];

for (const [name, fn, method, action, b] of ROUTES) {
  test(`${name}: removes only that account's tokens (any case), another account's stay`, async () => {
    const t = load(true);
    try {
      t.seed();
      const r = await t.run(fn, method, action, b);
      assert.strictEqual(r.status, 200, JSON.stringify(r.data));
      assert.deepStrictEqual(t.tokens(), ['sel3']);
    } finally { t.close(); }
  });

  test(`${name}: a database without login_tokens still completes`, async () => {
    const t = load(false);
    try {
      t.seed();
      const r = await t.run(fn, method, action, b);
      assert.strictEqual(r.status, 200, JSON.stringify(r.data));
    } finally { t.close(); }
  });

  test(`${name}: no token secret in a log line or the response`, async () => {
    const t = load(true);
    try {
      t.seed();
      const r = await t.run(fn, method, action, b);
      assert.ok(!JSON.stringify(r.data).includes(SECRET));
      assert.ok(!t.logs.join('\n').includes(SECRET));
    } finally { t.close(); }
  });
}

test('server source: the revoke statements never log, and each route has one', () => {
  const src = proxySource();
  const lines = src.split('\n').filter(l => l.includes('login_tokens'));
  assert.ok(lines.length >= 2, 'reset and ban statements');
  for (const l of lines) assert.ok(!/console\.|log\(/.test(l), l);
});
