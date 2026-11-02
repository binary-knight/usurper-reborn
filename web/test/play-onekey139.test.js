'use strict';
// web/play/index.html: one key menus and the combat round from Client.Menu (1.3.9, play test C1, his pick 4).
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { loadPage, readPage } = require('./harness');

const LANGS = ['en', 'es', 'fr', 'hu', 'it'];
const WEB = path.join(__dirname, '..');
const lang = (l) => JSON.parse(fs.readFileSync(path.join(WEB, 'lang', l + '.json'), 'utf8'));
function socket(sent) {
  return class { constructor() { this.readyState = 1; } send(d) { sent.push(d); } close() {} };
}
async function load(sent, storage) {
  const p = await loadPage('play/index.html', { storage, WebSocket: socket(sent), fetch: (url) => (url.startsWith('/lang/') ? lang('en') : url === '/api/languages' ? LANGS : {}) });
  await p.settle();
  return p;
}
const VIEWS = [['Classic', undefined], ['Chronicle', { playView: 'chronicle' }]];
function frame(p, gmcp, data) {
  p.ctx.__f = JSON.stringify({ gmcp, data });
  p.run('UsurperPlay.handleFrame(__f)');
}
const menu = (extra) => Object.assign({ screen: 'main_street', oneKey: true, groups: [
  { label: 'Places', items: [{ key: 'D', label: 'Dungeons' }, { key: 'A', label: 'Dark Alley', enabled: false }, { key: '1', label: 'Healer' }, { key: '/', label: 'Commands' }] },
] }, extra);
// One key stroke at the terminal, as the browser and xterm make it: keydown on the terminal, then xterm's data, then keyup.
function press(p, key, extra) {
  p.doc.dispatch(p.el('terminal'), 'keydown', Object.assign({ key }, extra || {}));
  p.ctx.__d = key;
  p.run('UsurperPlay.typedData(__d)');
  p.doc.dispatch(p.el('terminal'), 'keyup', { key });
}
function dataOnly(p, data) {
  p.ctx.__d = data;
  p.run('UsurperPlay.typedData(__d)');
}

