'use strict';
// web/play/index.html, the Chronicle view (2.0 phase 2): a per viewer switch beside Classic, which stays the default.
// The book draws the game's own screen as a text page, and a layout for the fight, a dungeon room or Main Street only
// while their GMCP frames say that screen is up. Every value from a frame is drawn as text.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { HOSTILE, loadPage, readPage, parseHtml, elements } = require('./harness');

const PAGE = 'play/index.html';
const LANGS = ['en', 'es', 'fr', 'hu', 'it'];
const WEB = path.join(__dirname, '..');
const lang = (l) => JSON.parse(fs.readFileSync(path.join(WEB, 'lang', l + '.json'), 'utf8'));
const TOUCH = (q) => ({ matches: /hover:\s*none/.test(q) && /pointer:\s*coarse/.test(q) });
const BROKEN = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); }, removeItem() { throw new Error('blocked'); } };

function socket(sent) {
  return class { constructor() { this.readyState = 1; } send(d) { sent.push(d); } close() {} };
}
async function load(opts = {}) {
  const p = await loadPage(PAGE, Object.assign({ fetch: (url) => (url.startsWith('/lang/') ? lang('en') : url === '/api/languages' ? LANGS : {}) }, opts));
  await p.settle();
  return p;
}
const chronicle = (opts = {}) => load(Object.assign({}, opts, { storage: Object.assign({ playView: 'chronicle' }, opts.storage || {}) }));
function frame(p, gmcp, data) {
  p.ctx.__f = JSON.stringify({ gmcp, data });
  p.run('UsurperPlay.handleFrame(__f)');
}
function screen(p, rows) {
  p.ctx.__rows = rows;
  p.run('UsurperPlay.renderChron(__rows)');
}
const caps = (p) => p.el('c-keys').querySelectorAll('.kc').map((b) => b.getAttribute('aria-label'));
const layout = (p) => p.run('UsurperPlay.layout()');
function descendants(el, out = []) {
  for (const c of el.children || []) { out.push(c); descendants(c, out); }
  return out;
}
// The switch: a radio in the View panel of the drawer.
const radios = (p) => p.el('view-choice').querySelectorAll('input');
function change(p, value) {
  const r = radios(p).find((x) => x.getAttribute('value') === value);
  if (!r) { p.ctx.__v = value; p.run('UsurperPlay.setView(__v)'); return; }
  radios(p).forEach((x) => { x.checked = x === r; });
  p.doc.dispatch(r, 'change');
}
const checked = (p) => radios(p).filter((x) => x.checked).map((x) => x.getAttribute('value'));
// The top bar as it is on main (0a3b1855): the Chronicle work adds nothing to it.
const MAIN_TOP = ['a.brand', 'span#conn.conn', 'button#reconnect.reconnect', 'button#fit-toggle.fit-toggle', 'nav.top-links', 'a', 'a', 'a', 'select#lang-select', 'option'];
const MAIN_TOP_HTML = "<header class=\"top\">\n    <a class=\"brand\" href=\"/\">Usurper Reborn</a>\n    <span id=\"conn\" class=\"conn\" role=\"status\" data-i18n=\"play.status.connecting\">Connecting...</span>\n    <button id=\"reconnect\" class=\"reconnect\" type=\"button\" hidden data-i18n=\"play.reconnect\">Connect again</button>\n    <button id=\"fit-toggle\" class=\"fit-toggle\" type=\"button\" aria-pressed=\"false\" aria-label=\"Bigger text\" title=\"Bigger text\">Aa</button>\n    <nav class=\"top-links\">\n        <a href=\"/\" data-i18n=\"play.home\">Home</a>\n        <a href=\"/wiki/\" target=\"_blank\" rel=\"noopener\" data-i18n=\"play.wiki\">Wiki</a>\n        <a href=\"https://discord.gg/EZhwgDT6Ta\" target=\"_blank\" rel=\"noopener\" data-i18n=\"play.discord\">Discord</a>\n        <select id=\"lang-select\" aria-label=\"Language\"><option value=\"en\">English</option></select>\n    </nav>\n</header>";
function player(p) {
  frame(p, 'Char.Vitals', { hp: 40, maxHp: 62, mp: 0, maxMp: 0, combatSp: 100, maxCombatSp: 105 });
  frame(p, 'Char.Status', { name: 'Rowan', class: 'Warrior', level: 3, race: 'Human', gold: 2500, bank: 0, xp: 900, location: 'Main Street' });
}

const STREET_MENU = {
  screen: 'main_street',
  groups: [
    { label: 'Places', items: [{ key: 'D', label: 'Dungeons', enabled: true }, { key: 'I', label: 'Inn', enabled: true }, { key: 'A', label: 'Dark Alley', enabled: false }] },
    { label: 'System', items: [{ key: '~', label: 'Settings', enabled: true }, { key: 'Q', label: 'Quit Game', enabled: true }] },
  ],
};
const STREET_TEXT = [
  'Main Street',
  ' Players online: 1  |  /say to chat  |  /who for player list',
  'You are standing on the main street of Usurper.',
  ' [D] Dungeons          [I] Inn',
  ' [~] Settings          [Q] Quit Game',
  '[40hp 100st] Main Street >',
];
const ROOM_DETAIL = {
  name: 'Hall of the Ancestors', level: 6, theme: 'Catacombs', header: 'Level 6 | Catacombs',
  danger: { rating: 2, max: 3 }, status: 'danger', boss: false,
  exits: [{ key: 'E', description: 'A dark passage leads east.', state: 'cleared', stateLabel: 'cleared' },
    { key: 'S', description: 'A narrow stair goes down.', state: 'unknown', stateLabel: 'unknown' }],
  features: ['ossuary wall', "embalmer's preparation table"],
};
const ROOM_MENU = {
  screen: 'dungeon_room',
  groups: [
    { label: 'Exits', items: [{ key: 'E', label: 'A dark passage leads east.', enabled: true }, { key: 'S', label: 'A narrow stair goes down.', enabled: true }] },
    { label: 'Actions', items: [{ key: 'F', label: 'Fight the monsters', enabled: true }, { key: 'M', label: 'Map', enabled: true }] },
  ],
};

// ---------------------------------------------------------------- the switch

test('Classic is the default for a new viewer: no book, the terminal page as today, nothing stored', async () => {
  const p = await load();
  assert.strictEqual(p.run('UsurperPlay.view()'), 'classic');
  assert.strictEqual(p.el('chron').hidden, true);
  assert.strictEqual(p.el('text-toggle').closest('.chron'), p.el('chron'), 'the Text page button is in the book');
  assert.ok(!p.doc.body.classList.contains('chron'));
  assert.ok(!p.doc.documentElement.classList.contains('hc'));
  assert.deepStrictEqual(checked(p), ['classic']);
  assert.strictEqual(p.storage.playView, undefined, 'the default is never written for anyone');
  // The Classic page behaves as before: a fight opens the drawer on a wide screen, and the book stays empty.
  const wide = await load({ matchMedia: (q) => ({ matches: /min-width: 1200px/.test(q) }) });
  frame(wide, 'Char.Combat.Start', { enemies: [{ name: 'Zombie', level: 1, hp: 5, maxHp: 9 }] });
  assert.strictEqual(wide.el('drawer').hidden, false);
  assert.strictEqual(wide.el('c-hud').children.length, 0, 'Classic never draws the book');
  assert.deepStrictEqual(wide.errors, []);
});

test('only the two Chronicle values turn the book on; anything else stored is Classic', async () => {
  for (const [stored, want] of [['chronicle', 'chronicle'], ['contrast', 'contrast'], ['classic', 'classic'], ['huge', 'classic'], ['', 'classic'], ['CHRONICLE', 'classic']]) {
    const p = await load({ storage: { playView: stored } });
    assert.strictEqual(p.run('UsurperPlay.view()'), want, stored);
    assert.strictEqual(p.el('chron').hidden, want === 'classic', stored);
    assert.strictEqual(p.doc.body.classList.contains('chron'), want !== 'classic', stored);
  }
});

test('the switch is kept per viewer, and back to Classic is kept too', async () => {
  const p = await load();
  change(p, 'chronicle');
  assert.strictEqual(p.storage.playView, 'chronicle');
  assert.strictEqual(p.el('chron').hidden, false);
  assert.ok(p.doc.body.classList.contains('chron'));
  const again = await load({ storage: { playView: p.storage.playView } });
  assert.strictEqual(again.run('UsurperPlay.view()'), 'chronicle');
  change(again, 'classic');
  assert.strictEqual(again.storage.playView, 'classic');
  assert.strictEqual(again.el('chron').hidden, true);
  assert.ok(!again.doc.body.classList.contains('chron'));
  change(again, 'nonsense');
  assert.strictEqual(again.run('UsurperPlay.view()'), 'classic', 'an unknown choice is Classic');
  assert.deepStrictEqual(again.errors, []);
});

test('a storage that throws: the page opens in Classic and the switch still works', async () => {
  const p = await load({ localStorage: BROKEN });
  assert.strictEqual(p.run('UsurperPlay.view()'), 'classic');
  change(p, 'chronicle');
  assert.strictEqual(p.run('UsurperPlay.view()'), 'chronicle');
  assert.strictEqual(p.el('chron').hidden, false);
  player(p);
  screen(p, ['Hello']);
  assert.ok(p.el('c-body').textContent.includes('Hello'));
  assert.deepStrictEqual(p.errors, []);
  // every storage call in the page is inside a try
  const code = readPage(PAGE);
  for (const m of code.matchAll(/localStorage\.(getItem|setItem)\([^)]*\)/g)) {
    const line = code.slice(code.lastIndexOf('\n', m.index), code.indexOf('\n', m.index));
    assert.match(line, /try \{/, line.trim());
  }
});

