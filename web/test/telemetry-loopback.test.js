'use strict';
// 1.2.7 (T2b item 3): the telemetry endpoint behind a real http server. This file holds the one
// socket the web tests open: a server from the ssh-proxy.js telemetry section bound to 127.0.0.1
// on a port the system picks (port 0), and raw clients that connect to that address only. Every
// address is an IP literal: the client's lookup throws if it is ever asked, and every lookup the
// server makes is recorded and must be a literal, so nothing resolves a name or leaves the machine.
// The remote database is a temp file. Each exchange waits a bounded time, so a reply that never
// comes or a socket left open fails in seconds instead of hanging.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const http = require('node:http');
const net = require('node:net');
const dns = require('node:dns');
const Database = require('better-sqlite3');
const H = require('./telemetry-api-harness');

const LOOPBACK = '127.0.0.1';
const BODY_TIMEOUT_MS = 300;    // the injected body time limit (10 s in the server)
const WAIT_MS = 3000;           // how long a client waits for the reply and the close

// Every name lookup made while the server runs: each must be an IP literal.
const lookups = [];
const realLookup = dns.lookup;
function recordLookups() {
  dns.lookup = function lookup(host, ...rest) { lookups.push(String(host)); return realLookup.call(this, host, ...rest); };
}
function restoreLookups() { dns.lookup = realLookup; }

// The endpoint with real timers and clock, the injected short body limit, and an http server for it.
async function withServer(fn) {
  const dir = H.makeDir();
  const loaded = H.loadSection();
  const bodyTimers = [];
  const ep = loaded.names.createTelemetryEndpoint({
    dbPath: dir.dbPath,
    stopPath: dir.stopPath,
    Database,
    fs,
    isIP: net.isIP,
    now: () => Date.now(),
    setTimeout: (f, ms) => { bodyTimers.push(ms); return setTimeout(f, ms); },
    clearTimeout,
    setInterval,
    clearInterval,
    console: loaded.ctx.console,
    bodyTimeoutMs: BODY_TIMEOUT_MS,
  });
  const server = http.createServer((req, res) => { ep.handle(req, res); });
  const sockets = new Set();
  server.on('connection', (s) => { sockets.add(s); s.on('close', () => sockets.delete(s)); });
  recordLookups();
  try {
    await new Promise((resolve, reject) => {
      server.once('error', reject);
      server.listen({ port: 0, host: LOOPBACK }, resolve);
    });
    const where = server.address();
    assert.strictEqual(where.address, LOOPBACK, 'bound to loopback only');
    assert.ok(where.port > 0, 'an ephemeral port');
    return await fn({ port: where.port, dir, bodyTimers, logs: loaded.logs });
  } finally {
    for (const s of sockets) s.destroy();
    await new Promise((resolve) => server.close(resolve));
    restoreLookups();
    ep.close();
    dir.cleanup();
    for (const h of lookups) assert.ok(net.isIP(h), 'a name was looked up: ' + h);
  }
}

// One raw exchange: connect to 127.0.0.1, write the parts (a string or Buffer each), never close our
// side, and wait at most WAIT_MS for the server to close. Resolves to what came back and whether
// the server closed the connection in time.
function exchange(port, parts) {
  return new Promise((resolve) => {
    const started = Date.now();
    const chunks = [];
    let settled = false;
    const sock = net.connect({
      host: LOOPBACK,
      port,
      lookup: () => { throw new Error('the client was asked to resolve a name'); },
    });
    const done = (closed, error) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      const data = Buffer.concat(chunks).toString('latin1');
      const peer = sock.remoteAddress;
      sock.destroy();
      resolve({ closed, error, data, peer, ms: Date.now() - started, ...parse(data) });
    };
    const timer = setTimeout(() => done(false, null), WAIT_MS);
    sock.on('connect', () => {
      assert.strictEqual(sock.remoteAddress, LOOPBACK);
      for (const p of parts) sock.write(p);
    });
    sock.on('data', (c) => chunks.push(c));
    sock.on('close', () => done(true, null));
    sock.on('error', (e) => { if (!settled && chunks.length === 0) done(true, e); });
    sock.setNoDelay(true);
  });
}