for (const [name, storage] of VIEWS) {
  test(name + ': a menu key on an empty line sends the character and Enter, in any case; other keys and a refused key', async () => {
    const sent = [];
    const p = await load(sent, storage);
    frame(p, 'Client.Menu', menu());
    press(p, 'd');
    assert.deepStrictEqual(sent, ['d\r']);
    frame(p, 'Client.Menu', menu());
    press(p, 'D');
    assert.deepStrictEqual(sent.slice(1), ['D\r']);
    frame(p, 'Client.Menu', menu());
    press(p, 'a');
    assert.deepStrictEqual(sent.slice(2), ['a\r'], 'a refused key goes too: the game answers it, as with key and Enter');
    frame(p, 'Client.Menu', menu());
    press(p, 'z');
    assert.deepStrictEqual(sent.slice(3), ['z'], 'not a key of this menu: typed');
    assert.deepStrictEqual(p.errors, []);
  });

  test(name + ': never with text on the line; the line empties at Enter or by Backspace', async () => {
    const sent = [];
    const p = await load(sent, storage);
    frame(p, 'Client.Menu', menu());
    press(p, 'x');
    press(p, 'd');
    assert.deepStrictEqual(sent, ['x', 'd'], 'typed text on the line');
    press(p, '1');
    assert.deepStrictEqual(sent.slice(2), ['1']);
    dataOnly(p, '\x7f');
    dataOnly(p, '\x7f');
    press(p, 'd');
    assert.deepStrictEqual(sent.slice(3), ['\x7f', '\x7f', 'd'], 'one character is still on the line');
    dataOnly(p, '\x7f');
    dataOnly(p, '\x7f');
    press(p, 'd');
    assert.deepStrictEqual(sent.slice(6), ['\x7f', '\x7f', 'd\r'], 'the line is empty again');
    frame(p, 'Client.Menu', menu());
    press(p, 'x');
    dataOnly(p, '\r');
    frame(p, 'Client.Menu', menu());
    press(p, 'd');
    assert.deepStrictEqual(sent.slice(9), ['x', '\r', 'd\r'], 'Enter empties it');
    frame(p, 'Client.Menu', menu());
    dataOnly(p, '\x1b[A');
    press(p, 'd');
    assert.deepStrictEqual(sent.slice(12), ['\x1b[A', 'd\r'], 'an arrow key puts nothing on the line');
  });

  test(name + ': never for a slash command, even when the menu lists /', async () => {
    const sent = [];
    const p = await load(sent, storage);
    frame(p, 'Client.Menu', menu());
    press(p, '/');
    press(p, 'd');
    assert.deepStrictEqual(sent, ['/', 'd'], 'the command goes on being typed');
  });

  test(name + ': never with no current menu: before any, after the next Enter, after a screen clear', async () => {
    const sent = [];
    const p = await load(sent, storage);
    press(p, 'd');
    assert.deepStrictEqual(sent, ['d'], 'no Client.Menu yet');
    dataOnly(p, '\x7f');
    frame(p, 'Client.Menu', menu());
    press(p, 'd');
    press(p, 'd');
    assert.deepStrictEqual(sent.slice(2), ['d\r', 'd'], 'the Enter it sent ends that menu: a name or number prompt types');
    dataOnly(p, '\x7f');
    frame(p, 'Client.Menu', menu());
    p.run('UsurperPlay.drawStarted(300)');
    press(p, 'd');
    assert.deepStrictEqual(sent.slice(5), ['d'], 'a new screen with no menu of its own');
  });

  test(name + ': never during an IME composition or for a key the IME holds', async () => {
    const sent = [];
    const p = await load(sent, storage);
    frame(p, 'Client.Menu', menu());
    press(p, 'd', { isComposing: true });
    assert.deepStrictEqual(sent, ['d']);
    dataOnly(p, '\x7f');
    press(p, 'd', { keyCode: 229 });
    assert.deepStrictEqual(sent.slice(2), ['d']);
    dataOnly(p, '\x7f');
    p.doc.dispatch(p.el('terminal'), 'compositionstart', {});
    press(p, 'd');
    assert.deepStrictEqual(sent.slice(4), ['d'], 'between compositionstart and compositionend');
    dataOnly(p, '\x7f');
    p.doc.dispatch(p.el('terminal'), 'compositionend', {});
    dataOnly(p, 'd');
    assert.deepStrictEqual(sent.slice(6), ['d'], 'the composition\'s result comes with no key stroke');
    dataOnly(p, '\x7f');
    press(p, 'd');
    assert.deepStrictEqual(sent.slice(8), ['d\r'], 'after the composition a key works');
  });

  test(name + ': never for a paste: one character or many, with or without a key stroke before it', async () => {
    const sent = [];
    const p = await load(sent, storage);
    frame(p, 'Client.Menu', menu());
    dataOnly(p, 'd');
    assert.deepStrictEqual(sent, ['d'], 'a pasted single character has no key stroke');
    dataOnly(p, '\x7f');
    p.doc.dispatch(p.el('terminal'), 'keydown', { key: 'd' });
    p.doc.dispatch(p.el('terminal'), 'paste', {});
    dataOnly(p, 'd');
    assert.deepStrictEqual(sent.slice(2), ['d'], 'a paste event disarms a key stroke in progress');
    dataOnly(p, '\x7f');
    dataOnly(p, 'dd');
    press(p, 'd');
    assert.deepStrictEqual(sent.slice(4), ['dd', 'd'], 'many characters, and the line now holds them');
    p.doc.dispatch(p.el('terminal'), 'keydown', { key: 'v', ctrlKey: true });
    dataOnly(p, 'v');
    assert.deepStrictEqual(sent.slice(6), ['v'], 'Ctrl+V arms nothing');
  });

  test(name + ': never when oneKey is false, absent or not exactly true; a held key does not repeat', async () => {
    for (const oneKey of [false, undefined, 'true', 1]) {
      const sent = [];
      const p = await load(sent, storage);
      frame(p, 'Client.Menu', menu({ oneKey }));
      press(p, 'd');
      assert.deepStrictEqual(sent, ['d'], String(oneKey));
    }
    const sent = [];
    const p = await load(sent, storage);
    frame(p, 'Client.Menu', menu());
    press(p, 'd', { repeat: true });
    assert.deepStrictEqual(sent, ['d']);
  });

  test(name + ': never with Ctrl, Alt or Meta held', async () => {
    for (const held of ['ctrlKey', 'altKey', 'metaKey']) {
      const sent = [];
      const p = await load(sent, storage);
      frame(p, 'Client.Menu', menu());
      p.doc.dispatch(p.el('terminal'), 'keydown', { key: 'd', [held]: true });
      dataOnly(p, 'd');
      assert.deepStrictEqual(sent, ['d'], held);
    }
  });
}