test('Classic\'s top bar is the one on main: the switch takes no room there at any width', () => {
  const html = readPage(PAGE);
  const tree = parseHtml(html);
  const top = elements(tree).find((e) => e.tag === 'header' && e.attrs.class === 'top');
  const sig = (e) => e.tag + (e.attrs.id ? '#' + e.attrs.id : '') + (e.attrs.class ? '.' + e.attrs.class.split(/\s+/).join('.') : '');
  assert.deepStrictEqual(elements(top).map(sig), MAIN_TOP);
  assert.strictEqual(/<header class="top">[\s\S]*?<\/header>/.exec(html)[0], MAIN_TOP_HTML, 'the top bar markup is byte for byte main\'s');
  // and the phone rules for the top bar are main's too: nothing narrows the language list
  const style = /<style>([\s\S]*?)<\/style>/.exec(html)[1];
  assert.deepStrictEqual(style.match(/\.top-links select \{[^}]*\}/g), ['.top-links select { font: inherit; font-size: 14px; text-transform: none; letter-spacing: 0; color: var(--bone); background: var(--card); border: 1px solid var(--iron); padding: 2px 6px; }']);
  const css = fs.readFileSync(path.join(WEB, 'play', 'chronicle.css'), 'utf8');
  assert.doesNotMatch(css, /(^|[\s,}])\.top[\s.{,-]/m, 'the Chronicle stylesheet leaves the top bar alone');
});

test('the switch is a View panel beside Combat and Chat; the book has a Text page and a Classic button; all from the language file', async () => {
  const sent = [];
  const p = await loadPage(PAGE, { storage: { lang: 'fr', playView: 'chronicle' }, WebSocket: socket(sent), fetch: (url) => (url === '/lang/fr.json' ? lang('fr') : url === '/api/languages' ? LANGS : {}) });
  await p.settle();
  assert.strictEqual(p.el('rail-view').textContent, 'Affichage');
  assert.strictEqual(p.el('tab-view').textContent, 'Affichage');
  assert.strictEqual(p.el('view-choice').getAttribute('aria-label'), 'Affichage');
  assert.strictEqual(p.el('view-choice').getAttribute('role'), 'radiogroup');
  const opts = radios(p).map((o) => [o.getAttribute('value'), o.getAttribute('type'), o.parentElement.textContent.trim()]);
  assert.deepStrictEqual(opts, [['classic', 'radio', 'Classique'], ['chronicle', 'radio', 'Chronique'], ['contrast', 'radio', 'Chronique, contraste élevé']]);
  assert.deepStrictEqual(checked(p), ['chronicle']);
  p.click(p.el('rail-view'));
  assert.strictEqual(p.el('pane-view').hidden, false);
  assert.strictEqual(p.el('rail-view').getAttribute('aria-pressed'), 'true');
  assert.strictEqual(p.el('pane-combat').hidden, true);
  assert.strictEqual(p.el('classic-back').textContent, 'Classique');
  p.click(p.el('classic-back'));
  assert.strictEqual(p.run('UsurperPlay.view()'), 'classic', 'the book has its own way back');
  assert.deepStrictEqual(checked(p), ['classic']);
  assert.strictEqual(p.el('chron').hidden, true);
  assert.strictEqual(p.el('text-toggle').textContent, 'Page de texte');
  assert.strictEqual(p.el('text-toggle').getAttribute('type'), 'button');
  assert.ok(p.el('c-verso').querySelector('.rh').querySelectorAll('button').includes(p.el('text-toggle')));
});

test('the new strings are real translations in all five languages', () => {
  const keys = Object.keys(lang('en')).filter((k) => /^play\.(view|chron)\./.test(k));
  assert.ok(keys.length >= 20);
  const en = lang('en');
  for (const l of LANGS.slice(1)) {
    const d = lang(l);
    const same = keys.filter((k) => d[k] === en[k]);
    // Only words that are the same in that language may match English (XP, and Danger and Potions in French).
    const alike = { fr: ['play.chron.xp', 'play.chron.danger', 'play.chron.potions'] }[l] || [];
    assert.deepStrictEqual(same.filter((k) => !alike.includes(k)), [], l);
    for (const k of keys) assert.ok(typeof d[k] === 'string' && d[k].trim().length > 0, l + ' ' + k);
  }
  for (const l of LANGS) for (const k of keys) assert.doesNotMatch(lang(l)[k], /[\u2013\u2014]/, l + ' ' + k);
});

// ---------------------------------------------------------------- the text page

test('with no new frames the book is the text page: the screen as the game prints it, keys read from the screen', async () => {
  const sent = [];
  const p = await chronicle({ WebSocket: socket(sent) });
  player(p);
  frame(p, 'Room.Info', { num: 1, name: 'Main Street', area: 'Usurper Reborn', exits: { theinn: 2 } });
  screen(p, STREET_TEXT);
  assert.strictEqual(layout(p), 'text');
  assert.strictEqual(p.el('chron').getAttribute('data-layout'), 'text');
  const pre = p.el('c-screen');
  assert.ok(pre, 'the text page');
  assert.strictEqual(pre.tagName, 'PRE');
  assert.deepStrictEqual(pre.children.map((r) => r.textContent), STREET_TEXT);
  assert.deepStrictEqual(caps(p), ['D, Dungeons', 'I, Inn', '~, Settings', 'Q, Quit Game']);
  assert.strictEqual(p.el('c-enterbar').hidden, true, 'a menu does not show the Enter key');
  p.click(p.el('c-keys').querySelectorAll('.kc')[1]);
  assert.deepStrictEqual(sent, ['I\r'], 'a key cap sends the key and Enter, as the pad does');
  assert.strictEqual(p.el('c-cap').textContent, 'Plate: Main Street');
  assert.deepStrictEqual(p.errors, []);
});

test('the text page keeps the game colours as runs and drops anything that is not a known colour class', async () => {
  const p = await chronicle();
  screen(p, [[{ t: 'HP: ', c: '' }, { t: '40', c: 'k-red' }, { t: '/62', c: 'k-wht' }], [{ t: 'odd', c: 'x" onclick="a()' }], 'plain']);
  const runs = p.el('c-screen').children[0].children.map((s) => [s.textContent, s.getAttribute('class')]);
  assert.deepStrictEqual(runs, [['HP: ', null], ['40', 'k-red'], ['/62', 'k-wht']]);
  assert.strictEqual(p.el('c-screen').children[1].children[0].getAttribute('class'), null, 'an unknown class is dropped');
});

test('a screen that waits for Enter shows the Enter key; a yes or no question offers its two letters', async () => {
  const sent = [];
  const p = await chronicle({ WebSocket: socket(sent) });
  screen(p, ['You wake with a gasp.', 'Press Enter to continue...']);
  assert.strictEqual(p.el('c-enterbar').hidden, false);
  assert.ok(p.el('c-enterbar').textContent.includes('Press Enter to continue...'));
  p.click(p.el('c-enterbar').querySelector('.kc'));
  assert.deepStrictEqual(sent, ['\r'], 'Enter alone');
  screen(p, ['¿Beber la poción? (S/N)']);
  assert.strictEqual(p.el('c-enterbar').hidden, true);
  assert.deepStrictEqual(caps(p), ['S, Yes', 'N, No']);
  screen(p, ['  Username:']);
  assert.deepStrictEqual(caps(p), [], 'a question that wants typing offers no keys');
  assert.strictEqual(p.el('c-keys').hidden, true);
});

// ---------------------------------------------------------------- Client.Menu

test('with Client.Menu the keys are the frame\'s, in its groups and order; a refused key is shown disabled and sends nothing', async () => {
  const sent = [];
  const p = await chronicle({ WebSocket: socket(sent) });
  player(p);
  frame(p, 'Room.Info', { num: 1, name: 'Main Street', area: 'Usurper Reborn' });
  screen(p, STREET_TEXT);
  frame(p, 'Client.Menu', STREET_MENU);
  assert.strictEqual(layout(p), 'street');
  assert.deepStrictEqual(p.el('c-keys').querySelectorAll('.kh').map((h) => h.textContent), ['Places', 'System']);
  assert.deepStrictEqual(caps(p), ['D, Dungeons', 'I, Inn', 'A, Dark Alley', '~, Settings', 'Q, Quit Game'],
    'A is in the frame though the screen does not show it, and no scanned group sits beside the frame\'s');
  const off = p.el('c-keys').querySelectorAll('.kc')[2];
  assert.ok(off.classList.contains('off'));
  assert.strictEqual(off.getAttribute('aria-disabled'), 'true');
  p.click(off);
  assert.deepStrictEqual(sent, [], 'a disabled key sends nothing');
  p.click(p.el('c-keys').querySelectorAll('.kc')[0]);
  assert.deepStrictEqual(sent, ['D\r']);
  // The menu lines are on the dock, so the street text leaves them out; the rest of the screen stays.
  const prose = p.el('c-body').textContent;
  assert.ok(prose.includes('You are standing on the main street of Usurper.'));
  assert.ok(!prose.includes('[D] Dungeons'), prose);
  assert.ok(prose.includes('Players online: 1'));
  assert.strictEqual(p.el('c-body').querySelector('h1').textContent, 'Main Street');
  assert.deepStrictEqual(p.errors, []);
});

test('a menu goes stale at the next Enter: the keys come from the screen again, and the street gives way when its draw ends without one', async () => {
  const sent = [];
  const p = await chronicle({ WebSocket: socket(sent) });
  frame(p, 'Room.Info', { num: 1, name: 'Main Street', area: 'Usurper Reborn' });
  frame(p, 'Client.Menu', STREET_MENU);
  assert.strictEqual(layout(p), 'street');
  p.click(p.el('c-keys').querySelectorAll('.kc')[1]);   // I, Inn
  assert.deepStrictEqual(sent, ['I\r']);
  screen(p, ['The Inn', ' [B] Buy a drink   [R] Rest', 'Your choice:']);
  assert.strictEqual(layout(p), 'street', 'nothing flashes while the draw is open');
  assert.deepStrictEqual(caps(p), ['B, Buy a drink', 'R, Rest'], 'the old menu is not offered on the new screen');
  p.run('UsurperPlay.drawEnded()');
  assert.strictEqual(layout(p), 'text', 'no Main Street menu in this draw');
  // A new menu in the next draw brings the street back.
  p.run('UsurperPlay.drawStarted(300)');
  frame(p, 'Client.Menu', STREET_MENU);
  p.run('UsurperPlay.drawEnded()');
  assert.strictEqual(layout(p), 'street');
});

