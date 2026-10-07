'use strict';
// 1.2.8 (CodeQL js/disabling-certificate-validation): method 2 of the admin SSL card.
// The Admin SSL Certificate Probe section of ssh-proxy.js is cut out by its markers and run in a
// vm context with no require, so it reaches a socket only through the tls it is handed.
// Two guards keep every connection on this machine:
//  1. the seam: the probe's host and port default to localhost:443, and every test that hands it
//     the real tls names 127.0.0.1 and a port of a server this file started there;
//  2. the hard check: any host other than localhost or 127.0.0.1 is refused before tls is touched.
// The guard test hands the probe a recording fake tls, never the real one, so with the hard check
// broken the attempt is recorded and no socket opens. On top of both, every real connect this file
// makes goes through a tripwire on net.Socket.prototype.connect that refuses anything but
// 127.0.0.1, and every name lookup is recorded and must be the literal 127.0.0.1.
// Certificates are made in a temp folder with the openssl command: one current self-signed
// certificate and one that expired in 2020.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const net = require('node:net');
const dns = require('node:dns');
const tls = require('node:tls');
const { EventEmitter } = require('node:events');
const { execFileSync } = require('node:child_process');

const LOOPBACK = '127.0.0.1';
const PROXY = path.join(__dirname, '..', 'ssh-proxy.js');
const START = '// --- Admin SSL Certificate Probe (1.2.8) ---';
const END = '// --- End Admin SSL Certificate Probe ---';
const NAME = 'usurper-reborn.net';
const DAY_MS = 86400000;

// ---------------------------------------------------------------- tripwire (whole file)

const connects = [];
const realConnect = net.Socket.prototype.connect;
net.Socket.prototype.connect = function connect(...args) {
  const o = args[0] && typeof args[0] === 'object' && !Array.isArray(args[0]) ? args[0] : { port: args[0], host: args[1] };
  connects.push(`${o.host}:${o.port}`);
  if (o.host !== LOOPBACK) throw new Error('test tripwire: connect to ' + o.host + ' refused');
  return realConnect.apply(this, args);
};
const lookups = [];
const realLookup = dns.lookup;
dns.lookup = function lookup(host, ...rest) { lookups.push(String(host)); return realLookup.call(this, host, ...rest); };

test.after(() => {
  net.Socket.prototype.connect = realConnect;
  dns.lookup = realLookup;
  console.log('ssl-probe real connects: ' + JSON.stringify(connects) + ' lookups: ' + JSON.stringify(lookups));
  assert.ok(connects.every((c) => c.startsWith(LOOPBACK + ':')), 'every real connect was to 127.0.0.1');
  assert.ok(lookups.every((h) => h === LOOPBACK), 'every lookup was the literal 127.0.0.1, so no name was resolved');
});

// ---------------------------------------------------------------- the section

function section() {
  const text = fs.readFileSync(PROXY, 'utf8');
  const a = text.indexOf(START);
  const b = text.indexOf(END, a);
  assert.ok(a >= 0 && b > a, 'probe section markers present');
  return text.slice(a, b);
}

function load() {
  const ctx = {};
  vm.createContext(ctx);
  return vm.runInContext(section() + `
;({ probeSslCertificate, applySslProbe, sslProbeTarget, assertSslProbeTarget,
   SSL_PROBE_DEFAULT_HOST, SSL_PROBE_DEFAULT_PORT, SSL_PROBE_HOSTS, SSL_PROBE_SERVERNAME });`,
  ctx, { filename: 'ssh-proxy.js (ssl probe section)' });
}

// A tls stand-in that records every connect and opens nothing. behave(sock, callback) decides
// what the fake socket does next (call back, emit an error, or nothing).
function fakeTls(behave) {
  const calls = [];
  return {
    calls,
    connect(options, callback) {
      calls.push({ ...options });
      const sock = new EventEmitter();
      sock.destroyed = false;
      sock.destroy = () => { sock.destroyed = true; };
      sock.setTimeout = () => {};
      sock.getPeerCertificate = () => null;
      if (behave) process.nextTick(() => behave(sock, callback));
      return sock;
    },
  };
}

// ---------------------------------------------------------------- certificates and a server

