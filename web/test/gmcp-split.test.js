'use strict';
// The /play telnet splitter (web/gmcp-split.js) and its wiring in ssh-proxy.js.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { wantsGmcp, gmcpEnvelope, createGmcpSplitter, MAX_FRAME_BYTES, MAX_SB_BYTES, MAX_SB_MS } = require('../gmcp-split');

const IAC = 0xff, SE = 0xf0, SB = 0xfa, WILL = 0xfb, WONT = 0xfc, DO = 0xfd, DONT = 0xfe, GMCP = 0xc9;

function gmcpFrame(text) {
  const body = Buffer.from(text, 'utf8');
  const escaped = [];
  for (const b of body) { escaped.push(b); if (b === IAC) escaped.push(IAC); }
  return Buffer.from([IAC, SB, GMCP, ...escaped, IAC, SE]);
}

// The game's opening offer (MudServer.cs:651) and its TTYPE probe.
const OFFER = Buffer.from([IAC, WILL, 0x01, IAC, WILL, 0x03, IAC, WILL, GMCP, IAC, DO, 0x18]);

function run(chunks, opts = {}) {
  const terminal = [];
  const gmcp = [];
  const replies = [];
  const fatal = [];
  const order = [];
  const s = createGmcpSplitter({
    onTerminal: (b) => { terminal.push(Buffer.from(b)); order.push('terminal'); },
    onGmcp: (j) => gmcp.push(JSON.parse(j)),
    onReply: (b) => replies.push(Buffer.from(b)),
    onFatal: (r) => { fatal.push(r); order.push('fatal'); },
    ...opts,
  });
  for (const c of chunks) { if (opts.tick) opts.tick(); s.push(c); }
  return { terminal: Buffer.concat(terminal), gmcp, replies, fatal, dead: s.dead, order };
}

// Every split of a buffer into two chunks, plus one byte at a time.
function splits(buf) {
  const out = [[buf], Array.from(buf, (b) => Buffer.of(b))];
  for (let i = 1; i < buf.length; i++) out.push([buf.subarray(0, i), buf.subarray(i)]);
  return out;
}

const VITALS = 'Char.Vitals {"hp":266,"maxHp":390,"mp":0,"maxMp":0,"sp":121,"mental":100,"maxMental":100}';

test('wantsGmcp: only gmcp=1 on the query', () => {
  assert.strictEqual(wantsGmcp('/ws?gmcp=1'), true);
  assert.strictEqual(wantsGmcp('/ws?x=2&gmcp=1'), true);
  assert.strictEqual(wantsGmcp('/ws'), false);
  assert.strictEqual(wantsGmcp('/'), false);
  assert.strictEqual(wantsGmcp('/ws?gmcp=0'), false);
  assert.strictEqual(wantsGmcp('/ws?gmcp=true'), false);
  assert.strictEqual(wantsGmcp('/ws?nogmcp=1'), false);
  assert.strictEqual(wantsGmcp(undefined), false);
});

test('answers DO GMCP once, only after the game sends WILL GMCP', () => {
  const r = run([OFFER, Buffer.from([IAC, WILL, GMCP])]);
  assert.deepStrictEqual(r.replies, [Buffer.from([IAC, DO, GMCP])]);
  assert.strictEqual(r.terminal.length, 0, 'negotiation never reaches the terminal');
});

test('no DO GMCP without WILL GMCP', () => {
  const r = run([Buffer.from([IAC, WILL, 0x01, IAC, WILL, 0x03, IAC, DO, 0x18, IAC, WONT, GMCP, IAC, DO, GMCP, IAC, DONT, GMCP]), Buffer.from('hello')]);
  assert.deepStrictEqual(r.replies, []);
  assert.strictEqual(r.terminal.toString(), 'hello');
});

test('a whole session splits the same way at every chunk boundary', () => {
  const stream = Buffer.concat([
    OFFER,
    Buffer.from('Welcome\r\n'),
    gmcpFrame(VITALS),
    Buffer.from('\x1b[1;31mThe zombie hits you\x1b[0m\r\n'),
    gmcpFrame('Comm.Channel.Text {"channel":"gossip","talker":"Ana","text":"hi"}'),
    gmcpFrame('Core.Ping'),
    Buffer.from([IAC, 0xf1]), // NOP
    Buffer.from('café ──\r\n', 'utf8'),
  ]);
  const want = Buffer.from('Welcome\r\n\x1b[1;31mThe zombie hits you\x1b[0m\r\ncafé ──\r\n', 'utf8');
  for (const chunks of splits(stream)) {
    const r = run(chunks);
    assert.ok(r.terminal.equals(want), 'terminal bytes at split ' + chunks[0].length);
    assert.deepStrictEqual(r.gmcp.map((g) => g.gmcp), ['Char.Vitals', 'Comm.Channel.Text', 'Core.Ping']);
    assert.deepStrictEqual(r.gmcp[0].data, { hp: 266, maxHp: 390, mp: 0, maxMp: 0, sp: 121, mental: 100, maxMental: 100 });
    assert.strictEqual(r.gmcp[2].data, null);
    assert.strictEqual(r.replies.length, 1);
    assert.deepStrictEqual(r.fatal, []);
  }
});