test('a Room.Info for another place ends the street layout at once', async () => {
  const p = await chronicle();
  frame(p, 'Client.Menu', STREET_MENU);
  assert.strictEqual(layout(p), 'street');
  frame(p, 'Room.Info', { num: 1, name: 'Main Street', area: 'Usurper Reborn' });
  assert.strictEqual(layout(p), 'street');
  frame(p, 'Room.Info', { num: 2, name: 'The Inn', area: 'Usurper Reborn' });
  assert.strictEqual(layout(p), 'text');
});

test('a menu for another screen ends a layout, and odd menu frames are dropped at the door', async () => {
  const p = await chronicle();
  frame(p, 'Client.Menu', STREET_MENU);
  frame(p, 'Client.Menu', { screen: 'something_new', groups: [{ label: 'X', items: [{ key: 'X', label: 'x' }] }] });
  assert.strictEqual(layout(p), 'text');
  for (const bad of [null, 5, 'x', [], { groups: 'x' }, { groups: [null, 5, { items: 'x' }, { items: [null, { key: 5 }, { key: '' }, { key: 'TOOLONGKEY' }, { key: 'A B' }, { key: { toString: null } }] }] }, { screen: { toString: null }, hint: 5 }]) {
    p.ctx.__d = bad;
    p.run("UsurperPlay.handleGmcp('Client.Menu', __d)");
  }
  assert.deepStrictEqual(p.errors, []);
  frame(p, 'Client.Menu', STREET_MENU);
  assert.strictEqual(layout(p), 'street');
  assert.strictEqual(caps(p).length, 5);
});

test('the fight menu fills the dock with its groups and the quickbar hint under its group', async () => {
  const p = await chronicle();
  player(p);
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Zombie', level: 13, hp: 1222, maxHp: 1811 }] });
  frame(p, 'Client.Menu', {
    screen: 'combat',
    hint: { text: 'type ?1-?9 to see what a slot does', label: 'Quickbar' },
    groups: [
      { label: 'Actions', items: [{ key: 'A', label: 'Attack', enabled: true }, { key: 'I', label: 'Use Item (0/5)', enabled: false }] },
      { label: 'Quickbar', items: [{ key: '1', label: 'Power Strike', enabled: true }] },
      { label: 'Options', items: [{ key: 'AUTO', label: 'Auto-Combat', enabled: true }, { key: 'SPD', label: 'Speed: Normal', enabled: true }] },
    ],
  });
  assert.strictEqual(layout(p), 'fight');
  assert.deepStrictEqual(caps(p), ['A, Attack', 'I, Use Item (0/5)', '1, Power Strike', 'AUTO, Auto-Combat', 'SPD, Speed: Normal']);
  const groups = p.el('c-keys').querySelectorAll('.kgroup');
  assert.strictEqual(groups[1].querySelector('.khint').textContent, 'type ?1-?9 to see what a slot does');
  assert.strictEqual(groups[0].querySelector('.khint'), null);
  assert.ok(p.el('c-keys').querySelectorAll('.cap').find((c) => c.textContent === 'AUTO').classList.contains('wide'));
});

// ---------------------------------------------------------------- Room.Detail

test('Room.Detail draws the room: its name, level and danger, what you notice, and only the exits it sends', async () => {
  const p = await chronicle();
  player(p);
  frame(p, 'Room.Info', { num: 'room_4', name: 'Hall of the Ancestors', area: 'Dungeon Floor 6', exits: { east: 'room_5', south: 'room_9' } });
  screen(p, ['╔═══════════╗', '║  Hall of the Ancestors  ║', '╚═══════════╝', 'Level 6 | Catacombs',
    'A grand hall where skeletal remains sit enthroned.', '  [E] A dark passage leads east. (cleared)', '  [S] A narrow stair goes down. (unknown)', '  [F] Fight the monsters  [M] Map', '[40hp 100st] Dungeon Fl.6 >']);
  frame(p, 'Room.Detail', ROOM_DETAIL);
  frame(p, 'Client.Menu', ROOM_MENU);
  assert.strictEqual(layout(p), 'room');
  const body = p.el('c-body');
  assert.strictEqual(body.querySelector('h1').textContent, 'Hall of the Ancestors');
  assert.strictEqual(body.querySelector('.chap-sub').textContent, 'Level 6 · Catacombs · Danger 2/3');
  assert.strictEqual(body.querySelector('.note').textContent, "You notice:ossuary wall, embalmer's preparation table");
  const prose = body.querySelector('.screen').textContent;
  assert.ok(prose.includes('A grand hall where skeletal remains sit enthroned.'));
  for (const gone of ['Hall of the Ancestors', 'Level 6 | Catacombs', '[E] A dark passage', '[F] Fight', '═══']) assert.ok(!prose.includes(gone), gone);
  const cells = body.querySelector('.ccomp').children;
  const on = cells.filter((c) => c.classList.contains('on')).map((c) => c.textContent);
  assert.deepStrictEqual(on, ['Ecleared', 'Sunknown'], 'the exits the frame sends, with their state words');
  const off = cells.filter((c) => !c.classList.contains('on') && !c.classList.contains('mid')).map((c) => c.textContent);
  assert.deepStrictEqual(off, ['N', 'W'], 'no exit north or west');
  assert.ok(cells.filter((c) => !c.classList.contains('on')).every((c) => c.getAttribute('aria-hidden') === 'true'));
  assert.strictEqual(p.el('c-cap').textContent, 'Plate: Catacombs, Level 6');
  assert.strictEqual(p.el('c-rh').textContent, 'Catacombs · Level 6');
  assert.deepStrictEqual(caps(p), ['E, A dark passage leads east.', 'S, A narrow stair goes down.', 'F, Fight the monsters', 'M, Map']);
  assert.deepStrictEqual(p.errors, []);
});

const ROOM_SCREEN = ['╔═══════════╗', '║  Hall of the Ancestors  ║', '╚═══════════╝', 'Level 6 | Catacombs   Danger: ** ', '',
  'A grand hall where skeletal remains sit enthroned.', 'Candles that should have burned out still flicker.', 'Something is watching you from the shadows...', '',
  'Exits:', '  [E] A dark passage leads east. (cleared)', '  [S] A narrow stair goes down. (unknown)', 'Actions:', '  [F] Fight the monsters  [M] Map', '', '[40hp 100st] Dungeon Fl.6 >'];
const KEPT = ['A grand hall where skeletal remains sit enthroned.', 'Candles that should have burned out still flicker.', 'Something is watching you from the shadows...', '[40hp 100st] Dungeon Fl.6 >'];

test('with Room.Detail and the room menu, the room text drops the emptied headings and the header line, and keeps the rest', async () => {
  const p = await chronicle();
  frame(p, 'Room.Info', { num: 'room_4', name: 'Hall of the Ancestors', area: 'Dungeon Floor 6' });
  screen(p, ROOM_SCREEN);
  frame(p, 'Room.Detail', ROOM_DETAIL);
  frame(p, 'Client.Menu', ROOM_MENU);
  const lines = p.el('c-body').querySelector('.screen').children.map((l) => l.textContent).filter((t) => t.trim());
  assert.deepStrictEqual(lines, KEPT);
});

test('without the room menu the headings and the header line stay; the text page keeps every line', async () => {
  const p = await chronicle();
  frame(p, 'Room.Info', { num: 'room_4', name: 'Hall of the Ancestors', area: 'Dungeon Floor 6' });
  screen(p, ROOM_SCREEN);
  frame(p, 'Room.Detail', ROOM_DETAIL);
  const lines = p.el('c-body').querySelector('.screen').children.map((l) => l.textContent).filter((t) => t.trim());
  for (const kept of ['Level 6 | Catacombs   Danger: **', 'Exits:', 'Actions:', ...KEPT]) assert.ok(lines.some((l) => l.trim() === kept.trim()), kept);
  // no frames at all: the text page, every row as the game printed it
  const q = await chronicle();
  screen(q, ROOM_SCREEN);
  assert.strictEqual(layout(q), 'text');
  assert.deepStrictEqual(q.el('c-screen').children.map((l) => l.textContent), ROOM_SCREEN.map((r) => r || ' '));
  // a heading whose lines stay on the page keeps its heading even with the menu
  const r = await chronicle();
  frame(r, 'Room.Info', { num: 'room_4', name: 'Hall', area: 'Dungeon Floor 6' });
  screen(r, ['Strange marks:', 'A spiral cut into the floor.', 'An inscription reads:', 'Exits:', '  [E] A dark passage leads east.', 'Hall of the Ancestors:', 'Level 6 | Catacombs']);
  frame(r, 'Room.Detail', ROOM_DETAIL);
  frame(r, 'Client.Menu', ROOM_MENU);
  assert.deepStrictEqual(r.el('c-body').querySelector('.screen').children.map((l) => l.textContent), ['Strange marks:', 'A spiral cut into the floor.', 'An inscription reads:', 'Hall of the Ancestors:'],
    'a heading goes only with a line that went to the dock, never with another heading or a header line');
});

