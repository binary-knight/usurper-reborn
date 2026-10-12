'use strict';
// 1.2.8: the passwords web/ssh-proxy.js stores.
//  1. A player reset from the web admin is stored in the game's PBKDF2 "salt:hash" form; the
//     fixed vector in Tests/Fixtures/game-password-vector.json is made here by the web helper and
//     verified by the game's VerifyPassword and a real login in Tests/WebPasswordReset128Tests.cs.
//  2. A new dashboard password is bcrypt only; without bcryptjs a change is refused with a clear
//     error and nothing is stored.
//  3. The default credential is checked without hashing it; the lock behaves as before.
//  4. A legacy SHA256 dashboard hash logs in only through the one-time migration to bcrypt, which
//     needs bcryptjs.
// The password section and the three routes are cut from the file by their markers and run in a vm
// context against a writable better-sqlite3 file, as the server opens it. Nothing touches the network.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const nodeCrypto = require('node:crypto');
const Database = require('better-sqlite3');
const realBcrypt = require('bcryptjs');
const { slice, proxySource } = require('./balance-api-harness');

const ROOT = path.join(__dirname, '..', '..');
const VECTOR = JSON.parse(fs.readFileSync(path.join(ROOT, 'Tests', 'Fixtures', 'game-password-vector.json'), 'utf8'));
const DEFAULT = 'changeme';

function playersTableSql() {
  const backend = fs.readFileSync(path.join(ROOT, 'Scripts', 'Systems', 'SqlSaveBackend.cs'), 'utf8');
  const m = backend.match(/CREATE TABLE IF NOT EXISTS players \([\s\S]*?\n\s*\);/);
  if (!m) throw new Error('players table not found in SqlSaveBackend.cs');
  return m[0];
}

// options.bcrypt: the bcryptjs module or null; options.env: process.env inside the section;
// options.salt: a fixed salt for crypto.randomBytes. Every createHash call is counted.
function loadAuth(options = {}) {
  const src = proxySource();
  const route = (name, start, end) =>
    `async function ${name}(method, url, playerUsername, playerAction, req, res) {\n${slice(src, start, end)}\n  return false;\n}`;
  const code = [
    slice(src, '// --- Password hashing (1.2.8) ---', '// 1.2.6: the token signature is compared in constant time'),
    route('resetRoute', '  // POST /api/admin/players/:username/reset-password', '  // DELETE /api/admin/players/:username\n'),
    route('balanceChangeRoute', '  // POST /api/balance/change-password', '  // v0.65.8 (T1-4): default credential still active'),
    route('adminChangeRoute', '  // POST /api/admin/change-password', '  // v0.65.8 (T1-4): default credential still active'),
    ';({ hashGamePassword, canHashBalancePassword, hashBalancePassword, getBalancePasswordHash, setBalancePasswordHash,' +
      ' isDefaultCredentialActive, verifyBalancePassword, resetRoute, balanceChangeRoute, adminChangeRoute });',
  ].join('\n');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'auth-test-'));
  const dbWrite = new Database(path.join(dir, 'usurper_online.db'));
  dbWrite.exec(playersTableSql());
  const calls = { createHash: [], randomBytes: 0 };
  const cryptoSpy = Object.assign(Object.create(nodeCrypto), {
    createHash: (...a) => { calls.createHash.push(a[0]); return nodeCrypto.createHash(...a); },
    randomBytes: (n) => { calls.randomBytes++; return options.salt ? Buffer.from(options.salt) : nodeCrypto.randomBytes(n); },
    pbkdf2Sync: nodeCrypto.pbkdf2Sync,
    timingSafeEqual: nodeCrypto.timingSafeEqual,
  });
  const logs = [];
  const log = (...a) => logs.push(a.join(' '));
  let body = {};
  const ctx = {
    crypto: cryptoSpy,
    bcrypt: options.bcrypt === undefined ? realBcrypt : options.bcrypt,
    BCRYPT_ROUNDS: 4, // the server uses 12 (checked below); 4 keeps the tests quick
    dbWrite,
    process: { env: options.env || {} },
    SHIPPED_DEFAULT_PASS: DEFAULT,
    BALANCE_DEFAULT_PASS: options.env && options.env.BALANCE_PASS ? options.env.BALANCE_PASS : DEFAULT,
    Buffer, JSON, String, Object,
    console: { log, warn: log, error: log, info: log },
    readBody: async () => body,
    sendJson(res, status, data) { res.status = status; res.data = data; },
  };
  vm.createContext(ctx);
  const api = vm.runInContext(code, ctx, { filename: 'ssh-proxy.js (password section)' });
  const stored = () => {
    if (!dbWrite.prepare("SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'balance_config'").get()) return null;
    const row = dbWrite.prepare("SELECT value FROM balance_config WHERE key = 'password_hash'").get();
    return row ? row.value : null;
  };
  const store = (hash) => { assert.strictEqual(api.setBalancePasswordHash(hash), true); };
  async function run(fn, url, opts) {
    body = opts.body;
    const res = {};
    const handled = await api[fn]('POST', url, opts.player || null, opts.action || '', {}, res);
    return { handled, status: res.status, data: res.data };
  }
  const close = () => { dbWrite.close(); fs.rmSync(dir, { recursive: true, force: true }); };
  return { api, calls, logs, dbWrite, stored, store, run, close };
}

