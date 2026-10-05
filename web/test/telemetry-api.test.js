'use strict';
// 1.2.7 telemetry endpoint (DESIGN.md section 4; T2 table rows 1 to 21 and A to F). The section
// of ssh-proxy.js runs in a vm with no require, http or net: no server starts and no socket
// opens. One test per row, named by its row.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');
const Database = require('better-sqlite3');
const H = require('./telemetry-api-harness');
const { T0, DAY_MS, HOUR_MS, ID, MARKER_ADDRESS, send, bodyOf, batch, goodRow } = H;

const DAY0 = Math.floor(T0 / DAY_MS);
const OK = '{"ok":true}';
const STOP = '{"stop":true}';

// A fresh folder (with the remote file unless remote: false) and an endpoint over it.
async function withEndpoint(fn, options = {}) {
  const dir = H.makeDir(options);
  const game = options.withGame ? H.makeGame(dir.dir) : null;
  const t = H.makeEndpoint(dir, Object.assign({}, options, { game }));
  try {
    return await fn(t, dir, game);
  } finally {
    t.ep.close();
    if (game) game.close();
    dir.cleanup();
  }
}

const post = (t, body, extra = {}) => send(t, Object.assign({ body }, extra));

// ---------------------------------------------------------------- row 1

test('row 1: one route, POST only; other paths under /api/telemetry 404; Content-Type must be application/json', async () => {
  await withEndpoint(async (t, dir) => {
    for (const method of ['GET', 'PUT', 'DELETE', 'PATCH', 'HEAD', 'OPTIONS']) {
      const r = await send(t, { method, body: bodyOf() });
      assert.strictEqual(r.status, 405, method);
      assert.strictEqual(r.body, '{"error":"method"}', method);
    }
    for (const url of ['/api/telemetry', '/api/telemetry/', '/api/telemetry/v1', '/api/telemetry/v1/combat/',
      '/api/telemetry/v2/combat', '/api/telemetry/v1/combat?x=1', '/api/telemetry/v1/COMBAT']) {
      const r = await send(t, { url, body: bodyOf() });
      assert.strictEqual(r.status, 404, url);
      assert.strictEqual(r.body, '{"error":"not_found"}', url);
    }
    for (const ct of [null, 'text/plain', 'application/x-www-form-urlencoded', 'multipart/form-data', 'application/jsonp',
      'application/json; charset=latin1', 'application/json; charset=utf-8; x=1']) {
      const r = await post(t, bodyOf(), { headers: { 'content-type': ct } });
      assert.strictEqual(r.status, 415, String(ct));
      assert.strictEqual(r.body, '{"error":"media_type"}', String(ct));
    }
    assert.strictEqual(H.remoteCount(dir.dbPath), 0, 'nothing stored by a refused request');
    for (const ct of ['application/json', 'Application/JSON', 'application/json; charset=utf-8', 'application/json;charset=UTF-8']) {
      const r = await post(t, bodyOf(), { headers: { 'content-type': ct } });
      assert.strictEqual(r.status, 200, ct);
      assert.strictEqual(r.body, OK, ct);
    }
    assert.strictEqual(H.remoteCount(dir.dbPath), 4);
  });
  // the router: every /api/telemetry path goes to this section, and before every other route
  const names = H.loadSection().names;
  for (const u of ['/api/telemetry', '/api/telemetry/v1/combat', '/api/telemetry/x?y', '/api/telemetry?x']) assert.ok(names.isTelemetryUrl(u), u);
  for (const u of ['/api/telemetryx', '/api/stats', '/api/bug-report', '/api/balance/telemetry']) assert.ok(!names.isTelemetryUrl(u), u);
  const src = H.proxySourceText();
  const handler = src.slice(src.indexOf('function handleHttpRequest(req, res) {'));
  const first = handler.indexOf('isTelemetryUrl(req.url)');
  assert.ok(first > 0 && first < handler.indexOf("startsWith('/api/admin/')"), 'telemetry is routed first');
});

// ---------------------------------------------------------------- row 2

test('row 2: body cap 128 KB in bytes: Content-Length over 131072 refused before reading; a body crossing it refused and the socket closed', async () => {
  await withEndpoint(async (t, dir) => {
    // exactly 131072 bytes is accepted (padding is JSON whitespace)
    const base = bodyOf();
    const exact = base + ' '.repeat(131072 - Buffer.byteLength(base));
    assert.strictEqual(Buffer.byteLength(exact), 131072);
    let r = await post(t, exact, { headers: { 'content-length': '131072' } });
    assert.strictEqual(r.status, 200, r.body);
    // Content-Length over the cap: 413, no data listener ever attached, socket closed
    r = await post(t, exact + ' ', { headers: { 'content-length': '131073' } });
    assert.strictEqual(r.status, 413);
    assert.strictEqual(r.body, '{"error":"too_large"}');
    assert.strictEqual(r.dataListened, false, 'refused before reading');
    assert.strictEqual(r.socketDestroyed, true);
    assert.strictEqual(r.headers.connection, 'close');
    // no Content-Length (chunked): the bytes are counted as they come
    const chunks = [];
    for (let i = 0; i < 140; i++) chunks.push(' '.repeat(1000));
    r = await post(t, [base].concat(chunks));
    assert.strictEqual(r.status, 413);
    assert.strictEqual(r.socketDestroyed, true);
    // multi byte characters: 70000 characters are 140000 bytes
    const wide = 'é'.repeat(70000);
    assert.ok(wide.length < 131072 && Buffer.byteLength(wide) > 131072);
    r = await post(t, wide);
    assert.strictEqual(r.status, 413, 'counted in bytes');
    assert.strictEqual(r.socketDestroyed, true);
    // a Content-Length that is not a number
    r = await post(t, base, { headers: { 'content-length': '12x' } });
    assert.strictEqual(r.status, 400);
    assert.strictEqual(H.remoteCount(dir.dbPath), 1);
  });
});

// ---------------------------------------------------------------- row 3