test('a key cap is as wide as its key: long keys are marked and never clipped', async () => {
  const p = await chronicle();
  screen(p, ['[AUTO] Auto-Combat  [SPD] Speed  [A] Attack', 'Choose action:']);
  const capsEl = p.el('c-keys').querySelectorAll('.cap');
  assert.deepStrictEqual(capsEl.map((c) => [c.textContent, c.classList.contains('wide')]), [['AUTO', true], ['SPD', true], ['A', false]]);
  const css = fs.readFileSync(path.join(WEB, 'play', 'chronicle.css'), 'utf8');
  assert.match(css, /^\.chron \.kc \.cap\.wide \{ width: auto !important; white-space: nowrap; \}$/m);
  for (const m of css.matchAll(/\.kc[^{]*\{[^}]*grid-template-columns: ([^;]+);/g)) assert.doesNotMatch(m[1], /^\d+px 1fr$/, 'the cap column is never a fixed width: ' + m[0].slice(0, 60));
  for (const m of css.matchAll(/\.cap[^{,]*\{([^}]*)\}/g)) assert.doesNotMatch(m[1], /overflow:\s*hidden/, 'a cap never clips its key');
});

test('the compass draws nothing the frame does not say: no exits, odd keys, and the Room.Info exits are not used', async () => {
  const p = await chronicle();
  frame(p, 'Room.Info', { num: 'room_1', name: 'Crypt', area: 'Dungeon Floor 2', exits: { north: 'r2', west: 'r3' } });
  frame(p, 'Room.Detail', Object.assign({}, ROOM_DETAIL, { exits: [{ key: 'X', stateLabel: 'odd' }, { key: 'north', stateLabel: 'odd' }, { key: 'n', stateLabel: 'odd' }, null, 5] }));
  const cells = p.el('c-body').querySelector('.ccomp').children;
  assert.strictEqual(p.run('UsurperPlay.chron.detail.exits.length'), 0, 'only N, S, E and W are kept');
  assert.strictEqual(cells.filter((c) => c.classList.contains('on')).length, 0);
  assert.ok(!p.el('c-body').querySelector('.ccomp').textContent.includes('odd'));
  frame(p, 'Room.Detail', Object.assign({}, ROOM_DETAIL, { exits: [{ key: 'W' }] }));
  const on = p.el('c-body').querySelector('.ccomp').children.filter((c) => c.classList.contains('on'));
  assert.deepStrictEqual(on.map((c) => c.textContent), ['W'], 'an exit with no state word shows its letter only');
});

test('the room layout gives way to the text page when a draw ends without a Room.Detail, and on leaving the dungeon', async () => {
  const sent = [];
  const p = await chronicle({ WebSocket: socket(sent) });
  frame(p, 'Room.Info', { num: 'room_1', name: 'Crypt', area: 'Dungeon Floor 2' });
  frame(p, 'Room.Detail', ROOM_DETAIL);
  assert.strictEqual(layout(p), 'room');
  p.run('UsurperPlay.drawStarted(300)');   // the screen clears for the map
  assert.strictEqual(layout(p), 'room', 'pending, not flashed');
  p.run('UsurperPlay.drawEnded()');
  assert.strictEqual(layout(p), 'text');
  p.run('UsurperPlay.drawStarted(300)');
  frame(p, 'Room.Detail', ROOM_DETAIL);
  p.run('UsurperPlay.drawEnded()');
  assert.strictEqual(layout(p), 'room', 'a detail in the draw keeps the room');
  frame(p, 'Room.Info', { num: 1, name: 'Main Street', area: 'Usurper Reborn' });
  assert.strictEqual(layout(p), 'text', 'out of the dungeon');
});

test('odd Room.Detail fields are dropped at the door', async () => {
  const p = await chronicle();
  const odd = [{ toString: null }, [1, 2], null, true, 'NaN', Infinity, NaN];
  for (const v of odd) {
    p.ctx.__d = { name: v, level: v, theme: v, header: v, danger: v, status: v, boss: v, exits: v, features: v };
    p.run("UsurperPlay.handleGmcp('Room.Detail', __d)");
    p.ctx.__d = { name: 'R', danger: { rating: v, max: v }, exits: [{ key: 'N', description: v, state: v, stateLabel: v }], features: [v, 'ok'] };
    p.run("UsurperPlay.handleGmcp('Room.Detail', __d)");
  }
  assert.deepStrictEqual(p.errors, []);
  assert.strictEqual(layout(p), 'room');
});

// ---------------------------------------------------------------- the fight

test('the fight layout turns on at Char.Combat.Start and off at Char.Combat.End', async () => {
  const p = await chronicle();
  player(p);
  assert.strictEqual(layout(p), 'text');
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Zombie', level: 13, hp: 1222, maxHp: 1811 }, { name: 'Ghoul', level: 16, hp: 940, maxHp: 940, isBoss: true }] });
  assert.strictEqual(layout(p), 'fight');
  frame(p, 'Char.Combat.Party', { members: [{ name: 'Rowan', level: 3, hp: 40, maxHp: 62, isAlive: true, isPlayer: true }, { name: 'Aldric', level: 4, hp: 0, maxHp: 50, isAlive: false }] });
  frame(p, 'Char.Combat.Enemies', { enemies: [{ name: 'Zombie', level: 13, hp: 600, maxHp: 1811, alive: true }, { name: 'Ghoul', level: 16, hp: 940, maxHp: 940, alive: true, isBoss: true }], round: 4 });
  frame(p, 'Char.Combat.Killed', { name: 'Zombie' });
  screen(p, ['Zombie attacks you!', '[Zombie rolls: 12 + 29 = 41 vs DC 34]', '71 damage gets through your defenses!', 'You deal 154 damage to Zombie!']);
  const cols = p.el('c-hud').querySelectorAll('.hcol');
  assert.strictEqual(cols.length, 2);
  const party = cols[0].querySelectorAll('.card');
  assert.strictEqual(party[0].querySelector('.nm').textContent, 'Rowan');
  assert.strictEqual(party[0].querySelectorAll('.m').length, 2, 'the player has HP and ST');
  assert.strictEqual(party[1].querySelectorAll('.m').length, 1, 'a companion has no stamina source: HP only');
  assert.ok(party[1].classList.contains('out') && party[1].textContent.includes('Dead'));
  const foes = cols[1].querySelectorAll('.card');
  assert.ok(foes[0].classList.contains('out') && foes[0].textContent.includes('Dead') && foes[0].textContent.includes('0/1,811'));
  assert.ok(foes[1].textContent.includes('Boss'));
  const log = p.el('c-body').querySelector('.clog');
  assert.deepStrictEqual(log.children.map((e) => e.getAttribute('class')), ['ev a3', 'ev a2 aside', 'ev a1', 'ev a0', 'rd now']);
  assert.strictEqual(log.children[4].textContent, 'Round 4');
  assert.strictEqual(p.el('c-rh-phone').textContent, 'Round 4');
  assert.strictEqual(p.el('drawer').hidden, true, 'the book shows the panels, so the drawer does not open on its own');
  frame(p, 'Char.Combat.End', { outcome: 'Victory', kills: 2, xpGained: 45, goldGained: 73 });
  assert.strictEqual(layout(p), 'text');
  assert.strictEqual(p.el('c-hud').children.length, 0);
  assert.ok(p.el('combat-result').textContent.startsWith('Victory'), 'the Combat drawer still gets the result');
  assert.deepStrictEqual(p.errors, []);
});

test('a draw ending in a fight keeps the fight; a Room.Detail during the fight does not replace it', async () => {
  const p = await chronicle();
  frame(p, 'Room.Info', { num: 'room_1', name: 'Crypt', area: 'Dungeon Floor 6' });
  frame(p, 'Room.Detail', ROOM_DETAIL);
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Zombie', level: 1, hp: 5, maxHp: 9 }] });
  p.run('UsurperPlay.drawStarted(300)');
  p.run('UsurperPlay.drawEnded()');
  assert.strictEqual(layout(p), 'fight');
  frame(p, 'Room.Detail', ROOM_DETAIL);
  assert.strictEqual(layout(p), 'fight');
  assert.strictEqual(p.el('c-body').querySelector('h1').textContent, 'Hall of the Ancestors', 'the room heads the fight');
  frame(p, 'Client.Menu', STREET_MENU);
  assert.strictEqual(layout(p), 'street', 'a new screen\'s frame still wins');
});

test('combat and room names with HTML stay plain text in the book', async () => {
  const p = await chronicle();
  const H = '<img src=x onerror=alert(2)><script>a()</script>&amp;';
  frame(p, 'Char.Status', { name: H, class: H, level: 1, gold: 1, xp: 1 });
  frame(p, 'Char.Vitals', { hp: 1, maxHp: 2 });
  frame(p, 'Room.Info', { num: 'r', name: H, area: 'Dungeon Floor 1' });
  frame(p, 'Room.Detail', { name: H, theme: H, header: H, level: 1, features: [H], exits: [{ key: 'N', stateLabel: H }] });
  frame(p, 'Client.Menu', { screen: 'dungeon_room', hint: { text: H, label: H }, groups: [{ label: H, items: [{ key: '<', label: H }] }] });
  screen(p, [HOSTILE, [{ t: HOSTILE, c: 'k-red' }]]);
  for (const id of ['c-body', 'c-hud', 'c-keys', 'c-cap', 'c-rh', 'c-foot']) {
    const tags = descendants(p.el(id)).map((e) => e.tagName);
    assert.ok(tags.every((t) => ['DIV', 'SPAN', 'B', 'SMALL', 'I', 'H1', 'PRE', 'BUTTON'].includes(t)), id + ': ' + tags.join(','));
  }
  assert.ok(p.el('c-body').textContent.includes(H));
  assert.ok(p.el('c-keys').textContent.includes(H));
  frame(p, 'Char.Combat.Start', { enemies: [{ name: H, level: 1, hp: 1, maxHp: 2 }] });
  screen(p, [HOSTILE]);
  assert.ok(p.el('c-hud').textContent.includes(H));
  assert.ok(p.el('c-body').textContent.includes(HOSTILE));
  assert.deepStrictEqual(p.errors, []);
});

// ---------------------------------------------------------------- footer, art

test('the footer shows only the fields the game sends, in one order', async () => {
  const p = await chronicle();
  screen(p, ['x']);
  assert.strictEqual(p.el('c-foot').children.length, 0);
  frame(p, 'Char.Vitals', { hp: 40, maxHp: 62 });
  assert.deepStrictEqual(p.el('c-foot').querySelectorAll('.fl').map((e) => e.textContent), ['HP']);
  player(p);
  const f = p.el('c-foot');
  assert.deepStrictEqual(f.querySelectorAll('.fl').map((e) => e.textContent), ['HP', 'Gold', 'Stamina', 'Level', 'XP']);
  assert.deepStrictEqual(f.querySelectorAll('b').map((e) => e.textContent), ['40/62', '2,500', '100/105', '3', '900']);
  for (const gone of ['Potions', 'Lives']) assert.ok(!f.textContent.includes(gone), gone);
  assert.ok(f.querySelectorAll('.sep').every((s) => s.getAttribute('aria-hidden') === 'true'));
  assert.deepStrictEqual(f.children.map((c) => c.getAttribute('class')), ['f', 'sep', 'f', 'sep sd', 'brk bn', 'f', 'sep sn', 'brk bd', 'f', 'sep sd', 'brk bn', 'f']);
});

test('art comes only from language neutral data: location id, dungeon floor, class name; otherwise none', async () => {
  const p = await chronicle();
  const bg = () => { const s = p.el('c-frame').querySelector('.scene'); return s ? s.style.backgroundImage || '' : null; };
  const img = () => { const i = p.el('c-frame').querySelector('img'); return i ? i.getAttribute('src') : null; };
  frame(p, 'Char.Status', { name: 'Rowan', class: 'MysticShaman', level: 1, gold: 1, xp: 1 });
  assert.strictEqual(img(), '/play/art/classes/mystic-shaman.png', 'no place yet: the class portrait');
  frame(p, 'Room.Info', { num: 1, name: 'Calle Mayor', area: 'Usurper Reborn' });
  assert.strictEqual(bg(), 'url("/play/art/scenes/main-street.png")');
  frame(p, 'Room.Info', { num: 999, name: 'Main Street', area: 'Usurper Reborn' });
  assert.strictEqual(bg(), null, 'a name is never read for art');
  assert.strictEqual(img(), '/play/art/classes/mystic-shaman.png', 'an unknown place: the portrait again');
  frame(p, 'Room.Info', { num: 'room_3', name: 'Crypt', area: 'Dungeon Floor 12' });
  frame(p, 'Room.Detail', Object.assign({}, ROOM_DETAIL, { theme: 'Frozen Depths' }));
  assert.strictEqual(bg(), 'url("/play/art/scenes/dungeon.png")', 'one dungeon scene: the theme is a translated name');
  frame(p, 'Room.Info', { num: 'room_3', name: 'Crypt', area: 'Dungeon Floor 12b' });
  frame(p, 'Char.Status', { name: 'Rowan', class: 'Wizard', level: 1, gold: 1, xp: 1 });
  assert.strictEqual(img(), null, 'an unknown class has no portrait');
  assert.strictEqual(bg(), '', 'and an area that is not exactly a floor has no scene: the blank frame');
  assert.ok(p.el('c-frame').querySelector('.scene').classList.contains('blank'));
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Skeleton', level: 1, hp: 1, maxHp: 1 }] });
  assert.strictEqual(p.el('c-frame').querySelectorAll('img').length, 0, 'no monster sprite in phase 2');
  // every file the page can name exists, and is pixel art drawn at whole scales
  const code = readPage(PAGE);
  const scenes = /var TOWN = \{([^}]*)\}/.exec(code)[1].match(/'([a-z-]+)'/g).map((s) => s.slice(1, -1)).concat(['main-street', 'dungeon']);
  for (const s of scenes) assert.ok(fs.existsSync(path.join(WEB, 'play', 'art', 'scenes', s + '.png')), s);
  const classes = /var CLASSES = \[([^\]]*)\]/.exec(code)[1].match(/'(\w+)'/g).map((s) => s.slice(1, -1));
  assert.strictEqual(classes.length, 17);
  for (const c of classes) assert.ok(fs.existsSync(path.join(WEB, 'play', 'art', 'classes', (c === 'MysticShaman' ? 'mystic-shaman' : c.toLowerCase()) + '.png')), c);
  const css = fs.readFileSync(path.join(WEB, 'play', 'chronicle.css'), 'utf8');
  assert.match(css, /\.chron \.scene \{[^}]*image-rendering: pixelated/);
  assert.match(css, /\.chron \.portrait img \{[^}]*image-rendering: pixelated/);
  const sizes = [...css.matchAll(/\.chron \.portrait(?:, \.chron \.portrait img)? \{[^}]*?width: (\d+)px; height: (\d+)px/g)].map((m) => [m[1], m[2]]);
  assert.ok(sizes.length >= 2);
  for (const [w, h] of sizes) assert.ok(w === h && ['192', '384'].includes(w), 'portraits at 1x or 2x only: ' + w);
  for (const m of css.matchAll(/background-size: (\d+)px (\d+)px/g)) assert.ok([384, 768].includes(Number(m[1])) && Number(m[2]) * 2 === Number(m[1]), m[0]);
});