test('in the book: keys typed with its focus follow the same rule; a key cap sends key and Enter whatever the line holds', async () => {
  const sent = [];
  const p = await load(sent, { playView: 'chronicle' });
  frame(p, 'Client.Menu', menu());
  const key = (k, extra) => p.doc.dispatch(p.el('c-body'), 'keydown', Object.assign({ key: k }, extra || {}));
  key('d');
  assert.deepStrictEqual(sent, ['d\r']);
  frame(p, 'Client.Menu', menu());
  key('x');
  key('d');
  assert.deepStrictEqual(sent.slice(1), ['x', 'd'], 'text on the line');
  frame(p, 'Client.Menu', menu());
  p.doc.dispatch(p.el('c-keys').querySelector('.kc'), 'click', {});
  assert.deepStrictEqual(sent.slice(3), ['D\r']);
  frame(p, 'Client.Menu', menu({ oneKey: false }));
  key('d');
  assert.deepStrictEqual(sent.slice(4), ['d'], 'oneKey false');
  dataOnly(p, '\x7f');
  frame(p, 'Client.Menu', menu());
  key('d', { repeat: true });
  assert.deepStrictEqual(sent.slice(6), ['d'], 'a held key');
});

test('the terminal\'s data goes through the one key rule, and its key strokes are noted before xterm sees them', () => {
  const html = readPage('play/index.html');
  assert.match(html, /terminal\.onData\(typedData\);/);
  assert.match(html, /termBox\.addEventListener\('keydown', keyStroke, true\);/);
});

test('a reconnect starts with an empty line and no menu', async () => {
  const sent = [];
  const p = await load(sent);
  frame(p, 'Client.Menu', menu());
  press(p, 'x');
  p.click(p.el('reconnect'));
  frame(p, 'Client.Menu', menu());
  press(p, 'd');
  assert.deepStrictEqual(sent, ['x', 'd\r']);
});

// ---------------------------------------------------------------- the round

const FOE = [{ name: 'Goblin', level: 1, hp: 50, maxHp: 58, alive: true }];
const fightMenu = (round, screen) => ({ screen: screen || 'combat', oneKey: true, round, groups: [{ label: 'Actions', items: [{ key: 'A', label: 'Attack' }] }] });
const marker = (p) => { const m = p.el('c-body').querySelector('.clog').children.filter((e) => /\brd\b/.test(e.getAttribute('class'))); return m.length ? m[0].textContent : ''; };
const places = (p) => [p.el('combat-round').textContent, marker(p), p.el('c-rh-phone').textContent];

test('the drawer\'s counter, the page marker and the phone header follow the round of the newest combat menu', async () => {
  const p = await load([], { playView: 'chronicle' });
  frame(p, 'Char.Combat.Start', { enemies: FOE });
  frame(p, 'Client.Menu', fightMenu(1));
  assert.deepStrictEqual(places(p), ['Round 1', 'Round 1', 'Round 1']);
  frame(p, 'Char.Combat.Enemies', { enemies: FOE, round: 1 });
  frame(p, 'Client.Menu', fightMenu(2));
  assert.deepStrictEqual(places(p), ['Round 2', 'Round 2', 'Round 2']);
  frame(p, 'Client.Menu', fightMenu(3));
  assert.deepStrictEqual(places(p), ['Round 3', 'Round 3', 'Round 3'], 'a round where no foe changed sends no Enemies frame');
  frame(p, 'Char.Combat.Enemies', { enemies: FOE, round: 2 });
  assert.deepStrictEqual(places(p), ['Round 3', 'Round 3', 'Round 3'], 'an Enemies frame never takes it back');
  frame(p, 'Client.Menu', fightMenu(4, 'combat_pvp'));
  assert.deepStrictEqual(places(p), ['Round 4', 'Round 4', 'Round 4']);
  assert.deepStrictEqual(p.errors, []);
});

