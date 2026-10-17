'use strict';
// 1.3.4: the web admin and the account address.
//  - the player detail route names its columns and returns the address only masked (AccountEmail.Mask);
//  - the admin reset refuses a password under 8 characters (GameConfig.MinPasswordLength) and one with ':' (D5);
//  - admin.html checks the same before it sends.
// The routes are cut from web/ssh-proxy.js by their markers and run in a vm context with the Account Mail
// section (for accountMask and the constant), against a temp better-sqlite3 file. Nothing touches the network.
delete process.env.RESEND_API_KEY;
delete process.env.MAIL_FROM;

const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const Database = require('better-sqlite3');
const { slice, proxySource } = require('./balance-api-harness');

const ROOT = path.join(__dirname, '..', '..');
const ADDRESS = 'holder.one@example.com';

function load() {
  const src = proxySource();
  const route = (name, start, end) =>
    `async function ${name}(method, url, playerUsername, playerAction, req, res) {\n${slice(src, start, end)}\n  return false;\n}`;
  const code = [
    slice(src, '// --- Account Mail (1.3.4) ---', '// --- End Account Mail ---'),
    route('detailRoute', '  // GET /api/admin/players/:username \u2014 full player detail', '  // POST /api/admin/players/:username/edit'),
    route('resetRoute', '  // POST /api/admin/players/:username/reset-password', '  // DELETE /api/admin/players/:username\n'),
    ';({ detailRoute, resetRoute });',
  ].join('\n');
  const backend = fs.readFileSync(path.join(ROOT, 'Scripts', 'Systems', 'SqlSaveBackend.cs'), 'utf8');
  const players = backend.match(/CREATE TABLE IF NOT EXISTS players \([\s\S]*?\n\s*\);/)[0];
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'admin-pw-reset134-'));
  const db = new Database(path.join(dir, 'usurper_online.db'));
  db.exec(players + ' CREATE TABLE online_players (username TEXT, ip_address TEXT, location TEXT, connection_type TEXT);' +
    ' CREATE TABLE wizard_log (wizard_name TEXT, action TEXT, target TEXT, details TEXT, created_at TEXT);');
  let body = {};
  const ctx = {
    db, dbWrite: db, Buffer, JSON,
    CLASS_NAMES: {}, RACE_NAMES: {}, FACTION_NAMES: { '-1': 'None' },
    hashGamePassword: (p) => 'hashed:' + p,
    console: { log() {}, warn() {}, error() {}, info() {} },
    readBody: async () => body,
    sendJson(res, status, data) { res.status = status; res.data = data; },
  };
  vm.createContext(ctx);
  const api = vm.runInContext(code, ctx, { filename: 'ssh-proxy.js (admin routes)' });
  async function run(fn, method, action, b) {
    body = b || {};
    const res = {};
    await api[fn](method, '/x', 'Holder', action, {}, res);
    // the vm's objects come back as plain JSON, so deepStrictEqual compares values only
    return { status: res.status, data: res.data === undefined ? undefined : JSON.parse(JSON.stringify(res.data)) };
  }
  const withEmail = () => {
    for (const c of ['email TEXT', 'email_verified_at TEXT', 'email_prompted_at TEXT']) db.exec(`ALTER TABLE players ADD COLUMN ${c};`);
  };
  const close = () => { db.close(); fs.rmSync(dir, { recursive: true, force: true }); };
  return { db, run, withEmail, close };
}

test('admin detail: the address is masked, never raw, and the route names its columns', async () => {
  const t = load();
  try {
    t.withEmail();
    t.db.prepare("INSERT INTO players (username, display_name, password_hash, player_data, email, email_verified_at) VALUES ('holder', 'Holder', 'secret-hash', '{}', ?, '2026-10-17 08:00:00')").run(ADDRESS);
    const r = await t.run('detailRoute', 'GET', '');
    assert.strictEqual(r.status, 200, JSON.stringify(r.data));
    const text = JSON.stringify(r.data);
    assert.ok(!text.includes(ADDRESS) && !text.includes('holder.one'), text.slice(0, 300));
    assert.strictEqual(r.data.email, 'h***@e***.com');
    assert.strictEqual(r.data.emailVerified, true);
    assert.ok(!text.includes('secret-hash'), 'no password hash either');
  } finally { t.close(); }
  const route = slice(proxySource(), '  // GET /api/admin/players/:username \u2014 full player detail', '  // POST /api/admin/players/:username/edit');
  assert.doesNotMatch(route, /SELECT \* FROM players/);
});

test('admin detail: a database from before 1.3.4 (no address columns) still answers', async () => {
  const t = load();
  try {
    t.db.prepare("INSERT INTO players (username, display_name, password_hash, player_data) VALUES ('holder', 'Holder', 'h', '{}')").run();
    const r = await t.run('detailRoute', 'GET', '');
    assert.strictEqual(r.status, 200, JSON.stringify(r.data));
    assert.strictEqual(r.data.email, null);
    assert.strictEqual(r.data.emailVerified, false);
  } finally { t.close(); }
});

test('admin reset: under 8 characters and a colon are refused, 8 characters are stored', async () => {
  const t = load();
  try {
    t.db.prepare("INSERT INTO players (username, display_name, password_hash, player_data) VALUES ('holder', 'Holder', 'old', '{}')").run();
    const hash = () => t.db.prepare("SELECT password_hash FROM players WHERE username = 'holder'").get().password_hash;
    const short = await t.run('resetRoute', 'POST', '/reset-password', { newPassword: '1234567' });
    assert.deepStrictEqual([short.status, short.data], [400, { error: 'Password must be at least 8 characters' }]);
    const colon = await t.run('resetRoute', 'POST', '/reset-password', { newPassword: 'colon:inside' });
    assert.deepStrictEqual([colon.status, colon.data], [400, { error: 'Password cannot contain a colon' }]);
    const notText = await t.run('resetRoute', 'POST', '/reset-password', { newPassword: 123456789 });
    assert.strictEqual(notText.status, 400);
    assert.strictEqual(hash(), 'old', 'nothing stored');
    const ok = await t.run('resetRoute', 'POST', '/reset-password', { newPassword: '12345678' });
    assert.strictEqual(ok.status, 200, JSON.stringify(ok.data));
    assert.strictEqual(hash(), 'hashed:12345678');
  } finally { t.close(); }
});

test('admin.html: the reset form checks 8 characters and the colon before it sends', () => {
  const html = fs.readFileSync(path.join(ROOT, 'web', 'admin.html'), 'utf8');
  const modal = slice(html, 'function showResetPwModal(username) {', '\n}\n');
  assert.match(modal, /if \(!pw \|\| pw\.length < 8\) throw new Error\('Password must be at least 8 characters'\);/);
  assert.match(modal, /if \(pw\.includes\(':'\)\) throw new Error\('Password cannot contain a colon'\);/);
  assert.doesNotMatch(modal, /min 4|< 4/);
});
