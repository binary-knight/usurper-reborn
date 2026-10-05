'use strict';
// 1.2.7 balance dashboard, Difficulty tab, Source filter (T4 table rows 1 to 11 and A to C).
// The balance section runs in a vm (balance-api-harness) with the telemetry section's real
// endpoint (telemetry-api-harness) standing in for the server's telemetryEndpoint, over temp
// database files: no server starts and no socket opens. One test per row, named by its row.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const Database = require('better-sqlite3');
const H = require('./telemetry-api-harness');
const B = require('./balance-api-harness');
const { HOSTILE, loadPage, assertSafe, readPage } = require('./harness');

const ROOT = path.join(__dirname, '..', '..');
const DAY_MS = H.DAY_MS;
// 2026-10-05 15:00 UTC, day 20366
const NOW = H.T0 + 3 * H.HOUR_MS;
const DAY0 = Math.floor(NOW / DAY_MS);
// install ids no response may ever hold
const IDS = ['feedfacefeedfacefeedfacefeedface', 'cafebabecafebabecafebabecafebabe', '0badc0de0badc0de0badc0de0badc0de'];
const UNVERIFIED = "Unverified: sent by players' copies, not checked by the server";
const SOURCES = [['official', 'Official'], ['remote', 'Remote all'], ['1', 'Remote single player'], ['2', 'Remote Steam'],
  ['3', 'Remote BBS'], ['4', 'Remote server']];

// Inserts rows straight into the remote table: each row is the fixture's good row with the
// given changes (batch fields included).
function insertRemote(dbPath, rows) {
  const d = new Database(dbPath, { fileMustExist: true });
  try {
    const cols = d.prepare('PRAGMA table_info(remote_combat_events)').all().map((c) => c.name);
    const st = d.prepare(`INSERT INTO remote_combat_events (${cols.join(', ')}) VALUES (${cols.map(() => '?').join(', ')})`);
    const base = Object.assign({ schema: 1, version_major: 1, version_minor: 2, version_patch: 7, source: 1, install_id: IDS[0], received_day: DAY0 }, H.goodRow());
    d.transaction(() => { for (const r of rows) { const v = Object.assign({}, base, r); st.run(...cols.map((c) => v[c])); } })();
  } finally { d.close(); }
}

// A value made in the vm, as a plain value of this realm (for deepStrictEqual).
function plain(v) { return JSON.parse(JSON.stringify(v)); }

function repeat(n, row) { return Array.from({ length: n }, () => Object.assign({}, row)); }

// Official rows: 1.2.6 rows in the last day.
function fillOfficial(w) {
  B.insertFight(w, { player_name: 'NewOne', outcome: 'victory', rounds: 1, ago: '-20 hours', tally: B.tallyOf({ floor_actual: 3 }) });
  B.insertFight(w, { player_name: 'NewTwo', player_class: 'Mage', outcome: 'death', is_boss: 1, ago: '-10 hours', tally: B.tallyOf({ floor_actual: 8 }) });
  B.insertFight(w, { player_name: 'NewTwo', player_class: 'Mage', outcome: 'fled', ago: '-2 hours', tally: B.tallyOf({ floor_actual: 12, difficulty: 2 }) });
}

// The remote file (made unless remote: false), the endpoint over it (whose writer makes the
// table), rows put in, an official database and the balance routes over both.
async function withRemote(rows, fn, options = {}) {
  const dir = H.makeDir({ remote: options.remote });
  const t = H.makeEndpoint(dir, { t: NOW });
  if (rows) insertRemote(dir.dbPath, typeof rows === 'function' ? rows() : rows);
  const game = B.makeDb(options.fillOfficial || fillOfficial);
  const a = B.loadApi({ db: game.db, endpoint: t.ep, now: NOW, locked: options.locked });
  try {
    return await fn(a, t, dir, game);
  } finally {
    t.ep.close();
    game.close();
    dir.cleanup();
  }
}

// The balance page over the given routes (a.call), logged in.
async function pageOver(a) {
  const p = await loadPage('balance.html', {
    storage: { balance_token: a.token },
    fetch: async (url, init) => a.call(url, { token: String(init.headers.Authorization).slice(7) }),
  });
  await p.settle();
  return p;
}

async function chooseSource(p, value) {
  const sel = p.el('diff-source');
  sel.value = value;
  p.doc.dispatch(sel, 'change');
  await p.settle();
}

function cells(p, id) {
  return p.el(id).querySelectorAll('tbody tr').map((tr) => tr.querySelectorAll('td').map((td) => td.textContent));
}

