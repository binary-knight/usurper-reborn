'use strict';
// web/play/index.html on a touch screen: the text size switch and the tap pad. The pad reads its
// buttons from the game's screen text, so every label is drawn as text and every tap sends the key
// the screen showed, followed by Enter, because the game reads whole lines.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { HOSTILE, loadPage, readPage } = require('./harness');

const PAGE = 'play/index.html';
const LANGS = ['en', 'es', 'fr', 'hu', 'it'];
const lang = (l) => JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'lang', l + '.json'), 'utf8'));

// A phone or tablet: no hover and a coarse pointer. Every other query (phone width, wide desktop) is false.
const TOUCH = (q) => ({ matches: /hover:\s*none/.test(q) && /pointer:\s*coarse/.test(q) });
const MOUSE = () => ({ matches: false });
function socket(sent, readyState = 1) {
  return class { constructor() { this.readyState = readyState; } send(d) { sent.push(d); } close() {} };
}
async function load(opts = {}) {
  const p = await loadPage(PAGE, Object.assign({ fetch: (url) => (url.startsWith('/lang/') ? lang('en') : url === '/api/languages' ? LANGS : {}) }, opts));
  await p.settle();
  return p;
}
function pad(p, rows) {
  p.ctx.__rows = rows;
  p.run('UsurperPlay.renderPad(__rows)');
}
function scan(p, rows) {
  p.ctx.__rows = rows;
  return JSON.parse(JSON.stringify(p.run('UsurperPlay.scanCommands(__rows)')));
}
function chips(p) {
  return p.el('pad-scroll').querySelectorAll('.chip').map((b) => b.getAttribute('aria-label'));
}
function descendants(el, out = []) {
  for (const c of el.children || []) { out.push(c); descendants(c, out); }
  return out;
}

// Main Street as the game draws it (recorded from a local server, colours removed).
const STREET = [
  ' Players online: 1  |  /say to chat  |  /who for player list',
  'Prefer the old layout? Set Main Street layout to Classic in Settings [~].',
  ' [D] Dungeons          [I] Inn',
  ' [M] Merchant Row ...  [G] Guild Row ...     [S] Status ...',
  ' [O] Online (1) ...',
  ' [~] Settings          [?] Help              [Q] Quit Game',
  'HP: 40/62 | Gold: 2,500 | Stamina: 100/105 | Level 1 | Lives: 3/3 (5%)',
  '  >>> New adventurer? Press [D] to enter the Dungeons! <<<',
  '  > Next: Enter the Dungeons [D] and win your first fight.  (/journal for more)',
  '[40hp 100st] Main Street >',
];
const ROOM = [
  'Exits:',
  '  [S] A dark passage leads south. (explored)',
  '  [N] A dark passage leads north. (unknown)',
  '  [W] A dark passage leads west. (unknown)',
  'Actions:',
  '  [F] Fight the monsters',
  '  [M] Map  [G] Guide  [*] Inventory  [P] Potions  [%] Status  [Q] Leave dungeon',
  'HP: [████████████░░░░░░░░] 40/62  Potions: 20/20  Gold: 2,450  Lv 1',
  '[40hp 100st] Dungeon Fl.1 >',
];

test('a mouse, or no matchMedia at all, keeps the desktop page: no pad, no touch layout', async () => {
  for (const opts of [{}, { matchMedia: MOUSE }]) {
    const p = await load(opts);
    assert.strictEqual(p.el('pad').hidden, true);
    assert.ok(!p.doc.body.classList.contains('touch'));
    assert.deepStrictEqual(p.errors, []);
  }
});

test('a touch screen shows the pad and the touch layout', async () => {
  const p = await load({ matchMedia: TOUCH });
  assert.strictEqual(p.el('pad').hidden, false);
  assert.ok(p.doc.body.classList.contains('touch'));
  assert.strictEqual(p.run('UsurperPlay.fitMode()'), 'whole', 'whole screen is the default');
  assert.deepStrictEqual(p.errors, []);
});