test('row 3: a body not complete in 10 s gets 408 and the socket is destroyed', async () => {
  await withEndpoint(async (t, dir) => {
    const r = await send(t, {
      end: false,
      beforeAwait: (req) => {
        req.emit('data', Buffer.from('{"schema":1,'));
        const live = t.timers.filter((x) => !x.cleared);
        assert.strictEqual(live.length, 1, 'one body timer');
        assert.strictEqual(live[0].ms, 10000);
        live[0].fn();
      },
    });
    assert.strictEqual(r.status, 408);
    assert.strictEqual(r.body, '{"error":"timeout"}');
    assert.strictEqual(r.socketDestroyed, true);
    assert.strictEqual(H.remoteCount(dir.dbPath), 0);
    // a body that ends in time clears its timer
    const ok = await post(t, bodyOf());
    assert.strictEqual(ok.status, 200);
    assert.ok(t.timers.slice(1).every((x) => x.cleared), 'timer cleared');
  });
});

// ---------------------------------------------------------------- row 4

test('row 4: top level keys exactly schema, version, source, install_id, rows; values checked', async () => {
  await withEndpoint(async (t, dir) => {
    const good = batch();
    const without = (k) => { const b = Object.assign({}, good); delete b[k]; return JSON.stringify(b); };
    const bad = [];
    for (const k of Object.keys(good)) bad.push(['missing ' + k, without(k)]);
    bad.push(['unknown key', JSON.stringify(Object.assign({}, good, { extra: 1 }))]);
    bad.push(['__proto__ key', bodyOf().replace('{', '{"__proto__":{"schema":1},')]);
    bad.push(['constructor key', bodyOf().replace('{', '{"constructor":1,')]);
    bad.push(['duplicate top key', bodyOf().replace('{', '{"source":2,')]);
    for (const v of [0, 2, '1', null, true, [1]]) bad.push(['schema ' + JSON.stringify(v), bodyOf({ schema: v })]);
    for (const v of [[1, 2], [1, 2, 7, 0], [1, 2, 1001], [1, -1, 7], [1, '2', 7], '1.2.7', null, []]) bad.push(['version ' + JSON.stringify(v), bodyOf({ version: v })]);
    for (const v of [0, 5, '1', null]) bad.push(['source ' + JSON.stringify(v), bodyOf({ source: v })]);
    for (const v of [ID.toUpperCase(), ID.slice(1), ID + '0', 'g' + ID.slice(1), 7, null, ID.slice(0, 31) + '\\u0030'])
      bad.push(['install_id ' + v, JSON.stringify(batch({ install_id: v })).replace('\\\\u0030', '\\u0030')]);
    bad.push(['rows empty', bodyOf({ rows: [] })]);
    bad.push(['rows not an array', bodyOf({ rows: goodRow() })]);
    bad.push(['top level array', JSON.stringify([good])]);
    bad.push(['trailing text', bodyOf() + 'x']);
    bad.push(['two objects', bodyOf() + bodyOf()]);
    bad.push(['empty body', '']);
    for (const [why, body] of bad) {
      const r = await post(t, body);
      assert.strictEqual(r.status, 400, why);
      assert.strictEqual(r.body, '{"error":"invalid"}', why);
    }
    assert.strictEqual(H.remoteCount(dir.dbPath), 0);
    for (const [why, body] of [['50 rows', bodyOf({ rows: Array.from({ length: 50 }, () => goodRow()) })],
      ['version bounds', bodyOf({ version: [0, 1000, 0] })], ['source 4', bodyOf({ source: 4 })],
      ['keys in another order', JSON.stringify({ rows: [goodRow()], install_id: ID, source: 2, version: [1, 2, 7], schema: 1 })],
      ['whitespace', JSON.stringify(batch(), null, 2)]]) {
      const r = await post(t, body);
      assert.strictEqual(r.status, 200, why);
    }
    assert.strictEqual(H.remoteCount(dir.dbPath), 54);
  });
  // 1 to 100 rows through the body path (T2b: the 128 KB cap holds 100 rows at every maximum),
  // and on the reader itself
  await withEndpoint(async (t, dir) => {
    const maxRows = (n) => bodyOf({ rows: Array.from({ length: n }, () => H.maxRow()) });
    let r = await post(t, maxRows(101));
    assert.strictEqual(r.status, 400, '101 rows');
    assert.strictEqual(r.body, '{"error":"invalid"}');
    r = await post(t, maxRows(100));
    assert.strictEqual(r.status, 200, '100 rows');
    assert.strictEqual(H.remoteCount(dir.dbPath), 100);
  });
  const { parseTelemetryBatch } = H.loadSection().names;
  const rowsOf = (n) => bodyOf({ rows: Array.from({ length: n }, () => goodRow()) });
  assert.strictEqual(parseTelemetryBatch(rowsOf(100)).rows.length, 100);
  assert.strictEqual(parseTelemetryBatch(rowsOf(101)), null);
  assert.strictEqual(parseTelemetryBatch(rowsOf(1)).rows.length, 1);
  assert.strictEqual(parseTelemetryBatch(rowsOf(0)), null);
});

// ---------------------------------------------------------------- row 5

test('row 5: every row has exactly the keys; any bad value refuses the whole batch and stores nothing', async () => {
  const keys = H.fixture().keys;
  await withEndpoint(async (t, dir) => {
    const bads = [];
    const dropped = goodRow(); delete dropped[keys[7]];
    bads.push(['missing key', dropped]);
    bads.push(['extra key', goodRow({ player_name: 1 })]);
    for (const v of [null, '5', true, false, [5], { v: 5 }, -1, 1.5]) bads.push(['value ' + JSON.stringify(v), goodRow({ rounds: v })]);
    bads.push(['out of bound', goodRow({ player_level: 101 })]);
    for (const [why, row] of bads) {
      for (const at of [0, 2]) {
        const rows = [goodRow(), goodRow(), goodRow()];
        rows[at] = row;
        const r = await post(t, bodyOf({ rows }));
        assert.strictEqual(r.status, 400, why + ' at ' + at);
      }
    }
    assert.strictEqual(H.remoteCount(dir.dbPath), 0, 'no good row of a mixed batch is stored');
    const r = await post(t, bodyOf({ rows: [goodRow(), goodRow(), goodRow()] }));
    assert.strictEqual(r.status, 200);
    const stored = H.remoteRows(dir.dbPath);
    assert.strictEqual(stored.length, 3);
    for (const k of keys) assert.strictEqual(stored[0][k], goodRow()[k], k);
  });
});