function withAuth(options, fn) {
  return async () => {
    const a = loadAuth(options);
    try { await fn(a); } finally { a.close(); }
  };
}

const sha256hex = (s) => nodeCrypto.createHash('sha256').update(s).digest('hex');

// The game's VerifyPassword, step for step: split, base64, PBKDF2-SHA256 at the stored salt, compare.
function gameVerifies(password, storedHash) {
  const parts = String(storedHash).split(':');
  if (parts.length !== 2) return false;
  const salt = Buffer.from(parts[0], 'base64');
  const expected = Buffer.from(parts[1], 'base64');
  const actual = nodeCrypto.pbkdf2Sync(password, salt, 100000, 32, 'sha256');
  return expected.length === actual.length && nodeCrypto.timingSafeEqual(actual, expected);
}

// ---------------------------------------------------------------- 1. game reset

test('game reset helper: the fixed vector byte for byte, and a fresh salt every call', withAuth({ salt: Buffer.from(VECTOR.salt_base64, 'base64') }, async (a) => {
  assert.strictEqual(a.api.hashGamePassword(VECTOR.password), VECTOR.stored, 'the vector the C# test verifies');
  assert.notStrictEqual(a.api.hashGamePassword(VECTOR.wrong_password), VECTOR.stored);
  assert.strictEqual(a.calls.randomBytes, 2);
}));

test('game reset helper: 16 byte salt, 32 byte key, base64, different each time, verifies as the game does', withAuth({}, async (a) => {
  const h1 = a.api.hashGamePassword('fresh secret');
  const h2 = a.api.hashGamePassword('fresh secret');
  assert.notStrictEqual(h1, h2, 'a random salt per hash');
  for (const h of [h1, h2]) {
    const [salt, key] = h.split(':');
    assert.strictEqual(Buffer.from(salt, 'base64').length, 16);
    assert.strictEqual(Buffer.from(key, 'base64').length, 32);
    assert.ok(gameVerifies('fresh secret', h));
    assert.ok(!gameVerifies('fresh secreT', h));
  }
  assert.deepStrictEqual(a.calls.createHash, [], 'no plain hash of a player password');
}));

test('reset route stores the game form the game verifies (cross-check with WebPasswordReset128Tests)', withAuth({ salt: Buffer.from(VECTOR.salt_base64, 'base64') }, async (a) => {
  a.dbWrite.prepare("INSERT INTO players (username, display_name, password_hash, player_data) VALUES ('web reset player', 'Web Reset Player', 'old', '{}')").run();
  const r = await a.run('resetRoute', '/api/admin/players/Web%20Reset%20Player/reset-password',
    { player: 'Web Reset Player', action: '/reset-password', body: { newPassword: VECTOR.password } });
  assert.strictEqual(r.handled, true);
  assert.strictEqual(r.status, 200, JSON.stringify(r.data));
  const hash = a.dbWrite.prepare("SELECT password_hash FROM players WHERE username = 'web reset player'").get().password_hash;
  assert.strictEqual(hash, VECTOR.stored, 'exactly the vector the game accepts');
  assert.ok(gameVerifies(VECTOR.password, hash));
  assert.ok(!gameVerifies(VECTOR.wrong_password, hash));
  // a short password is refused and nothing changes
  const short = await a.run('resetRoute', '/x', { player: 'Web Reset Player', action: '/reset-password', body: { newPassword: 'abc' } });
  assert.strictEqual(short.status, 400);
  assert.strictEqual(a.dbWrite.prepare("SELECT password_hash FROM players WHERE username = 'web reset player'").get().password_hash, VECTOR.stored);
}));