test('menu options on screen become buttons, in screen order, one per key', async () => {
  const p = await load({ matchMedia: TOUCH });
  pad(p, STREET);
  assert.deepStrictEqual(chips(p), ['D, Dungeons', 'I, Inn', 'M, Merchant Row ...', 'G, Guild Row ...', 'S, Status ...', 'O, Online (1) ...', '~, Settings', '?, Help', 'Q, Quit Game']);
  // "in Settings [~]." names the key inside a sentence and "Press [D] to enter" repeats it; neither claims it.
  assert.ok(!chips(p).some((c) => /^~, \.$/.test(c)));
  assert.strictEqual(chips(p).filter((c) => c.startsWith('D,')).length, 1);
  assert.deepStrictEqual(p.errors, []);
});

test('glued, long and spaced keys keep their labels; a capital after the key is not glued', async () => {
  const p = await load({ matchMedia: TOUCH });
  assert.deepStrictEqual(scan(p, [' [E]nter the dungeon      [J]ournal - Quest progress', ' [T]Party management      [S]tatus']), [
    { key: 'E', label: 'Enter the dungeon' }, { key: 'J', label: 'Journal - Quest progress' },
    { key: 'T', label: 'Party management' }, { key: 'S', label: 'Status' },
  ]);
  assert.deepStrictEqual(scan(p, ['║ [AUTO] Auto-Combat                    ║', '║ [SPD]  Combat Speed: Normal           ║']), [
    { key: 'AUTO', label: 'Auto-Combat' }, { key: 'SPD', label: 'Combat Speed: Normal' },
  ]);
  assert.deepStrictEqual(scan(p, ['[1] Male  [2] Female:']), [{ key: '1', label: 'Male' }, { key: '2', label: 'Female' }]);
});

test('status bars and the game prompt are not options', async () => {
  const p = await load({ matchMedia: TOUCH });
  assert.deepStrictEqual(scan(p, ['HP: [████████████░░░░░░░░] 40/62', '[40hp 100st] Main Street >', '[40hp] Main Street >', '[12st] Inn >', 'Lives: [3/3]']), []);
});

test('a button sends its key and Enter; Keys sends one character; Enter and Back send their bytes', async () => {
  const sent = [];
  const p = await load({ matchMedia: TOUCH, WebSocket: socket(sent) });
  pad(p, STREET);
  p.click(p.el('pad-scroll').querySelectorAll('.chip')[0]);
  assert.deepStrictEqual(sent, ['D\r']);
  p.click(p.el('pad-enter'));
  p.click(p.el('pad-back'));
  assert.deepStrictEqual(sent.slice(1), ['\r', '\x7f']);
  p.click(p.el('pad-keys'));
  assert.strictEqual(p.el('pad-keys').getAttribute('aria-pressed'), 'true');
  const keys = p.el('pad-scroll').querySelectorAll('.key');
  assert.strictEqual(keys.length, 50);
  p.click(keys.find((k) => k.textContent === 'Q'));
  p.click(keys.find((k) => k.textContent === 'Space'));
  assert.deepStrictEqual(sent.slice(3), ['Q', ' '], 'one character each, no Enter');
  p.click(p.el('pad-keys'));
  assert.strictEqual(p.el('pad-keys').getAttribute('aria-pressed'), 'false');
  assert.strictEqual(p.el('pad-scroll').querySelectorAll('.key').length, 0, 'back to the screen buttons');
  assert.deepStrictEqual(p.errors, []);
});

test('nothing is sent while the connection is not open', async () => {
  const sent = [];
  const p = await load({ matchMedia: TOUCH, WebSocket: socket(sent, 0) });
  pad(p, STREET);
  p.click(p.el('pad-scroll').querySelectorAll('.chip')[0]);
  p.click(p.el('pad-enter'));
  assert.deepStrictEqual(sent, []);
  assert.deepStrictEqual(p.errors, []);
});

