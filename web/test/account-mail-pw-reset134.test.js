'use strict';
// 1.3.4: password self-reset by email, the web side (the Account Mail section of web/ssh-proxy.js).
// The section is cut from the file by its markers and run in a vm context with no require, so it can
// reach nothing but what a test hands it: a temp game database with the schema read from the C#, a
// fake clock, a fake sender that records each mail, and (for the Resend call itself) a fake https
// module. No test reaches the network: the mail key and sender are removed from process.env first,
// and every test that sends anything uses the fake sender.
delete process.env.RESEND_API_KEY;
delete process.env.MAIL_FROM;

const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const net = require('node:net');
const nodeCrypto = require('node:crypto');
const { EventEmitter } = require('node:events');
const Database = require('better-sqlite3');
const { slice, proxySource } = require('./balance-api-harness');

const ROOT = path.join(__dirname, '..', '..');
const START = '// --- Account Mail (1.3.4) ---';
const END = '// --- End Account Mail ---';
const KEY_ENV = { RESEND_API_KEY: 'test-key-not-real-0123456789', MAIL_FROM: 'Usurper Reborn <noreply@example.com>' };
const T0 = Date.UTC(2026, 9, 17, 9, 0, 0);
const MIN = 60 * 1000;
const HOUR = 60 * MIN;
const DAY = 24 * HOUR;
const GOOD_PW = 'new-pass-ok';

const read = (...p) => fs.readFileSync(path.join(ROOT, ...p), 'utf8');
const sqlTime = (ms) => new Date(ms).toISOString().slice(0, 19).replace('T', ' ');
const sha256Hex = (s) => nodeCrypto.createHash('sha256').update(s).digest('hex');
const LANG = {};
for (const l of ['en', 'es', 'fr', 'hu', 'it']) LANG[l] = JSON.parse(read('web', 'lang', l + '.json'));

// ---------------------------------------------------------------- schema from the C#

function csharpSchema() {
  const backend = read('Scripts', 'Systems', 'SqlSaveBackend.cs');
  const mail = read('Scripts', 'Systems', 'SqlSaveBackend.AccountMail.cs');
  const table = (name) => {
    const m = backend.match(new RegExp('CREATE TABLE IF NOT EXISTS ' + name + ' \\([\\s\\S]*?\\n\\s*\\);'));
    if (!m) throw new Error('table not found in SqlSaveBackend.cs: ' + name);
    return m[0];
  };
  const ddl = mail.match(/AccountMailDdl = @"([\s\S]*?)";/);
  const cols = mail.match(/new\[\] \{ ("email TEXT"[^}]*)\}/);
  if (!ddl || !cols) throw new Error('account mail DDL not found');
  const alters = [...cols[1].matchAll(/"([^"]+)"/g)].map((m) => `ALTER TABLE players ADD COLUMN ${m[1]};`);
  return [table('players'), table('world_state'), table('admin_commands'), table('login_tokens'),
    ...alters, 'CREATE UNIQUE INDEX IF NOT EXISTS idx_players_email ON players(email) WHERE email IS NOT NULL;', ddl[1]].join('\n');
}

// ---------------------------------------------------------------- the section

function loadSection() {
  const ctx = { Buffer, setInterval: () => 0, clearInterval: () => {} };
  vm.createContext(ctx);
  return vm.runInContext(slice(proxySource(), START, END) + `
;({ createAccountMail, accountNormalizeAddress, accountMask, accountResetName, accountMailMessage, accountResendSend,
   accountClientIp, accountSqlTime, accountEscapeHtml, ACCOUNT_MIN_PASSWORD_LENGTH, ACCOUNT_MAX_PASSWORD_LENGTH,
   ACCOUNT_PER_IP_PER_HOUR, ACCOUNT_NAME_MAX, ACCOUNT_ADDRESS_MAX, ACCOUNT_MAIL_TEMPLATES, ACCOUNT_LANGS });`,
  ctx, { filename: 'ssh-proxy.js (account mail section)' });
}
const S = loadSection();

// A fake https module: every request is recorded; it answers 200 without any socket.
function fakeHttps(status) {
  const calls = [];
  return {
    calls,
    request(opts, cb) {
      const req = new EventEmitter();
      let body = '';
      req.end = (b) => {
        body += b || '';
        calls.push({ opts, body });
        const res = new EventEmitter();
        res.statusCode = status || 200;
        process.nextTick(() => { cb(res); res.emit('end'); });
      };
      req.destroy = () => {};
      return req;
    },
  };
}