// ---------------------------------------------------------------- phase 3a: footer, sprites, theme scenes, map, sheet

// Frames as the game sends them (WebClient3aTests and the r200-p3 capture): real labels and values.
const FOOTER = { potions: 7, maxPotions: 31, lives: 2, maxLives: 3, livesKind: 'lives', xpPercent: 37 };

test('Client.Footer brings all seven fields, in one order, with the XP percent after the last as the status line prints it', async () => {
  const p = await chronicle();
  player(p);
  frame(p, 'Client.Footer', FOOTER);
  const f = p.el('c-foot');
  assert.deepStrictEqual(f.querySelectorAll('.fl').map((e) => e.textContent), ['HP', 'Potions', 'Gold', 'Stamina', 'Level', 'XP', 'Lives']);
  assert.deepStrictEqual(f.querySelectorAll('b').map((e) => e.textContent), ['40/62', '7/31', '2,500', '100/105', '3', '900', '2/3']);
  assert.deepStrictEqual(f.querySelectorAll('.pc').map((e) => e.textContent), ['(37%)']);
  assert.ok(f.querySelectorAll('.f')[6].querySelector('.pc'), 'the percent sits with the last field');
  // desktop rows HP Potions Gold Stamina / Level XP Lives; narrower HP Potions Gold / Stamina Level / XP Lives
  assert.deepStrictEqual(f.children.map((c) => c.getAttribute('class')),
    ['f', 'sep', 'f', 'sep', 'f', 'sep sd', 'brk bn', 'f', 'sep sn', 'brk bd', 'f', 'sep sd', 'brk bn', 'f', 'sep', 'f']);
  // revives in single player wording, and no lives where the line prints none: the percent moves to XP
  frame(p, 'Client.Footer', Object.assign({}, FOOTER, { livesKind: 'revives' }));
  assert.strictEqual(f.querySelectorAll('.fl')[6].textContent, 'Revives');
  frame(p, 'Client.Footer', { potions: 7, maxPotions: 31, xpPercent: 5 });
  assert.deepStrictEqual(f.querySelectorAll('.fl').map((e) => e.textContent), ['HP', 'Potions', 'Gold', 'Stamina', 'Level', 'XP']);
  assert.strictEqual(f.querySelectorAll('.f')[5].querySelector('.pc').textContent, '(5%)');
  frame(p, 'Client.Footer', { potions: 7, maxPotions: 31 });
  assert.deepStrictEqual(f.querySelectorAll('.pc'), [], 'no percent at the level cap');
  assert.deepStrictEqual(p.errors, []);
});

test('odd Client.Footer values are dropped at the door: a pair comes whole, lives only with their kind', async () => {
  const p = await chronicle();
  player(p);
  for (const bad of [null, 5, 'x', [], { potions: '7', maxPotions: 31 }, { potions: 7 }, { lives: 2, maxLives: 3 }, { lives: 2, maxLives: 3, livesKind: 'souls' },
    { potions: -4, maxPotions: NaN, xpPercent: Infinity }, { potions: { toString: null }, livesKind: { toString: null } }]) {
    p.ctx.__d = bad;
    p.run("UsurperPlay.handleGmcp('Client.Footer', __d)");
    const labels = p.el('c-foot').querySelectorAll('.fl').map((e) => e.textContent);
    assert.ok(!labels.includes('Lives') && !labels.includes('Revives'), JSON.stringify(bad));
  }
  p.ctx.__d = { potions: -4, maxPotions: 2.7, xpPercent: 250 };
  p.run("UsurperPlay.handleGmcp('Client.Footer', __d)");
  assert.strictEqual(p.el('c-foot').querySelectorAll('b')[1].textContent, '0/2');
  assert.strictEqual(p.el('c-foot').querySelector('.pc').textContent, '(100%)');
  assert.deepStrictEqual(p.errors, []);
});

const ENEMIES = { enemies: [{ name: 'Bone Guard A', family: 'Undead' }, { name: 'Nameless Thing', family: '' }, { name: 'Imp', family: 'Demonic' }] };
function fight(p) {
  player(p);
  frame(p, 'Room.Info', { num: 'room_4', name: 'Hall of the Ancestors', area: 'Dungeon Floor 6' });
  frame(p, 'Room.Detail', Object.assign({}, ROOM_DETAIL, { themeId: 'Catacombs' }));
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Bone Guard A', level: 8, hp: 260, maxHp: 260 }, { name: 'Nameless Thing', level: 8, hp: 200, maxHp: 200 }, { name: 'Imp', level: 3, hp: 20, maxHp: 20 }] });
}
const foes = (p) => { const row = p.el('c-frame').querySelector('.foes-in'); return row ? row.children : []; };
const foesOf = (p) => foes(p).map((e) => e.tagName === 'IMG' ? 'img ' + e.getAttribute('src') + ' ' + e.getAttribute('alt') : 'name ' + e.textContent);


test('a foe stands in the fight plate as its family sprite, or as its name when its family has no art', async () => {
  const p = await chronicle();
  fight(p);
  assert.deepStrictEqual(foesOf(p), [], 'no Client.Enemies yet: the scene alone, as before');
  frame(p, 'Client.Enemies', ENEMIES);
  assert.deepStrictEqual(foesOf(p), ['img /play/art/monsters/skeleton.png Bone Guard A', 'name Nameless Thing', 'img /play/art/monsters/demon.png Imp']);
  assert.ok(p.el('c-frame').querySelector('.foes-in').classList.contains('n3'));
  // a fallen foe leaves the plate; the cards keep it
  frame(p, 'Char.Combat.Killed', { name: 'Imp' });
  assert.deepStrictEqual(foesOf(p), ['img /play/art/monsters/skeleton.png Bone Guard A', 'name Nameless Thing']);
  assert.ok(p.el('c-hud').textContent.includes('Imp'));
  // a list that does not match the fight's own (order or names) names nothing
  frame(p, 'Client.Enemies', { enemies: [{ name: 'Nameless Thing', family: 'Undead' }, { name: 'Bone Guard A', family: 'Undead' }] });
  assert.deepStrictEqual(foesOf(p), []);
  // a new fight forgets the last one's families until its own Client.Enemies comes
  frame(p, 'Client.Enemies', ENEMIES);
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Bone Guard A', level: 8, hp: 260, maxHp: 260 }] });
  assert.deepStrictEqual(foesOf(p), []);
  frame(p, 'Client.Enemies', { enemies: [{ name: 'Bone Guard A', family: 'Undead' }] });
  assert.ok(p.el('c-frame').querySelector('.foes-in').classList.contains('n1'));
  assert.deepStrictEqual(p.errors, []);
});

