'use strict';
// 1.3.11: the browser bridge in web/ssh-proxy.js writes its own lines (the client address line, then the client
// type line) to the game connection ahead of anything the browser sends. The MUD bridge block is cut from ssh-proxy.js and run in a vm context with a fake
// websocket. The first test records the writes on a fake socket; the second uses a real socket to a listener on
// 127.0.0.1 only, with two guards: the listener binds the loopback literal, and the connect wrapper refuses any
// other host before a socket is made.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const net = require('node:net');
const path = require('node:path');
const vm = require('node:vm');
const { wantsGmcp, createGmcpSplitter } = require('../gmcp-split');

const SRC = fs.readFileSync(path.join(__dirname, '..', 'ssh-proxy.js'), 'utf8');
const LOOPBACK = '127.0.0.1';
const CLIENT_IP = '203.0.113.5';
const WAIT_MS = 3000;

function slice(startMarker, endMarker) {
  const a = SRC.indexOf(startMarker);
  assert.ok(a !== -1, 'marker found: ' + startMarker);
  const b = SRC.indexOf(endMarker, a);
  assert.ok(b !== -1, 'end marker found: ' + endMarker);
  return SRC.slice(a, b);
}

// The MUD bridge block, closed so it runs on its own, and the resize filter it calls.
const BRIDGE = slice('  if (MUD_MODE) {\n    // MUD mode: connect directly', '  } else {') + '  }\n';
const RESIZE = slice('function isResizeControlMessage(', '\nconst MUD_HOST');

function fakeWs() {
  const handlers = {};
  return {
    OPEN: 1,
    readyState: 1,
    handlers,
    on(ev, f) { handlers[ev] = f; },
    send() {},
    close() { this.readyState = 3; },
  };
}

function runBridge(netLib, ws, port) {
  const ctx = vm.createContext({
    MUD_MODE: true,
    MUD_HOST: LOOPBACK,
    MUD_PORT: port,
    origin: '',
    req: { url: '/ws', headers: {} },
    clientIP: CLIENT_IP,
    ws,
    net: netLib,
    wantsGmcp,
    createGmcpSplitter,
    console: { log() {}, error() {}, warn() {} },
    Buffer,
  });
  vm.runInContext(RESIZE + '\n' + BRIDGE, ctx, { filename: 'ssh-proxy.js (bridge)' });
}

test('browser bytes follow the proxy lines', () => {
  const writes = [];
  let onConnect = null;
  const socket = {
    destroyed: false,
    write(d) { writes.push(Buffer.isBuffer(d) ? d.toString('utf8') : String(d)); return true; },
    on() {},
    destroy() { this.destroyed = true; },
  };
  const fakeNet = { connect(opts, cb) { assert.strictEqual(opts.host, LOOPBACK); onConnect = cb; return socket; } };
  const ws = fakeWs();
  runBridge(fakeNet, ws, 4001);
  ws.handlers.message(Buffer.from('typed early\n'));
  assert.ok(onConnect, 'a connect callback');
  onConnect();
  ws.handlers.message(Buffer.from('typed later\n'));
  assert.strictEqual(writes.length, 4);
  assert.match(writes[0], /^X-IP:203\.0\.113\.5\n$/);
  assert.match(writes[1], /^X-Client:Web\n$/);
  assert.strictEqual(writes[2], 'typed early\n');
  assert.strictEqual(writes[3], 'typed later\n');
});

test('over a real loopback socket the game reads the proxy lines first', async () => {
  const received = [];
  const server = net.createServer((s) => { s.on('data', (d) => received.push(d)); });
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen({ port: 0, host: LOOPBACK }, resolve); });
  const where = server.address();
  assert.strictEqual(where.address, LOOPBACK, 'bound to loopback only');
  const guardedNet = {
    connect(opts, cb) {
      if (opts.host !== LOOPBACK) throw new Error('test sockets stay on loopback; refused ' + opts.host);
      return net.connect(opts, cb);
    },
  };
  const ws = fakeWs();
  try {
    runBridge(guardedNet, ws, where.port);
    ws.handlers.message(Buffer.from('typed early\n'));
    const want = 'X-IP:203.0.113.5\nX-Client:Web\ntyped early\n';
    const deadline = Date.now() + WAIT_MS;
    while (Buffer.concat(received).length < want.length && Date.now() < deadline) await new Promise((r) => setTimeout(r, 20));
    assert.match(Buffer.concat(received).toString('utf8'), /^X-IP:203\.0\.113\.5\nX-Client:Web\ntyped early\n$/);
  } finally {
    if (ws.handlers.close) ws.handlers.close();
    await new Promise((resolve) => server.close(resolve));
  }
});