function makeCerts() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ssl-probe-'));
  const run = (args) => execFileSync('openssl', args, { cwd: dir, stdio: ['ignore', 'pipe', 'pipe'] });
  const ec = ['-newkey', 'ec', '-pkeyopt', 'ec_paramgen_curve:P-256', '-nodes'];
  const subject = ['-subj', '/CN=' + NAME, '-addext', 'subjectAltName=DNS:' + NAME];
  run(['req', '-x509', ...ec, '-keyout', 'current.key', '-out', 'current.crt', '-days', '30', ...subject]);
  // An expired certificate needs explicit dates, which only the ca command takes.
  fs.writeFileSync(path.join(dir, 'index.txt'), '');
  fs.writeFileSync(path.join(dir, 'serial'), '01\n');
  fs.writeFileSync(path.join(dir, 'ca.cnf'), [
    '[ ca ]', 'default_ca = d', '[ d ]', 'database = index.txt', 'new_certs_dir = .', 'serial = serial',
    'default_md = sha256', 'policy = p', 'copy_extensions = copy', 'unique_subject = no', '[ p ]', 'commonName = supplied', '',
  ].join('\n'));
  run(['req', '-new', ...ec, '-keyout', 'expired.key', '-out', 'expired.csr', ...subject]);
  run(['ca', '-batch', '-selfsign', '-config', 'ca.cnf', '-keyfile', 'expired.key', '-in', 'expired.csr', '-out', 'expired.crt',
    '-startdate', '20200101000000Z', '-enddate', '20200102000000Z', '-notext']);
  const read = (f) => fs.readFileSync(path.join(dir, f));
  const out = {
    current: { key: read('current.key'), cert: read('current.crt') },
    expired: { key: read('expired.key'), cert: read('expired.crt') },
  };
  fs.rmSync(dir, { recursive: true, force: true });
  return out;
}

const CERTS = makeCerts();

async function withServer(pair, fn) {
  const server = tls.createServer({ key: pair.key, cert: pair.cert }, (s) => s.end());
  server.on('tlsClientError', () => {});
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen({ port: 0, host: LOOPBACK }, resolve);
  });
  const where = server.address();
  assert.strictEqual(where.address, LOOPBACK, 'bound to loopback only');
  try {
    return await fn(where.port);
  } finally {
    await new Promise((resolve) => server.close(resolve));
  }
}

// ---------------------------------------------------------------- guards first

test('guard 1, the seam: with no host or port the probe aims at localhost:443', async () => {
  const S = load();
  assert.strictEqual(S.SSL_PROBE_DEFAULT_HOST, 'localhost');
  assert.strictEqual(S.SSL_PROBE_DEFAULT_PORT, 443);
  assert.deepStrictEqual({ ...S.sslProbeTarget({}) }, { host: 'localhost', port: 443 });
  assert.deepStrictEqual({ ...S.sslProbeTarget() }, { host: 'localhost', port: 443 });
  const fake = fakeTls((sock) => sock.emit('error', Object.assign(new Error('refused'), { code: 'ECONNREFUSED' })));
  const outcome = await S.probeSslCertificate({ tls: fake });
  assert.deepStrictEqual(fake.calls.map((c) => [c.host, c.port, c.servername]), [['localhost', 443, NAME]]);
  assert.deepStrictEqual({ ...outcome }, { status: 'unavailable', code: 'ECONNREFUSED' });
});

test('guard 1, the seam: the server calls the probe with tls only, so the default target is used', () => {
  const text = fs.readFileSync(PROXY, 'utf8');
  const calls = text.match(/probeSslCertificate\(\{[^}]*\}\)/g) || [];
  assert.deepStrictEqual(calls, ["probeSslCertificate({ tls: require('tls') })"]);
});

test('guard 2, the hard check: a host other than localhost or 127.0.0.1 is refused before any socket opens', () => {
  const S = load();
  assert.deepStrictEqual([...S.SSL_PROBE_HOSTS], ['localhost', LOOPBACK]);
  // IP literals and names; none is ever handed to a real tls or resolved.
  for (const host of ['203.0.113.77', NAME, '127.0.0.2', '::1', 'LOCALHOST', 'localhost.', '', null, 0]) {
    const fake = fakeTls(null);
    assert.throws(() => S.probeSslCertificate({ tls: fake, host, port: 443 }), /SSL probe refused/, `host ${host} refused`);
    assert.strictEqual(fake.calls.length, 0, `no connect attempted for ${host}`);
  }
  for (const port of [0, 65536, -1, 1.5, '443']) {
    const fake = fakeTls(null);
    assert.throws(() => S.probeSslCertificate({ tls: fake, host: LOOPBACK, port }), /SSL probe refused/, `port ${port} refused`);
    assert.strictEqual(fake.calls.length, 0);
  }
  assert.doesNotThrow(() => S.assertSslProbeTarget({ host: 'localhost', port: 443 }));
  assert.doesNotThrow(() => S.assertSslProbeTarget({ host: LOOPBACK, port: 8443 }));
});

// ---------------------------------------------------------------- the card over real TLS on 127.0.0.1