// One server: a temp database file, the fake clock and the fake sender. reopen() starts a fresh
// instance on the same file, as a restart does.
function server(options = {}) {
  const dir = options.dir || fs.mkdtempSync(path.join(os.tmpdir(), 'pw-reset134-'));
  const file = path.join(dir, 'usurper_online.db');
  const db = new Database(file);
  if (!options.dir) db.exec(csharpSchema());
  const clock = options.clock || { t: T0 };
  const sent = options.sent || [];
  const logs = options.logs || [];
  const capture = (level) => (...a) => logs.push(level + ' ' + a.map(String).join(' '));
  const mail = S.createAccountMail({
    db,
    env: options.env || KEY_ENV,
    https: options.https,
    crypto: nodeCrypto,
    isIP: net.isIP,
    hashGamePassword: (p) => 'game-hash:' + sha256Hex('salt' + p),
    loadStrings: (l) => LANG[l],
    now: () => clock.t,
    log: { info: capture('info'), warn: capture('warn'), error: capture('error') },
    send: options.https ? undefined : async (msg) => { sent.push(msg); return { ok: options.sendOk !== false }; },
  });
  const s = {
    dir, file, db, clock, sent, logs, mail,
    close() { try { db.close(); } catch (e) { /* closed */ } },
    cleanup() { s.close(); fs.rmSync(dir, { recursive: true, force: true }); },
    reopen(extra = {}) { s.close(); return server(Object.assign({ dir, clock, sent, logs }, extra)); },
    tick: () => mail.tick(),
    account(name, email, verified, hash) {
      db.prepare('INSERT INTO players (username, display_name, password_hash, player_data, email, email_verified_at) VALUES (?, ?, ?, ?, ?, ?)')
        .run(name.toLowerCase(), name, hash || 'old-hash-' + name, '{}', email || null, email && verified !== false ? sqlTime(clock.t - DAY) : null);
    },
    // a request row as the game writes it
    gameRow(kind, username, address, ip, lang, at) {
      return db.prepare('INSERT INTO mail_requests (kind, username, address, ip, lang, created_at) VALUES (?, ?, ?, ?, ?, ?)')
        .run(kind, username, address, ip === undefined ? '198.51.100.1' : ip, lang || 'en', sqlTime(at === undefined ? clock.t : at)).lastInsertRowid;
    },
    rows: () => db.prepare('SELECT * FROM mail_requests ORDER BY id').all(),
    row: (id) => db.prepare('SELECT * FROM mail_requests WHERE id = ?').get(id),
    player: (name) => db.prepare('SELECT * FROM players WHERE username = ?').get(name),
    heartbeat: () => db.prepare("SELECT value, updated_at FROM world_state WHERE key = 'mail_enabled'").get(),
    async api(route, body, opts = {}) {
      const req = new EventEmitter();
      req.url = route;
      req.method = opts.method || 'POST';
      req.headers = Object.assign({ 'content-type': 'application/json', 'x-real-ip': opts.ip || '203.0.113.9' }, opts.headers || {});
      req.socket = { remoteAddress: opts.peer || '127.0.0.1' };
      req.destroy = () => {};
      const res = { headers: {}, setHeader(k, v) { this.headers[k.toLowerCase()] = v; }, writeHead(st) { this.status = st; }, end(b) { this.raw = b; } };
      const done = mail.handle(req, res);
      process.nextTick(() => { req.emit('data', Buffer.from(typeof body === 'string' ? body : JSON.stringify(body))); req.emit('end'); });
      await done;
      return { status: res.status, data: JSON.parse(res.raw), headers: res.headers, raw: res.raw };
    },
  };
  return s;
}

async function withServer(options, fn) {
  const s = server(options);
  try { await fn(s); } finally { s.cleanup(); }
}

