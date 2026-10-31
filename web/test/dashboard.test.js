'use strict';
// The Observatory (dashboard.html): every view builds its DOM with createElement and textContent, so a
// hostile name, team, place, item or message must come out as text in every view, with the same element
// structure as a plain name. Empty, failing and missing endpoints give empty states, never a page error;
// a missing history endpoint hides the trend card and every sparkline.
const test = require('node:test');
const assert = require('node:assert');
const { HOSTILE, loadPage, assertSafe, readPage, pageScripts } = require('./harness');

const VIEWS = ['view-now', 'view-town', 'view-power', 'view-people'];

// The fake DOM keeps no markup for built nodes, so the tests serialize the tree themselves.
const escText = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const escAttr = (s) => escText(s).replace(/"/g, '&quot;');
function serialize(node) {
  if (!node) return '';
  if (node.nodeType === 3) return escText(node.textContent);
  const tag = node.tagName.toLowerCase();
  const attrs = Object.keys(node._attrs).map((k) => ` ${k}="${escAttr(node._attrs[k])}"`).join('');
  const inner = node._text ? escText(node._text) : node.childNodes.map(serialize).join('');
  return `<${tag}${attrs}>${inner}</${tag}>`;
}

const sqlTime = (ms) => new Date(ms).toISOString().slice(0, 19).replace('T', ' ');
const isoTime = (ms) => new Date(ms).toISOString();

// Every endpoint's answer with every string field set from v.
function world(v) {
  const now = Date.now();
  const npc = (name, extra) => Object.assign({
    name, level: 40, class: 4, race: 2, npcFaction: 1, team: v + ' Guild', location: v, isDead: false, isPermaDead: false,
    isAgedDeath: false, isKing: false, wealth: 900, seeking: true,
  }, extra);
  const hourKey = new Date(now - (now % 3600000)).toISOString().slice(0, 13).replace('T', ' ') + ':00';
  return {
    '/api/dash/people': [npc(v, { isKing: true, level: 50 }), npc(v + '2', { level: 30, location: 'Main Street' }),
      npc(v + '3', { level: 20, isDead: true, location: 'Gym' })],
    '/api/dash/realm': {
      ruler: { name: v, title: 'Queen', reignDays: 3, since: isoTime(now - 3 * 86400000) },
      treasury: 5000, taxes: { king: 5, city: 2 }, daily: { income: 900, expenses: 300 },
      cityControl: { team: v + ' Guild', leader: v, members: 2 },
      god: { name: v, week: 4, rule: v },
      boss: { now: { name: v, level: 30, hpPct: 40, endsAt: isoTime(now + 3600000) }, next: null },
      bossLeaders: [{ name: v, damage: 100 }, { name: v + '2', damage: 50 }],
    },
    '/api/dash/ties': { dislikes: [{ from: v, to: v + '2', value: -80 }], likes: [{ from: v + '2', to: v, value: 60 }] },
    '/api/dash/movers': { days: 7, xp: [{ name: v, xp: 300 }], gold: [{ name: v + '2', gold: 50 }] },
    '/api/dash/depths': { days: 7, bands: [{ band: '1-10', deaths: 3 }, { band: '11-20', deaths: 1 }], unknown: 1 },
    '/api/dash/market': { count: 1, listings: [{ item: v, price: 100, expiresAt: isoTime(now + 86400000) }] },
    '/api/dash/fallen': [{ name: v, level: 20, className: v, killer: v, diedAt: isoTime(now - 3600000) }],
    '/api/dash/events': [
      { id: 3, message: v + ' was slain by ' + v + '2 at the Inn', category: 'npc', playerName: null, time: sqlTime(now - 60000) },
      { id: 2, message: v, category: 'npc', playerName: null, time: sqlTime(now - 120000) },
      { id: 1, message: v, category: 'pvp', playerName: v, time: sqlTime(now - 180000) },
    ],
    '/api/dash/events/hourly': [{ hour: hourKey, category: 'npc', count: 5 }],
    '/api/dash/history': [0, 1, 2].map((i) => ({
      taken_at: sqlTime(now - (2 - i) * 3600000), npcs_alive: 90 + i, npcs_dead: 5, npcs_married: 10, children: 3,
      treasury: 4000 + i * 100, players_online: i, births: i, deaths: 1, marriages: 0,
    })),
    '/api/stats': {
      onlineCount: 1,
      online: [{ name: v, level: 5, className: v, location: v, isImmortal: false }],
      leaderboard: [{ rank: 1, name: v, className: v, level: 9, experience: 10, isOnline: true }],
      pvpLeaderboard: [{ rank: 1, name: v, className: v, level: 9, wins: 2, losses: 1, goldStolen: 5 }],
    },
    '/api/dash/profile': {
      name: v, level: 50, class: 4, race: 2, npcFaction: 1, team: v, location: v, gold: 10, age: 30, hp: 40, maxHP: 50,
      isDead: false, isPermaDead: false, isAgedDeath: false, isKing: true, isMarried: true, spouseName: v + '2', pregnancyDueDate: v,
      personalityProfile: { aggression: 0.2, compassion: 0.8, honesty: 0.6 },
      emotionalState: { happiness: 0.5, anger: 0.1 },
      relationships: { [v + '2']: -50, [v + '3']: 40 },
      currentGoals: [{ name: v, type: v, priority: 0.5, isActive: true }],
      memories: [{ description: v, type: v, importance: 0.5, timestamp: isoTime(now - 60000) }],
    },
  };
}

// A fetch stub over a route map; a route missing from the map answers 404.
function server(routes, over) {
  return (url) => {
    const pathOnly = url.split('?')[0];
    if (over && Object.prototype.hasOwnProperty.call(over, pathOnly)) return over[pathOnly](url);
    if (Object.prototype.hasOwnProperty.call(routes, pathOnly)) return routes[pathOnly];
    return { status: 404, body: { error: 'Not found' } };
  };
}

async function open(routes, options) {
  const p = await loadPage('dashboard.html', Object.assign({ fetch: server(routes) }, options || {}));
  await p.settle();
  return p;
}

function noErrors(p, label) {
  assert.deepStrictEqual(p.errors, [], `${label}: console errors`);
  assert.strictEqual(p.run('JSON.stringify(Observatory.state.errors)'), '[]', `${label}: renderer errors`);
}

test('the page loads escape.js first, no chart library, and has no HTML sink in its script', () => {
  const html = readPage('dashboard.html');
  const scripts = pageScripts(html);
  assert.deepStrictEqual(scripts.filter((s) => s.src !== undefined).map((s) => s.src), ['/escape.js']);
  assert.ok(scripts.findIndex((s) => s.src === '/escape.js') < scripts.findIndex((s) => s.code !== undefined));
  const code = scripts.filter((s) => s.code !== undefined).map((s) => s.code).join('\n');
  for (const sink of ['innerHTML', 'outerHTML', 'insertAdjacentHTML', 'document.write', 'eval(', 'new Function']) {
    assert.ok(!code.includes(sink), `page script uses ${sink}`);
  }
  assert.ok(!/javascript:/i.test(html));
  assert.ok(!/\/api\/dash\/(npcs|summary)\b/.test(html), 'the retired endpoints are not read');
});

test('the page runs without errors and loads every endpoint once', async () => {
  const p = await open(world('Alice'));
  assert.deepStrictEqual(p.loaded, ['escape.js']);
  noErrors(p, 'load');
  const urls = p.calls.fetch.map((c) => c.url).sort();
  assert.deepStrictEqual(urls, ['/api/dash/depths', '/api/dash/events', '/api/dash/events/hourly', '/api/dash/fallen',
    '/api/dash/history?hours=24', '/api/dash/market', '/api/dash/movers', '/api/dash/people', '/api/dash/realm',
    '/api/dash/ties', '/api/stats']);
  assert.strictEqual(p.run('Observatory.state.feed.url'), '/api/dash/feed');
});

test('every view shows hostile names only as text, with the same structure as plain names', async () => {
  const watch = (v) => ({ 'usurper.observatory.watch': JSON.stringify({ npcs: [v], teams: [v + ' Guild'] }) });
  const h = await open(world(HOSTILE), { storage: watch(HOSTILE) });
  const b = await open(world('Alice'), { storage: watch('Alice') });
  for (const id of VIEWS) {
    const hh = serialize(h.el(id));
    assert.ok(hh.length > 400, `${id} rendered`);
    assertSafe(assert, hh, serialize(b.el(id)), id);
    assert.ok(h.el(id).textContent.includes(HOSTILE), `${id} shows the name as text`);
  }
  // the chronicle highlights the watched name, escaped into a pattern safely
  assert.ok(h.el('chron').querySelectorAll('.ev.watch').length >= 2, 'watched name highlighted');
  // the town lists who is at the selected place
  assert.ok(h.el('town-who').textContent.includes(HOSTILE));
  noErrors(h, 'hostile');
  noErrors(b, 'benign');
});

test('the profile shows hostile values as text; a name click requests it with the name encoded', async () => {
  const h = await open(world(HOSTILE));
  const b = await open(world('Alice'));
  for (const p of [h, b]) {
    const link = p.el('dir').querySelector('[data-npc]');
    p.click(link);
    await p.settle();
  }
  const url = h.calls.fetch.map((c) => c.url).find((u) => u.startsWith('/api/dash/profile'));
  assert.strictEqual(url, '/api/dash/profile?name=' + encodeURIComponent(HOSTILE));
  assert.strictEqual(h.el('view-profile').hidden, false);
  assert.strictEqual(h.el('view-people').hidden, true);
  const hh = serialize(h.el('view-profile'));
  assertSafe(assert, hh, serialize(b.el('view-profile')), 'profile');
  const text = h.el('profile').textContent;
  assert.ok(text.includes(HOSTILE));
  assert.ok(text.includes('Sociability') && text.includes('Trustworthiness'), 'the game names of two traits');
  assert.ok(!text.includes('Compassion') && !text.includes('Honesty'));
  noErrors(h, 'profile');
  // Back returns to the tab the profile was opened from
  h.click(h.el('view-profile').querySelector('[data-action="back"]'));
  assert.strictEqual(h.el('view-profile').hidden, true);
  assert.strictEqual(h.el('view-now').hidden, false);
});

test('an unknown profile (404) and a failed profile request show a message, not an error', async () => {
  const p = await open(world('Alice'), {});
  p.ctx.fetch = async (url) => (url.startsWith('/api/dash/profile')
    ? { status: 404, ok: false, json: async () => ({ error: 'Not found' }) }
    : { status: 200, ok: true, json: async () => [] });
  await p.run("Observatory.openProfile('Nobody Here')");
  assert.ok(p.el('profile').textContent.includes('No NPC named Nobody Here is on record'));
  p.ctx.fetch = async () => { throw new Error('network'); };
  await p.run("Observatory.openProfile('Somebody')");
  assert.ok(p.el('profile').textContent.includes('could not be loaded'));
  noErrors(p, 'profile failures');
});

const EMPTY_STATES = {
  'town-list': true, crown: true, factions: true, teams: true, movers: true, depths: true, market: true,
  dir: true, rich: true, ties: true, seeking: true, fallen: true, leaderboard: true, pvp: true, boss: true, online: true, chron: true,
};

async function assertEmptyStates(fetchImpl, label) {
  const p = await loadPage('dashboard.html', { fetch: fetchImpl });
  await p.settle();
  noErrors(p, label);
  for (const id of Object.keys(EMPTY_STATES)) {
    const node = p.el(id);
    assert.ok(node.querySelector('.empty'), `${label}: ${id} shows an empty state`);
  }
  assert.strictEqual(p.el('trends-card').hidden, true, `${label}: trends hidden`);
  assert.strictEqual(p.el('activity').hidden, true, `${label}: activity hidden`);
  assert.strictEqual(p.el('kpis').querySelectorAll('.spark').length, 0, `${label}: no sparklines`);
  return p;
}

test('every endpoint answering an empty list renders empty states without errors', async () => {
  await assertEmptyStates(() => [], 'empty lists');
});

test('every endpoint failing (network error) renders empty states without errors', async () => {
  await assertEmptyStates(() => { throw new Error('offline'); }, 'network error');
});

test('every endpoint answering 500 or a malformed body renders empty states without errors', async () => {
  await assertEmptyStates(() => ({ status: 500, body: { error: 'Internal error' } }), 'HTTP 500');
  await assertEmptyStates(() => 'not json at all', 'string body');
  await assertEmptyStates(() => ({ status: 200, body: { error: 'Stats query failed', online: 7 } }), 'error object');
});

test('history: a 404 hides the trend card and every sparkline; rows show them', async () => {
  const routes = world('Alice');
  delete routes['/api/dash/history'];
  const missing = await open(routes);
  assert.strictEqual(missing.el('trends-card').hidden, true);
  assert.strictEqual(missing.el('kpis').querySelectorAll('.spark').length, 0);
  assert.ok(missing.el('kpis').textContent.includes('NPCs alive'), 'the numbers still show');
  noErrors(missing, 'history 404');

  const present = await open(world('Alice'));
  assert.strictEqual(present.el('trends-card').hidden, false);
  assert.ok(present.el('kpis').querySelectorAll('.spark').length >= 3, 'tiles carry sparklines');
  assert.strictEqual(present.el('trends').querySelectorAll('.mini').length, 6);
  noErrors(present, 'history rows');
});

test('history: a range that fails shows an empty note and switching back restores the 24 hours', async () => {
  const routes = world('Alice');
  const p = await loadPage('dashboard.html', {
    fetch: server(routes, { '/api/dash/history': (url) => (url.endsWith('hours=24') ? routes['/api/dash/history'] : { status: 404, body: {} }) }),
  });
  await p.settle();
  p.click(p.el('trend-chips').querySelector('[data-range="168"]'));
  await p.settle();
  assert.ok(p.el('trends').querySelector('.empty'), 'empty note for 7 days');
  assert.ok(p.calls.fetch.some((c) => c.url === '/api/dash/history?hours=168'));
  p.click(p.el('trend-chips').querySelector('[data-range="24"]'));
  await p.settle();
  assert.strictEqual(p.el('trends').querySelectorAll('.mini').length, 6);
  noErrors(p, 'history ranges');
});

test('town: one normalisation rule for places; unknown places go to Elsewhere; the dead are not counted', async () => {
  const people = [
    ['A', 'Main Street'], ['B', 'MainStreet'], ['C', 'main_street'], ['D', 'Church'], ['E', 'The Divine Realm'],
    ['F', 'Auction House'], ['G', 'gym'], ['H', 'Gym', true], ['I', ''],
  ].map(([name, location, isDead]) => ({ name, level: 10, class: 1, race: 0, npcFaction: 0, team: '', location, isDead: !!isDead, wealth: 1 }));
  const p = await open({ '/api/dash/people': people });
  assert.strictEqual(p.run("Observatory.placeKey('Main Street')"), 'mainstreet');
  assert.strictEqual(p.run("Observatory.placeKey('Quest Hall')"), 'questhall');
  assert.strictEqual(p.run("Observatory.placeKey('Church')"), 'elsewhere');
  const tile = (k) => p.el('town-tiles').querySelector(`[data-place="${k}"]`);
  assert.strictEqual(tile('mainstreet').querySelector('.c').textContent, '3');
  assert.strictEqual(tile('elsewhere').querySelector('.c').textContent, '3');
  assert.strictEqual(tile('gym').querySelector('.c').textContent, '1', 'the dead NPC at the Gym is not counted');
  assert.strictEqual(tile('auctionhouse').querySelector('.c').textContent, '1');
  assert.strictEqual(tile('prison').querySelector('.c').textContent, '0');
  assert.strictEqual(p.el('town-tiles').querySelectorAll('.tile').length, 23, '22 places and Elsewhere');
  assert.ok(tile('mainstreet').classList.contains('s2'));
  // tapping a tile lists who is there; Elsewhere shows each raw place
  p.click(tile('elsewhere'));
  const whoText = p.el('town-who').textContent;
  assert.ok(whoText.includes('Church') && whoText.includes('The Divine Realm'));
  assert.ok(p.el('who-title').textContent.startsWith('Elsewhere'));
  noErrors(p, 'town');
});

test('chronicle: kinds as chips, a chip filters, live news arrives from the feed, other events are ignored', async () => {
  const now = Date.now();
  const ev = (message, category, ago) => ({ message, category, time: sqlTime(now - ago) });
  const p = await open({
    '/api/dash/events': [
      ev('† Malachi the Dark was slain by Lysandra the Pure at Bank!', 'npc', 60000),
      ev('Several townsfolk witnessed Noric Elderbranch brawl with Mord at the Weapon Shop', 'npc', 120000),
      ev("Lady Morgana joined the team 'The Nautical Guild'!", 'npc', 180000),
      ev('Sister Mercy is telling anyone who\'ll listen: "Lysandra got into a brawl"', 'npc', 240000),
      ev('Sir Darius the Lost has been training hard at the Gym!', 'npc', 300000),
      ev('The Lich King rises', 'world_boss', 360000),
    ],
  });
  const chipText = p.el('chron-chips').textContent;
  for (const c of ['All 6', 'Deaths 1', 'Fights 1', 'Teams 1', 'Gossip 1', 'Training and levels 1', 'World boss 1', 'Watched 0']) assert.ok(chipText.includes(c), c);
  assert.ok(p.el('chron').querySelector('.w'), 'a death is marked');
  assert.ok(p.el('chron-note').textContent.startsWith('Covers the last 6 events'));
  p.click(p.el('chron-chips').querySelector('[data-chip="death"]'));
  assert.strictEqual(p.el('chron').querySelectorAll('.ev').length, 1);
  p.click(p.el('chron-chips').querySelector('[data-chip="all"]'));

  const feed = p.run('Observatory.state.feed');
  assert.strictEqual(typeof feed.listeners.news, 'function');
  assert.strictEqual(feed.listeners['npc-snapshot'], undefined, 'the NPC snapshot is not read');
  feed.listeners.news({ data: JSON.stringify({ items: [{ message: HOSTILE, category: 'npc', time: sqlTime(now) }] }) });
  assert.strictEqual(p.el('chron').querySelectorAll('.ev').length, 7);
  assert.ok(p.el('chron').textContent.includes(HOSTILE));
  assertSafe(assert, serialize(p.el('chron')), undefined, 'live news');
  feed.listeners.news({ data: '{not json' });
  feed.listeners.news({ data: JSON.stringify({ items: 'x' }) });
  // the same item twice is shown once
  feed.listeners.news({ data: JSON.stringify({ items: [{ message: HOSTILE, category: 'npc', time: sqlTime(now) }] }) });
  assert.strictEqual(p.el('chron').querySelectorAll('.ev').length, 7);
  noErrors(p, 'chronicle');
});

test('watch list: a profile and a team can be watched, saved in localStorage, and highlighted', async () => {
  const routes = world('Elara Vane');
  routes['/api/dash/events'] = [{ id: 1, message: 'Elara Vane has been training hard at the Gym!', category: 'npc', time: sqlTime(Date.now()) }];
  const p = await open(routes);
  await p.run("Observatory.openProfile('Elara Vane')");
  await p.settle();
  p.click(p.el('profile').querySelector('[data-watch-npc]'));
  assert.deepStrictEqual(JSON.parse(p.storage['usurper.observatory.watch']), { npcs: ['Elara Vane'], teams: [] });
  assert.strictEqual(p.el('profile').querySelector('[data-watch-npc]').getAttribute('aria-pressed'), 'true');
  assert.ok(p.el('chron').querySelector('.ev.watch'), 'watched name highlighted');
  assert.ok(p.el('watched').textContent.includes('Elara Vane'));
  p.click(p.el('teams').querySelector('[data-watch-team]'));
  assert.deepStrictEqual(JSON.parse(p.storage['usurper.observatory.watch']).teams, ['Elara Vane Guild']);
  // removing from the Watched card
  p.click(p.el('watched').querySelector('[data-watch-npc]'));
  assert.deepStrictEqual(JSON.parse(p.storage['usurper.observatory.watch']).npcs, []);
  noErrors(p, 'watch');
});

test('watch list: blocked or broken storage never breaks the page', async () => {
  const broken = await open(world('Alice'), { storage: { 'usurper.observatory.watch': '{not json' } });
  noErrors(broken, 'broken json');
  const odd = await open(world('Alice'), { storage: { 'usurper.observatory.watch': JSON.stringify({ npcs: [1, null, 'Alice'], teams: 'x' }) } });
  assert.ok(odd.el('watched').textContent.includes('Alice'));
  noErrors(odd, 'odd shape');
  const blocked = {
    getItem() { throw new Error('SecurityError'); },
    setItem() { throw new Error('SecurityError'); },
    removeItem() { throw new Error('SecurityError'); },
  };
  const p = await open(world('Alice'), { localStorage: blocked });
  p.click(p.el('teams').querySelector('[data-watch-team]'));
  assert.ok(p.el('watched').textContent.includes('Alice Guild'), 'kept for this visit');
  noErrors(p, 'blocked storage');
});

test('directory: search and filters narrow the list; the dead show only when asked', async () => {
  const people = [
    { name: 'Shadow', level: 80, class: 1, race: 0, npcFaction: 1, team: '', location: 'Gym', isDead: false, wealth: 1 },
    { name: 'Sister Mercy', level: 71, class: 4, race: 2, npcFaction: 2, team: 'The Tidal Corsairs', location: 'Temple', isDead: false, wealth: 1 },
    { name: 'Ragnar Bloodaxe', level: 70, class: 2, race: 0, npcFaction: -1, team: '', location: 'Gym', isDead: true, isPermaDead: true, wealth: 1 },
  ];
  const p = await open({ '/api/dash/people': people });
  const rows = () => p.el('dir').querySelectorAll('tbody tr').map((tr) => tr.querySelector('td').textContent);
  assert.deepStrictEqual(rows(), ['Shadow', 'Sister Mercy']);
  p.el('dir-search').value = 'mer';
  p.doc.dispatch(p.el('dir-search'), 'input');
  assert.deepStrictEqual(rows(), ['Sister Mercy']);
  p.el('dir-search').value = '';
  p.el('dir-status').value = 'dead';
  p.doc.dispatch(p.el('dir-status'), 'change');
  assert.deepStrictEqual(rows(), ['Ragnar Bloodaxegone for good']);
  p.el('dir-status').value = 'all';
  p.el('dir-class').value = '4';
  p.doc.dispatch(p.el('dir-class'), 'change');
  assert.deepStrictEqual(rows(), ['Sister Mercy']);
  const classOptions = p.el('dir-class').querySelectorAll('option').map((o) => o.textContent);
  assert.deepStrictEqual(classOptions, ['Every class', 'Assassin', 'Barbarian', 'Cleric']);
  noErrors(p, 'directory');
});

test('tabs switch the visible view; one view at a time', async () => {
  const p = await open(world('Alice'));
  const visible = () => ['view-now', 'view-town', 'view-power', 'view-people', 'view-profile'].filter((id) => !p.el(id).hidden);
  assert.deepStrictEqual(visible(), ['view-now']);
  for (const tab of ['town', 'power', 'people', 'now']) {
    p.click(p.doc.querySelector(`.tabbar [data-tab="${tab}"]`));
    assert.deepStrictEqual(visible(), ['view-' + tab]);
    assert.strictEqual(p.doc.querySelector(`.tabs [data-tab="${tab}"]`).getAttribute('aria-pressed'), 'true');
  }
});

test('dataviz: bars carry a text value and a title; feelings diverge around the midline', async () => {
  const p = await open(world('Alice'));
  const rows = p.el('view-power').querySelectorAll('.brow');
  assert.ok(rows.length >= 6);
  for (const r of rows) {
    assert.ok(r.querySelector('.val').textContent.length > 0, 'value beside the bar');
    assert.ok(r.getAttribute('title'), 'title on the mark');
  }
  const ties = p.el('ties').querySelectorAll('.div');
  assert.strictEqual(ties.length, 2);
  assert.ok(ties[0].textContent.includes('dislikes') && ties[0].querySelector('i').classList.contains('neg'));
  assert.strictEqual(ties[0].querySelector('i').style.right, '50%');
  assert.ok(ties[1].textContent.includes('likes') && ties[1].querySelector('i').classList.contains('pos'));
  assert.strictEqual(ties[1].querySelector('i').style.left, '50%');
  const depth = p.el('depths').textContent;
  assert.ok(depth.includes('Floors 1 to 10') && depth.includes('1 death with no floor recorded'));
});

// ---------------------------------------------------------------- review fixes

test('names such as __proto__, constructor and toString are only keys: true counts, Object.prototype untouched', async () => {
  const n = (name, team, extra) => Object.assign({ name, level: 10, class: 1, race: 0, npcFaction: 0, team, location: 'Gym', isDead: false, wealth: 1 }, extra);
  const people = [
    n('A1', '__proto__'),
    n('B1', 'constructor'), n('B2', 'constructor'),
    n('C1', 'toString'), n('C2', 'toString'), n('C3', 'toString'),
    n('D1', 'hasOwnProperty', { location: '__proto__', class: 'constructor', race: '__proto__', npcFaction: '__proto__' }),
    n('E1', '', { location: 'constructor' }),
  ];
  const now = Date.now();
  const p = await open({
    '/api/dash/people': people,
    '/api/dash/events/hourly': [{ hour: '__proto__', category: 'npc', count: 3 }, { hour: 'constructor', category: 'npc', count: 2 }],
    '/api/dash/events': [{ message: '__proto__', category: 'npc', time: sqlTime(now) }, { message: 'x', category: '__proto__', time: sqlTime(now) }],
  }, { storage: { 'usurper.observatory.watch': JSON.stringify({ npcs: ['__proto__'], teams: ['constructor'] }) } });
  const teams = p.el('teams').querySelectorAll('tbody tr').map((tr) => tr.querySelectorAll('td').slice(0, 3).map((td) => td.textContent));
  assert.deepStrictEqual(teams, [['toString', '3', '30'], ['constructor', '2', '20'], ['__proto__', '1', '10'], ['hasOwnProperty', '1', '10']]);
  // the two odd places are both Elsewhere, and the faction, class and race fall back to their plain names
  assert.strictEqual(p.el('town-tiles').querySelector('[data-place="elsewhere"]').querySelector('.c').textContent, '2');
  assert.ok(p.el('factions').textContent.includes('The Crown'));
  assert.ok(p.el('dir').textContent.includes('Unknown class') && p.el('dir').textContent.includes('Unknown race'));
  assert.strictEqual(p.run('Object.getOwnPropertyNames(Object.prototype).filter(function (k) { return ["members", "levels", "all", "watched", "other", "players"].indexOf(k) !== -1; }).length'), 0);
  assert.strictEqual(p.run('({}).members === undefined && ({}).levels === undefined && typeof ({}).toString === "function"'), true);
  noErrors(p, 'prototype names');
});

test('focus: a profile opened from a name focuses Back; Back returns focus to that name', async () => {
  const p = await open(world('Alice'));
  p.click(p.doc.querySelector('.tabbar [data-tab="people"]'));
  const link = p.el('dir').querySelector('[data-npc]');
  link.focus();
  p.click(link);
  await p.settle();
  const back = p.el('view-profile').querySelector('[data-action="back"]');
  assert.strictEqual(p.doc.activeElement, back, 'focus on Back');
  p.click(back);
  assert.strictEqual(p.el('view-people').hidden, false);
  assert.strictEqual(p.doc.activeElement, link, 'focus back on the name that opened the profile');
  noErrors(p, 'focus');
});

test('focus: a refresh while a profile is open keeps focus; Back then falls to the active tab', async () => {
  const p = await open(world('Alice'));
  p.click(p.doc.querySelector('.tabbar [data-tab="people"]'));
  const link = p.el('dir').querySelector('[data-npc]');
  link.focus();
  p.click(link);
  await p.settle();
  const back = p.el('view-profile').querySelector('[data-action="back"]');
  p.run('Observatory.renderAll()');
  assert.strictEqual(p.doc.activeElement, back, 'the refresh did not move focus');
  assert.strictEqual(link.isConnected, false, 'the refresh replaced the opener');
  p.click(back);
  assert.strictEqual(p.doc.activeElement.getAttribute('data-tab'), 'people', 'focus on the active tab button');
  // watching from the profile keeps focus on the rebuilt Watch button
  const again = p.el('dir').querySelector('[data-npc]');
  again.focus();
  p.click(again);
  await p.settle();
  const watch = p.el('profile').querySelector('[data-watch-npc]');
  watch.focus();
  p.click(watch);
  assert.strictEqual(p.doc.activeElement, p.el('profile').querySelector('[data-watch-npc]'));
  assert.strictEqual(p.doc.activeElement.getAttribute('aria-pressed'), 'true');
  noErrors(p, 'focus refresh');
});

test('town: an empty place stays selected and says so', async () => {
  const people = [{ name: 'Gymrat', level: 10, class: 1, race: 0, npcFaction: 0, team: '', location: 'Gym', isDead: false, wealth: 1 }];
  const p = await open({ '/api/dash/people': people });
  const prison = p.el('town-tiles').querySelector('[data-place="prison"]');
  p.click(prison);
  assert.strictEqual(p.el('who-title').textContent, 'Prison');
  assert.ok(p.el('town-who').textContent.includes('Prison: no NPCs here right now.'));
  assert.ok(p.el('town-tiles').querySelector('[data-place="prison"]').classList.contains('sel'));
  p.run('Observatory.renderAll()');
  assert.strictEqual(p.el('who-title').textContent, 'Prison', 'a refresh keeps the selection');
  noErrors(p, 'empty place');
});

// Messages built from the game's English news templates (Localization/en.json) with hostile names.
const H = HOSTILE;
const KIND_CASES = [
  ['death', `† ${H} was slain by ${H} at Bank!`, 'news.death'],
  ['death', '† Whisperwind was slain by Alice at Bank!', 'news.death, a name that holds "whisper"'],
  ['death', `${H} has passed away peacefully at the age of 81. The soul moves on...`, 'worldsim.news_natural_death'],
  ['death', `⚱ ${H}, a Elf of 80 years, has passed away peacefully. The soul moves on...`, 'news.natural_death'],
  ['death', `${H} has been slain by ${H} and will not return. The realm mourns.`, 'worldsim.news_permadeath'],
  ['death', `${H} murdered ${H} in cold blood! They will not return.`, 'street_encounter.news.murdered'],
  ['gossip', `${H} is telling anyone who'll listen: "${H} got into a brawl with ${H} at the Bank"`, 'worldsim.news_gossip'],
  ['gossip', 'Sister Mercy is telling anyone who\'ll listen: "Whisperwind was slain by Alice at Bank!"', 'gossip that quotes a death'],
  ['gossip', `Dark whispers about ${H} circulate through the taverns`, 'social.news_dark_whispers'],
  ['brawl', `Several townsfolk witnessed ${H} brawl with ${H} at the Bank`, 'social.news_witnessed_brawl'],
  ['brawl', `Tensions are rising between ${H} and ${H} at the Inn.`, 'worldsim.news_tension'],
  ['teams', `${H} recruited ${H} into '${H}'!`, 'worldsim.news_team_recruited'],
  ['teams', `'${H}' has taken control of the town!`, 'worldsim.news_town_control'],
  ['teams', `${H} joined the team '${H}'!`, 'team.news_joined'],
  ['teams', `⚑ Gang Recruitment! ${H} joined ${H}!`, 'street_encounter.gang.news_header and news_joined'],
  ['training', `⬆ ${H} the Elf Cleric has achieved Level 12!`, 'news.npc_level_up'],
  ['training', `${H} has been training hard at the Gym!`, 'worldsim.news_training'],
  ['life', `♥ ${H} and ${H} are proud parents of ${H}!`, 'news.birth'],
  ['life', `${H} and ${H} are expecting a child!`, 'worldsim.news_expecting'],
  ['life', `♥ ${H} and ${H} were married at the Church!`, 'news.marriage'],
  ['life', `${H} has returned from the realm of the dead!`, 'worldsim.news_respawned'],
  ['crown', `${H} has challenged King ${H} for the throne!`, 'challenge.news_challenged'],
  ['other', `${H} purchased Steel Sword from the shop.`, 'worldsim.news_purchased'],
  ['other', 'Whisperwind', 'a bare name picks no kind'],
  ['other', 'Sister Mercy trained a gang of whispering teams', 'loose words pick no kind'],
];

test('chronicle kinds come from whole game templates; names never decide the kind', async () => {
  const p = await open({});
  for (const [kind, message, source] of KIND_CASES) {
    p.ctx.__m = message;
    assert.strictEqual(p.run('Observatory.kindOf({ message: __m, category: "npc" })'), kind, source + ': ' + message);
  }
  assert.strictEqual(p.run('Observatory.kindOf({ message: "x", category: "world_boss" })'), 'boss');
  assert.strictEqual(p.run('Observatory.kindOf({ message: "x", category: "pvp" })'), 'players');
});

test('every Chronicle template is an English news template of the game', async () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const en = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'Localization', 'en.json'), 'utf8'));
  const norm = (t) => t.replace(/\{[^}]*\}/g, '{}').replace(/(\{\})+/g, '{}').replace(/" -- \{\}$/, '" {}');
  const known = new Set(Object.values(en).filter((v) => typeof v === 'string').map(norm));
  const p = await open({});
  const templates = JSON.parse(p.run('JSON.stringify(Observatory.templates)'));
  let n = 0;
  for (const [, list] of templates) {
    for (const t of list) {
      n++;
      if (t === 'Gang Recruitment! {} joined {}!') {
        assert.ok(known.has('Gang Recruitment!') && known.has('{} joined {}!'), t);
        continue;
      }
      assert.ok(known.has(norm(t)), 'not an en.json template: ' + t);
    }
  }
  assert.ok(n > 100, 'templates read');
});