function headers(p, id) {
  return p.el(id).querySelectorAll('th').map((th) => th.textContent);
}

function notes(p) {
  return p.el('difficulty-table').querySelectorAll('.table-note').map((n) => n.textContent);
}

const VIEW_SRC = () => B.slice(B.proxySource(), '// --- Balance Remote View (1.2.7) ---', '// --- End Balance Remote View ---');
const BALANCE_SRC = () => B.slice(B.proxySource(), '// --- Balance Dashboard API ---', 'async function handleDashRequest');

// ---------------------------------------------------------------- row 1

test('row 1: a Source select, Official first and chosen; the API takes official, remote and 1 to 4, anything else 400', async () => {
  await withRemote([{}, { source: 2 }], async (a) => {
    const p = await pageOver(a);
    const opts = p.el('diff-source').querySelectorAll('option').map((o) => [o.getAttribute('value'), o.textContent]);
    assert.deepStrictEqual(opts, SOURCES);
    const urls = p.calls.fetch.map((c) => c.url).filter((u) => u.includes('/difficulty'));
    assert.deepStrictEqual(urls, ['/api/balance/difficulty?window=since126'], 'Official sends no source');
    p.calls.fetch.length = 0;
    await chooseSource(p, '2');
    assert.deepStrictEqual(p.calls.fetch.map((c) => c.url), ['/api/balance/difficulty?source=2&window=since126']);
    assert.deepStrictEqual(plain(Object.keys(a.api.BALANCE_SOURCES)), ['1', '2', '3', '4', 'official', 'remote']);
    const official = (await a.call('/api/balance/difficulty?source=official')).body;
    assert.deepStrictEqual(official, (await a.call('/api/balance/difficulty')).body);
    for (const s of ['remote', '1', '2', '3', '4']) {
      const r = await a.call('/api/balance/difficulty?source=' + s);
      assert.strictEqual(r.status, 200, s);
      assert.strictEqual(r.body.source, s);
    }
    assert.strictEqual((await a.call('/api/balance/difficulty?source=2')).body.fights, 1);
    assert.strictEqual((await a.call('/api/balance/difficulty?source=remote')).body.fights, 2);
    const readsBefore = a.remoteCalls.length;
    for (const s of ['', 'all', 'ALL', '0', '5', '01', '1.0', ' 1', 'Remote', 'Official', '__proto__', 'constructor', 'toString', 'hasOwnProperty']) {
      const r = await a.call('/api/balance/difficulty?source=' + encodeURIComponent(s));
      assert.strictEqual(r.status, 400, JSON.stringify(s));
      assert.match(r.body.error, /^Unknown source filter/, JSON.stringify(s));
    }
    assert.strictEqual(a.remoteCalls.length, readsBefore, 'a refused source reads nothing');
  });
});

// ---------------------------------------------------------------- row 2

// The tables a statement names after FROM or JOIN (the CTE names w and f left out).
function tablesOf(sql) {
  return [...new Set([...sql.matchAll(/\b(?:FROM|JOIN)\s+([A-Za-z_][\w]*)/gi).map((m) => m[1])])].filter((t) => t !== 'w' && t !== 'f');
}