test('only a family the page knows names art: odd and inherited names draw the foe by name', async () => {
  const p = await chronicle();
  fight(p);
  for (const fam of ['__proto__', 'constructor', 'toString', 'undead', '../x', '<img src=x>', 5, null]) {
    frame(p, 'Client.Enemies', { enemies: [{ name: 'Bone Guard A', family: fam }] });
    assert.ok(!foesOf(p)[0] || foesOf(p)[0].startsWith('name '), String(fam));
  }
  for (const bad of [null, 5, 'x', { enemies: 'x' }, { enemies: [null, 5, { name: { toString: null } }] }]) {
    p.ctx.__d = bad;
    p.run("UsurperPlay.handleGmcp('Client.Enemies', __d)");
  }
  assert.deepStrictEqual(p.errors, []);
});

test('the floor scene comes from themeId, the same in every language; the translated theme is never read for art', async () => {
  const p = await chronicle();
  const bg = () => p.el('c-frame').querySelector('.scene').style.backgroundImage;
  frame(p, 'Room.Info', { num: 'room_3', name: 'Cripta', area: 'Dungeon Floor 12' });
  frame(p, 'Room.Detail', Object.assign({}, ROOM_DETAIL, { theme: 'Profundidades Heladas', themeId: 'FrozenDepths' }));
  assert.strictEqual(bg(), 'url("/play/art/scenes/frozen-depths.png")');
  assert.strictEqual(p.el('c-cap').textContent, 'Plate: Profundidades Heladas, Level 6', 'the caption keeps the game\'s own words');
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Wight', level: 12, hp: 1, maxHp: 1 }] });
  assert.strictEqual(bg(), 'url("/play/art/scenes/frozen-depths.png")', 'the fight keeps the floor scene');
  frame(p, 'Char.Combat.End', { outcome: 'Victory' });
  for (const odd of ['Frozen Depths', 'frozendepths', '__proto__', 'constructor', 5, null]) {
    frame(p, 'Room.Detail', Object.assign({}, ROOM_DETAIL, { theme: 'Frozen Depths', themeId: odd }));
    assert.strictEqual(bg(), 'url("/play/art/scenes/dungeon.png")', String(odd));
  }
  // every scene and sprite the page can name is a file in web/play/art
  const code = readPage(PAGE);
  const themes = /var THEMES = \{([^}]*)\}/.exec(code)[1].match(/'([a-z-]+)'/g).map((x) => x.slice(1, -1));
  assert.strictEqual(themes.length, 8);
  for (const s of themes) assert.ok(fs.existsSync(path.join(WEB, 'play', 'art', 'scenes', s + '.png')), s);
  const sprites = /var SPRITES = \{([^}]*)\}/.exec(code)[1].match(/'([a-z-]+)'/g).map((x) => x.slice(1, -1));
  assert.strictEqual(sprites.length, 15);
  for (const s of sprites) assert.ok(fs.existsSync(path.join(WEB, 'play', 'art', 'monsters', s + '.png')), s);
  const css = fs.readFileSync(path.join(WEB, 'play', 'chronicle.css'), 'utf8');
  for (const m of css.matchAll(/\.foe[^{]*\{[^}]*?width: (\d+)px; height: (\d+)px/g)) assert.ok(m[1] === m[2] && ['64', '128', '256'].includes(m[1]), 'sprites at whole scales of 128: ' + m[0]);
  assert.match(css, /\.chron \.foe \{[^}]*image-rendering: pixelated/);
});

const MAP = {
  level: 6, theme: 'Catacombs', themeId: 'Catacombs', explored: 4, total: 6, cleared: 2, room: 'room_4', location: 'Hall of the Ancestors', bossDefeated: false,
  rooms: [{ x: 0, y: 0, kind: 'here' }, { x: 0, y: -1, kind: 'cleared' }, { x: -1, y: 0, kind: 'safe' }, { x: -1, y: 1, kind: 'monsters' }],
  links: [{ x1: 0, y1: 0, x2: -1, y2: 0 }, { x1: -1, y1: 0, x2: -1, y2: 1 }],
};
function inRoomWithMap(p, map) {
  frame(p, 'Room.Info', { num: 'room_4', name: 'Hall of the Ancestors', area: 'Dungeon Floor 6' });
  frame(p, 'Client.Map', map || MAP);
  frame(p, 'Room.Detail', ROOM_DETAIL);
  frame(p, 'Client.Menu', ROOM_MENU);
}

test('the room shows the map beside the exits (D3) while it is the map drawn from this room', async () => {
  const p = await chronicle();
  inRoomWithMap(p);
  assert.strictEqual(layout(p), 'room');
  const nav = p.el('c-body').querySelector('.nav');
  assert.ok(nav, 'the map and exits panel');
  assert.deepStrictEqual(nav.children.map((c) => c.getAttribute('class')), ['card mapcard', 'card compcard'], 'map first, the exits beside it');
  const m = nav.querySelector('.cmap');
  assert.strictEqual(m.getAttribute('role'), 'img');
  assert.strictEqual(m.getAttribute('aria-label'), 'Map of the floor, 4 of 6 rooms explored');
  assert.deepStrictEqual(m.querySelectorAll('.mroom').map((r) => r.getAttribute('class')), ['mroom k-here', 'mroom k-cleared', 'mroom k-safe', 'mroom k-monsters']);
  assert.deepStrictEqual(m.querySelectorAll('.mlink').map((r) => r.getAttribute('class')), ['mlink h', 'mlink v']);
  assert.strictEqual(nav.querySelector('.mapcard .cap').textContent, 'Map: 4/6 explored');
  assert.strictEqual(m.textContent, '', 'the map is a picture with its words in the label');
  // the rooms sit on a grid: the current room east of the safe one, the cleared one above it
  const at = (r) => [parseFloat(r.style.left) + parseFloat(r.style.width) / 2, parseFloat(r.style.top) + parseFloat(r.style.height) / 2];
  const [here, up, west, down] = m.querySelectorAll('.mroom').map(at);
  assert.ok(here[0] === up[0] && up[1] < here[1] && west[1] === here[1] && west[0] < here[0] && down[0] === west[0] && down[1] > west[1]);
  // a move to another room: the map would be out of date, so it stays out until the map screen is opened again
  frame(p, 'Room.Info', { num: 'room_5', name: 'Crypt', area: 'Dungeon Floor 6' });
  frame(p, 'Room.Detail', ROOM_DETAIL);
  assert.strictEqual(p.el('c-body').querySelector('.mapcard'), null);
  assert.ok(p.el('c-body').querySelector('.compcard'), 'the exits stay');
  // another floor's map is not this floor's
  const q = await chronicle();
  inRoomWithMap(q, Object.assign({}, MAP, { level: 7 }));
  assert.strictEqual(q.el('c-body').querySelector('.mapcard'), null);
  assert.deepStrictEqual(p.errors, []);
});

test('odd Client.Map rooms and corridors are dropped: unknown kinds, broken places, corridors to rooms not listed', async () => {
  const p = await chronicle();
  inRoomWithMap(p, Object.assign({}, MAP, {
    rooms: MAP.rooms.concat([{ x: 1, y: 0, kind: 'unknown' }, { x: 2.5, y: 0, kind: 'safe' }, { x: '3', y: 0, kind: 'safe' }, { x: 0, y: 0, kind: 'safe' },
      { x: 9, y: 9, kind: '__proto__' }, { x: 1e9, y: 0, kind: 'safe' }, null, 5]),
    links: MAP.links.concat([{ x1: 0, y1: 0, x2: 1, y2: 0 }, { x1: 0, y1: 0, x2: 0, y2: 1 }, { x1: 0, y1: 0, x2: -1, y2: 1 }, { x1: 'a' }, null]),
  }));
  const m = p.el('c-body').querySelector('.cmap');
  assert.strictEqual(m.querySelectorAll('.mroom').length, 4, 'only the four good rooms');
  assert.strictEqual(m.querySelectorAll('.mlink').length, 2, 'only corridors between two listed rooms side by side');
  for (const bad of [null, 5, 'x', { rooms: 'x', links: 5 }, { rooms: [{ x: { toString: null } }] }]) {
    p.ctx.__d = bad;
    p.run("UsurperPlay.handleGmcp('Client.Map', __d)");
  }
  assert.deepStrictEqual(p.errors, []);
});

const SHEET = {
  title: 'CHARACTER STATUS', name: 'Webtest', classId: 'Warrior',
  basic: { heading: 'BASIC INFORMATION', rows: [{ label: 'Name', value: 'Webtest' }, { label: 'Class', value: 'Warrior' }, { label: 'Race', value: 'Human' }, { label: 'Sex', value: 'Female' },
    { label: 'Age', value: '22' }, { label: 'Height', value: '180cm' }, { label: 'Weight', value: '80kg' }], note: 'Royal Authority: +10% ATK, +10% DEF, +5% HP' },
  level: { heading: 'LEVEL & EXPERIENCE', rows: [{ label: 'Current Level', value: '12' }, { label: 'Experience', value: '15,880' }, { label: 'XP to Next Level', value: '25,020', extra: '(Need 40900 total)' }], xp: 15880, xpNext: 40900 },
  combat: { heading: 'COMBAT STATISTICS', hp: { label: 'HP', value: '400/400', now: 400, max: 400 }, mana: { label: 'Mana', value: '20/50', now: 20, max: 50 },
    stats: [['Strength', '60'], ['Defense', '20'], ['Agility', '20'], ['Dexterity', '30'], ['Stamina', '100'], ['Wisdom', '20'], ['Intelligence', '20'], ['Charisma', '15'], ['Constitution', '30']].map(([label, value]) => ({ label, value })), more: [] },
  equipment: { heading: 'EQUIPMENT', style: { label: 'Combat Style', value: 'One-Handed' }, slots: [
    { slot: 'MainHand', label: 'Main Hand', item: 'Webtest Blade', stats: '(WP:19 Str:+2)', empty: false }, { slot: 'OffHand', label: 'Off Hand', item: 'Empty', empty: true },
    { slot: 'Head', label: 'Head', item: 'Webtest Helm', stats: '(AC:11 Con:+1)', empty: false }, { slot: 'Body', label: 'Body', item: 'Empty', empty: true },
    { slot: 'Arms', label: 'Arms', item: 'Empty', empty: true }, { slot: 'Hands', label: 'Hands', item: 'Empty', empty: true }, { slot: 'Legs', label: 'Legs', item: 'Empty', empty: true },
    { slot: 'Feet', label: 'Feet', item: 'Empty', empty: true }, { slot: 'Waist', label: 'Waist', item: 'Empty', empty: true }, { slot: 'Face', label: 'Face', item: 'Empty', empty: true },
    { slot: 'Cloak', label: 'Cloak', item: 'Empty', empty: true }, { slot: 'Neck', label: 'Neck', item: 'Empty', empty: true },
    { slot: 'LFinger', label: 'Left Ring', item: 'Empty', empty: true }, { slot: 'RFinger', label: 'Right Ring', item: 'Empty', empty: true }] },
  totals: { heading: 'Equipment Totals', rows: [{ label: 'Weapon Power', value: '19' }, { label: 'Armor Power', value: '11' }], bonusLabel: 'Bonuses', bonuses: [{ label: 'Str', value: '+2' }, { label: 'Con', value: '+1' }] },
};
const SHEET_TEXT = ['CHARACTER STATUS', 'BASIC INFORMATION', 'Name: Webtest', 'Class: Warrior  |  Race: Human  |  Sex: Female', 'Press Enter to continue...'];