// ---------------------------------------------------------------- row 6

test('row 6: the node bounds equal TelemetryRow.Columns; every fixture good row accepted, every bad row refused', async () => {
  const names = H.loadSection().names;
  const node = JSON.parse(JSON.stringify(names.TELEMETRY_COLUMNS));
  const cs = H.csharpColumns();
  assert.strictEqual(cs.length, 39);
  assert.deepStrictEqual(node, cs);
  const fx = H.fixture();
  assert.deepStrictEqual(node.map((c) => c[0]), fx.keys);
  await withEndpoint(async (t, dir) => {
    for (const g of fx.good) {
      const r = await post(t, bodyOf({ rows: [g] }));
      assert.strictEqual(r.status, 200, JSON.stringify(g));
    }
    assert.ok(fx.bad.length > 80);
    for (const b of fx.bad) {
      const r = await post(t, bodyOf({ rows: [b.row] }));
      assert.strictEqual(r.status, 400, b.why);
    }
    assert.strictEqual(H.remoteCount(dir.dbPath), fx.good.length);
  });
});

// ---------------------------------------------------------------- row 7

test('row 7: integer literals only on the raw text; 2^53 minus 1 is the top; a duplicate key refused; the fixture bad_raw bodies refused', async () => {
  const fx = H.fixture();
  assert.ok(fx.bad_raw.length > 10);
  const row = JSON.stringify(goodRow());
  const swap = (key, tok) => {
    const out = row.replace(new RegExp('"' + key + '":[0-9]+'), '"' + key + '":' + tok);
    assert.notStrictEqual(out, row);
    return bodyOf().replace(row, out);
  };
  await withEndpoint(async (t, dir) => {
    for (const tok of ['1.0', '1e2', '1E2', '-0', '01', '+1', '0.0', '1e0', '9007199254740992', '9007199254740993', '1' + '0'.repeat(30), '0x1', 'NaN', 'Infinity', '"1"']) {
      const r = await post(t, swap('player_max_hp', tok));
      assert.strictEqual(r.status, 400, tok);
    }
    for (const b of fx.bad_raw) {
      const r = await post(t, b.body);
      assert.strictEqual(r.status, 400, b.why);
    }
    // duplicate keys at both levels
    assert.strictEqual((await post(t, bodyOf().replace(row, row.replace('{', '{"rounds":1,')))).status, 400, 'row key twice');
    assert.strictEqual((await post(t, bodyOf().replace('{', '{"schema":1,'))).status, 400, 'top key twice');
    // escapes and non-ASCII in a key
    assert.strictEqual((await post(t, bodyOf().replace('"rounds"', '"\\u0072ounds"'))).status, 400, 'escaped key');
    assert.strictEqual(H.remoteCount(dir.dbPath), 0);
    const top = await post(t, swap('player_max_hp', '9007199254740991'));
    assert.strictEqual(top.status, 200);
    assert.strictEqual(H.remoteRows(dir.dbPath)[0].player_max_hp, 9007199254740991);
  });
});

// ---------------------------------------------------------------- row 8

test('row 8: 12 batches an hour per address, the 13th 429, the next hour accepted; the address map pruned and bounded', async () => {
  await withEndpoint(async (t) => {
    const peer = '198.51.100.20';
    for (let i = 0; i < 12; i++) assert.strictEqual((await post(t, bodyOf(), { peer })).status, 200, 'batch ' + (i + 1));
    const r = await post(t, bodyOf(), { peer });
    assert.strictEqual(r.status, 429);
    assert.strictEqual(r.body, '{"error":"rate_limited"}');
    assert.strictEqual((await post(t, bodyOf())).status, 200, 'another address is not limited');
    t.clock.t += HOUR_MS;
    assert.strictEqual((await post(t, bodyOf(), { peer })).status, 200, 'next hour');
    assert.strictEqual(t.ep.sizes().addresses, 1, 'the past hour pruned when the hour turned');
  });
  // bounded: a full map of this hour refuses a new address; the next hour starts empty
  await withEndpoint(async (t) => {
    for (let i = 0; i < 20; i++) assert.strictEqual((await post(t, bodyOf())).status, 200);
    assert.strictEqual(t.ep.sizes().addresses, 20);
    assert.strictEqual((await post(t, bodyOf())).status, 429, 'map full');
    assert.strictEqual(t.ep.sizes().addresses, 20);
    t.clock.t += HOUR_MS;
    assert.strictEqual((await post(t, bodyOf())).status, 200);
    assert.strictEqual(t.ep.sizes().addresses, 1);
  }, { mapMax: 20 });
  assert.strictEqual(H.loadSection().names.TELEMETRY_MAP_MAX, 10000);
});

// ---------------------------------------------------------------- row 9

test('row 9: 24 batches a day per install_id, the 25th 429, the next day accepted', async () => {
  await withEndpoint(async (t, dir) => {
    for (let i = 0; i < 24; i++) assert.strictEqual((await post(t, bodyOf())).status, 200, 'batch ' + (i + 1));
    const r = await post(t, bodyOf());
    assert.strictEqual(r.status, 429);
    assert.strictEqual(r.body, '{"error":"rate_limited"}');
    assert.strictEqual(H.remoteCount(dir.dbPath), 24);
    assert.strictEqual((await post(t, bodyOf({ install_id: 'f'.repeat(32) }))).status, 200, 'another id');
    t.clock.t += DAY_MS;
    assert.strictEqual((await post(t, bodyOf())).status, 200, 'next day');
  });
});

// ---------------------------------------------------------------- row 10