test('with no round field the places keep following Char.Combat.Enemies; a new fight starts over; odd values and other screens are not read', async () => {
  const p = await load([], { playView: 'chronicle' });
  frame(p, 'Char.Combat.Start', { enemies: FOE });
  frame(p, 'Client.Menu', fightMenu(undefined));
  frame(p, 'Char.Combat.Enemies', { enemies: FOE, round: 2 });
  assert.deepStrictEqual(places(p), ['Round 2', 'Round 2', 'Round 2'], 'no field');
  for (const odd of ['9', -1, 0, null, {}]) {
    frame(p, 'Client.Menu', fightMenu(odd));
    assert.deepStrictEqual(places(p), ['Round 2', 'Round 2', 'Round 2'], JSON.stringify(odd));
  }
  frame(p, 'Client.Menu', fightMenu(7));
  frame(p, 'Client.Menu', Object.assign(menu(), { round: 9 }));
  assert.strictEqual(p.el('combat-round').textContent, 'Round 7', 'only a combat screen gives the round');
  frame(p, 'Char.Combat.Start', { enemies: FOE });
  assert.strictEqual(p.el('combat-round').textContent, '', 'a new fight');
  frame(p, 'Char.Combat.Enemies', { enemies: FOE, round: 1 });
  assert.strictEqual(p.el('combat-round').textContent, 'Round 1', 'the last fight\'s menu does not hold the new one');
  assert.deepStrictEqual(p.errors, []);
});

test('in Classic the drawer\'s counter follows the menu too', async () => {
  const p = await load([]);
  frame(p, 'Char.Combat.Start', { enemies: FOE });
  frame(p, 'Client.Menu', fightMenu(3));
  assert.strictEqual(p.el('combat-round').textContent, 'Round 3');
});

// ---------------------------------------------------------------- F1: the wait after a one key send

const waiting = (p) => frame(p, 'Client.Waiting', {});
async function waitLoad(sent, storage) {
  const p = await load(sent, storage);
  waiting(p);
  frame(p, 'Client.Menu', menu());
  return p;
}
for (const [name, storage] of VIEWS) {
  test(name + ': F1: a second key before the next read is dropped, sent nowhere and not on the line', async () => {
    const sent = [];
    const p = await waitLoad(sent, storage);
    press(p, 'd');
    press(p, 'd');
    press(p, 'x');
    assert.deepStrictEqual(sent, ['d\r']);
    waiting(p);
    frame(p, 'Client.Menu', menu());
    press(p, 'd');
    assert.deepStrictEqual(sent.slice(1), ['d\r'], 'nothing was left on the line: one key still works');
  });

  test(name + ': F1: a held key\'s repeats are dropped too while waiting, and typed when not', async () => {
    const sent = [];
    const p = await waitLoad(sent, storage);
    press(p, 'd');
    press(p, 'd', { repeat: true });
    assert.deepStrictEqual(sent, ['d\r']);
    waiting(p);
    press(p, 'd', { repeat: true });
    assert.deepStrictEqual(sent.slice(1), ['d'], 'no wait: as typed');
  });

  test(name + ': F1: Enter, Backspace, a paste, an IME result and a key with Ctrl are never dropped', async () => {
    const sent = [];
    const p = await waitLoad(sent, storage);
    press(p, 'd');
    dataOnly(p, '\r');
    p.doc.dispatch(p.el('terminal'), 'keydown', { key: 'Enter' });
    dataOnly(p, '\r');
    dataOnly(p, '\x7f');
    dataOnly(p, 'q');
    p.doc.dispatch(p.el('terminal'), 'keydown', { key: 'c', ctrlKey: true });
    dataOnly(p, '\x03');
    assert.deepStrictEqual(sent, ['d\r', '\r', '\r', '\x7f', 'q', '\x03']);
  });

  test(name + ': F1: a key on a showing prompt with no menu is kept: Client.Waiting ends the wait, and only it', async () => {
    const sent = [];
    const p = await waitLoad(sent, storage);
    press(p, 'a');
    frame(p, 'Client.Menu', menu());
    press(p, 'd');
    assert.deepStrictEqual(sent, ['a\r'], 'a menu frame alone does not end the wait');
    waiting(p);
    p.run('UsurperPlay.drawStarted(300)');
    press(p, '2');
    assert.deepStrictEqual(sent.slice(1), ['2'], 'the target prompt: kept, on the line');
    press(p, 'Q');
    assert.deepStrictEqual(sent.slice(2), ['Q']);
  });

  test(name + ': F1: the wait ends by itself at 20 seconds, not before', async () => {
    const sent = [];
    const p = await waitLoad(sent, storage);
    p.run('var __real = Date.now; var __t = 1000000; Date.now = function () { return __t; };');
    press(p, 'd');
    p.run('__t += 19999;');
    press(p, 'x');
    assert.deepStrictEqual(sent, ['d\r'], 'still waiting');
    p.run('__t += 1;');
    press(p, 'x');
    assert.deepStrictEqual(sent.slice(1), ['x'], 'after the limit nothing is dropped');
  });

  test(name + ': F1: a game that sends no Client.Waiting has no wait; a reconnect starts over', async () => {
    const sent = [];
    const p = await load(sent, storage);
    frame(p, 'Client.Menu', menu());
    press(p, 'd');
    press(p, 'x');
    assert.deepStrictEqual(sent, ['d\r', 'x'], 'nothing is lost on an older server');
    const s2 = [];
    const q = await waitLoad(s2, storage);
    press(q, 'd');
    q.click(q.el('reconnect'));
    frame(q, 'Client.Menu', menu());
    press(q, 'd');
    press(q, 'x');
    assert.deepStrictEqual(s2, ['d\r', 'd\r', 'x'], 'a new connection has neither the old wait nor the old frame');
  });
}