test('Client.Sheet draws Character Status as G2: the slots around the portrait, the sheet on the right, the game\'s Enter key', async () => {
  const sent = [];
  const p = await chronicle({ WebSocket: socket(sent) });
  player(p);
  screen(p, SHEET_TEXT);
  assert.strictEqual(layout(p), 'text', 'the text page until the sheet arrives');
  frame(p, 'Client.Sheet', SHEET);
  assert.strictEqual(layout(p), 'sheet');
  // the doll: top row, left column, the portrait, right column, bottom row, in reading order
  const row = p.el('c-frame').parentElement;
  assert.ok(row.classList.contains('doll'));
  assert.deepStrictEqual(row.children.map((c) => c.id || c.getAttribute('class')), ['drow dt', 'dcol dl', 'c-frame', 'dcol dr', 'drow db']);
  const slotText = (cls) => row.querySelector('.' + cls).querySelectorAll('.slot').map((x) => x.querySelector('.sl').textContent + ':' + x.querySelector('.it').textContent);
  assert.deepStrictEqual(slotText('dt'), ['Head:Webtest Helm', 'Face:Empty', 'Neck:Empty']);
  assert.deepStrictEqual(slotText('dl'), ['Main Hand:Webtest Blade', 'Arms:Empty', 'Hands:Empty', 'Left Ring:Empty']);
  assert.deepStrictEqual(slotText('dr'), ['Off Hand:Empty', 'Body:Empty', 'Cloak:Empty', 'Right Ring:Empty']);
  assert.deepStrictEqual(slotText('db'), ['Waist:Empty', 'Legs:Empty', 'Feet:Empty']);
  assert.strictEqual(row.querySelector('.dl .slot .st').textContent, '(WP:19 Str:+2)');
  assert.ok(row.querySelectorAll('.slot.empty').every((x) => x.querySelector('.it').textContent === 'Empty'), 'an empty slot is dashed and says so');
  assert.strictEqual(p.el('c-frame').querySelector('img').getAttribute('src'), '/play/art/classes/warrior.png');
  assert.strictEqual(p.el('c-cap').textContent, 'Plate: Webtest, Warrior');
  assert.strictEqual(p.el('c-hud').querySelector('.cstyle').textContent, 'Combat StyleOne-Handed');
  // the right page: the game's own headings, labels and values
  const body = p.el('c-body');
  assert.strictEqual(body.querySelector('h1').textContent, 'CHARACTER STATUS');
  assert.deepStrictEqual(body.querySelectorAll('h2').map((h) => h.textContent), ['BASIC INFORMATION', 'LEVEL & EXPERIENCE', 'COMBAT STATISTICS', 'Equipment Totals']);
  const pairs = body.querySelectorAll('dl').flatMap((dl) => dl.children).map((d) => d.querySelector('dt').textContent + '=' + d.querySelector('dd').textContent);
  for (const want of ['Name=Webtest', 'Race=Human', 'Height=180cm', 'Experience=15,880', 'XP to Next Level=25,020 (Need 40900 total)', 'Strength=60', 'Constitution=30', 'Weapon Power=19'])
    assert.ok(pairs.includes(want), want);
  assert.strictEqual(body.querySelector('dl.three').children.length, 9);
  assert.strictEqual(body.querySelector('.snote').textContent, SHEET.basic.note);
  assert.deepStrictEqual(body.querySelectorAll('.bonus .chipc').map((c) => c.textContent), ['Str +2', 'Con +1']);
  assert.deepStrictEqual(body.querySelectorAll('.hp .n').map((c) => c.textContent), ['400/400', '20/50']);
  assert.strictEqual(body.querySelector('.xprule .n').textContent, '15,880/40,900');
  // the game's Press Enter handling, unchanged: the Enter key from the screen sends Enter alone
  assert.strictEqual(p.el('c-enterbar').hidden, false);
  p.click(p.el('c-enterbar').querySelector('.kc'));
  assert.deepStrictEqual(sent, ['\r']);
  assert.deepStrictEqual(p.errors, []);
});

test('the sheet lasts for its own draw: the next page is the text page, and the Text page button shows the text at any time', async () => {
  const p = await chronicle();
  frame(p, 'Client.Sheet', SHEET);
  screen(p, SHEET_TEXT);
  assert.strictEqual(layout(p), 'sheet');
  p.run('UsurperPlay.drawStarted(300)');   // Enter for the next page
  assert.strictEqual(layout(p), 'sheet', 'pending, not flashed');
  p.run('UsurperPlay.drawEnded()');
  assert.strictEqual(layout(p), 'text', 'no sheet in this draw');
  assert.strictEqual(p.el('c-frame').parentElement.className, 'plate-row', 'the doll goes with it');
  assert.strictEqual(p.el('c-frame').parentElement.children.length, 1);
  p.run('UsurperPlay.drawStarted(300)');
  frame(p, 'Client.Sheet', SHEET);
  p.click(p.el('text-toggle'));
  assert.strictEqual(layout(p), 'text');
  assert.ok(p.el('c-screen').textContent.includes('Name: Webtest'), 'the game\'s own lines are always one press away');
  p.click(p.el('text-toggle'));
  assert.strictEqual(layout(p), 'sheet');
  // a street menu takes over; a fight is never replaced by a sheet
  frame(p, 'Client.Menu', STREET_MENU);
  assert.strictEqual(layout(p), 'street');
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Zombie', level: 1, hp: 5, maxHp: 9 }] });
  frame(p, 'Client.Sheet', SHEET);
  assert.strictEqual(layout(p), 'fight');
});

test('odd Client.Sheet fields are dropped at the door, and every value is drawn as text', async () => {
  const p = await chronicle();
  const H = '<img src=x onerror=alert(2)><script>a()</script>&amp;';
  const hostile = JSON.parse(JSON.stringify(SHEET).split('Webtest').join(H).split('Empty').join(H));
  hostile.classId = H;
  frame(p, 'Client.Sheet', hostile);
  for (const id of ['c-body', 'c-hud', 'c-cap', 'c-rh']) {
    const tags = descendants(p.el(id)).map((e) => e.tagName);
    assert.ok(tags.every((t) => ['DIV', 'SPAN', 'B', 'SMALL', 'I', 'H1', 'H2', 'SECTION', 'DL', 'DT', 'DD', 'PRE', 'BUTTON'].includes(t)), id + ': ' + tags.join(','));
  }
  const doll = descendants(p.el('c-frame').parentElement).map((e) => e.tagName);
  assert.ok(doll.every((t) => ['DIV', 'SPAN'].includes(t)), 'no portrait for an unknown class, and no markup from the frame: ' + doll.join(','));
  assert.ok(p.el('c-body').textContent.includes(H));
  for (const bad of [null, 5, 'x', [], { basic: 5, level: 'x', combat: [], equipment: { slots: [null, 5, { slot: 'Toes' }, { slot: '__proto__' }, { slot: { toString: null } }] }, totals: { bonuses: 'x' } },
    { basic: { rows: [null, 5, { label: { toString: null }, value: NaN }] }, combat: { hp: 5, mana: { now: 'x', max: Infinity } }, level: { xp: -5, xpNext: 'x' } }]) {
    p.ctx.__d = bad;
    p.run("UsurperPlay.handleGmcp('Client.Sheet', __d)");
  }
  assert.deepStrictEqual(p.errors, []);
  assert.strictEqual(layout(p), 'sheet');
});

// ---------------------------------------------------------------- accessibility