test('reset route needs no bcryptjs: the game form is made the same without it', withAuth({ bcrypt: null }, async (a) => {
  a.dbWrite.prepare("INSERT INTO players (username, display_name, password_hash, player_data) VALUES ('other player', 'Other Player', 'old', '{}')").run();
  const r = await a.run('resetRoute', '/x', { player: 'Other Player', action: '/reset-password', body: { newPassword: 'new secret' } });
  assert.strictEqual(r.status, 200);
  const hash = a.dbWrite.prepare("SELECT password_hash FROM players WHERE username = 'other player'").get().password_hash;
  assert.ok(gameVerifies('new secret', hash), hash);
}));

// ---------------------------------------------------------------- 2. new dashboard hashes

test('server source: bcrypt at 12 rounds; SHA256 only inside the one-time migration; no old hashPassword', () => {
  const src = proxySource();
  assert.match(src, /\nconst BCRYPT_ROUNDS = 12;\n/);
  assert.doesNotMatch(src, /\bhashPassword(Sha256)?\s*\(/);
  const sha = [...src.matchAll(/createHash\('sha256'\)/g)].map((m) => m.index);
  const mig = slice(src, 'function migrateLegacyBalancePassword(', '\n}\n');
  const migAt = src.indexOf(mig);
  const inMigration = sha.filter((i) => i > migAt && i < migAt + mig.length);
  assert.strictEqual(inMigration.length, 1, 'the one legacy verify');
  const section = slice(src, '// --- Password hashing (1.2.8) ---', '// 1.2.6: the token signature is compared in constant time');
  assert.strictEqual([...section.matchAll(/createHash\(/g)].length, 1, 'no other hash in the password section');
});

for (const [name, fn, url] of [['balance', 'balanceChangeRoute', '/api/balance/change-password'], ['admin', 'adminChangeRoute', '/api/admin/change-password']]) {
  test(`${name} change-password stores bcrypt; the new password logs in, the default no longer`, withAuth({}, async (a) => {
    assert.strictEqual(a.api.isDefaultCredentialActive(), true);
    const r = await a.run(fn, url, { body: { newPassword: 'operator pass' } });
    assert.strictEqual(r.status, 200, JSON.stringify(r.data));
    assert.match(a.stored(), /^\$2[ab]\$04\$/);
    assert.strictEqual(a.api.verifyBalancePassword('operator pass'), true);
    assert.strictEqual(a.api.verifyBalancePassword(DEFAULT), false);
    assert.strictEqual(a.api.isDefaultCredentialActive(), false, 'the lock lifts');
    assert.deepStrictEqual(a.calls.createHash, []);
  }));

  test(`${name} change-password without bcryptjs: refused with a clear error, nothing stored, never SHA256`, withAuth({ bcrypt: null }, async (a) => {
    const r = await a.run(fn, url, { body: { newPassword: 'operator pass' } });
    assert.strictEqual(r.status, 503);
    assert.match(r.data.error, /bcryptjs is not installed/);
    assert.strictEqual(a.stored(), null);
    assert.strictEqual(a.api.canHashBalancePassword(), false);
    assert.throws(() => a.api.hashBalancePassword('operator pass'), /bcryptjs is not installed/);
    assert.strictEqual(a.stored(), null);
    assert.strictEqual(a.api.isDefaultCredentialActive(), true, 'still locked');
    assert.deepStrictEqual(a.calls.createHash, []);
  }));
}

// ---------------------------------------------------------------- 3. the default credential

test('default credential: checked without hashing, the lock as before', withAuth({}, async (a) => {
  // nothing stored: the default is in force and logs in; nothing is hashed or written
  assert.strictEqual(a.api.getBalancePasswordHash(), null);
  assert.strictEqual(a.api.isDefaultCredentialActive(), true);
  assert.strictEqual(a.api.verifyBalancePassword(DEFAULT), true);
  assert.strictEqual(a.api.verifyBalancePassword('changemE'), false);
  assert.strictEqual(a.api.verifyBalancePassword(undefined), false);
  assert.strictEqual(a.stored(), null);
  // a bcrypt hash of the default (what the 1.2.7 migration could store) still locks
  a.store(realBcrypt.hashSync(DEFAULT, 4));
  assert.strictEqual(a.api.isDefaultCredentialActive(), true);
  assert.strictEqual(a.api.verifyBalancePassword(DEFAULT), true);
  // a real password unlocks
  a.store(realBcrypt.hashSync('operator pass', 4));
  assert.strictEqual(a.api.isDefaultCredentialActive(), false);
  // a legacy SHA256 hash locks until a login migrates it, and the check hashes nothing
  a.store(sha256hex('operator pass'));
  assert.strictEqual(a.api.isDefaultCredentialActive(), true);
  a.store(sha256hex(DEFAULT));
  assert.strictEqual(a.api.isDefaultCredentialActive(), true);
  assert.deepStrictEqual(a.calls.createHash, [], 'the default check never hashes');
}));

test('default credential: a bcrypt hash with no bcryptjs to read it stays locked', withAuth({ bcrypt: null }, async (a) => {
  a.store(realBcrypt.hashSync('operator pass', 4));
  assert.strictEqual(a.api.isDefaultCredentialActive(), true);
  assert.strictEqual(a.api.verifyBalancePassword('operator pass'), false);
}));

test('default credential: BALANCE_PASS env as before (its own value unlocks, the shipped default locks)', async () => {
  const own = loadAuth({ env: { BALANCE_PASS: 'operator env pass' } });
  try {
    assert.strictEqual(own.api.isDefaultCredentialActive(), false);
    assert.strictEqual(own.api.verifyBalancePassword('operator env pass'), true);
    assert.strictEqual(own.api.verifyBalancePassword(DEFAULT), false);
  } finally { own.close(); }
  const shipped = loadAuth({ env: { BALANCE_PASS: DEFAULT } });
  try {
    assert.strictEqual(shipped.api.isDefaultCredentialActive(), true);
  } finally { shipped.close(); }
});

// ---------------------------------------------------------------- 4. legacy migration

test('legacy SHA256 dashboard hash: logs in once through the migration to bcrypt, then by bcrypt', withAuth({}, async (a) => {
  a.store(sha256hex('legacy pass'));
  assert.strictEqual(a.api.verifyBalancePassword('wrong pass'), false);
  assert.strictEqual(a.stored(), sha256hex('legacy pass'), 'a wrong password migrates nothing');
  assert.strictEqual(a.api.verifyBalancePassword('legacy pass'), true);
  assert.match(a.stored(), /^\$2[ab]\$/, 'replaced by bcrypt');
  assert.ok(a.logs.some((l) => /Migrated password hash from SHA256 to bcrypt/.test(l)));
  const sha = a.calls.createHash.length;
  assert.strictEqual(sha, 2, 'one legacy verify per attempt while the legacy hash is stored');
  assert.strictEqual(a.api.verifyBalancePassword('legacy pass'), true);
  assert.strictEqual(a.calls.createHash.length, sha, 'after the migration no SHA256 at all');
  assert.strictEqual(a.api.isDefaultCredentialActive(), false, 'the migrated password unlocks');
}));

test('legacy SHA256 dashboard hash without bcryptjs: login refused, the hash left for the migration', withAuth({ bcrypt: null }, async (a) => {
  a.store(sha256hex('legacy pass'));
  assert.strictEqual(a.api.verifyBalancePassword('legacy pass'), false);
  assert.strictEqual(a.stored(), sha256hex('legacy pass'));
  assert.ok(a.logs.some((l) => /bcryptjs is not installed; login refused/.test(l)));
  assert.deepStrictEqual(a.calls.createHash, [], 'not even verified without bcryptjs');
}));