test('a yes or no question offers the letters its prompt shows, in any language', async () => {
  const sent = [];
  const p = await load({ matchMedia: TOUCH, WebSocket: socket(sent) });
  pad(p, ['Phonetest is what you want? (Y/n)']);
  assert.deepStrictEqual(chips(p), ['Y, Yes', 'N, No']);
  pad(p, ['¿Es correcto? (S/N)']);
  assert.deepStrictEqual(chips(p), ['S, Yes', 'N, No']);
  p.click(p.el('pad-scroll').querySelectorAll('.chip')[0]);
  assert.deepStrictEqual(sent, ['S\r'], 'the Spanish yes letter, not Y');
  // A pair earlier on the screen, with a menu after it, is not the question being asked.
  pad(p, ['Toggle sound (Y/N) in Settings', ' [A] Attack', 'Choose action:']);
  assert.deepStrictEqual(chips(p), ['A, Attack']);
});

test('a question that ends in a colon offers the keyboard; a screen with nothing to pick lights Enter', async () => {
  const p = await load({ matchMedia: TOUCH });
  pad(p, ['  Username:']);
  assert.deepStrictEqual(chips(p), ['Aa, Open the keyboard']);
  assert.ok(!p.el('pad-enter').classList.contains('hot'));
  pad(p, ['  You wake with a gasp.', 'Press Enter to continue...']);
  assert.deepStrictEqual(chips(p), []);
  assert.ok(p.el('pad-enter').classList.contains('hot'));
  assert.ok(p.el('pad-scroll').textContent.includes('Nothing to tap on this screen'));
  pad(p, STREET);
  assert.ok(!p.el('pad-enter').classList.contains('hot'), 'a menu does not light Enter');
});

test('Type raises the phone keyboard only when tapped', async () => {
  const p = await load({ matchMedia: TOUCH });
  assert.ok(!p.doc.body.classList.contains('typing'));
  p.click(p.el('pad-type'));
  assert.ok(p.doc.body.classList.contains('typing'));
  const html = readPage(PAGE);
  assert.match(html, /ws\.onopen = function \(\) \{ setConn\('play\.status\.connected', 'ok'\); if \(terminal && !isTouch\(\)\) terminal\.focus\(\); \};/);
  assert.match(html, /document\.getElementById\('terminal'\)\.addEventListener\('click', function \(\) \{ if \(!isTouch\(\)\) terminal\.focus\(\); \}\);/);
});

test('the screen is read from the cursor up to the previous prompt, never above the top of the screen', async () => {
  const p = await load({ matchMedia: TOUCH });
  const lines = ['[1] Phonetest', '[@] Support', '[D] Dungeons', '[40hp 100st] Main Street > D', '[E]nter the dungeon', '', '[40hp 100st] Dungeons >'];
  p.ctx.__buf = { baseY: 2, cursorY: 4, getLine: (y) => ({ translateToString: () => lines[y] }) };
  assert.deepStrictEqual([...p.run('UsurperPlay.rowsFromBuffer(__buf)')], ['[E]nter the dungeon', '', '[40hp 100st] Dungeons >']);
  p.ctx.__buf = { baseY: 2, cursorY: 1, getLine: (y) => ({ translateToString: () => lines[y] }) };
  assert.deepStrictEqual([...p.run('UsurperPlay.rowsFromBuffer(__buf)')], ['[D] Dungeons', '[40hp 100st] Main Street > D'], 'stops at the top of the screen');
  for (const bad of [null, {}, { getLine: () => null, baseY: 0, cursorY: 0 }]) {
    p.ctx.__buf = bad;
    assert.ok(Array.isArray(p.run('UsurperPlay.rowsFromBuffer(__buf)')));
  }
  assert.deepStrictEqual(p.errors, []);
});

test('the compass shows the exits the game sent that this screen offers, and sends the key', async () => {
  const sent = [];
  const p = await load({ matchMedia: TOUCH, WebSocket: socket(sent) });
  p.run(`UsurperPlay.handleFrame('{"gmcp":"Room.Info","data":{"name":"Burial Chamber","exits":{"south":"room_0","north":"room_9","west":"room_11"}}}')`);
  pad(p, ROOM);
  const dirs = p.el('pad-scroll').querySelectorAll('.dir');
  assert.deepStrictEqual(dirs.map((d) => [d.textContent, !!d.disabled]), [['N', false], ['W', false], ['E', true], ['S', false]]);
  assert.deepStrictEqual(chips(p), ['F, Fight the monsters', 'M, Map', 'G, Guide', '*, Inventory', 'P, Potions', '%, Status', 'Q, Leave dungeon'], 'exit keys move to the compass');
  p.click(dirs[0]);
  assert.deepStrictEqual(sent, ['N\r']);
  assert.deepStrictEqual(p.errors, []);
});