test('the screen reader path: the terminal is never hidden, the text page is real text, and the Text page button forces it', async () => {
  const p = await chronicle();
  for (const id of ['terminal', 'term-scroll']) {
    assert.strictEqual(p.el(id).hidden, false, id);
    assert.notStrictEqual(p.el(id).getAttribute('aria-hidden'), 'true', id);
  }
  const css = fs.readFileSync(path.join(WEB, 'play', 'chronicle.css'), 'utf8');
  const termRule = /body\.chron \.term-scroll \{([^}]*)\}/.exec(css)[1];
  assert.doesNotMatch(termRule, /display:\s*none|visibility:\s*hidden/, 'out of sight, not out of the page');
  assert.doesNotMatch(css, /#terminal[^{]*\{[^}]*display:\s*none/);
  player(p);
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Zombie', level: 1, hp: 5, maxHp: 9 }] });
  screen(p, ['Zombie attacks you!']);
  assert.strictEqual(layout(p), 'fight');
  const tt = p.el('text-toggle');
  assert.strictEqual(tt.getAttribute('aria-pressed'), 'false');
  p.click(tt);
  assert.strictEqual(tt.getAttribute('aria-pressed'), 'true');
  assert.strictEqual(layout(p), 'text', 'the text page on every screen');
  assert.strictEqual(p.el('c-screen').textContent, 'Zombie attacks you!');
  assert.strictEqual(p.el('c-screen').getAttribute('aria-hidden'), null);
  assert.strictEqual(p.el('c-screen').getAttribute('tabindex'), '0');
  assert.ok(p.el('c-screen').getAttribute('aria-label').length > 0);
  p.click(tt);
  assert.strictEqual(layout(p), 'fight');
  // every key is a real button with its key and label as text
  frame(p, 'Client.Menu', { screen: 'combat', groups: [{ label: 'Actions', items: [{ key: 'A', label: 'Attack', enabled: true }, { key: 'G', label: 'Rage', enabled: false }] }] });
  const keys = p.el('c-keys').querySelectorAll('.kc');
  assert.strictEqual(keys.length, 2);
  assert.ok(keys.every((b) => b.tagName === 'BUTTON' && b.type === 'button'));
  assert.deepStrictEqual(keys.map((b) => b.getAttribute('aria-label')), ['A, Attack', 'G, Rage']);
  assert.strictEqual(keys[1].getAttribute('aria-disabled'), 'true');
  assert.strictEqual(p.el('c-keys').querySelector('.kgroup').getAttribute('role'), 'group');
  assert.strictEqual(p.el('c-keys').querySelector('.kgroup').getAttribute('aria-label'), 'Actions');
});

test('the high contrast variant: its own choice, or the system asking for more contrast; never in Classic', async () => {
  const hc = await load({ storage: { playView: 'contrast' } });
  assert.ok(hc.doc.documentElement.classList.contains('hc'));
  const more = (q) => ({ matches: /prefers-contrast:\s*more/.test(q) });
  const sys = await load({ storage: { playView: 'chronicle' }, matchMedia: more });
  assert.ok(sys.doc.documentElement.classList.contains('hc'));
  const classic = await load({ matchMedia: more });
  assert.ok(!classic.doc.documentElement.classList.contains('hc'), 'Classic is untouched');
  const plain = await load({ storage: { playView: 'chronicle' } });
  assert.ok(!plain.doc.documentElement.classList.contains('hc'));
  change(hc, 'classic');
  assert.ok(!hc.doc.documentElement.classList.contains('hc'));
  change(sys, 'classic');
  assert.ok(!sys.doc.documentElement.classList.contains('hc'), 'back to Classic drops it even when the system asks for contrast');
  const css = fs.readFileSync(path.join(WEB, 'play', 'chronicle.css'), 'utf8');
  assert.match(css, /html\.hc \.chron \.frame, html\.hc \.chron \.scene, html\.hc \.chron \.portrait, html\.hc \.chron img \{ display: none !important; \}/);
  assert.match(css, /html\.hc \.chron \.rule \{ display: none; \}/);
  assert.match(css, /html\.hc \.chron \.kc\.off \{ border-style: dashed; \}/);
  assert.match(css, /html\.hc \.chron \.cdir \{[^}]*text-decoration: line-through/);
  assert.match(css, /html\.hc \.chron button:focus-visible[^{]*\{ outline: 4px solid #ffe14d/);
  assert.match(css, /@media \(prefers-reduced-motion: reduce\) \{\s*\.chron, \.chron \* \{ transition: none !important; animation: none !important;/);
  assert.match(css, /\.chron button:focus-visible, \.chron \[tabindex\]:focus-visible \{ outline: 2px solid var\(--st-ink\); outline-offset: 2px; \}/);
  assert.match(css, /^\.chron \.kc\.off \{ border-style: dashed;/m, 'disabled is dashed, not only dim');
  assert.match(css, /\.chron \.card\.out \.nm \{ text-decoration: line-through/, 'a dead foe is struck through as well as marked');
});

test('keys typed with the book focused go to the game as the terminal sends them; a button keeps Enter and Space', async () => {
  const sent = [];
  const p = await chronicle({ WebSocket: socket(sent) });
  screen(p, [' [D] Dungeons']);
  const cap = p.el('c-keys').querySelector('.kc');
  const key = (target, k, extra) => p.doc.dispatch(target, 'keydown', Object.assign({ key: k }, extra || {}));
  key(p.el('c-screen'), 'd');
  key(p.el('c-screen'), 'Enter');
  key(p.el('c-screen'), 'Backspace');
  key(cap, 'x');
  key(cap, 'Enter');
  key(cap, ' ');
  key(cap, 'Tab');
  key(cap, 'ArrowUp');
  key(p.el('c-screen'), 'c', { ctrlKey: true });
  key(p.el('c-screen'), 'v', { metaKey: true });
  assert.deepStrictEqual(sent, ['d', '\r', '\x7f', 'x']);
  assert.deepStrictEqual(p.errors, []);
});

test('a redraw keeps the keyboard where it was: the text page, or the key cap with the same key', async () => {
  const sent = [];
  const p = await chronicle({ WebSocket: socket(sent) });
  screen(p, [' [D] Dungeons   [I] Inn', 'Your choice:']);
  p.el('c-screen').focus();
  screen(p, [' [D] Dungeons   [I] Inn', 'Your choice: d']);
  assert.strictEqual(p.doc.activeElement, p.el('c-screen'), 'the new text page has the focus');
  p.doc.dispatch(p.doc.activeElement, 'keydown', { key: 'x' });
  assert.deepStrictEqual(sent, ['x'], 'and typed keys still reach the game');
  p.el('c-keys').querySelectorAll('.kc')[1].focus();
  screen(p, [' [D] Dungeons   [I] Inn', 'Your choice: x']);
  assert.strictEqual(p.doc.activeElement.getAttribute('data-key'), 'I');
  assert.strictEqual(p.doc.activeElement, p.el('c-keys').querySelectorAll('.kc')[1]);
  // a reader scrolled back in the text page stays there; one at the end follows new lines
  const pre = p.el('c-screen');
  Object.assign(pre, { scrollTop: 40, clientHeight: 100, scrollHeight: 500 });
  screen(p, ['a', 'b']);
  assert.strictEqual(p.el('c-screen').scrollTop, 40);
  assert.deepStrictEqual(p.errors, []);
});

test('the book never focuses the terminal on a touch screen, and keeps the pad: its fixed row on every screen', async () => {
  const p = await chronicle({ matchMedia: TOUCH });
  assert.strictEqual(p.el('pad').hidden, false);
  assert.ok(p.doc.body.classList.contains('touch') && p.doc.body.classList.contains('chron'));
  frame(p, 'Client.Menu', STREET_MENU);
  assert.ok(p.doc.body.classList.contains('chron-lay'), 'a layout: the dock has the keys');
  p.click(p.el('pad-keys'));
  assert.ok(p.el('pad').classList.contains('keys-open'));
  const css = fs.readFileSync(path.join(WEB, 'play', 'chronicle.css'), 'utf8');
  assert.match(css, /body\.touch\.chron\.chron-lay \.pad:not\(\.keys-open\) \.pad-scroll \{ display: none; \}/);
  assert.doesNotMatch(css, /\.pad-fixed[^{]*\{[^}]*display:\s*none/);
  const lines = readPage(PAGE).split('\n').filter((l) => l.includes('terminal.focus()'));
  for (const l of lines) assert.ok(l.includes('!isTouch()') || l.includes('the one deliberate way'), l.trim());
});

// ---------------------------------------------------------------- origins

test('no new third party origins: the page loads only Google Fonts and the pinned xterm from jsDelivr', () => {
  const html = readPage(PAGE);
  const els = elements(parseHtml(html));
  const urls = [];
  for (const e of els) {
    if (e.tag === 'script' && e.attrs.src !== undefined) urls.push(e.attrs.src);
    if (e.tag === 'link' && e.attrs.href !== undefined) urls.push(e.attrs.href);
    if (['img', 'iframe', 'source', 'video', 'audio'].includes(e.tag) && e.attrs.src !== undefined) urls.push(e.attrs.src);
  }
  const external = urls.filter((u) => /^(https?:)?\/\//.test(u));
  const hosts = [...new Set(external.map((u) => new URL(u, 'https://usurper.test/').host))].sort();
  assert.deepStrictEqual(hosts, ['cdn.jsdelivr.net', 'fonts.googleapis.com']);
  for (const u of external.filter((x) => new URL(x).host === 'cdn.jsdelivr.net')) assert.match(u, /^https:\/\/cdn\.jsdelivr\.net\/npm\/@xterm\/xterm@5\.5\.0\//, u);
  const fonts = external.filter((x) => new URL(x).host === 'fonts.googleapis.com' && !/^https:\/\/fonts\.googleapis\.com\/?$/.test(x));
  assert.strictEqual(fonts.length, 1);
  assert.match(fonts[0], /^https:\/\/fonts\.googleapis\.com\/css2\?family=IM\+Fell\+English\+SC&family=EB\+Garamond:ital,wght@0,400;0,500;1,400&family=IBM\+Plex\+Mono:wght@400;600&family=JetBrains\+Mono:wght@400;600;700&display=swap$/);
  const local = urls.filter((u) => !/^(https?:)?\/\//.test(u) && !/^data:/.test(u));
  assert.deepStrictEqual(local, ['/play/chronicle.css']);
  // The stylesheet and the script reach nothing outside the site: no imports, no remote url(), no new socket or fetch.
  const css = fs.readFileSync(path.join(WEB, 'play', 'chronicle.css'), 'utf8');
  assert.doesNotMatch(css, /@import/);
  const outside = css.replace(/url\("data:image\/svg\+xml,[^"]*"\)/g, '');
  assert.doesNotMatch(outside, /url\(/, 'only the inline grain image');
  assert.match(html, /style\.backgroundImage = 'url\("\/play\/art\/scenes\/' \+ scene \+ '\.png"\)'/);
  assert.match(html, /img\.setAttribute\('src', '\/play\/art\/classes\/' \+ portrait \+ '\.png'\)/);
  assert.match(html, /img\.setAttribute\('src', '\/play\/art\/monsters\/' \+ x\.art \+ '\.png'\)/);
  assert.strictEqual(html.match(/new WebSocket\(/g).length, 1);
  assert.deepStrictEqual(html.match(/fetch\('[^']*'/g), ["fetch('/lang/'", "fetch('/api/languages'"]);
});