const tokenIn = (msg) => {
  const m = msg.text.match(/\/account\/(reset|verify)#([A-Za-z0-9_-]{43})/);
  return m ? m[2] : null;
};

// A verified account and one sent reset mail; returns the token.
async function resetToken(s, name, email) {
  s.account(name, email);
  s.gameRow('reset', name.toLowerCase(), null);
  await s.tick();
  const msg = s.sent[s.sent.length - 1];
  assert.ok(msg && msg.to === email, 'a reset mail to the account address');
  return tokenIn(msg);
}

// ---------------------------------------------------------------- P2-2, O3: no network, the heartbeat

test('P2-2: the test process has no mail key or sender address', () => {
  assert.strictEqual(process.env.RESEND_API_KEY, undefined);
  assert.strictEqual(process.env.MAIL_FROM, undefined);
});

test('mail off: with no key the sender makes no request, rows wait, and world_state says 0', async () => {
  const https = fakeHttps();
  await withServer({ env: {}, https }, async (s) => {
    s.account('Keeper', 'keeper@example.com');
    const id = s.gameRow('reset', 'keeper', null);
    await s.tick();
    await s.tick();
    assert.strictEqual(https.calls.length, 0, 'no request at all while the key is unset');
    assert.strictEqual(s.heartbeat().value, '0');
    assert.match(s.heartbeat().updated_at, /^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d$/);
    assert.strictEqual(s.row(id).status, 'pending', 'the row waits for the key');
    // past the hour a waiting request is dropped, never sent late
    s.clock.t += HOUR + MIN;
    await s.tick();
    assert.strictEqual(s.row(id).status, 'dropped');
    assert.strictEqual(https.calls.length, 0);
  });
});

test('mail off: a key without MAIL_FROM is still off', async () => {
  const https = fakeHttps();
  await withServer({ env: { RESEND_API_KEY: KEY_ENV.RESEND_API_KEY }, https }, async (s) => {
    s.account('Keeper', 'keeper@example.com');
    s.gameRow('reset', 'keeper', null);
    await s.tick();
    assert.strictEqual(https.calls.length, 0);
    assert.strictEqual(s.heartbeat().value, '0');
  });
});

test('mail on: world_state says 1 and the Resend call is one HTTPS POST (fake https, no socket)', async () => {
  const https = fakeHttps(200);
  await withServer({ https }, async (s) => {
    s.account('Keeper', 'keeper@example.com');
    const id = s.gameRow('reset', 'keeper', null);
    await s.tick();
    assert.strictEqual(s.heartbeat().value, '1');
    assert.strictEqual(https.calls.length, 1);
    const { opts, body } = https.calls[0];
    assert.strictEqual(opts.hostname, 'api.resend.com');
    assert.strictEqual(opts.port, 443);
    assert.strictEqual(opts.path, '/emails');
    assert.strictEqual(opts.method, 'POST');
    assert.strictEqual(opts.headers.Authorization, 'Bearer ' + KEY_ENV.RESEND_API_KEY);
    assert.strictEqual(opts.headers['Content-Type'], 'application/json');
    const sent = JSON.parse(body);
    assert.deepStrictEqual(sent.to, ['keeper@example.com']);
    assert.strictEqual(sent.from, KEY_ENV.MAIL_FROM);
    assert.ok(sent.subject && sent.text && sent.html);
    assert.strictEqual(s.row(id).status, 'done');
  });
});

test('a refused send (Resend answers 4xx) drops the row', async () => {
  const https = fakeHttps(422);
  await withServer({ https }, async (s) => {
    s.account('Keeper', 'keeper@example.com');
    const id = s.gameRow('reset', 'keeper', null);
    await s.tick();
    assert.strictEqual(s.row(id).status, 'dropped');
    assert.strictEqual(s.row(id).address, null);
  });
});

// ---------------------------------------------------------------- reset requests

test('reset: a confirmed address gets one mail; the link is the fragment form; the token is hashed at rest', async () => {
  await withServer({}, async (s) => {
    const token = await resetToken(s, 'Keeper', 'keeper@example.com');
    assert.ok(token);
    assert.strictEqual(s.sent.length, 1);
    assert.ok(s.sent[0].text.includes('https://usurper-reborn.net/account/reset#' + token));
    const row = s.db.prepare("SELECT * FROM account_tokens WHERE kind = 'reset'").get();
    assert.strictEqual(row.token_hash, sha256Hex(token));
    assert.ok(!JSON.stringify(s.db.prepare('SELECT * FROM account_tokens').all()).includes(token), 'no raw token stored');
    assert.strictEqual(row.expires_at, sqlTime(T0 + 30 * MIN), '30 minutes');
    assert.strictEqual(row.username, 'keeper');
  });
});

test('reset: an unverified address, no address and an unknown name get no mail', async () => {
  await withServer({}, async (s) => {
    s.account('Pending', 'pending@example.com', false);
    s.account('Bare', null);
    const ids = [s.gameRow('reset', 'pending', null), s.gameRow('reset', 'bare', null), s.gameRow('reset', 'nobody here', null),
      s.gameRow('reset', 'pending@example.com', null)];
    await s.tick();
    assert.strictEqual(s.sent.length, 0, 'nothing sent');
    for (const id of ids) assert.strictEqual(s.row(id).status, 'dropped');
    assert.strictEqual(s.db.prepare('SELECT COUNT(*) AS n FROM account_tokens').get().n, 0);
  });
});

test('reset: the typed text is looked up as a name, then as a confirmed address', async () => {
  await withServer({}, async (s) => {
    s.account('Keeper', 'keeper@example.com');
    s.gameRow('reset', 'keeper@example.com', null);
    await s.tick();
    assert.strictEqual(s.sent.length, 1);
    assert.strictEqual(s.sent[0].to, 'keeper@example.com');
  });
});

// ---------------------------------------------------------------- 2.5 the website form

test('form: the same reply for a known and an unknown name and address, and the same row', async () => {
  await withServer({}, async (s) => {
    s.account('Keeper', 'keeper@example.com');
    const inputs = ['Keeper', 'nobody here', 'keeper@example.com', 'nobody@example.net'];
    const replies = [];
    for (const [i, name] of inputs.entries()) replies.push(await s.api('/api/account/forgot', { name, lang: 'fr' }, { ip: '203.0.113.' + (10 + i) }));
    for (const r of replies) {
      assert.strictEqual(r.status, replies[0].status);
      assert.strictEqual(r.raw, replies[0].raw);
      assert.deepStrictEqual(r.headers, replies[0].headers);
    }
    assert.deepStrictEqual(replies[0].data, { ok: true });
    const rows = s.rows();
    assert.strictEqual(rows.length, 4, 'one row for every input');
    rows.forEach((r, i) => {
      assert.deepStrictEqual({ kind: r.kind, username: r.username, address: r.address, ip: r.ip, lang: r.lang, status: r.status },
        { kind: 'reset', username: inputs[i].toLowerCase(), address: null, ip: '203.0.113.' + (10 + i), lang: 'fr', status: 'pending' });
    });
  });
});

test('form: an address is kept whole (S1 rule, up to 254), so a 70 character confirmed address gets the mail', async () => {
  await withServer({}, async (s) => {
    const address = 'a'.repeat(58) + '@example.com';
    assert.strictEqual(address.length, 70);
    s.account('Longmail', address);
    const r = await s.api('/api/account/forgot', { name: '  ' + address.toUpperCase() + ' ' });
    assert.deepStrictEqual([r.status, r.data], [200, { ok: true }]);
    assert.strictEqual(s.rows()[0].username, address, 'kept whole and normalized');
    await s.tick();
    assert.strictEqual(s.sent.length, 1);
    assert.strictEqual(s.sent[0].to, address);
    assert.ok(tokenIn(s.sent[0]));
    // a name without '@' is still cut at 64
    await s.api('/api/account/forgot', { name: 'n'.repeat(80) });
    assert.strictEqual(s.rows()[1].username, 'n'.repeat(64));
  });
});

test('form: per IP 5 an hour, counted with the game rows, the same reply past it', async () => {
  await withServer({}, async (s) => {
    s.gameRow('verify', 'someone', 'someone@example.com', '203.0.113.20');
    for (let i = 0; i < 5; i++) {
      const r = await s.api('/api/account/forgot', { name: 'name ' + i }, { ip: '203.0.113.20' });
      assert.deepStrictEqual(r.data, { ok: true });
    }
    assert.strictEqual(s.rows().length, 5, 'the game row and four from the form');
    s.clock.t += HOUR + MIN;
    await s.api('/api/account/forgot', { name: 'later' }, { ip: '203.0.113.20' });
    assert.strictEqual(s.rows().length, 6, 'the hour has passed');
  });
});

test('form: X-Real-IP counts only from the local proxy; a direct peer is its own bucket', async () => {
  await withServer({}, async (s) => {
    await s.api('/api/account/forgot', { name: 'a' }, { peer: '192.0.2.50', ip: '203.0.113.1' });
    assert.strictEqual(s.rows()[0].ip, '192.0.2.50');
  });
});

// ---------------------------------------------------------------- 2.6, 2.7 the reset API

test('reset API: a used token is refused', async () => {
  await withServer({}, async (s) => {
    const token = await resetToken(s, 'Keeper', 'keeper@example.com');
    const first = await s.api('/api/account/reset', { token, password: GOOD_PW });
    assert.strictEqual(first.status, 200, first.raw);
    const again = await s.api('/api/account/reset', { token, password: 'another-pass-1' });
    assert.strictEqual(again.status, 400);
    assert.deepStrictEqual(again.data, { error: 'invalid_link' });
    assert.strictEqual(s.player('keeper').password_hash, 'game-hash:' + sha256Hex('salt' + GOOD_PW));
  });
});

test('reset API: a token 31 minutes old is refused, at 29 minutes it works', async () => {
  await withServer({}, async (s) => {
    const token = await resetToken(s, 'Keeper', 'keeper@example.com');
    s.clock.t = T0 + 31 * MIN;
    const late = await s.api('/api/account/reset', { token, password: GOOD_PW });
    assert.deepStrictEqual([late.status, late.data], [400, { error: 'invalid_link' }]);
    s.clock.t = T0 + 29 * MIN;
    const ok = await s.api('/api/account/reset', { token, password: GOOD_PW });
    assert.strictEqual(ok.status, 200);
  });
});

test('reset API: a verify token is refused on the reset endpoint, and a reset token on the verify endpoint', async () => {
  await withServer({}, async (s) => {
    s.account('Changer', 'old@example.com');
    s.gameRow('verify', 'changer', 'new@example.com');
    await s.tick();
    const verifyToken = tokenIn(s.sent[0]);
    const r = await s.api('/api/account/reset', { token: verifyToken, password: GOOD_PW });
    assert.deepStrictEqual([r.status, r.data], [400, { error: 'invalid_link' }]);
    assert.strictEqual(s.player('changer').password_hash, 'old-hash-Changer');
    const resetTok = await resetToken(s, 'Keeper', 'keeper@example.com');
    const v = await s.api('/api/account/verify', { token: resetTok });
    assert.deepStrictEqual([v.status, v.data], [400, { error: 'invalid_link' }]);
  });
});

test('S6: every bad token gets one generic error (garbage, unknown, used, expired, wrong kind)', async () => {
  await withServer({}, async (s) => {
    const used = await resetToken(s, 'Used', 'used@example.com');
    await s.api('/api/account/reset', { token: used, password: GOOD_PW });
    const expired = await resetToken(s, 'Late', 'late@example.com');
    s.account('Verifier', null);
    s.gameRow('verify', 'verifier', 'verifier@example.com');
    await s.tick();
    const wrongKind = tokenIn(s.sent[s.sent.length - 1]);
    s.clock.t = T0 + 40 * MIN;
    const cases = { garbage: 'not a token', short: 'abc', unknown: nodeCrypto.randomBytes(32).toString('base64url'), used, expired, wrongKind, none: undefined, number: 12345 };
    const out = {};
    for (const [k, token] of Object.entries(cases)) {
      const r = await s.api('/api/account/reset', { token, password: GOOD_PW }, { headers: { cookie: 'session=whatever' } });
      out[k] = [r.status, r.raw];
    }
    for (const [k, v] of Object.entries(out)) assert.deepStrictEqual(v, [400, '{"error":"invalid_link"}'], k);
  });
});

test('reset API: under 8 characters and a colon are refused, 8 characters work', async () => {
  await withServer({}, async (s) => {
    const token = await resetToken(s, 'Keeper', 'keeper@example.com');
    const short = await s.api('/api/account/reset', { token, password: '1234567' });
    assert.deepStrictEqual([short.status, short.data], [400, { error: 'password_short' }]);
    const colon = await s.api('/api/account/reset', { token, password: 'has:colon-long' });
    assert.deepStrictEqual([colon.status, colon.data], [400, { error: 'password_colon' }]);
    assert.strictEqual(s.player('keeper').password_hash, 'old-hash-Keeper', 'nothing changed');
    const ok = await s.api('/api/account/reset', { token, password: '12345678' });
    assert.strictEqual(ok.status, 200, ok.raw);
  });
});

test('G4: after a reset every login_tokens row of the account is gone and a kick row is queued', async () => {
  await withServer({}, async (s) => {
    const token = await resetToken(s, 'Keeper', 'keeper@example.com');
    const ins = s.db.prepare("INSERT INTO login_tokens (selector, verifier_hash, username, pw_stamp, created_at, expires_at, client_label) VALUES (?, 'v', ?, 's', 'c', 'e', 'l')");
    ins.run('k1', 'keeper'); ins.run('k2', 'KEEPER'); ins.run('o1', 'other');
    const r = await s.api('/api/account/reset', { token, password: GOOD_PW });
    assert.strictEqual(r.status, 200);
    assert.deepStrictEqual(s.db.prepare('SELECT selector FROM login_tokens').all().map((x) => x.selector), ['o1']);
    const kicks = s.db.prepare('SELECT command, target_username, args, status FROM admin_commands').all();
    assert.deepStrictEqual(kicks, [{ command: 'kick', target_username: 'keeper', args: null, status: 'pending' }]);
    const log = s.db.prepare('SELECT username, event, ip FROM account_security_log').all();
    assert.deepStrictEqual(log, [{ username: 'keeper', event: 'password_reset', ip: '203.0.113.9' }]);
    // the "password changed" mail goes out on the next loop
    await s.tick();
    const changed = s.sent[s.sent.length - 1];
    assert.strictEqual(changed.to, 'keeper@example.com');
    assert.strictEqual(changed.subject, LANG.en['mail.changed.subject']);
  });
});

test('reset: a password change after the mail voids its token, and a newer token voids the older', async () => {
  await withServer({}, async (s) => {
    const first = await resetToken(s, 'Keeper', 'keeper@example.com');
    s.gameRow('reset', 'keeper', null);
    await s.tick();
    const second = tokenIn(s.sent[1]);
    const r1 = await s.api('/api/account/reset', { token: first, password: GOOD_PW });
    assert.deepStrictEqual([r1.status, r1.data], [400, { error: 'invalid_link' }], 'older token void');
    // the player changes the password in the game (P), which writes only password_hash
    s.db.prepare("UPDATE players SET password_hash = 'changed-in-game' WHERE username = 'keeper'").run();
    const r2 = await s.api('/api/account/reset', { token: second, password: GOOD_PW });
    assert.deepStrictEqual([r2.status, r2.data], [400, { error: 'invalid_link' }], 'void after a password change');
    assert.strictEqual(s.player('keeper').password_hash, 'changed-in-game');
  });
});

// ---------------------------------------------------------------- S4 limits

test('S4: per account 3 an hour, held across a restart (fresh instance, same database)', async () => {
  let s = server();
  try {
    s.account('Keeper', 'keeper@example.com');
    for (let i = 0; i < 3; i++) s.gameRow('reset', 'keeper', null, '198.51.100.' + i);
    await s.tick();
    assert.strictEqual(s.sent.length, 3);
    s = s.reopen();
    const id = s.gameRow('reset', 'keeper', null, '198.51.100.9');
    await s.tick();
    assert.strictEqual(s.sent.length, 3, 'still over the limit after the restart');
    assert.strictEqual(s.row(id).status, 'dropped');
    s.clock.t += HOUR + MIN;
    s.gameRow('reset', 'keeper', null, '198.51.100.10');
    await s.tick();
    assert.strictEqual(s.sent.length, 4, 'the hour has passed');
  } finally { s.cleanup(); }
});

test('S4: per address 3 an hour across accounts (verify requests to one address)', async () => {
  await withServer({}, async (s) => {
    for (let i = 0; i < 4; i++) { s.account('Asker' + i, null); s.gameRow('verify', 'asker' + i, 'target@example.com', '198.51.100.' + i); }
    await s.tick();
    assert.strictEqual(s.sent.filter((m) => m.to === 'target@example.com').length, 3);
    assert.strictEqual(s.rows()[3].status, 'dropped');
  });
});

test('S4: the security notices count but are never held back', async () => {
  await withServer({}, async (s) => {
    s.account('Keeper', 'keeper@example.com');
    for (let i = 0; i < 3; i++) s.gameRow('reset', 'keeper', null, '198.51.100.' + i);
    s.gameRow('remove_notice', 'keeper', 'keeper@example.com', '198.51.100.7');
    await s.tick();
    assert.strictEqual(s.sent.length, 4);
    assert.strictEqual(s.sent[3].subject, LANG.en['mail.remove.subject']);
  });
});

// ---------------------------------------------------------------- S7 the claim

test('S7: one atomic claim; a second sender gets no change and the row is sent once', async () => {
  let a = server();
  try {
    a.account('Keeper', 'keeper@example.com');
    const id = a.gameRow('reset', 'keeper', null);
    const b = server({ dir: a.dir, clock: a.clock, sent: a.sent, logs: a.logs });
    try {
      assert.strictEqual(a.mail.claim(id), true);
      assert.strictEqual(b.mail.claim(id), false, 'the second claim changes nothing');
      assert.strictEqual(a.row(id).status, 'sending');
      // two senders on one database, one loop each at the same time
      const id2 = a.gameRow('reset', 'keeper', null);
      await Promise.all([a.tick(), b.tick()]);
      assert.strictEqual(a.sent.filter((m) => m.to === 'keeper@example.com').length, 1, 'sent once');
      assert.strictEqual(a.row(id2).status, 'done');
    } finally { b.close(); }
  } finally { a.cleanup(); }
});

test('a row left sending by a stopped sender is dropped after 10 minutes, never sent', async () => {
  await withServer({}, async (s) => {
    s.account('Keeper', 'keeper@example.com');
    const id = s.gameRow('reset', 'keeper', null);
    assert.strictEqual(s.mail.claim(id), true);
    await s.tick();
    assert.strictEqual(s.row(id).status, 'sending');
    s.clock.t += 11 * MIN;
    await s.tick();
    assert.strictEqual(s.row(id).status, 'dropped');
    assert.strictEqual(s.sent.length, 0);
  });
});

// ---------------------------------------------------------------- S3 retention

test('S3: a processed row loses its address at once and its IP after the hour; rows and tokens go after 7 days', async () => {
  await withServer({}, async (s) => {
    s.account('Leaver', null);
    const id = s.gameRow('remove_notice', 'leaver', 'leaver@example.com', '198.51.100.4');
    const token = await resetToken(s, 'Keeper', 'keeper@example.com');
    assert.strictEqual(s.row(id).status, 'done');
    assert.strictEqual(s.row(id).address, null, 'address gone at once');
    assert.strictEqual(s.row(id).ip, '198.51.100.4', 'the IP stays for the hour the game cap counts');
    s.clock.t += HOUR + MIN;
    await s.tick();
    assert.strictEqual(s.row(id).ip, '', 'IP gone after the hour');
    await s.api('/api/account/reset', { token, password: GOOD_PW });
    assert.strictEqual(s.db.prepare('SELECT COUNT(*) AS n FROM account_tokens').get().n, 1);
    s.clock.t += 7 * DAY;
    await s.tick();
    assert.strictEqual(s.row(id), undefined, 'processed row deleted after 7 days');
    assert.strictEqual(s.db.prepare('SELECT COUNT(*) AS n FROM account_tokens').get().n, 0, 'token deleted 7 days after use');
    assert.strictEqual(s.db.prepare('SELECT COUNT(*) AS n FROM account_token_stamps').get().n, 0);
    assert.strictEqual(s.db.prepare('SELECT COUNT(*) AS n FROM mail_limits').get().n, 0);
    // the security log holds no address
    for (const r of s.db.prepare('SELECT * FROM account_security_log').all()) assert.ok(!JSON.stringify(r).includes('@'), JSON.stringify(r));
  });
});

test('S3: an unused token is deleted 7 days after it expired', async () => {
  await withServer({}, async (s) => {
    await resetToken(s, 'Keeper', 'keeper@example.com');
    s.clock.t = T0 + 30 * MIN + 6 * DAY;
    await s.tick();
    assert.strictEqual(s.db.prepare('SELECT COUNT(*) AS n FROM account_tokens').get().n, 1);
    s.clock.t = T0 + 30 * MIN + 7 * DAY + MIN;
    await s.tick();
    assert.strictEqual(s.db.prepare('SELECT COUNT(*) AS n FROM account_tokens').get().n, 0);
  });
});

// ---------------------------------------------------------------- S1, S2

test('S1: the address rule is AccountEmail.Normalize (the PW-1 cases) and the mask is AccountEmail.Mask', () => {
  assert.strictEqual(S.accountNormalizeAddress('  Player.One@Example.COM \t'), 'player.one@example.com');
  assert.strictEqual(S.accountNormalizeAddress('a@b.invalid'), 'a@b.invalid');
  for (const bad of ['', '   ', 'no-at.example.com', 'two@@example.com', 'a@b@example.com', '@example.com', 'player@',
    'pla\nyer@example.com', 'player@example.com\r\nBcc: x@example.net', 'pla\ryer@example.com', 'pla\u0001yer@example.com',
    'pla\u007fyer@example.com', 'pla yer@example.com', null, undefined, 42, 'x'.repeat(250) + '@e.invalid']) {
    assert.strictEqual(S.accountNormalizeAddress(bad), null, JSON.stringify(bad));
  }
  assert.strictEqual(S.accountNormalizeAddress('x'.repeat(244) + '@e.invalid').length, 254);
  assert.strictEqual(S.accountMask('bob@example.invalid'), 'b***@e***.invalid');
  assert.strictEqual(S.accountMask('a@d.example.net'), 'a***@d***.net');
  assert.strictEqual(S.accountMask('x@host'), 'x***@h***');
  assert.strictEqual(S.accountMask('broken'), '***');
  assert.strictEqual(S.accountResetName('  Some\u0007One  '), 'someone');
  assert.strictEqual(S.accountResetName('x'.repeat(80)).length, 64);
});

test('S1: a verify row with an address the rule refuses gets no mail', async () => {
  await withServer({}, async (s) => {
    s.account('Asker', null);
    const id = s.gameRow('verify', 'asker', 'two@@example.com');
    await s.tick();
    assert.strictEqual(s.sent.length, 0);
    assert.strictEqual(s.row(id).status, 'dropped');
  });
});

test('S2: a name and an address in a mail are escaped in the HTML', async () => {
  await withServer({}, async (s) => {
    const name = 'Bob<b>&"\'x';
    s.account(name, null);
    s.gameRow('verify', name.toLowerCase(), '<script>alert(1)</script>@example.com');
    await s.tick();
    assert.strictEqual(s.sent.length, 1);
    const m = s.sent[0];
    assert.strictEqual(m.to, '<script>alert(1)</script>@example.com');
    assert.ok(!m.html.includes('<b>') && !m.html.includes('<script'), m.html);
    assert.ok(m.html.includes('bob&lt;b&gt;&amp;&quot;&#39;x'), m.html);
    assert.ok(m.text.includes("bob<b>&\"'x"), 'plain text keeps the name as it is');
    const direct = S.accountMailMessage(LANG.en, LANG.en, 'remove_notice', { name: '<img src=x onerror=alert(1)>' });
    assert.ok(!direct.html.includes('<img'), direct.html);
    assert.ok(direct.html.includes('&lt;img src=x onerror=alert(1)&gt;'));
  });
});

// ---------------------------------------------------------------- 2.4 verify, D-e, G1

test('verify: the link sets the address; the old address gets a change notice', async () => {
  await withServer({}, async (s) => {
    s.account('Changer', 'old@example.com');
    s.gameRow('verify', 'changer', 'new@example.com', '198.51.100.3', 'hu');
    await s.tick();
    assert.strictEqual(s.sent[0].to, 'new@example.com');
    assert.strictEqual(s.sent[0].subject, LANG.hu['mail.verify.subject']);
    const token = tokenIn(s.sent[0]);
    const row = s.db.prepare("SELECT * FROM account_tokens WHERE kind = 'verify'").get();
    assert.strictEqual(row.expires_at, sqlTime(T0 + 24 * HOUR), '24 hours');
    s.clock.t += 5 * MIN;
    const r = await s.api('/api/account/verify', { token, lang: 'hu' });
    assert.deepStrictEqual([r.status, r.data], [200, { ok: true }]);
    const p = s.player('changer');
    assert.strictEqual(p.email, 'new@example.com');
    assert.strictEqual(p.email_verified_at, sqlTime(s.clock.t));
    await s.tick();
    assert.strictEqual(s.sent[1].to, 'old@example.com');
    assert.strictEqual(s.sent[1].subject, LANG.hu['mail.change.subject']);
    const again = await s.api('/api/account/verify', { token });
    assert.deepStrictEqual([again.status, again.data], [400, { error: 'invalid_link' }], 'single use');
  });
});

test('D-e: a verify token is void once the address was removed or changed after its request', async () => {
  await withServer({}, async (s) => {
    s.account('Changer', 'old@example.com');
    s.gameRow('verify', 'changer', 'new@example.com');
    await s.tick();
    const token = tokenIn(s.sent[0]);
    // the game's remove (E, then remove): clears the columns and logs email_removed
    s.clock.t += MIN;
    s.db.prepare("UPDATE players SET email = NULL, email_verified_at = NULL WHERE username = 'changer'").run();
    s.db.prepare("INSERT INTO account_security_log (username, event, ip, created_at) VALUES ('changer', 'email_removed', '', ?)").run(sqlTime(s.clock.t));
    const r = await s.api('/api/account/verify', { token });
    assert.deepStrictEqual([r.status, r.data], [400, { error: 'invalid_link' }]);
    assert.strictEqual(s.player('changer').email, null, 'not set');

    s.account('Other', 'first@example.com');
    s.gameRow('verify', 'other', 'second@example.com');
    await s.tick();
    const t2 = tokenIn(s.sent[s.sent.length - 1]);
    s.clock.t += MIN;
    s.db.prepare("UPDATE players SET email = 'third@example.com', email_verified_at = ? WHERE username = 'other'").run(sqlTime(s.clock.t));
    const r2 = await s.api('/api/account/verify', { token: t2 });
    assert.deepStrictEqual([r2.status, r2.data], [400, { error: 'invalid_link' }]);
    assert.strictEqual(s.player('other').email, 'third@example.com');
  });
});

test('G1: an address on another account gets an already-linked notice and is not moved', async () => {
  await withServer({}, async (s) => {
    s.account('Holder', 'taken@example.com');
    s.account('Asker', null);
    const id = s.gameRow('verify', 'asker', 'Taken@Example.com');
    await s.tick();
    assert.strictEqual(s.sent.length, 1);
    assert.strictEqual(s.sent[0].to, 'taken@example.com');
    assert.strictEqual(s.sent[0].subject, LANG.en['mail.linked.subject']);
    assert.strictEqual(tokenIn(s.sent[0]), null, 'no link in the notice');
    assert.ok(!s.sent[0].text.toLowerCase().includes('holder'), 'the other account is not named');
    assert.strictEqual(s.row(id).status, 'done');
    assert.strictEqual(s.player('asker').email, null);
    assert.strictEqual(s.player('holder').email, 'taken@example.com');
    assert.strictEqual(s.db.prepare('SELECT COUNT(*) AS n FROM account_tokens').get().n, 0);
  });
});

test('G1: an address taken by another account after the mail: the same reply, no change, a notice', async () => {
  await withServer({}, async (s) => {
    s.account('Asker', null);
    s.gameRow('verify', 'asker', 'race@example.com');
    await s.tick();
    const token = tokenIn(s.sent[0]);
    s.account('Faster', 'race@example.com');
    const r = await s.api('/api/account/verify', { token });
    assert.deepStrictEqual([r.status, r.data], [200, { ok: true }], 'the same reply as a success');
    assert.strictEqual(s.player('asker').email, null);
    assert.strictEqual(s.player('faster').email, 'race@example.com');
    await s.tick();
    assert.strictEqual(s.sent[1].to, 'race@example.com');
    assert.strictEqual(s.sent[1].subject, LANG.en['mail.linked.subject']);
  });
});

test('S5: a remove_notice row mails the removed address', async () => {
  await withServer({}, async (s) => {
    s.account('Leaver', null);
    s.gameRow('remove_notice', 'leaver', 'leaver@example.com', '198.51.100.4', 'it');
    await s.tick();
    assert.strictEqual(s.sent.length, 1);
    assert.strictEqual(s.sent[0].to, 'leaver@example.com');
    assert.strictEqual(s.sent[0].subject, LANG.it['mail.remove.subject']);
  });
});

// ---------------------------------------------------------------- logs, headers, routes

test('no raw token, address, key or password in any log line (capture)', async () => {
  await withServer({}, async (s) => {
    const token = await resetToken(s, 'Keeper', 'keeper@example.com');
    await s.api('/api/account/reset', { token, password: GOOD_PW });
    await s.api('/api/account/reset', { token, password: GOOD_PW });
    s.account('Changer', 'old@example.com');
    s.gameRow('verify', 'changer', 'new@example.com');
    await s.api('/api/account/forgot', { name: 'typed@example.net' });
    await s.tick();
    const vt = tokenIn(s.sent[s.sent.length - 1]);
    await s.api('/api/account/verify', { token: vt });
    await s.tick();
    s.close();
    await s.tick(); // a failing loop logs too
    assert.ok(s.logs.length >= 4, 'the capture saw the log lines');
    const all = s.logs.join('\n');
    for (const secret of [token, vt, sha256Hex(token), 'keeper@example.com', 'old@example.com', 'new@example.com', 'typed@example.net',
      KEY_ENV.RESEND_API_KEY, GOOD_PW, '@']) {
      assert.ok(!all.includes(secret), 'a log line holds ' + secret);
    }
  });
});

test('every account API reply carries no-store and no-referrer, and no CORS header', async () => {
  await withServer({}, async (s) => {
    const replies = [
      await s.api('/api/account/forgot', { name: 'x' }),
      await s.api('/api/account/reset', { token: 'bad', password: GOOD_PW }),
      await s.api('/api/account/verify', { token: 'bad' }),
      await s.api('/api/account/nothing', {}),
      await s.api('/api/account/forgot', {}, { method: 'GET' }),
      await s.api('/api/account/forgot', 'name=x', { headers: { 'content-type': 'application/x-www-form-urlencoded' } }),
      await s.api('/api/account/forgot', 'not json'),
    ];
    assert.deepStrictEqual(replies.map((r) => r.status), [200, 400, 400, 404, 405, 415, 400]);
    for (const r of replies) {
      assert.strictEqual(r.headers['cache-control'], 'no-store');
      assert.strictEqual(r.headers['referrer-policy'], 'no-referrer');
      assert.strictEqual(r.headers['access-control-allow-origin'], undefined);
    }
  });
});

test('S6: the account section reads no cookie and no Authorization header', () => {
  const src = slice(proxySource(), START, END).split('\n').filter((l) => !/^\s*\/\//.test(l)).map((l) => l.replace(/\s\/\/ .*$/, '')).join('\n');
  assert.doesNotMatch(src, /cookie/i);
  assert.doesNotMatch(src, /headers(\[['"]|\.)authorization/i);
});

test('the server routes /api/account/ to the section before every other API route', () => {
  const src = proxySource();
  const handler = slice(src, 'function handleHttpRequest(req, res) {', '// Admin dashboard routes');
  assert.match(handler, /req\.url\.startsWith\('\/api\/account\/'\)\) \{\n\s+accountMail\.handle\(req, res\)/);
  assert.match(src, /const accountMail = createAccountMail\(\{\n\s+db: dbWrite, env: process\.env, https, crypto, isIP: net\.isIP, hashGamePassword,/);
  assert.match(slice(src, 'function gracefulShutdown() {', '\n}\n'), /accountMail\.stop\(\);/);
});

// ---------------------------------------------------------------- constants against the C#

test('the Node constants equal the game values (GameConfig and the account mail code)', () => {
  const config = read('Scripts', 'Core', 'GameConfig.cs');
  const num = (re, text) => { const m = text.match(re); assert.ok(m, String(re)); return Number(m[1]); };
  assert.strictEqual(S.ACCOUNT_MIN_PASSWORD_LENGTH, num(/public const int MinPasswordLength = (\d+);/, config));
  assert.strictEqual(S.ACCOUNT_PER_IP_PER_HOUR, num(/public const int MailRequestsPerIpPerHour = (\d+);/, config));
  assert.strictEqual(S.ACCOUNT_NAME_MAX, num(/internal const int ResetNameMaxLength = (\d+);/, read('Scripts', 'Systems', 'SqlSaveBackend.AccountMail.cs')));
  assert.strictEqual(S.ACCOUNT_ADDRESS_MAX, num(/public const int MaxLength = (\d+);/, read('Scripts', 'Systems', 'AccountMail.cs')));
  const kinds = read('Scripts', 'Systems', 'AccountMail.cs');
  for (const k of ['reset', 'verify', 'remove_notice']) assert.ok(kinds.includes(`= "${k}";`), k);
  assert.ok(read('Scripts', 'Systems', 'AccountMail.cs').includes('public const string Key = "mail_enabled";'));
});

// ---------------------------------------------------------------- templates

test('templates: six mails in five languages, the same placeholders as English, no remote content', () => {
  const kinds = Object.values(S.ACCOUNT_MAIL_TEMPLATES);
  assert.deepStrictEqual(kinds.slice().sort(), ['change', 'changed', 'linked', 'remove', 'reset', 'verify']);
  const holes = (s) => [...s.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort().join(',');
  for (const l of S.ACCOUNT_LANGS) {
    for (const k of kinds) {
      for (const part of ['subject', 'body']) {
        const key = `mail.${k}.${part}`;
        const v = LANG[l][key];
        assert.ok(typeof v === 'string' && v.length > 10, `${l} ${key}`);
        assert.strictEqual(holes(v), holes(LANG.en[key]), `${l} ${key} placeholders`);
        if (l !== 'en') assert.notStrictEqual(v, LANG.en[key], `${l} ${key} is translated`);
        assert.doesNotMatch(v, /<|https?:\/\/(?!usurper-reborn\.net\/)/, `${l} ${key}`);
      }
    }
    assert.ok(LANG[l]['mail.footer'].includes('https://usurper-reborn.net'));
  }
  const m = S.accountMailMessage(LANG.fr, LANG.en, 'reset_request', { name: 'Keeper', link: 'https://usurper-reborn.net/account/reset#abc', minutes: 30 });
  assert.match(m.html, /^<!DOCTYPE html><html><body[^>]*>/);
  assert.doesNotMatch(m.html, /<img|<script|<link|src=|https?:\/\/(?!usurper-reborn\.net)/);
  assert.ok(m.html.includes('<a href="https://usurper-reborn.net/account/reset#abc">'));
  assert.ok(m.text.includes('30 minutes') && m.subject === LANG.fr['mail.reset.subject']);
});

// ---------------------------------------------------------------- G3

test('G3: the addresses in these tests and in the account mail section use reserved domains only', () => {
  const files = fs.readdirSync(__dirname).filter((f) => /pw-reset134/.test(f)).map((f) => [f, fs.readFileSync(path.join(__dirname, f), 'utf8')]);
  files.push(['ssh-proxy.js (account mail section)', slice(proxySource(), START, END)]);
  const found = [];
  for (const [f, text] of files) {
    for (const m of text.matchAll(/[A-Za-z0-9._%+-]+@([A-Za-z0-9.-]+\.[A-Za-z]{2,})/g)) {
      const d = m[1].toLowerCase();
      const reserved = d === 'example.com' || d.endsWith('.example.com') || d === 'example.net' || d.endsWith('.example.net') || d.endsWith('.invalid');
      if (!reserved) found.push(f + ': ' + m[0]);
    }
  }
  assert.deepStrictEqual(found, []);
});