test('IAC IAC becomes one 0xFF, in text and inside a frame', () => {
  const raw = Buffer.concat([Buffer.from([IAC, SB, GMCP]), Buffer.from('Core.X "a'), Buffer.from([IAC, IAC]), Buffer.from('b"'), Buffer.from([IAC, SE])]);
  const r = run([Buffer.from([0x41, IAC, IAC, 0x42]), raw]);
  assert.ok(r.terminal.equals(Buffer.from([0x41, 0xff, 0x42])));
  assert.strictEqual(r.gmcp[0].data, 'a�b', 'the lone 0xFF byte is kept and decodes as U+FFFD');
});

test('a 16 KB payload, the most the game sends, passes whole', () => {
  const pad = 16384 - 'Char.Items.List {"items":[""]}'.length;
  const text = 'Char.Items.List {"items":["' + 'x'.repeat(pad) + '"]}';
  assert.strictEqual(Buffer.byteLength(text), 16384);
  for (const chunks of [[gmcpFrame(text)], [gmcpFrame(text).subarray(0, 7000), gmcpFrame(text).subarray(7000)]]) {
    const r = run(chunks);
    assert.strictEqual(r.gmcp.length, 1);
    assert.strictEqual(r.gmcp[0].data.items[0].length, pad);
    assert.ok(MAX_FRAME_BYTES >= 16384 + 1024, 'cap leaves room past the game cap');
  }
});

test('an oversized frame is dropped whole and the stream resyncs at IAC SE', () => {
  const big = 'Char.Items.List {"items":["' + 'x'.repeat(MAX_FRAME_BYTES) + '"]}';
  const r = run([Buffer.from('before '), gmcpFrame(big), Buffer.from('after'), gmcpFrame(VITALS)]);
  assert.strictEqual(r.terminal.toString(), 'before after');
  assert.deepStrictEqual(r.gmcp.map((g) => g.gmcp), ['Char.Vitals']);
  assert.deepStrictEqual(r.fatal, []);
});

test('bad JSON or a bad package name drops that frame only', () => {
  const r = run([gmcpFrame('Char.Vitals {not json'), gmcpFrame('<b>x</b> {}'), gmcpFrame(VITALS), Buffer.from('ok')]);
  assert.deepStrictEqual(r.gmcp.map((g) => g.gmcp), ['Char.Vitals']);
  assert.strictEqual(r.terminal.toString(), 'ok');
});

test('other subnegotiations are consumed, never shown', () => {
  const r = run([Buffer.from([IAC, SB, 0x18, 0x01, IAC, SE]), Buffer.from([IAC, SB, 0x18]), Buffer.from('Char.Vitals {}'), Buffer.from([IAC, SE]), Buffer.from('x')]);
  assert.strictEqual(r.terminal.toString(), 'x');
  assert.deepStrictEqual(r.gmcp, []);
});

test('an unknown IAC command inside a frame closes the session, with nothing after it shown', () => {
  const r = run([Buffer.concat([Buffer.from('a'), Buffer.from([IAC, SB, GMCP, 0x41, IAC, 0x41, 0x42]), Buffer.from('same chunk')]), Buffer.from('later')]);
  assert.strictEqual(r.terminal.toString(), 'a');
  assert.strictEqual(r.fatal.length, 1);
  assert.strictEqual(r.dead, true);
  assert.deepStrictEqual(r.order, ['terminal', 'fatal'], 'the text before the error is handed over before the close');
});

test('payload size boundaries: the game maximum, the proxy cap, one byte over', () => {
  // A run of digits stays valid JSON when cut short, so a cap that kept one byte too many would show.
  const payload = (n) => 'Core.N ' + '1'.repeat(n - 7);
  for (const [n, kept] of [[16379, true], [MAX_FRAME_BYTES, true], [MAX_FRAME_BYTES + 1, false]]) {
    const r = run([gmcpFrame(payload(n)), Buffer.from('ok')]);
    assert.strictEqual(r.gmcp.length, kept ? 1 : 0, 'payload ' + n);
    assert.strictEqual(r.terminal.toString(), 'ok');
    assert.deepStrictEqual(r.fatal, []);
  }
  assert.strictEqual(gmcpFrame(payload(16379)).length, 16384, 'a 16379 byte payload is the game cap of 16384 with framing');
});