test('F1: in the book: a typed key is dropped while waiting, a key cap is not, and a cap starts no wait', async () => {
  const sent = [];
  const p = await waitLoad(sent, { playView: 'chronicle' });
  const key = (k) => p.doc.dispatch(p.el('c-body'), 'keydown', { key: k });
  key('d');
  key('d');
  assert.deepStrictEqual(sent, ['d\r']);
  frame(p, 'Client.Menu', menu());
  p.doc.dispatch(p.el('c-keys').querySelector('.kc'), 'click', {});
  assert.deepStrictEqual(sent.slice(1), ['D\r'], 'a cap during a wait');
  waiting(p);
  frame(p, 'Client.Menu', menu());
  p.doc.dispatch(p.el('c-keys').querySelector('.kc'), 'click', {});
  frame(p, 'Client.Menu', menu());
  p.doc.dispatch(p.el('c-keys').querySelector('.kc'), 'click', {});
  key('x');
  assert.deepStrictEqual(sent.slice(2), ['D\r', 'D\r', 'x'], 'caps start no wait');
});

// ---------------------------------------------------------------- F2: the part typed line on the fight page

const typedLine = (p) => { const e = p.el('c-body').querySelector('.typedline'); return e ? e.textContent : null; };
function fightScreen(p, rows, cursor) {
  p.ctx.__rows = rows;
  p.ctx.__cursor = cursor;
  p.run('UsurperPlay.renderChron(__rows, 0, __cursor)');
}
test('F2: the fight page shows the part typed line as the terminal does, until it is gone', async () => {
  const sent = [];
  const p = await load(sent, { playView: 'chronicle' });
  frame(p, 'Char.Combat.Start', { enemies: FOE });
  frame(p, 'Client.Menu', fightMenu(1));
  fightScreen(p, ['You hit it.', 'Choose action: '], 1);
  assert.strictEqual(typedLine(p), null, 'nothing typed');
  press(p, 'x');
  assert.strictEqual(typedLine(p), 'x', 'before the echo comes back');
  fightScreen(p, ['You hit it.', 'Choose action: x'], 1);
  assert.strictEqual(typedLine(p), 'Choose action: x', 'with the game\'s own prompt');
  assert.ok(!p.el('c-body').querySelector('.clog').children.some((e) => /\bev\b/.test(e.getAttribute('class')) && e.textContent.includes('Choose action')), 'the log still has no prompt line');
  frame(p, 'Char.Combat.Enemies', { enemies: FOE, round: 1 });
  assert.strictEqual(typedLine(p), 'Choose action: x', 'a redraw never clears it');
  dataOnly(p, '\x7f');
  assert.strictEqual(typedLine(p), null, 'Backspace');
  press(p, 'y');
  dataOnly(p, '\r');
  assert.strictEqual(typedLine(p), null, 'Enter');
  assert.strictEqual(p.el('c-body').querySelector('.clog').children.filter((e) => e.tagName === 'IMG').length, 0);
  assert.deepStrictEqual(p.errors, []);
});

// ---------------------------------------------------------------- F3: the round text in long fights

