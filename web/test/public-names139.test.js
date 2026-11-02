'use strict';
// 1.3.9: the public payloads of /api/stats, /api/dash/events and the SSE news event of /api/dash/feed, and the
// Discord who reply, carry character names only, never a login name (news.player_name, players.username, online_players.username). The
// sections are cut out of web/ssh-proxy.js and run in a vm context against an in memory game database made with
// the tables' real DDL, read from the C# (SqlSaveBackend.cs).
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const Database = require('better-sqlite3');

const ROOT = path.join(__dirname, '..', '..');
const PROXY = fs.readFileSync(path.join(ROOT, 'web', 'ssh-proxy.js'), 'utf8');
const BACKEND = fs.readFileSync(path.join(ROOT, 'Scripts', 'Systems', 'SqlSaveBackend.cs'), 'utf8');

// Login names that differ from every character name, so any copy of one in a payload is a login name.
const LOGIN_A = 'zqloginalpha';
const LOGIN_B = 'zqloginbravo';
const SHOWN_A = 'Aldric Stormborn';

function cut(text, start, end) {
  const a = text.indexOf(start);
  assert.ok(a >= 0, 'start not found: ' + start);
  const b = text.indexOf(end, a + start.length);
  assert.ok(b > a, 'end not found: ' + end);
  return text.slice(a, b);
}

function ddl(table) {
  const head = 'CREATE TABLE IF NOT EXISTS ' + table + ' (';
  const a = BACKEND.indexOf(head);
  assert.ok(a >= 0, 'table not found in SqlSaveBackend.cs: ' + table);
  const b = BACKEND.indexOf(');', a);
  return BACKEND.slice(a, b + 2);
}

function makeDb() {
  const db = new Database(':memory:');
  for (const t of ['players', 'world_state', 'news', 'online_players']) db.exec(ddl(t));
  const data = JSON.stringify({ player: { level: 7, class: 1 } });
  const addPlayer = db.prepare('INSERT INTO players (username, display_name, player_data) VALUES (?, ?, ?)');
  addPlayer.run(LOGIN_A, SHOWN_A, data);
  addPlayer.run(LOGIN_B, 'Brenna Ashfall', data);
  const addOnline = db.prepare('INSERT INTO online_players (username, display_name, location) VALUES (?, ?, ?)');
  addOnline.run(LOGIN_A, SHOWN_A, 'Main Street');
  addOnline.run(LOGIN_B, null, 'Inn');   // a row with no display name
  const addNews = db.prepare('INSERT INTO news (message, category, player_name) VALUES (?, ?, ?)');
  addNews.run(SHOWN_A + ' slew a goblin.', 'combat', LOGIN_A);
  addNews.run('Brenna Ashfall reached level 8.', 'level', LOGIN_B);
  addNews.run('Greta opened her shop.', 'npc', LOGIN_A);
  return db;
}

function run(code, ctx, names) {
  const context = Object.assign({ console: { log() {}, error() {}, warn() {} }, Date, JSON }, ctx);
  vm.createContext(context);
  return vm.runInContext(code + `\n;({ ${names} });`, context, { filename: 'ssh-proxy.js (section)' });
}

function assertNoLogin(payload, label) {
  const json = JSON.stringify(payload);
  for (const login of [LOGIN_A, LOGIN_B]) assert.ok(!json.includes(login), `${label} carries the login name ${login}: ${json}`);
  assert.ok(!json.includes('"playerName"'), `${label} carries a playerName field`);
  assert.ok(!json.includes('"username"'), `${label} carries a username field`);
}

test('/api/stats: online list, news and the rest carry character names, never a login name', () => {
  const db = makeDb();
  const section = cut(PROXY, 'let statsCache = null;', '// --- SSE Live Feed (public) ---');
  const { getStats } = run(section, {
    db, CACHE_TTL: 0, CLASS_NAMES: {}, GOD_TITLES: [],
    getDashNpcs: () => [], getRoyalCourtFromWorldState: () => null,
  }, 'getStats');
  const stats = JSON.parse(JSON.stringify(getStats()));
  assert.strictEqual(stats.error, undefined, 'the stats query ran');
  assert.deepStrictEqual(stats.online.map((o) => o.name).sort(), ['', SHOWN_A], 'a row with no display name sends an empty name');
  assert.strictEqual(stats.news.length, 2, 'the two news rows that are not npc');
  assert.ok(stats.news.every((n) => typeof n.message === 'string' && n.message.length > 0));
  assertNoLogin(stats, '/api/stats');
  db.close();
});

test('/api/dash/events: each news row carries id, message, category and time only', () => {
  const db = makeDb();
  const section = cut(PROXY, 'function getDashEvents() {', 'function getDashEventsHourly() {');
  const { getDashEvents } = run(section, {
    db, DASH_EVENTS_CACHE_TTL: 0, dashEventsCache: null, dashEventsCacheTime: 0,
  }, 'getDashEvents');
  const events = JSON.parse(JSON.stringify(getDashEvents()));
  assert.strictEqual(events.length, 3);
  for (const e of events) assert.deepStrictEqual(Object.keys(e).sort(), ['category', 'id', 'message', 'time']);
  assertNoLogin(events, '/api/dash/events');
  db.close();
});

test('the Discord who reply names characters, never a login name', () => {
  const db = makeDb();
  const section = cut(PROXY, 'function buildWhoResponse() {', 'function insertDiscordInbound(');
  const { buildWhoResponse } = run(section, { db, CLASS_NAMES: {} }, 'buildWhoResponse');
  const reply = buildWhoResponse();
  assert.ok(reply.startsWith('**2 adventurers in-game:**'), reply);
  assert.ok(reply.includes(`**${SHOWN_A}**`), reply);
  for (const login of [LOGIN_A, LOGIN_B]) assert.ok(!reply.includes(login), `the who reply carries the login name ${login}: ${reply}`);
  db.close();
});

test('/api/dash/feed: the news event carries message, category and time only', () => {
  const db = makeDb();
  const written = [];
  const section = cut(PROXY, 'function dashPollFeed() {', '// Dashboard heartbeat');
  const { dashPollFeed } = run(section, {
    db, dashLastNewsId: 0, dashSseClients: new Set([{ write: (m) => written.push(m) }]),
  }, 'dashPollFeed');
  dashPollFeed();
  assert.strictEqual(written.length, 1, 'one news event');
  const data = JSON.parse(written[0].split('\ndata: ')[1]);
  assert.strictEqual(data.items.length, 3);
  for (const item of data.items) assert.deepStrictEqual(Object.keys(item).sort(), ['category', 'message', 'time']);
  assertNoLogin(data, '/api/dash/feed');
  db.close();
});