test('card: an expired certificate is reported as a validation failure, CERT_HAS_EXPIRED, expiry unknown', async () => {
  const S = load();
  await withServer(CERTS.expired, async (port) => {
    const outcome = await S.probeSslCertificate({ tls, host: LOOPBACK, port, timeoutMs: 3000 });
    assert.strictEqual(outcome.status, 'invalid');
    assert.strictEqual(outcome.code, 'CERT_HAS_EXPIRED');
    assert.strictEqual(outcome.cert, null);
    const result = S.applySslProbe({ domain: NAME, expiresAt: null, daysRemaining: null, issuer: null }, outcome, Date.now());
    assert.deepStrictEqual({ ...result }, {
      domain: NAME, expiresAt: null, daysRemaining: null, issuer: null, certValid: false, validationError: 'CERT_HAS_EXPIRED',
    });
  });
});

test('card: a current self-signed certificate is reported as a validation failure, not read through', async () => {
  const S = load();
  await withServer(CERTS.current, async (port) => {
    const outcome = await S.probeSslCertificate({ tls, host: LOOPBACK, port, timeoutMs: 3000 });
    assert.strictEqual(outcome.status, 'invalid');
    assert.strictEqual(outcome.code, 'DEPTH_ZERO_SELF_SIGNED_CERT');
    const result = S.applySslProbe({ expiresAt: null }, outcome, Date.now());
    assert.strictEqual(result.certValid, false);
    assert.strictEqual(result.validationError, 'DEPTH_ZERO_SELF_SIGNED_CERT');
    assert.strictEqual(result.expiresAt, null, 'a rejected certificate gives no expiry');
  });
});

test('card: a closed port on 127.0.0.1 is unavailable, not a validation failure', async () => {
  const S = load();
  const port = await withServer(CERTS.current, async (p) => p); // a port that was just closed
  const outcome = await S.probeSslCertificate({ tls, host: LOOPBACK, port, timeoutMs: 3000 });
  assert.strictEqual(outcome.status, 'unavailable');
  assert.strictEqual(outcome.code, 'ECONNREFUSED');
  const result = S.applySslProbe({ expiresAt: null }, outcome, Date.now());
  assert.deepStrictEqual({ ...result }, { expiresAt: null });
});

// ---------------------------------------------------------------- the paths a real server cannot give without trust

test('card: a certificate that validates is read for expiry, days left and issuer', async () => {
  const S = load();
  const validTo = 'Jan  1 00:00:00 2031 GMT';
  const fake = fakeTls((sock, cb) => {
    sock.getPeerCertificate = () => ({ valid_to: validTo, issuer: { O: "Let's Encrypt", CN: 'R11' } });
    cb();
  });
  const outcome = await S.probeSslCertificate({ tls: fake });
  assert.strictEqual(fake.calls[0].rejectUnauthorized, true, 'validation on');
  assert.strictEqual(outcome.status, 'valid');
  const now = Date.UTC(2030, 11, 22);
  const result = S.applySslProbe({ expiresAt: null }, outcome, now);
  assert.strictEqual(result.certValid, true);
  assert.strictEqual(result.validationError, undefined);
  assert.strictEqual(result.expiresAt, '2031-01-01T00:00:00.000Z');
  assert.strictEqual(result.daysRemaining, Math.floor((Date.UTC(2031, 0, 1) - now) / DAY_MS));
  assert.strictEqual(result.issuer, "Let's Encrypt");
});

test('card: a name mismatch keeps the certificate the error carries, so the expiry is still shown', async () => {
  const S = load();
  const fake = fakeTls((sock) => {
    sock.authorizationError = 'ERR_TLS_CERT_ALTNAME_INVALID';
    const err = Object.assign(new Error('altname'), { code: 'ERR_TLS_CERT_ALTNAME_INVALID', cert: { valid_to: 'Mar  1 00:00:00 2031 GMT', issuer: { O: 'Example CA' } } });
    sock.emit('error', err);
  });
  const outcome = await S.probeSslCertificate({ tls: fake });
  assert.strictEqual(outcome.status, 'invalid');
  const result = S.applySslProbe({ expiresAt: null }, outcome, Date.UTC(2031, 0, 30));
  assert.strictEqual(result.certValid, false);
  assert.strictEqual(result.validationError, 'ERR_TLS_CERT_ALTNAME_INVALID');
  assert.strictEqual(result.expiresAt, '2031-03-01T00:00:00.000Z');
  assert.strictEqual(result.daysRemaining, 30);
});

test('ssh-proxy.js never turns certificate validation off', () => {
  const text = fs.readFileSync(PROXY, 'utf8');
  assert.ok(!/rejectUnauthorized\s*:\s*false/.test(text), 'no rejectUnauthorized: false');
  assert.ok(!/NODE_TLS_REJECT_UNAUTHORIZED/.test(text), 'no NODE_TLS_REJECT_UNAUTHORIZED');
});