// The status, lower case headers and body of a raw HTTP/1.1 reply.
function parse(data) {
  const at = data.indexOf('\r\n\r\n');
  if (at < 0) return { status: 0, headers: {}, body: '' };
  const lines = data.slice(0, at).split('\r\n');
  const status = Number((lines[0].match(/^HTTP\/1\.1 (\d{3}) /) || [])[1] || 0);
  const headers = {};
  for (const l of lines.slice(1)) {
    const i = l.indexOf(':');
    headers[l.slice(0, i).trim().toLowerCase()] = l.slice(i + 1).trim();
  }
  let body = data.slice(at + 4);
  if (headers['transfer-encoding'] === 'chunked') body = dechunk(body);
  return { status, headers, body };
}

// The body of a chunked reply (the server sends these replies without a Content-Length).
function dechunk(raw) {
  let out = '';
  let i = 0;
  for (;;) {
    const eol = raw.indexOf('\r\n', i);
    if (eol < 0) return out + '<incomplete>';
    const size = parseInt(raw.slice(i, eol), 16);
    if (!(size > 0)) return out;
    out += raw.slice(eol + 2, eol + 2 + size);
    i = eol + 2 + size + 2;
  }
}

function head(port, length, extra = '') {
  return `POST /api/telemetry/v1/combat HTTP/1.1\r\nHost: ${LOOPBACK}:${port}\r\n` +
    `Content-Type: application/json\r\nContent-Length: ${length}\r\n${extra}\r\n`;
}

test('T2b item 3: a real server on 127.0.0.1 stores a valid batch, answers an oversized body 413 and closes, a stalled body 408', { timeout: 20000 }, async () => {
  await withServer(async ({ port, dir, bodyTimers }) => {
    // a valid batch: the C# BuildBody fixture, 100 rows at every maximum, in three writes
    const body = H.fixtureBodyMax();
    const third = Math.ceil(body.length / 3);
    let r = await exchange(port, [head(port, body.length, 'Connection: close\r\n'),
      body.subarray(0, third), body.subarray(third, 2 * third), body.subarray(2 * third)]);
    assert.strictEqual(r.status, 200, r.data.slice(0, 200));
    assert.strictEqual(r.body, '{"ok":true}');
    assert.strictEqual(r.peer, LOOPBACK);
    assert.strictEqual(H.remoteCount(dir.dbPath), 100);

    // oversized: a Content-Length over 131072 and the start of a body, then nothing more. The
    // reply is 413 with Connection: close, and the server closes the connection; the client never
    // closes its side, so only the server can have closed it.
    r = await exchange(port, [head(port, 200000), Buffer.alloc(512, 0x20)]);
    assert.strictEqual(r.status, 413, r.data);
    assert.strictEqual(r.body, '{"error":"too_large"}');
    assert.strictEqual(r.closed, true, 'the server closed the connection after the 413');
    assert.strictEqual(r.headers.connection, 'close');
    assert.ok(r.ms < WAIT_MS);

    // stalled: 20 of 1000 bytes, then nothing. The injected body limit answers 408 and closes.
    const before = bodyTimers.length;
    r = await exchange(port, [head(port, 1000), '{"schema":1,"source"']);
    assert.strictEqual(r.status, 408, r.data);
    assert.strictEqual(r.body, '{"error":"timeout"}');
    assert.strictEqual(r.closed, true, 'the server closed the connection after the 408');
    assert.strictEqual(r.headers.connection, 'close');
    assert.ok(r.ms >= BODY_TIMEOUT_MS - 50, 'answered by the body timer, after ' + r.ms + ' ms');
    assert.deepStrictEqual(bodyTimers.slice(before), [BODY_TIMEOUT_MS], 'one body timer, the injected limit');

    // nothing of the refused requests was stored
    assert.strictEqual(H.remoteCount(dir.dbPath), 100);
  });
  assert.ok(lookups.every((h) => net.isIP(h)), 'only IP literals were looked up');
});