test('an escaped IAC that crosses the cap still resyncs; a bad byte after an escaped IAC closes', () => {
  const head = Buffer.from('Core.X "');
  const body = Buffer.concat([head, Buffer.alloc(MAX_FRAME_BYTES - head.length, 0x61)]);
  const over = Buffer.concat([Buffer.from([IAC, SB, GMCP]), body, Buffer.from([IAC, IAC, 0x22, IAC, SE]), Buffer.from('ok')]);
  const r = run([over]);
  assert.deepStrictEqual(r.gmcp, []);
  assert.strictEqual(r.terminal.toString(), 'ok');
  const bad = run([Buffer.from([IAC, SB, GMCP, 0x41, IAC, IAC, IAC, 0x41]), Buffer.from('later')]);
  assert.strictEqual(bad.fatal.length, 1);
  assert.strictEqual(bad.terminal.length, 0);
});

test('a short frame left open past the time limit closes the session at the next bytes', () => {
  let t = 1000;
  const r = run([Buffer.from([IAC, SB, GMCP, 0x41]), Buffer.from('more text')], { now: () => t, tick: () => { t += MAX_SB_MS / 2; } });
  assert.deepStrictEqual(r.fatal, []);
  assert.strictEqual(r.terminal.length, 0);
  let u = 1000;
  const late = run([Buffer.from([IAC, SB, GMCP, 0x41]), Buffer.from('more text')], { now: () => u, tick: () => { u += MAX_SB_MS + 1; } });
  assert.strictEqual(late.fatal.length, 1);
  assert.strictEqual(late.terminal.length, 0);
  let v = 1000;
  const ok = run([Buffer.from([IAC, SB, GMCP]), Buffer.from('Core.Ping'), Buffer.from([IAC, SE, 0x41])], { now: () => v, tick: () => { v += 10; } });
  assert.deepStrictEqual(ok.gmcp.map((g) => g.gmcp), ['Core.Ping']);
  assert.strictEqual(ok.terminal.toString(), 'A');
});

test('a frame that never ends closes the session, and its bytes never reach the terminal', () => {
  const open = Buffer.concat([Buffer.from([IAC, SB, GMCP]), Buffer.alloc(MAX_SB_BYTES + 10, 0x41)]);
  const r = run([Buffer.from('a'), open.subarray(0, 30000), open.subarray(30000), Buffer.from('later')]);
  assert.strictEqual(r.terminal.toString(), 'a');
  assert.strictEqual(r.fatal.length, 1);
});

test('a frame still open at a chunk end writes nothing partial', () => {
  const r = run([Buffer.from([0x41, IAC, SB, GMCP, 0x42, 0x43])]);
  assert.strictEqual(r.terminal.toString(), 'A');
  assert.deepStrictEqual(r.gmcp, []);
  assert.deepStrictEqual(r.fatal, []);
});

test('gmcpEnvelope keeps hostile text as a JSON string value', () => {
  const hostile = '<img src=x onerror=alert(1)><script>alert(2)</script>';
  const json = gmcpEnvelope(Buffer.from('Comm.Channel.Text ' + JSON.stringify({ channel: 'gossip', talker: hostile, text: hostile })));
  assert.deepStrictEqual(JSON.parse(json), { gmcp: 'Comm.Channel.Text', data: { channel: 'gossip', talker: hostile, text: hostile } });
});

test('ssh-proxy.js splits only a gmcp=1 connection, inside the MUD bridge', () => {
  const src = fs.readFileSync(path.join(__dirname, '..', 'ssh-proxy.js'), 'utf8');
  assert.match(src, /const \{ wantsGmcp, createGmcpSplitter \} = require\('\.\/gmcp-split'\);/);
  const mud = src.slice(src.indexOf('  if (MUD_MODE) {\n    // MUD mode: connect directly'), src.indexOf('  } else {', src.indexOf('  if (MUD_MODE) {\n    // MUD mode: connect directly')));
  assert.ok(mud.length > 0);
  assert.match(mud, /const splitter = wantsGmcp\(req\.url\) \? createGmcpSplitter\(/);
  // Without the flag, the bytes go to the websocket unchanged, as before.
  assert.match(mud, /if \(splitter\) splitter\.push\(data\);\n\s+else ws\.send\(data\);/);
  assert.strictEqual(src.split('createGmcpSplitter(').length - 1, 1, 'one splitter site');
});