test('row 10: 50000 rows a day in all; a batch that would cross it refused whole; the next day accepted', async () => {
  await withEndpoint(async (t, dir) => {
    assert.strictEqual((await post(t, bodyOf())).status, 200, 'creates the table');
    H.fillRemote(dir.dbPath, DAY0, 49948);
    H.fillRemote(dir.dbPath, DAY0 - 1, 100);
    assert.strictEqual(H.remoteCount(dir.dbPath), 50049);
    const many = Array.from({ length: 52 }, () => goodRow());
    let r = await post(t, bodyOf({ rows: many, install_id: 'a'.repeat(32) }));
    assert.strictEqual(r.status, 429, '49949 + 52 crosses 50000');
    assert.strictEqual(H.remoteCount(dir.dbPath), 50049, 'nothing of it stored');
    r = await post(t, bodyOf({ rows: many.slice(2), install_id: 'a'.repeat(32) }));
    assert.strictEqual(r.status, 200, '49949 + 50 is 49999');
    r = await post(t, bodyOf({ install_id: 'b'.repeat(32) }));
    assert.strictEqual(r.status, 200, 'the 50000th');
    r = await post(t, bodyOf({ install_id: 'b'.repeat(32) }));
    assert.strictEqual(r.status, 429, 'the 50001st');
    t.clock.t += DAY_MS;
    r = await post(t, bodyOf({ install_id: 'b'.repeat(32) }));
    assert.strictEqual(r.status, 200, 'next day');
  });
});

// ---------------------------------------------------------------- row 11 and F

test('row 11: X-Real-IP is used only from a loopback peer; a spoofed header cannot reset the per address limit', async () => {
  await withEndpoint(async (t) => {
    const peer = '198.51.100.30';
    for (let i = 0; i < 12; i++) {
      const r = await post(t, bodyOf(), { peer, headers: { 'x-real-ip': '192.0.2.' + (i + 1) } });
      assert.strictEqual(r.status, 200);
    }
    for (let i = 0; i < 3; i++) {
      const r = await post(t, bodyOf(), { peer, headers: { 'x-real-ip': '192.0.2.' + (100 + i) } });
      assert.strictEqual(r.status, 429, 'a header from a non-loopback peer is ignored');
    }
    // through the local proxy each forwarded address is its own bucket
    const other = bodyOf({ install_id: '1'.repeat(32) });
    for (let i = 0; i < 12; i++) assert.strictEqual((await post(t, other, { peer: '127.0.0.1', headers: { 'x-real-ip': MARKER_ADDRESS } })).status, 200);
    assert.strictEqual((await post(t, other, { peer: '127.0.0.1', headers: { 'x-real-ip': MARKER_ADDRESS } })).status, 429);
    assert.strictEqual((await post(t, other, { peer: '127.0.0.1', headers: { 'x-real-ip': '192.0.2.200' } })).status, 200);
  });
  const { telemetryAddress } = H.loadSection().names;
  const isIP = require('node:net').isIP;
  const a = (peer, h) => telemetryAddress({ socket: { remoteAddress: peer }, headers: h === undefined ? {} : { 'x-real-ip': h } }, isIP);
  assert.strictEqual(a('198.51.100.30', '192.0.2.1'), '198.51.100.30');
  assert.strictEqual(a('10.0.0.1', '192.0.2.1'), '10.0.0.1');
  assert.strictEqual(a('127.0.0.1', '192.0.2.1'), '192.0.2.1');
  assert.strictEqual(a('::1', '192.0.2.1'), '192.0.2.1');
  assert.strictEqual(a('127.0.0.1', 'not an address'), '127.0.0.1');
  assert.strictEqual(a('127.0.0.1'), '127.0.0.1');
  assert.strictEqual(a('127.0.0.2', '192.0.2.1'), '127.0.0.2');
});

test('row F: no reply sets a cookie; ::ffff:127.0.0.1 counts as loopback', async () => {
  const { telemetryAddress } = H.loadSection().names;
  const isIP = require('node:net').isIP;
  assert.strictEqual(telemetryAddress({ socket: { remoteAddress: '::ffff:127.0.0.1' }, headers: { 'x-real-ip': '192.0.2.9' } }, isIP), '192.0.2.9');
  assert.strictEqual(telemetryAddress({ socket: { remoteAddress: '::ffff:10.0.0.1' }, headers: { 'x-real-ip': '192.0.2.9' } }, isIP), '::ffff:10.0.0.1');
  const replies = await everyReply();
  assert.ok(replies.length >= 12);
  for (const r of replies) assert.ok(!('set-cookie' in r.headers), r.why);
});

// ---------------------------------------------------------------- row 12