test('row 2: never mixed: Official reads only combat_events, Remote only remote_combat_events, no statement names both', async () => {
  await withRemote([{}, { source: 3 }], async (a) => {
    a.prepared.length = 0;
    await a.call('/api/balance/difficulty');
    await a.call('/api/balance/difficulty?source=official&class=Mage&difficulty=2');
    assert.ok(a.prepared.length >= 4);
    for (const s of a.prepared) assert.deepStrictEqual(tablesOf(s.sql), ['combat_events'], s.sql);
    assert.deepStrictEqual(a.remotePrepared, []);
    a.prepared.length = 0;
    for (const s of ['remote', '1', '2', '3', '4']) {
      await a.call('/api/balance/difficulty?source=' + s + '&window=7d&class=Warrior&difficulty=1');
    }
    assert.deepStrictEqual(a.prepared, [], 'a remote view runs no statement on the game database');
    assert.strictEqual(a.remotePrepared.length, 5);
    for (const s of a.remotePrepared) assert.deepStrictEqual(tablesOf(s.sql), ['remote_combat_events'], s.sql);
  });
  // every SQL text in the file that names the remote table names no other table, and nothing attaches
  const src = B.proxySource();
  const literals = src.match(/`[^`]*`|'(?:[^'\\\n]|\\.)*'|"(?:[^"\\\n]|\\.)*"/g);
  const remote = literals.filter((l) => /remote_combat_events/.test(l));
  assert.ok(remote.length >= 4, 'the writer statements and the view');
  for (const l of remote) {
    assert.doesNotMatch(l, /(?<!remote_)combat_events|npc_decision_log|onboarding_events|player_name/, l.slice(0, 200));
    assert.doesNotMatch(l, /\bJOIN\b/i, l.slice(0, 200));
  }
  assert.doesNotMatch(src, /\bATTACH\b/i);
  // no choice counts both sources: each remote source is a range of 1..4, official has none
  const { BALANCE_SOURCES } = B.loadApi().api;
  assert.strictEqual(BALANCE_SOURCES.official, null);
  for (const [k, v] of Object.entries(BALANCE_SOURCES)) {
    if (k === 'official') continue;
    assert.ok(v.from >= 1 && v.to <= 4 && v.from <= v.to, k);
  }
  assert.doesNotMatch(readPage('balance.html').match(/<select id="diff-source">[\s\S]*?<\/select>/)[0], /value="all"|All sources/i);
});

// ---------------------------------------------------------------- row 3

test('row 3: the remote view reads through its own read-only connection, never the writer; no file is an empty view', async () => {
  await withRemote([{}], async (a, t, dir) => {
    const before = t.opened.length;
    assert.ok(before >= 1 && t.opened.every((o) => !o.opts.readonly), 'the writer, opened at start');
    await a.call('/api/balance/difficulty');
    assert.strictEqual(t.opened.length, before, 'Official opens nothing');
    const r = await a.call('/api/balance/difficulty?source=remote');
    assert.strictEqual(r.status, 200);
    assert.strictEqual(r.body.fights, 1);
    const opened = t.opened.slice(before);
    assert.deepStrictEqual(plain(opened.map((o) => [o.file, o.opts])), [[dir.dbPath, { readonly: true, fileMustExist: true }]]);
    const handle = t.ep.reader();
    assert.strictEqual(handle.readonly, true);
    assert.throws(() => handle.prepare('DELETE FROM remote_combat_events').run(), /readonly/i);
    await a.call('/api/balance/difficulty?source=1');
    assert.strictEqual(t.opened.length, before + 1, 'opened once, kept');
    // the writer still writes after the reader opened
    const p = await H.send(t, { body: H.bodyOf() });
    assert.strictEqual(p.status, 200);
    assert.strictEqual((await a.call('/api/balance/difficulty?source=remote')).body.fights, 2);
  });
  // no remote file: an empty view with a note, the file not created, and opened once it exists
  await withRemote(null, async (a, t, dir) => {
    const r = await a.call('/api/balance/difficulty?source=remote');
    assert.strictEqual(r.status, 200);
    assert.strictEqual(r.body.missing, true);
    assert.deepStrictEqual(r.body.bands, []);
    assert.strictEqual(fs.existsSync(dir.dbPath), false);
    const p = await pageOver(a);
    await chooseSource(p, 'remote');
    assert.ok(notes(p).includes('No remote telemetry on this server yet.'), notes(p).join('\n'));
    assert.match(p.el('difficulty-table').textContent, /No remote fights in this window/);
    assert.deepStrictEqual(p.errors, []);
    // the operator makes the file; the writer has not opened it yet, so it holds no table
    new Database(dir.dbPath).close();
    assert.strictEqual((await a.call('/api/balance/difficulty?source=remote')).body.missing, true);
    assert.strictEqual(H.remoteCount(dir.dbPath), 0);
    await H.send(t, { body: H.bodyOf() });
    const after = (await a.call('/api/balance/difficulty?source=remote')).body;
    assert.strictEqual(after.missing, false);
    assert.strictEqual(after.fights, 1);
  }, { remote: false });
});

// ---------------------------------------------------------------- row 4

test('row 4: every remote view says Unverified above the table and in the JSON; official views never do', async () => {
  await withRemote([{}], async (a) => {
    for (const s of ['remote', '1', '2', '3', '4']) {
      const r = (await a.call('/api/balance/difficulty?source=' + s)).body;
      assert.strictEqual(r.unverified, true, s);
      assert.strictEqual(r.unverifiedText, UNVERIFIED, s);
    }
    const off = await a.call('/api/balance/difficulty');
    assert.ok(!('unverified' in off.body));
    assert.doesNotMatch(JSON.stringify(off.body), /Unverified/i);
    const p = await pageOver(a);
    assert.doesNotMatch(p.el('difficulty-table').textContent, /Unverified/i);
    for (const s of ['remote', '1', '2', '3', '4']) {
      await chooseSource(p, s);
      const html = p.el('difficulty-table').innerHTML;
      const label = p.el('difficulty-table').querySelectorAll('.table-note.unverified').map((n) => n.textContent);
      assert.deepStrictEqual(label, [UNVERIFIED], s);
      if (s === '1') assert.ok(html.indexOf('Unverified') < html.indexOf('<table'), 'above the table');
    }
    // with no rows too
    await chooseSource(p, '4');
    assert.match(p.el('difficulty-table').textContent, /No remote fights/);
    assert.ok(p.el('difficulty-table').textContent.startsWith(UNVERIFIED));
    await chooseSource(p, 'official');
    assert.doesNotMatch(p.el('difficulty-table').textContent, /Unverified/i);
  });
});

// ---------------------------------------------------------------- row 5

// CharacterClass from Scripts/Core/Character.cs: identifiers only, comments left out.
function csharpClasses() {
  const src = fs.readFileSync(path.join(ROOT, 'Scripts', 'Core', 'Character.cs'), 'utf8');
  const body = src.match(/public enum CharacterClass\s*\{([\s\S]*?)\}/)[1];
  return body.split('\n').map((l) => l.replace(/\/\/.*$/, '').trim()).join(' ').split(',')
    .map((s) => s.trim()).filter(Boolean);
}

test('row 5: outcomes 0, 1, 2 are victory, fled, death; the class filter is the CharacterClass index; windows by UTC day', async () => {
  assert.deepStrictEqual(plain(B.loadApi().api.BALANCE_CLASS_NAMES), csharpClasses());
  assert.strictEqual(csharpClasses().length, 17);
  assert.deepStrictEqual(H.csharpColumns().find((c) => c[0] === 'player_class'), ['player_class', 0, 16]);
  const rows = [
    { outcome: 0, player_class: 10 }, { outcome: 0, player_class: 10 }, { outcome: 1, player_class: 10 }, { outcome: 2, player_class: 10 },
    { outcome: 0, player_class: 0, floor_actual: 30 }, { outcome: 2, player_class: 16, floor_actual: 30 }, { outcome: 1, player_class: 11, floor_actual: 30 },
    // days back from today: 1, 2, 7, 8, 30, 31
    ...[1, 2, 7, 8, 30, 31].map((back) => ({ received_day: DAY0 - back, floor_actual: 60, player_class: 9 })),
  ];
  await withRemote(rows, async (a) => {
    const warrior = (await a.call('/api/balance/difficulty?source=remote&class=Warrior')).body;
    assert.deepStrictEqual(warrior.bands.map((b) => [b.floor_from, b.fights, b.win_pct, b.flee_pct, b.death_pct]), [[6, 4, 50, 25, 25]]);
    assert.deepStrictEqual(warrior.filters, { class: 'Warrior', difficulty: null, source: 'remote' });
    for (const [name, outcome] of [['Alchemist', 'win_pct'], ['MysticShaman', 'death_pct'], ['Tidesworn', 'flee_pct'], ['Sage', 'win_pct']]) {
      const b = (await a.call('/api/balance/difficulty?source=remote&window=30d&class=' + name)).body.bands;
      assert.strictEqual(b.length, 1, name);
      assert.strictEqual(b[0][outcome], 100, name);
    }
    for (const name of ['Assassin', 'Voidreaver']) {
      assert.deepStrictEqual((await a.call('/api/balance/difficulty?source=remote&class=' + name)).body.bands, [], name);
    }
    const all = (await a.call('/api/balance/difficulty?source=remote')).body;
    assert.deepStrictEqual(all.classes, ['Alchemist', 'Sage', 'Warrior', 'Tidesworn', 'MysticShaman']);
    for (const bad of ['Mage', 'warrior', 'Warrior ', 'Mystic Shaman', 'Class']) {
      assert.strictEqual((await a.call('/api/balance/difficulty?source=remote&class=' + encodeURIComponent(bad))).status, 400, bad);
    }
    // the window starts at the start of the UTC day of (now minus the window)
    const count = async (w) => (await a.call('/api/balance/difficulty?source=remote&window=' + w)).body;
    const d24 = await count('24h');
    assert.deepStrictEqual([d24.window.fromDay, d24.window.from, d24.fights], [DAY0 - 1, '2026-10-04', 8]);
    const d7 = await count('7d');
    assert.deepStrictEqual([d7.window.fromDay, d7.window.from, d7.fights], [DAY0 - 7, '2026-09-28', 10]);
    const d30 = await count('30d');
    assert.deepStrictEqual([d30.window.fromDay, d30.fights], [DAY0 - 30, 12]);
    const since = await count('since126');
    assert.deepStrictEqual([since.window.fromDay, since.window.from, since.fights], [0, null, 13]);
    assert.strictEqual((await a.call('/api/balance/difficulty?source=remote&window=1y')).status, 400);
    assert.deepStrictEqual(plain(Object.keys(a.api.BALANCE_REMOTE_WINDOW_MS)), plain(Object.keys(a.api.BALANCE_WINDOWS)));
  });
});

// ---------------------------------------------------------------- row 6

test('row 6: remote views count Installs (distinct install ids), not Players, and say a BBS or server shares one id', async () => {
  const rows = [{ install_id: IDS[0] }, { install_id: IDS[0] }, { install_id: IDS[1] }, { install_id: IDS[2], is_boss: 1 }];
  await withRemote(rows, async (a) => {
    const r = (await a.call('/api/balance/difficulty?source=remote')).body;
    assert.deepStrictEqual(r.bands.map((b) => [b.boss, b.fights, b.installs]), [[0, 3, 2], [1, 1, 1]]);
    for (const b of r.bands) assert.ok(!('players' in b));
    const p = await pageOver(a);
    assert.strictEqual(headers(p, 'difficulty-table')[3], 'Players', 'Official keeps Players');
    await chooseSource(p, 'remote');
    const h = headers(p, 'difficulty-table');
    assert.strictEqual(h[3], 'Installs');
    assert.ok(!h.includes('Players'));
    assert.deepStrictEqual(cells(p, 'difficulty-table').map((c) => c[3]), ['2', '1']);
    assert.ok(notes(p).includes('Installs counts install ids, not players: a BBS or a server sends one id for all its players.'), notes(p).join('\n'));
  });
});

// ---------------------------------------------------------------- row 7

// TelemetryRow.Saturating from the C#.
function csharpSaturating() {
  const src = fs.readFileSync(path.join(ROOT, 'Scripts', 'Systems', 'TelemetryRow.cs'), 'utf8');
  return [...src.match(/Saturating\s*=\s*\{([\s\S]*?)\};/)[1].matchAll(/"(\w+)"/g)].map((m) => m[1]);
}

test('row 7: the note counts the fights with a value held at a bound, names the columns, floor or ceiling', async () => {
  const held = plain(B.loadApi().api.BALANCE_REMOTE_HELD);
  assert.deepStrictEqual(held.map((h) => h[0]).sort(), csharpSaturating().sort());
  const cols = new Map(H.csharpColumns().map((c) => [c[0], c]));
  for (const [name, at, bound] of held) {
    const [, min, max] = cols.get(name);
    assert.strictEqual(at, bound === 'floor' ? min : max, name);
  }
  const rows = [{}, {}, { rounds: 10000 }, { rounds: 10000, player_str: 0 }, { monster_level: 200 }, { spells_used: 9999, player_dex: 1 }];
  await withRemote(rows, async (a) => {
    const r = (await a.call('/api/balance/difficulty?source=remote')).body;
    assert.strictEqual(r.held.fights, 3);
    const by = Object.fromEntries(r.held.columns.map((c) => [c.column, [c.at, c.bound, c.fights]]));
    assert.deepStrictEqual(by.rounds, [10000, 'ceiling', 2]);
    assert.deepStrictEqual(by.player_str, [0, 'floor', 1]);
    assert.deepStrictEqual(by.monster_level, [200, 'ceiling', 1]);
    assert.deepStrictEqual(by.spells_used, [10000, 'ceiling', 0]);
    assert.deepStrictEqual(by.player_dex, [0, 'floor', 0]);
    // the average counts a held value as the bound
    assert.strictEqual(r.bands[0].avg_rounds, Math.round((6 + 6 + 10000 + 10000 + 6 + 6) / 6 * 10) / 10);
    const p = await pageOver(a);
    await chooseSource(p, 'remote');
    const note = notes(p).find((n) => n.startsWith('Held at a bound'));
    assert.strictEqual(note, 'Held at a bound: 3 fights (player_str at 0 (floor: that or less) 1; monster_level at 200 (ceiling: that or more) 1; ' +
      'rounds at 10000 (ceiling: that or more) 2). Averages count a held value as the bound itself.');
  });
  await withRemote([{}], async (a) => {
    const p = await pageOver(a);
    await chooseSource(p, 'remote');
    assert.ok(notes(p).includes('Held at a bound: 0 fights. Averages count a held value as the bound itself.'), notes(p).join('\n'));
  });
});

// ---------------------------------------------------------------- row 8

test('row 8: with a full remote file every official view answers byte for byte what it answers without one', async () => {
  const full = () => Array.from({ length: 600 }, (_, i) => ({ outcome: i % 3, player_class: i % 17, source: 1 + (i % 4),
    install_id: IDS[i % 3], received_day: DAY0 - (i % 20), floor_actual: i % 100, is_boss: i % 7 === 0 ? 1 : 0, difficulty: i % 4 }));
  await withRemote(full, async (a1, t, dir, game) => {
    const a0 = B.loadApi({ db: game.db, now: NOW });
    const routes = [...new Set([...BALANCE_SRC().matchAll(/method === 'GET' && url === '\/api\/balance\/([\w-]+)'/g)].map((m) => m[1]))];
    assert.ok(routes.length >= 12, routes.join(' '));
    let compared = 0;
    for (const route of routes.concat(['difficulty?class=Mage', 'difficulty?difficulty=2', 'difficulty?source=official', 'player-activity?player=NewTwo'])) {
      for (const w of ['since126', '24h', '7d', '30d']) {
        const url = `/api/balance/${route}${route.includes('?') ? '&' : '?'}window=${w}`;
        // windows count from SQLite's now, to the second: the remote side must match one of two calls around it
        const x1 = JSON.stringify(await a0.call(url));
        const y = JSON.stringify(await a1.call(url));
        const x2 = JSON.stringify(await a0.call(url));
        assert.ok(y === x1 || y === x2, url + '\n' + y.slice(0, 400) + '\n' + x1.slice(0, 400));
        assert.doesNotMatch(y, /unverified|installs|install_id|Remote/, url);
        compared++;
      }
    }
    assert.ok(compared >= 60);
    assert.deepStrictEqual(a1.remoteCalls, [], 'no official view asked for the remote file');
    assert.deepStrictEqual(a1.remotePrepared, []);
    assert.strictEqual(H.remoteCount(dir.dbPath), 600);
    // and the remote file was full: the remote view sees it
    assert.strictEqual((await a1.call('/api/balance/difficulty?source=remote')).body.fights, 600);
  });
});

// ---------------------------------------------------------------- row 9

test('row 9: only the Difficulty remote view reads remote_combat_events; nothing else in web or the game names it', () => {
  const src = B.proxySource();
  const view = VIEW_SRC();
  const outside = src.replace(H.section(), '').replace(view, '');
  assert.doesNotMatch(outside, /remote_combat_events/);
  // inside the view block: the one statement, read through reader()
  assert.strictEqual((view.match(/\.prepare\(/g) || []).length, 1);
  assert.strictEqual((view.match(/telemetryEndpoint\.reader\(\)/g) || []).length, 2, 'named in the comment, called once');
  assert.doesNotMatch(view, /\.(run|exec|transaction)\(/);
  // the view is called from one place: the difficulty route, for a remote source
  const callers = BALANCE_SRC().replace(view, '').match(/balanceRemoteDifficulty\(/g) || [];
  assert.strictEqual(callers.length, 1);
  // the telemetry section reads the table only to count the day (the cap) and to prune
  const reads = H.section().match(/SELECT [^']*remote_combat_events[^']*/g) || [];
  assert.deepStrictEqual(reads.map((s) => s.replace(/\s+/g, ' ')),
    ['SELECT COUNT(*) AS n FROM remote_combat_events WHERE received_day = ?', 'SELECT rowid FROM remote_combat_events ']);
  // no other web file and no C# file names it
  const web = path.join(__dirname, '..');
  for (const f of fs.readdirSync(web).filter((n) => /\.(js|html)$/.test(n) && n !== 'ssh-proxy.js')) {
    assert.doesNotMatch(fs.readFileSync(path.join(web, f), 'utf8'), /remote_combat_events|remote_telemetry/, f);
  }
  const walk = (dir) => fs.readdirSync(dir, { withFileTypes: true }).flatMap((e) => (e.isDirectory() ? walk(path.join(dir, e.name)) : [path.join(dir, e.name)]));
  const cs = walk(path.join(ROOT, 'Scripts')).filter((f) => f.endsWith('.cs'));
  assert.ok(cs.length > 100);
  for (const f of cs) assert.doesNotMatch(fs.readFileSync(f, 'utf8'), /remote_combat_events|remote_telemetry/, f);
});

// ---------------------------------------------------------------- row 10

test('row 10: remote values render escaped; the source label and the Unverified line are the page\'s own', async () => {
  const hostileView = (v) => ({
    source: v, sourceLabel: v, unverified: v, unverifiedText: v, missing: false, truncated: true, ceiling: v, fights: v, rowsRead: v,
    largestInstallPct: v,
    window: { key: v, label: v, from: v, fromDay: v, byDay: true },
    filters: { class: v, difficulty: v, source: v },
    classes: [v, 'Sage'], difficulties: [1],
    held: { fights: v, columns: [{ column: v, at: v, bound: v, fights: 2 }] },
    bands: [{ band: 1, boss: 1, floor_from: v, floor_to: v, fights: v, installs: v, win_pct: v, death_pct: v, flee_pct: v, avg_rounds: v,
      one_round_win_pct: v, hp_lost_pct: v, dmg_to_player_basic: v, dmg_to_player_ability: v, dmg_to_player_spell: v, dmg_to_player_dot: v,
      dmg_to_team: v, dmg_by_player: v, dmg_by_team: v, heal_player: v, potions_used: v, abilities_used: v, spells_used: v, party_size: v,
      encounter_size: v, monster_first_pct: v, teammates_lost: v }],
  });
  const render = async (v) => {
    const p = await loadPage('balance.html', {
      storage: { balance_token: 'tok' },
      fetch: (url) => (url.includes('/difficulty') ? hostileView(v) : {}),
    });
    await p.settle();
    await chooseSource(p, '3');
    return p;
  };
  const h = await render(HOSTILE);
  const b = await render('Alice');
  assert.deepStrictEqual(h.errors, []);
  assertSafe(assert, h.el('difficulty-table').innerHTML, b.el('difficulty-table').innerHTML, 'difficulty-table');
  assertSafe(assert, h.el('diff-class').innerHTML, b.el('diff-class').innerHTML, 'diff-class');
  assert.ok(!h.el('difficulty-table').innerHTML.includes('<img'));
  assert.ok(h.el('difficulty-table').textContent.includes(HOSTILE), 'shown as text');
  const n = notes(h);
  assert.strictEqual(n[0], UNVERIFIED, 'the page\'s own line, not the server\'s');
  assert.ok(n[1].includes('; Remote BBS; '), 'the page\'s own label: ' + n[1]);
  assert.strictEqual(n[1].split(HOSTILE).length - 1, 2, 'the window label and its day; the server label is not used');
  // on the server, a class name comes from the class table, never from the row
  await withRemote([{ player_class: 10 }, { player_class: 99 }, { player_class: -1 }], async (a) => {
    const r = await a.call('/api/balance/difficulty?source=remote');
    assert.deepStrictEqual(r.body.classes, ['Warrior']);
    assert.strictEqual(r.body.sourceLabel, 'Remote all');
  });
});

// ---------------------------------------------------------------- row 11

test('row 11: the remote view sits behind the balance login and the default password lock', async () => {
  await withRemote([{}], async (a) => {
    for (const s of ['remote', '1', '4']) {
      const url = '/api/balance/difficulty?source=' + s;
      assert.strictEqual((await a.call(url, { token: '' })).status, 401, s);
      assert.strictEqual((await a.call(url, { token: a.token + 'x' })).status, 401, s);
      assert.strictEqual((await a.call(url, { headers: {} })).status, 401, s);
    }
    a.state.locked = true;
    const r = await a.call('/api/balance/difficulty?source=remote');
    assert.strictEqual(r.status, 403);
    assert.match(r.body.error, /default password/);
    assert.deepStrictEqual(a.remoteCalls, [], 'the file is not touched before the login and the lock');
    a.state.locked = false;
    assert.strictEqual((await a.call('/api/balance/difficulty?source=remote')).status, 200);
    assert.strictEqual(a.remoteCalls.length, 1);
  });
});

// ---------------------------------------------------------------- A

test('row A: no install id leaves the server: counts only, no per install breakdown, in the JSON or the page', async () => {
  const rows = [{ install_id: IDS[0] }, { install_id: IDS[0], source: 3 }, { install_id: IDS[1], source: 4 }, { install_id: IDS[2], is_boss: 1 }];
  await withRemote(rows, async (a) => {
    const bodies = [];
    for (const s of ['remote', '1', '2', '3', '4']) {
      for (const q of ['', '&class=Warrior', '&difficulty=1', '&window=24h']) {
        const r = await a.call('/api/balance/difficulty?source=' + s + q);
        assert.strictEqual(r.status, 200);
        bodies.push(JSON.stringify(r.body));
      }
    }
    for (const body of bodies) {
      for (const id of IDS) assert.ok(!body.includes(id), 'an install id in ' + body.slice(0, 200));
      assert.doesNotMatch(body, /install_id|[0-9a-f]{32}/);
    }
    const r = (await a.call('/api/balance/difficulty?source=remote')).body;
    assert.deepStrictEqual(Object.keys(r).sort(), ['bands', 'ceiling', 'classes', 'difficulties', 'fights', 'filters', 'held', 'largestInstallPct',
      'missing', 'rowsRead', 'source', 'sourceLabel', 'truncated', 'unverified', 'unverifiedText', 'window']);
    const p = await pageOver(a);
    await chooseSource(p, 'remote');
    for (const id of IDS) assert.ok(!p.el('difficulty-table').innerHTML.includes(id));
  });
});

// ---------------------------------------------------------------- B

test('row B: each remote view gives the share of its fights from the single largest install', async () => {
  const rows = [{ install_id: IDS[0] }, { install_id: IDS[0] }, { install_id: IDS[0], player_class: 0 }, { install_id: IDS[1], player_class: 0 }];
  await withRemote(rows, async (a) => {
    assert.strictEqual((await a.call('/api/balance/difficulty?source=remote')).body.largestInstallPct, 75);
    assert.strictEqual((await a.call('/api/balance/difficulty?source=remote&class=Alchemist')).body.largestInstallPct, 50, 'of the view');
    assert.strictEqual((await a.call('/api/balance/difficulty?source=remote&class=Warrior')).body.largestInstallPct, 100);
    assert.strictEqual((await a.call('/api/balance/difficulty?source=2')).body.largestInstallPct, null, 'no fights');
    const p = await pageOver(a);
    await chooseSource(p, 'remote');
    assert.ok(notes(p).includes('Largest single install: 75% of these 4 fights.'), notes(p).join('\n'));
    await chooseSource(p, '2');
    assert.ok(notes(p).includes('Largest single install: none of these 0 fights.'), notes(p).join('\n'));
  });
});

// ---------------------------------------------------------------- C

test('row C: one statement per remote view, through idx_rce_day, at most the row ceiling, newest days first', async () => {
  const { BALANCE_REMOTE_ROW_CEILING: CEIL } = B.loadApi().api;
  assert.strictEqual(CEIL, 50000);
  const rows = () => [...repeat(5, { received_day: DAY0 - 3, player_class: 0 }), ...repeat(CEIL, { received_day: DAY0 })];
  await withRemote(rows, async (a, t) => {
    for (const w of ['since126', '24h', '7d', '30d']) {
      a.remotePrepared.length = 0;
      const r = (await a.call('/api/balance/difficulty?source=remote&class=Warrior&window=' + w)).body;
      assert.strictEqual(a.remotePrepared.length, 1, 'one statement');
      const s = a.remotePrepared[0];
      assert.strictEqual(s.args[3], CEIL);
      const plan = t.ep.reader().prepare('EXPLAIN QUERY PLAN ' + s.sql).all(...s.args).map((x) => x.detail);
      assert.ok(plan.some((x) => /^SEARCH remote_combat_events USING INDEX idx_rce_day \(received_day>\?\)$/.test(x)), plan.join('\n'));
      assert.ok(!plan.some((x) => /^SCAN remote_combat_events\b/.test(x)), plan.join('\n'));
      assert.ok(!plan.some((x) => /TEMP B-TREE FOR ORDER BY/.test(x)), plan.join('\n'));
      assert.strictEqual(r.rowsRead, CEIL, w);
      assert.strictEqual(r.fights, CEIL, w);
      assert.strictEqual(r.truncated, true, w);
    }
    const all = (await a.call('/api/balance/difficulty?source=remote')).body;
    assert.deepStrictEqual(all.classes, ['Warrior'], 'the older day is past the ceiling');
    const p = await pageOver(a);
    await chooseSource(p, 'remote');
    assert.ok(notes(p).includes('Row ceiling reached: only the newest 50,000 rows of this window are counted.'), notes(p).join('\n'));
  });
  await withRemote(repeat(3, {}), async (a) => {
    const r = (await a.call('/api/balance/difficulty?source=remote')).body;
    assert.deepStrictEqual([r.rowsRead, r.truncated], [3, false]);
  });
});