test('no compass when the screen offers no exit, or when the exits are town places', async () => {
  const p = await load({ matchMedia: TOUCH });
  p.run(`UsurperPlay.handleFrame('{"gmcp":"Room.Info","data":{"name":"Burial Chamber","exits":{"north":"room_9","south":"room_0"}}}')`);
  pad(p, ['  [*] Inventory  [Q] Back', 'Your choice:']);
  assert.strictEqual(p.el('pad-scroll').querySelectorAll('.dir').length, 0, 'inventory open in the dungeon');
  p.run(`UsurperPlay.handleFrame('{"gmcp":"Room.Info","data":{"name":"Main Street","exits":{"theinn":2,"church":4}}}')`);
  pad(p, STREET);
  assert.strictEqual(p.el('pad-scroll').querySelectorAll('.dir').length, 0, 'town exits are places');
  assert.ok(chips(p).includes('S, Status ...'), 'S stays a Status button');
});

test('screen text with HTML stays text on the buttons', async () => {
  const p = await load({ matchMedia: TOUCH });
  pad(p, [' [X] ' + HOSTILE, ' [Y]' + '<img src=x onerror=alert(9)>']);
  const box = p.el('pad-scroll');
  assert.strictEqual(box.querySelectorAll('.chip').length, 2);
  assert.ok(box.textContent.includes(HOSTILE.slice(0, 40)));
  const tags = descendants(box).map((e) => e.tagName);
  assert.ok(tags.every((t) => ['DIV', 'BUTTON', 'B', 'SPAN'].includes(t)), tags.join(','));
  assert.deepStrictEqual(p.errors, []);
});

test('the text size switch is remembered, and a broken storage is no problem', async () => {
  const p = await load({ matchMedia: TOUCH });
  assert.strictEqual(p.el('fit-toggle').getAttribute('aria-pressed'), 'false');
  p.click(p.el('fit-toggle'));
  assert.strictEqual(p.storage.playFit, 'read');
  assert.strictEqual(p.el('fit-toggle').getAttribute('aria-pressed'), 'true');
  const q = await load({ matchMedia: TOUCH, storage: { playFit: 'read' } });
  assert.strictEqual(q.run('UsurperPlay.fitMode()'), 'read');
  const odd = await load({ matchMedia: TOUCH, storage: { playFit: 'huge' } });
  assert.strictEqual(odd.run('UsurperPlay.fitMode()'), 'whole', 'an unknown value is the default');
  const broken = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); }, removeItem() {} };
  const r = await load({ matchMedia: TOUCH, localStorage: broken });
  assert.strictEqual(r.run('UsurperPlay.fitMode()'), 'whole');
  r.click(r.el('fit-toggle'));
  assert.strictEqual(r.run('UsurperPlay.fitMode()'), 'read');
  assert.deepStrictEqual(r.errors, []);
});

test('the pad labels are drawn in the page language', async () => {
  const p = await loadPage(PAGE, { matchMedia: TOUCH, storage: { lang: 'fr' }, fetch: (url) => (url === '/lang/fr.json' ? lang('fr') : url === '/api/languages' ? LANGS : {}) });
  await p.settle();
  assert.strictEqual(p.el('pad-enter').textContent, 'Entrée');
  assert.strictEqual(p.el('fit-toggle').getAttribute('aria-label'), 'Texte plus grand');
  assert.strictEqual(p.el('pad').getAttribute('aria-label'), 'Boutons du jeu');
  pad(p, ['Est-ce correct ? (O/N)']);
  assert.deepStrictEqual(chips(p), ['O, Oui', 'N, Non']);
});