test('row 12: rows go only to the remote file, opened with fileMustExist; the game database is never written or attached', async () => {
  await withEndpoint(async (t, dir, game) => {
    const snap = () => {
      const d = new Database(game.file, { readonly: true, fileMustExist: true });
      try {
        const master = d.prepare('SELECT type, name, sql FROM sqlite_master ORDER BY name').all();
        const counts = master.filter((m) => m.type === 'table').map((m) => [m.name, d.prepare(`SELECT COUNT(*) AS n FROM "${m.name}"`).get().n]);
        return { master, counts };
      } finally { d.close(); }
    };
    const before = snap();
    assert.strictEqual((await post(t, bodyOf({ rows: [goodRow(), goodRow()] }))).status, 200);
    t.intervals[0].fn();
    assert.deepStrictEqual(snap(), before);
    for (const c of [game.db, game.dbWrite]) assert.deepStrictEqual(c.pragma('database_list').map((x) => x.name), ['main']);
    assert.strictEqual(H.remoteCount(dir.dbPath), 2);
    assert.ok(t.opened.length >= 1);
    for (const o of t.opened) {
      assert.strictEqual(o.file, dir.dbPath);
      assert.strictEqual(o.opts.fileMustExist, true);
      assert.ok(!o.opts.readonly);
    }
  }, { withGame: true });
  // the paths are tested in 'T2b item 4'
  // the server builds the endpoint from those paths, and the section never names the game database
  const src = H.proxySourceText();
  assert.match(src, /createTelemetryEndpoint\(Object\.assign\(telemetryPaths\(process\.env\)/);
  assert.doesNotMatch(H.section(), /\bdbWrite\b|\bDB_PATH\b|usurper_online|ATTACH/i);
});

// ---------------------------------------------------------------- row 13

test('row 13: with no remote file: 503, the file is not created, nothing else written', async () => {
  await withEndpoint(async (t, dir) => {
    const list = () => fs.readdirSync(dir.dir).sort();
    const before = list();
    assert.deepStrictEqual(before, []);
    const r = await post(t, bodyOf());
    assert.strictEqual(r.status, 503);
    assert.strictEqual(r.body, '{"error":"unavailable"}');
    t.intervals[0].fn();
    assert.strictEqual(fs.existsSync(dir.dbPath), false);
    assert.deepStrictEqual(list(), before);
    // the operator makes the file: the next batch is stored, no restart
    new Database(dir.dbPath).close();
    assert.strictEqual((await post(t, bodyOf())).status, 200);
    assert.strictEqual(H.remoteCount(dir.dbPath), 1);
  }, { remote: false });
});

// ---------------------------------------------------------------- row 14

const FIXED_COLUMNS = ['schema', 'version_major', 'version_minor', 'version_patch', 'source', 'install_id', 'received_day'];

test('row 14: no address stored: the file holds no marker address; the column list is fixed', async () => {
  await withEndpoint(async (t, dir) => {
    for (const peer of [MARKER_ADDRESS, '127.0.0.1', '::1']) {
      const r = await post(t, bodyOf(), { peer, headers: { 'x-real-ip': MARKER_ADDRESS, 'user-agent': MARKER_ADDRESS, forwarded: 'for=' + MARKER_ADDRESS } });
      assert.strictEqual(r.status, 200);
    }
    t.ep.close();
    const cols = (() => {
      const d = new Database(dir.dbPath, { readonly: true });
      try { return d.prepare('PRAGMA table_info(remote_combat_events)').all().map((c) => c.name); } finally { d.close(); }
    })();
    assert.deepStrictEqual(cols, FIXED_COLUMNS.concat(H.fixture().keys));
    for (const f of fs.readdirSync(dir.dir)) {
      const bytes = fs.readFileSync(path.join(dir.dir, f));
      assert.strictEqual(bytes.indexOf(Buffer.from(MARKER_ADDRESS)), -1, f);
      assert.strictEqual(bytes.indexOf(Buffer.from('203.0.113')), -1, f);
    }
    assert.strictEqual(H.remoteCount(dir.dbPath), 3);
  });
});

// ---------------------------------------------------------------- row 15

// One request of every outcome, sent with the marker address (through the proxy and as the peer)
// and the install id: the log lines of all of them, and every reply.
async function everyOutcome(fn, options) {
  return withEndpoint(async (t, dir) => {
    const out = [];
    const viaProxy = { peer: '127.0.0.1', headers: { 'x-real-ip': MARKER_ADDRESS } };
    // each outcome is checked to be what its label says, so no later test reads a mislabelled reply
    const add = (why, r, status, body) => {
      assert.strictEqual(r.status, status, why);
      if (body !== undefined) assert.strictEqual(r.body, body, why);
      r.why = why;
      out.push(r);
      return r;
    };
    const ok = (extra) => Object.assign({}, viaProxy, extra);
    add('accept', await post(t, bodyOf(), ok()), 200, OK);
    add('400', await post(t, bodyOf({ schema: 2 }), ok()), 400);
    add('400 bad key', await post(t, bodyOf().replace('"schema"', '"zz_marker_key"'), ok()), 400);
    add('405', await send(t, ok({ method: 'GET' })), 405);
    add('OPTIONS', await send(t, ok({ method: 'OPTIONS', headers: { 'x-real-ip': MARKER_ADDRESS, origin: 'https://evil.example', 'access-control-request-method': 'POST' } })), 405);
    add('404', await send(t, ok({ url: '/api/telemetry/v9' })), 404);
    add('408', await send(t, Object.assign(ok(), { end: false, beforeAwait: () => t.timers.filter((x) => !x.cleared).forEach((x) => x.fn()) })), 408);
    add('413', await post(t, bodyOf(), ok({ headers: { 'x-real-ip': MARKER_ADDRESS, 'content-length': '140000' } })), 413);
    add('415 type', await post(t, bodyOf(), ok({ headers: { 'x-real-ip': MARKER_ADDRESS, 'content-type': 'text/plain' } })), 415);
    add('415 encoding', await post(t, zlib.gzipSync(bodyOf()), ok({ headers: { 'x-real-ip': MARKER_ADDRESS, 'content-encoding': 'gzip' } })), 415);
    // seven POSTs so far count against the marker address (404, 405 and OPTIONS are refused before
    // the count); five more as the peer reach 12, the next is the 13th
    for (let i = 0; i < 4; i++) await post(t, bodyOf(), { peer: MARKER_ADDRESS });
    add('accept as the peer', await post(t, bodyOf(), { peer: MARKER_ADDRESS }), 200, OK);
    add('429 address', await post(t, bodyOf(), { peer: MARKER_ADDRESS }), 429);
    t.clock.t += HOUR_MS;
    for (let i = 0; i < 24; i++) await post(t, bodyOf({ install_id: 'c'.repeat(32) }));
    add('429 install', await post(t, bodyOf({ install_id: 'c'.repeat(32) }), ok()), 429);
    fs.writeFileSync(dir.stopPath, '');
    t.clock.t += 60000;
    add('stop', await post(t, bodyOf(), ok()), 200, STOP);
    fs.rmSync(dir.stopPath);
    t.clock.t += 60000;
    t.ep.close();
    fs.rmSync(dir.dbPath);
    add('503', await post(t, bodyOf(), ok()), 503);
    return fn ? fn(out, t, dir) : out;
  }, options);
}
const everyReply = () => everyOutcome();

test('row 15: no log line holds the address or the install_id over accept, 400, 405, 408, 413, 415, 429 and 503', async () => {
  await everyOutcome((replies, t) => {
    const statuses = new Set(replies.map((r) => r.status));
    for (const s of [200, 400, 404, 405, 408, 413, 415, 429, 503]) assert.ok(statuses.has(s), 'covers ' + s);
    const logs = t.logs.join('\n');
    assert.ok(t.logs.length >= 1, 'something was logged (the limits)');
    assert.ok(!logs.includes(MARKER_ADDRESS), logs);
    assert.ok(!logs.includes(ID), logs);
    assert.ok(!logs.includes('c'.repeat(32)), logs);
  });
  // the same with the remote file present at start (the start and prune path)
  await withEndpoint(async (t) => {
    await post(t, bodyOf(), { peer: MARKER_ADDRESS });
    t.intervals[0].fn();
    assert.ok(!t.logs.join('\n').includes(MARKER_ADDRESS));
  });
});

// ---------------------------------------------------------------- row 16

test('row 16: received_day is the UTC day number; nothing finer than a day is stored', async () => {
  const late = Date.UTC(2026, 9, 5, 23, 59, 59, 999);
  await withEndpoint(async (t, dir) => {
    assert.strictEqual((await post(t, bodyOf())).status, 200);
    t.clock.t = late + 1;
    assert.strictEqual((await post(t, bodyOf())).status, 200);
    const rows = H.remoteRows(dir.dbPath);
    assert.deepStrictEqual(rows.map((r) => r.received_day), [DAY0, DAY0 + 1]);
    const fineValues = [late, late + 1, T0, Math.floor(T0 / 1000), Math.floor(late / 1000)];
    for (const r of rows) for (const [k, v] of Object.entries(r)) assert.ok(!fineValues.includes(v), k);
    const d = new Database(dir.dbPath, { readonly: true });
    try {
      const cols = d.prepare('PRAGMA table_info(remote_combat_events)').all().map((c) => c.name);
      assert.ok(!cols.some((c) => /time|_at$|date|stamp|hour|minute|second/i.test(c)), cols.join(','));
    } finally { d.close(); }
  }, { t: late });
});

// ---------------------------------------------------------------- row 17

test('row 17: one transaction per batch: an insert failing on the last row stores none', async () => {
  await withEndpoint(async (t, dir) => {
    assert.strictEqual((await post(t, bodyOf())).status, 200);
    const d = new Database(dir.dbPath, { fileMustExist: true });
    d.exec("CREATE TRIGGER fail_last BEFORE INSERT ON remote_combat_events WHEN NEW.player_hp_end = 424242 BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
    d.close();
    const rows = Array.from({ length: 10 }, () => goodRow());
    rows[9] = goodRow({ player_hp_end: 424242 });
    const r = await post(t, bodyOf({ rows, install_id: 'd'.repeat(32) }));
    assert.strictEqual(r.status, 500);
    assert.strictEqual(r.body, '{"error":"internal"}');
    assert.strictEqual(H.remoteCount(dir.dbPath), 1, 'none of the ten stored');
    assert.strictEqual((await post(t, bodyOf({ install_id: 'd'.repeat(32) }))).status, 200, 'the next batch is fine');
  });
});

// ---------------------------------------------------------------- row 18

test('row 18: prune at start and every 24 h: older than 30 days removed, then the newest 200000 kept', async () => {
  const dir = H.makeDir();
  try {
    let t = H.makeEndpoint(dir);
    t.ep.close();
    H.fillRemote(dir.dbPath, DAY0 - 31, 3, 1);
    H.fillRemote(dir.dbPath, DAY0 - 30, 2, 11);
    H.fillRemote(dir.dbPath, DAY0, 4, 21);
    // at start
    t = H.makeEndpoint(dir);
    let rows = H.remoteRows(dir.dbPath);
    assert.deepStrictEqual(rows.map((r) => r.player_hp_end), [11, 12, 21, 22, 23, 24], 'the 31 day old rows went at start');
    assert.strictEqual(t.intervals.length, 1);
    assert.strictEqual(t.intervals[0].ms, 24 * 60 * 60 * 1000);
    // a day later the 30 day old rows are 31 days old
    t.clock.t += DAY_MS;
    t.intervals[0].fn();
    assert.deepStrictEqual(H.remoteRows(dir.dbPath).map((r) => r.player_hp_end), [21, 22, 23, 24]);
    t.ep.close();
    // the newest 200000 kept (by day, then arrival)
    H.fillRemote(dir.dbPath, DAY0 + 1, 200000, 1000);
    t = H.makeEndpoint(dir, { t: T0 + DAY_MS });
    const d = new Database(dir.dbPath, { readonly: true });
    try {
      assert.strictEqual(d.prepare('SELECT COUNT(*) AS n FROM remote_combat_events').get().n, 200000);
      assert.strictEqual(d.prepare('SELECT MIN(player_hp_end) AS m FROM remote_combat_events').get().m, 1000, 'the 4 older rows went');
    } finally { d.close(); }
    t.ep.close();
  } finally {
    dir.cleanup();
  }
});

// ---------------------------------------------------------------- row 19

test('row 19: a stop file answers every valid batch {"stop":true} and stores nothing; adding or removing it needs no restart', async () => {
  await withEndpoint(async (t, dir) => {
    assert.strictEqual((await post(t, bodyOf())).body, OK);
    fs.writeFileSync(dir.stopPath, '');
    // cached briefly: within the cache the old answer holds, after it the file counts
    t.clock.t += 1000;
    assert.strictEqual((await post(t, bodyOf())).body, OK, 'inside the cache');
    t.clock.t += 5000;
    for (let i = 0; i < 3; i++) {
      const r = await post(t, bodyOf());
      assert.strictEqual(r.status, 200);
      assert.strictEqual(r.body, STOP);
    }
    assert.strictEqual(H.remoteCount(dir.dbPath), 2, 'nothing stored while stopped');
    assert.strictEqual((await post(t, bodyOf({ schema: 2 }))).status, 400, 'an invalid batch is still refused');
    fs.rmSync(dir.stopPath);
    t.clock.t += 5000;
    const r = await post(t, bodyOf());
    assert.strictEqual(r.body, OK);
    assert.strictEqual(H.remoteCount(dir.dbPath), 3);
  });
  // stopped with no remote file: still {"stop":true}
  await withEndpoint(async (t, dir) => {
    fs.writeFileSync(dir.stopPath, '');
    assert.strictEqual((await post(t, bodyOf())).body, STOP);
    assert.strictEqual(fs.existsSync(dir.dbPath), false);
  }, { remote: false });
  assert.strictEqual(H.loadSection().names.TELEMETRY_STOP_CACHE_MS, 5000);
});

// ---------------------------------------------------------------- row 20

test('row 20: replies fixed: {"ok":true}, {"stop":true} or {"error":"<word>"}; nothing from the request echoed', async () => {
  const words = H.loadSection().names.TELEMETRY_ERROR_WORDS;
  await everyOutcome((replies) => {
    for (const r of replies) {
      assert.strictEqual(r.headers['content-type'], 'application/json', r.why);
      if (r.status >= 200 && r.status < 300) assert.ok(r.body === OK || r.body === STOP, r.why + ' ' + r.body);
      else {
        const m = /^\{"error":"([a-z_]+)"\}$/.exec(r.body);
        assert.ok(m && words.includes(m[1]), r.why + ' ' + r.body);
      }
      for (const s of ['zz_marker_key', MARKER_ADDRESS, ID, 'evil.example', 'c'.repeat(32)]) {
        assert.ok(!r.body.includes(s), r.why + ' echoes ' + s);
        for (const v of Object.values(r.headers)) assert.ok(!String(v).includes(s), r.why + ' header echoes ' + s);
      }
    }
  });
});

// ---------------------------------------------------------------- row 21

// 1.2.7 T4: the balance Difficulty tab's remote view (its own marked block) is the one reader;
// balance-remote.test.js row 9 tests that block and its read-only handle.
test('row 21: no other route reads the remote file; the section and the Difficulty remote view are the only places that name it', () => {
  const src = H.proxySourceText();
  const sec = H.section();
  const view = require('./balance-api-harness').slice(src, '// --- Balance Remote View (1.2.7) ---', '// --- End Balance Remote View ---');
  const outside = src.replace(sec, '').replace(view, '');
  assert.doesNotMatch(outside, /remote_combat_events|remote_telemetry|REMOTE_TELEMETRY/);
  assert.strictEqual((outside.match(/telemetryEndpoint\b/g) || []).length, 3, 'built once, routed once, closed once');
  assert.deepStrictEqual(view.match(/telemetryEndpoint\.?\w*/g), ['telemetryEndpoint.reader', 'telemetryEndpoint.reader'],
    'the view only reads, through reader() (named once in its comment, called once)');
});

// ---------------------------------------------------------------- A to E

test('row A: the address limit is decided before the body; refused batches (400, 413, 415) count', async () => {
  await withEndpoint(async (t) => {
    const peer = '198.51.100.40';
    const refused = [
      () => post(t, bodyOf({ schema: 9 }), { peer }),
      () => post(t, bodyOf(), { peer, headers: { 'content-length': '199999' } }),
      () => post(t, bodyOf(), { peer, headers: { 'content-type': 'text/plain' } }),
    ];
    for (let i = 0; i < 12; i++) {
      const r = await refused[i % 3]();
      assert.ok([400, 413, 415].includes(r.status), String(r.status));
    }
    const r = await post(t, bodyOf(), { peer });
    assert.strictEqual(r.status, 429, 'the 13th, valid, is over the limit');
    assert.strictEqual(r.dataListened, false, 'decided before the body');
  });
});

test('row B: the install_id map is pruned and bounded like the address map', async () => {
  await withEndpoint(async (t) => {
    for (let i = 0; i < 20; i++) assert.strictEqual((await post(t, bodyOf({ install_id: (i + 1).toString(16).padStart(32, '0') }), { peer: '127.0.0.1', headers: { 'x-real-ip': '192.0.2.' + (1 + (i % 4)) } })).status, 200);
    assert.strictEqual(t.ep.sizes().installs, 20);
    const r = await post(t, bodyOf({ install_id: 'e'.repeat(32) }), { peer: '127.0.0.1', headers: { 'x-real-ip': '192.0.2.1' } });
    assert.strictEqual(r.status, 429, 'map full');
    assert.strictEqual(t.ep.sizes().installs, 20);
    t.clock.t += DAY_MS;
    assert.strictEqual((await post(t, bodyOf({ install_id: 'e'.repeat(32) }), { peer: '127.0.0.1', headers: { 'x-real-ip': '192.0.2.1' } })).status, 200);
    assert.strictEqual(t.ep.sizes().installs, 1, 'the past day pruned');
  }, { mapMax: 20 });
  // the limiter itself at the real size
  const { makeTelemetryLimiter, TELEMETRY_MAP_MAX } = H.loadSection().names;
  const lim = makeTelemetryLimiter(24, TELEMETRY_MAP_MAX);
  for (let i = 0; i < TELEMETRY_MAP_MAX; i++) assert.ok(lim.take('k' + i, 1));
  assert.strictEqual(lim.take('new', 1), false);
  assert.strictEqual(lim.size(), TELEMETRY_MAP_MAX);
  assert.ok(lim.take('new', 2));
  assert.strictEqual(lim.size(), 1);
});

test('row C: the daily row cap comes from the table, so a fresh handler (a restart) still refuses past 50000', async () => {
  const dir = H.makeDir();
  try {
    let t = H.makeEndpoint(dir);
    assert.strictEqual((await post(t, bodyOf())).status, 200);
    t.ep.close();
    H.fillRemote(dir.dbPath, DAY0, 49998);
    t = H.makeEndpoint(dir);
    let r = await post(t, bodyOf({ rows: [goodRow(), goodRow()] }));
    assert.strictEqual(r.status, 429);
    r = await post(t, bodyOf());
    assert.strictEqual(r.status, 200, 'the 50000th');
    t.ep.close();
    t = H.makeEndpoint(dir);
    r = await post(t, bodyOf());
    assert.strictEqual(r.status, 429, 'after another restart');
    t.ep.close();
    assert.strictEqual(H.remoteCount(dir.dbPath), 50000);
  } finally {
    dir.cleanup();
  }
});

test('row D: any Content-Encoding is refused; nothing is decompressed', async () => {
  await withEndpoint(async (t, dir) => {
    const gz = zlib.gzipSync(bodyOf());
    for (const [enc, body] of [['gzip', gz], ['deflate', zlib.deflateSync(bodyOf())], ['br', zlib.brotliCompressSync(bodyOf())], ['identity', bodyOf()], ['', bodyOf()]]) {
      const r = await post(t, body, { headers: { 'content-encoding': enc } });
      assert.strictEqual(r.status, 415, enc);
      assert.strictEqual(r.body, '{"error":"encoding"}', enc);
      assert.strictEqual(r.dataListened, false, enc);
    }
    assert.strictEqual(H.remoteCount(dir.dbPath), 0);
    assert.doesNotMatch(H.section(), /zlib|gunzip|inflate|decompress/i);
  });
});

test('row E: OPTIONS gets no allow headers; no Access-Control header on any reply of this route', async () => {
  const replies = await everyReply();
  const options = replies.find((r) => r.why === 'OPTIONS');
  assert.strictEqual(options.status, 405);
  for (const r of replies) {
    for (const k of Object.keys(r.headers)) {
      assert.ok(!k.startsWith('access-control-'), r.why + ' ' + k);
      assert.notStrictEqual(k, 'allow', r.why);
    }
  }
  assert.doesNotMatch(H.section(), /Access-Control/i);
});

// ---------------------------------------------------------------- T2b (cap, joined fixture, paths)

test('T2b item 1: the cap is 131072 bytes; a 100 row batch with every value at its maximum is accepted, 101 rows refused', async () => {
  const { TELEMETRY_MAX_BODY_BYTES } = H.loadSection().names;
  assert.strictEqual(TELEMETRY_MAX_BODY_BYTES, 131072);
  await withEndpoint(async (t, dir) => {
    const maxBatch = (n) => batch({ version: [1000, 1000, 1000], source: 4, rows: Array.from({ length: n }, () => H.maxRow()) });
    // the largest body a client builds, padded to the cap with JSON whitespace: still taken whole
    const full = JSON.stringify(maxBatch(100));
    const padded = full + ' '.repeat(131072 - Buffer.byteLength(full));
    let r = await post(t, padded, { headers: { 'content-length': String(Buffer.byteLength(padded)) } });
    assert.strictEqual(r.status, 200, r.body);
    assert.strictEqual(r.body, OK);
    assert.strictEqual(H.remoteCount(dir.dbPath), 100);
    // one row more is refused whole, and nothing of it is stored
    r = await post(t, JSON.stringify(maxBatch(101)));
    assert.strictEqual(r.status, 400);
    assert.strictEqual(r.body, '{"error":"invalid"}');
    assert.strictEqual(H.remoteCount(dir.dbPath), 100);
    // every stored value is the maximum, read back exactly (2^53 minus 1 included)
    for (const row of H.remoteRows(dir.dbPath)) for (const [k, , max] of H.csharpColumns()) assert.strictEqual(row[k], max, k);
  });
});

test('T2b item 2: the body the C# BuildBody wrote (Tests/Fixtures/telemetry-body-max.json) is read, parsed and stored as 100 rows', async () => {
  const body = H.fixtureBodyMax();
  assert.ok(body.length > 65536 && body.length <= 131072, 'between the old and the new cap: ' + body.length);
  const { parseTelemetryBatch } = H.loadSection().names;
  const parsed = parseTelemetryBatch(body.toString('latin1'));
  assert.ok(parsed, 'the parser takes it');
  assert.strictEqual(parsed.rows.length, 100);
  assert.deepStrictEqual(Array.from(parsed.version), [1000, 1000, 1000]);
  assert.strictEqual(parsed.source, 4);
  await withEndpoint(async (t, dir) => {
    // whole, with its Content-Length, as the client sends it
    let r = await post(t, body, { headers: { 'content-length': String(body.length) } });
    assert.strictEqual(r.status, 200, r.body);
    assert.strictEqual(r.body, OK);
    // and in small pieces with no Content-Length (the reader counts as the bytes come)
    const pieces = [];
    for (let i = 0; i < body.length; i += 1460) pieces.push(body.subarray(i, i + 1460));
    r = await post(t, pieces);
    assert.strictEqual(r.status, 200, r.body);
    const rows = H.remoteRows(dir.dbPath);
    assert.strictEqual(rows.length, 200);
    for (const row of rows) {
      assert.strictEqual(row.install_id, '0123456789abcdef0123456789abcdef');
      assert.deepStrictEqual([row.schema, row.version_major, row.version_minor, row.version_patch, row.source], [1, 1000, 1000, 1000, 4]);
      for (const [k, , max] of H.csharpColumns()) assert.strictEqual(row[k], max, k);
    }
  });
});

test('T2b item 4: the remote database and the stop file default to their own directory, /var/usurper/telemetry', () => {
  const { telemetryPaths } = H.loadSection().names;
  assert.deepStrictEqual(JSON.parse(JSON.stringify(telemetryPaths({}))),
    { dbPath: '/var/usurper/telemetry/remote_telemetry.db', stopPath: '/var/usurper/telemetry/remote_telemetry.stop' });
  // the environment still moves them; the stop file follows the database unless named itself
  assert.deepStrictEqual(JSON.parse(JSON.stringify(telemetryPaths({ REMOTE_TELEMETRY_DB_PATH: '/srv/t/r.db' }))), { dbPath: '/srv/t/r.db', stopPath: '/srv/t/remote_telemetry.stop' });
  assert.deepStrictEqual(JSON.parse(JSON.stringify(telemetryPaths({ REMOTE_TELEMETRY_STOP_FILE: '/etc/x.stop' }))),
    { dbPath: '/var/usurper/telemetry/remote_telemetry.db', stopPath: '/etc/x.stop' });
  // not the game database's directory itself: nothing else of the game lives in /var/usurper/telemetry
  const dir = telemetryPaths({}).dbPath.split('/').slice(0, -1).join('/');
  assert.strictEqual(dir, '/var/usurper/telemetry');
  assert.strictEqual(telemetryPaths({}).stopPath.split('/').slice(0, -1).join('/'), dir);
});