// A fake xterm buffer: absolute rows, a viewport of `rows` at the bottom.
function fakeBuf(lines, viewport) {
  return {
    baseY: Math.max(0, lines.length - viewport), cursorY: Math.min(lines.length, viewport) - 1, cursorX: lines[lines.length - 1].length,
    getLine(y) { const s = lines[y]; return s === undefined ? undefined : { isWrapped: false, translateToString(trim, a, b) { const t = b === undefined ? s : s.slice(a, b); return trim ? t.replace(/\s+$/, '') : t; } }; },
  };
}
const BOX = Array.from({ length: 13 }, () => '║ box row ║').concat(['║ [A] Attack ║'], Array.from({ length: 14 }, () => '║ box row ║'));
const roundRows = (n) => ['You hit the Goblin in round ' + n + '.', 'Goblin misses you in round ' + n + '.', '', '  Round ' + (n + 1)].concat(BOX, ['', 'Choose action: ']);
function read(p, lines, viewport, mark) {
  p.ctx.__buf = fakeBuf(lines, viewport);
  p.ctx.__mark = mark;
  p.run('UsurperPlay.readScreen(__buf, __mark)');
}
for (const [name, storage] of VIEWS) {
  test(name + ': F3: the drawer keeps every round\'s text past round 18, with the boxes filling the visible rows', async () => {
    const p = await load([], storage);
    frame(p, 'Char.Combat.Start', { enemies: FOE });
    const all = ['filler'];
    for (let n = 1; n <= 25; n++) {
      p.run('UsurperPlay.drawStarted(300)');
      const mark = all.length - 1;
      all.push(...roundRows(n));
      read(p, all, 29, mark);
    }
    const log = p.el('combat-log').children.map((e) => e.textContent);
    for (let n = 1; n <= 25; n++) {
      assert.ok(log.includes('You hit the Goblin in round ' + n + '.'), 'hit, round ' + n);
      assert.ok(log.includes('Goblin misses you in round ' + n + '.'), 'miss, round ' + n);
      assert.ok(log.includes('Round ' + (n + 1)), 'round line ' + (n + 1));
    }
    assert.ok(!log.some((l) => /box row|Choose action/.test(l)));
    assert.deepStrictEqual(p.errors, []);
  });

  test(name + ': F3: one long draw (Auto-Combat) keeps its early rounds as they scroll out of sight', async () => {
    const p = await load([], storage);
    frame(p, 'Char.Combat.Start', { enemies: FOE });
    p.run('UsurperPlay.drawStarted(300)');
    const all = ['Choose action: AUTO'];
    for (let n = 1; n <= 8; n++) {
      all.push(...roundRows(n).slice(0, -1), 'x');
      read(p, all, 50, 0);
    }
    const log = p.el('combat-log').children.map((e) => e.textContent);
    assert.ok(log.includes('You hit the Goblin in round 1.'), 'the first round, 250 rows up');
    assert.ok(log.includes('You hit the Goblin in round 8.'));
  });
}

test('F3: an empty Enter\'s repeat of the prompt stays out of the log, and the log holds its last 400 lines', async () => {
  const p = await load([]);
  frame(p, 'Char.Combat.Start', { enemies: FOE });
  p.run('UsurperPlay.drawStarted(300)');
  read(p, ['mark', 'You take a stance.', 'Choose action: ', 'Choose action: '], 29, 0);
  assert.deepStrictEqual(p.el('combat-log').children.map((e) => e.textContent), ['You take a stance.']);
  p.run('UsurperPlay.drawStarted(300)');
  const many = ['mark'].concat(Array.from({ length: 450 }, (_, i) => 'line ' + i), ['Choose action: ']);
  read(p, many, 29, 0);
  const log = p.el('combat-log').children.map((e) => e.textContent);
  assert.strictEqual(log.length, 400);
  assert.strictEqual(log[399], 'line 449');
});

test('F3: a screen clear starts the read at the top of the screen: rows of the old screen are not read again', async () => {
  const p = await load([]);
  frame(p, 'Char.Combat.Start', { enemies: FOE });
  p.run('UsurperPlay.drawStarted(300)');
  read(p, ['scrolled away earlier', 'You hit it.', 'Choose action: '], 2, -1);
  assert.deepStrictEqual(p.el('combat-log').children.map((e) => e.textContent), ['You hit it.']);
});
