'use strict';
// web/play/index.html and chronicle.css: the high play test findings of 1.3.9 (r200 play test). The fight page and the
// drawer show each round's text (B1, B2), the footer takes each value from the newest frame (B3), a text page keeps a
// readable window that opens at the new screen's first line (R2), key labels wrap between words (R3), the terminal's
// own wraps are undone (R4), block art is set solid (A1), and on a phone the keys never cover the text (P1, P2).
// N1 (the View button before login) is in play-page.test.js.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { loadPage } = require('./harness');

const PAGE = 'play/index.html';
const LANGS = ['en', 'es', 'fr', 'hu', 'it'];
const WEB = path.join(__dirname, '..');
const CSS = fs.readFileSync(path.join(WEB, 'play', 'chronicle.css'), 'utf8');
const lang = (l) => JSON.parse(fs.readFileSync(path.join(WEB, 'lang', l + '.json'), 'utf8'));

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
function screen(p, rows, start, cursor) {
  p.ctx.__rows = rows;
  p.ctx.__start = start;
  p.ctx.__cursor = cursor;
  p.run('UsurperPlay.renderChron(__rows, __start, __cursor)');
}
const enter = (p) => p.run('UsurperPlay.drawStarted(1500)');
const logText = (p) => p.el('c-body').querySelector('.clog').children.filter((e) => /\bev\b/.test(e.getAttribute('class'))).map((e) => e.textContent);
const drawerLog = (p) => p.el('combat-log').children.map((e) => e.textContent);
const footFields = (p) => p.el('c-foot').querySelectorAll('.f').map((f) => f.textContent);
// A block of the stylesheet: from the line that opens it to its closing brace at column 0.
function block(head) {
  const at = CSS.indexOf(head);
  assert.ok(at !== -1, 'no block ' + head);
  const rest = CSS.slice(at);
  return rest.slice(0, rest.indexOf('\n}'));
}

const FIGHT_MENU = {
  screen: 'combat',
  groups: [
    { label: 'Actions', items: [{ key: 'A', label: 'Attack' }, { key: 'D', label: 'Defend' }, { key: 'I', label: 'Use Item (Potions: 20/20)' }] },
    { label: 'Quickbar', items: [{ key: '1', label: 'Power Strike (15 ST)' }, { key: '2', label: 'Second Wind (25 ST)' }] },
    { label: 'Options', items: [{ key: 'R', label: 'Flee' }, { key: 'AUTO', label: 'Auto-Combat' }, { key: 'SPD', label: 'Combat Speed: Normal' }] },
  ],
};
// The status box and the action menu box the game prints after each round's text (as in the play test, round 2).
const BOXES = (round, hp) => [
  '  Round ' + round,
  '╔════════╗',
  '║                      COMBAT STATUS                       ║',
  '╠════════╣',
  '║ [1] Zombie             ████░░░░    ' + hp + '/65',
  '║ Hwtest             HP:█████     2/6',
  '║ ATK: 24    DEF: -3 ',
  '╚════════╝',
  '',
  '╔════════╗',
  '║            Choose action:             ║',
  '╠════════╣',
  '║ [A] Attack                            ║',
  '║ [R] Flee                              ║',
  '╚════════╝',
  '',
  'Choose action: ',
];
const ROUND1 = ['═══ COMBAT ═══', '                  ▄▄▄▄▄▄▄           ', '                ▓ █▒▒▒▒▒█ ▓         ', 'A Zombie shambles out of the dark!'].concat(BOXES(1, 65));
const ROUND2 = ['You attack Zombie!', '[You roll 15; you need 2 or more]', 'You shatter Zombie for 40 damage!', '',
  'Zombie attacks you!', '[Zombie rolls: 17 + -2 = 15 vs DC 10]', 'Zombie RIPS you apart!', '4 damage gets through your defenses!', ''].concat(BOXES(2, 25));
const ROUND3 = ['You drink a Healing Potion and recover 12 HP!', 'Potions remaining: 19/21', 'You try to flee but the Zombie blocks your way!', ''].concat(BOXES(3, 25));
async function fight(p) {
  frame(p, 'Char.Vitals', { hp: 6, maxHp: 6, mp: 0, maxMp: 0, combatSp: 73, maxCombatSp: 73 });
  frame(p, 'Char.Status', { name: 'Hwtest', class: 'Warrior', level: 1, gold: 2000, xp: 10 });
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Zombie', level: 1, hp: 65, maxHp: 65 }, { name: 'Zombie', level: 1, hp: 65, maxHp: 65 }] });
  frame(p, 'Client.Menu', FIGHT_MENU);
}

// ---------------------------------------------------------------- B1: the fight page

test('B1: the fight page shows each round\'s text in order, newest at the foot, without the boxes or the prompt', async () => {
  const p = await chronicle();
  await fight(p);
  screen(p, ROUND1);
  assert.strictEqual(p.run('UsurperPlay.layout()'), 'fight');
  assert.deepStrictEqual(logText(p), ['COMBAT', 'A Zombie shambles out of the dark!', 'Round 1']);
  // the game echoes the key as it is typed, before the Enter: the prompt row still goes
  screen(p, ROUND1.slice(0, -1).concat(['Choose action: A']), 0, ROUND1.length - 1);
  assert.deepStrictEqual(logText(p), ['COMBAT', 'A Zombie shambles out of the dark!', 'Round 1']);
  enter(p);
  frame(p, 'Client.Menu', FIGHT_MENU);
  screen(p, ROUND2);
  assert.deepStrictEqual(logText(p).slice(-8), ['You attack Zombie!', '[You roll 15; you need 2 or more]', 'You shatter Zombie for 40 damage!',
    'Zombie attacks you!', '[Zombie rolls: 17 + -2 = 15 vs DC 10]', 'Zombie RIPS you apart!', '4 damage gets through your defenses!', 'Round 2']);
  enter(p);
  frame(p, 'Client.Menu', FIGHT_MENU);
  screen(p, ROUND3);
  const all = logText(p);
  assert.deepStrictEqual(all.slice(-4), ['You drink a Healing Potion and recover 12 HP!', 'Potions remaining: 19/21', 'You try to flee but the Zombie blocks your way!', 'Round 3'], 'potion and flee lines, newest last');
  assert.strictEqual(all.indexOf('Round 1') < all.indexOf('You attack Zombie!') && all.indexOf('Round 2') < all.indexOf('You drink a Healing Potion and recover 12 HP!'), true, 'in the order printed');
  for (const gone of ['Choose action:', 'ATK: 24    DEF: -3', 'COMBAT STATUS', '[A] Attack', 'Hwtest']) assert.ok(!all.some((l) => l.includes(gone)), gone);
  // the newest line is the largest; the roll lines are asides
  const evs = p.el('c-body').querySelector('.clog').children.filter((e) => /\bev\b/.test(e.getAttribute('class')));
  assert.match(evs[evs.length - 1].getAttribute('class'), /^ev a0$/);
  assert.ok(evs.filter((e) => /^\[/.test(e.textContent)).every((e) => /\baside\b/.test(e.getAttribute('class'))));
  assert.deepStrictEqual(p.errors, []);
});

test('B1: text that arrives in pieces is read once per draw, and rows from before a draw are never stored under it', async () => {
  const p = await chronicle();
  await fight(p);
  screen(p, ROUND1);
  enter(p);
  screen(p, ROUND2.slice(0, 3));
  screen(p, ROUND2);
  assert.strictEqual(logText(p).filter((l) => l === 'You attack Zombie!').length, 1, 'the same draw read twice is one entry');
  // an Enter, then a frame that redraws the page and starts nothing new before the next screen is read
  const before = logText(p).length;
  enter(p);
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Zombie', level: 1, hp: 65, maxHp: 65 }] });
  assert.deepStrictEqual(logText(p), [], 'a new fight starts an empty log; the old rows are not stored under the new draw');
  assert.ok(before > 0);
  assert.deepStrictEqual(p.errors, []);
});

test('B1: a sentence that names a key stays; a menu line whose keys are on the dock goes', async () => {
  const p = await chronicle();
  await fight(p);
  screen(p, ['┌─── TIP ───┐', '│ Use [1] Power Strike for 1.75x damage, or [A] for a regular Attack.', '└───┘',
    '[A] Attack  [D] Defend  [R] Flee', 'Choose action: ']);
  assert.deepStrictEqual(logText(p), ['TIP', 'Use [1] Power Strike for 1.75x damage, or [A] for a regular Attack.']);
});

// ---------------------------------------------------------------- B2: the drawer log

test('B2: the drawer\'s combat log fills with each round in Classic, and keeps the killed lines after it', async () => {
  const p = await load({ matchMedia: (q) => ({ matches: /min-width: 1200px/.test(q) }) });
  await fight(p);
  assert.strictEqual(p.el('drawer').hidden, false, 'Classic opens the drawer for the fight');
  screen(p, ROUND1);
  assert.deepStrictEqual(drawerLog(p), ['COMBAT', 'A Zombie shambles out of the dark!', 'Round 1']);
  enter(p);
  screen(p, ROUND2);
  enter(p);
  screen(p, ROUND3);
  const log = drawerLog(p);
  assert.deepStrictEqual(log.slice(3, 5), ['You attack Zombie!', '[You roll 15; you need 2 or more]']);
  assert.strictEqual(log[log.length - 1], 'Round 3');
  frame(p, 'Char.Combat.Killed', { name: 'Zombie' });
  assert.match(drawerLog(p).slice(-1)[0], /Zombie/);
  assert.strictEqual(drawerLog(p).slice(-2)[0], 'Round 3', 'the killed line follows the round text');
  // the last round's text still comes in the draw the fight ended in; the next draw is not the fight's
  screen(p, ROUND3.slice(0, 1));
  frame(p, 'Char.Combat.End', { outcome: 'Victory', kills: 1, xpGained: 10, goldGained: 5 });
  screen(p, ROUND3.slice(0, 2).concat(['The room is cleared!']));
  assert.strictEqual(drawerLog(p).filter((l) => l === 'The room is cleared!').length, 1);
  enter(p);
  screen(p, ['Main Street', 'You are standing on the main street.']);
  assert.ok(!drawerLog(p).includes('You are standing on the main street.'), 'the next screen is not fight text');
  assert.deepStrictEqual(p.errors, []);
});

// ---------------------------------------------------------------- B3: the footer

test('B3: the footer takes each value from the newest frame that carries it, never an older one over a newer one', async () => {
  const p = await chronicle();
  screen(p, ['x']);
  frame(p, 'Char.Vitals', { hp: 1, maxHp: 6, mp: 0, maxMp: 0, combatSp: 73, maxCombatSp: 73 });
  frame(p, 'Char.Status', { name: 'Hwtest', class: 'Warrior', level: 1, gold: 26, xp: 200 });
  frame(p, 'Client.Footer', { potions: 20, maxPotions: 20, lives: 3, maxLives: 3, livesKind: 'lives', xpPercent: 5 });
  assert.deepStrictEqual(footFields(p), ['HP1/6', 'Potions20/20', 'Gold26', 'Stamina73/73', 'Level1', 'XP200', 'Lives3/3(5%)']);
  // a newer Client.Footer that carries HP, level, gold and XP wins over the older Char.Vitals and Char.Status
  frame(p, 'Client.Footer', { hp: 33, maxHp: 33, level: 2, gold: 2654, xp: 210, potions: 20, maxPotions: 21, lives: 3, maxLives: 3, livesKind: 'lives', xpPercent: 1 });
  assert.deepStrictEqual(footFields(p), ['HP33/33', 'Potions20/21', 'Gold2,654', 'Stamina73/73', 'Level2', 'XP210', 'Lives3/3(1%)']);
  // and the next Char.Vitals or Char.Status wins over that footer in turn
  frame(p, 'Char.Vitals', { hp: 30, maxHp: 33, mp: 0, maxMp: 0, combatSp: 70, maxCombatSp: 73 });
  frame(p, 'Char.Status', { name: 'Hwtest', class: 'Warrior', level: 3, gold: 2700, xp: 700 });
  assert.deepStrictEqual(footFields(p), ['HP30/33', 'Potions20/21', 'Gold2,700', 'Stamina70/73', 'Level3', 'XP700', 'Lives3/3(1%)']);
  // a footer without those keys leaves them; its null for potions or lives means the status line left them out
  frame(p, 'Client.Footer', { potions: null, maxPotions: null, lives: 2, maxLives: 3, livesKind: 'lives', xpPercent: 40 });
  assert.deepStrictEqual(footFields(p), ['HP30/33', 'Gold2,700', 'Stamina70/73', 'Level3', 'XP700', 'Lives2/3(40%)']);
  // odd values in the new keys are dropped at the door: a pair comes whole
  frame(p, 'Client.Footer', { hp: 'x', maxHp: 33, level: -4, gold: 'lots', xp: null, potions: 1, maxPotions: 2, lives: 2, maxLives: 3, livesKind: 'lives', xpPercent: 40 });
  assert.deepStrictEqual(footFields(p), ['HP30/33', 'Potions1/2', 'Gold2,700', 'Stamina70/73', 'Level0', 'XP700', 'Lives2/3(40%)']);
  assert.deepStrictEqual(p.errors, []);
});

// ---------------------------------------------------------------- R2: the text page window

test('R2: a new screen opens at its own first line; the same draw keeps the reader\'s place', async () => {
  const p = await chronicle();
  // the harness has no layout: each line sits 20px below the one before it
  const proto = Object.getPrototypeOf(p.el('c-body'));
  Object.defineProperty(proto, 'offsetTop', { configurable: true, get() { const up = this.parentElement; return up ? up.childNodes.indexOf(this) * 20 : 0; } });
  try {
    const old = Array.from({ length: 30 }, (_, i) => 'old line ' + i);
    screen(p, old.concat(['THE INN', 'The inn is dimly lit.', '[D] Buy a drink', 'The Inn >']), 30);
    assert.strictEqual(p.el('c-screen').scrollTop, 600, 'opens at the new screen\'s first line (row 30)');
    p.el('c-screen').scrollTop = 640;
    screen(p, old.concat(['THE INN', 'The inn is dimly lit.', '[D] Buy a drink', 'More text arrives.', 'The Inn >']), 30);
    assert.strictEqual(p.el('c-screen').scrollTop, 640, 'more text in the same draw keeps the place');
    enter(p);
    screen(p, old.concat(['THE INN', 'x', 'y', 'X-BIT - THE BARTENDER', 'X-bit polishes a glass.', 'Your choice:']), 33);
    assert.strictEqual(p.el('c-screen').scrollTop, 660, 'opens at the bartender screen\'s first line (row 33)');
    // text arriving in pieces keeps the page on the new screen's first line until the reader scrolls
    screen(p, ['earlier'].concat(old, ['THE INN', 'x', 'y', 'X-BIT - THE BARTENDER', 'X-bit polishes a glass.', 'Your choice:']), 34);
    assert.strictEqual(p.el('c-screen').scrollTop, 680, 'the screen scrolled up a row: still its first line');
  } finally { delete proto.offsetTop; }
  // the floor: the text window keeps 16em beside many keys, and the right page scrolls when both do not fit
  const wide = block('@media (min-width: 601px) {');
  assert.match(wide, /^ {2}\.chron \.cbody\.b-text \{ flex: 1 0 16em; \}$/m);
  assert.match(wide, /^ {2}\.chron\[data-layout="text"\] \.recto, \.chron\[data-layout="fight"\] \.recto \{ overflow-y: auto; \}$/m);
  assert.deepStrictEqual(p.errors, []);
});

// ---------------------------------------------------------------- R3: key labels

test('R3: a key label wraps between words, and a narrow right page sets its keys three across', () => {
  for (const m of CSS.matchAll(/\.lb[^{]*\{([^}]*)\}/g)) assert.doesNotMatch(m[1], /overflow-wrap:\s*anywhere|word-break:\s*break-all/, 'a label never breaks letter by letter: ' + m[0].slice(0, 70));
  assert.match(CSS, /^\.chron \.kc \.lb \{[^}]*overflow-wrap: break-word; word-break: normal; \}$/m);
  assert.match(CSS, /^\.chron \.dock \{[^}]*container: dock \/ inline-size; \}$/m);
  const narrow = block('@container dock (max-width: 589px) {');
  assert.match(narrow, /^ {2}\.chron \.grid, \.chron\[data-layout\] \.grid \{ grid-template-columns: repeat\(3, minmax\(0, 1fr\)\);/m);
  assert.match(narrow, /^ {2}\.chron\[data-layout="fight"\] \.kgroups \{ grid-template-columns: repeat\(4, minmax\(0, 1fr\)\);/m);
  assert.match(block('@container dock (max-width: 479px) {'), /\.chron\[data-layout="fight"\] \.kgroups \{ grid-template-columns: repeat\(3, minmax\(0, 1fr\)\); \}/);
  assert.match(narrow, /^ {2}\.chron \.kc \.lb \{ font-size: 12\.5px; letter-spacing: 0; \}$/m);
});

// ---------------------------------------------------------------- R4: the terminal's wraps

// Rows as xterm holds them: the game's line runs past column 80, so the rest goes on a row marked wrapped.
function wrapAt80(line) {
  const out = [];
  for (let i = 0; i < line.length; i += 80) out.push({ text: line.slice(i, i + 80), wrapped: i > 0 });
  return out;
}
const pad = (s) => ' '.repeat(80 - s.length) + s;
const bookLines = (p) => p.el('c-screen').children.map((l) => l.textContent);

test('R4: a row the terminal wrapped joins the row above: the four play test examples read whole', async () => {
  const p = await chronicle();
  const bartender = "X-bit polishes a glass and squints at you. The ex-adventurer's scarred hands move with practiced ease.";
  assert.deepStrictEqual(wrapAt80(bartender).map((r) => r.text.slice(-4)), [' mov', 'ase.'], 'the shot\'s split: "hands mov" / "e with"');
  const greta = pad('Greta grins. "Every blade here was forged by my he') + 'ad smith, and he never misses."';
  const drink = pad('You reach for a potion and dri') + 'nk it down.';
  const way = pad('The whispers follow you, wherever you make your') + ' way.';
  screen(p, wrapAt80(bartender).concat(wrapAt80(greta), wrapAt80(drink), wrapAt80(way), ['Your choice:']));
  const lines = bookLines(p).map((l) => l.trim());
  assert.deepStrictEqual(lines, [bartender, greta.trim(), drink.trim(), way.trim(), 'Your choice:']);
  for (const whole of ['hands move with', 'my head smith', 'and drink it', 'your way.']) assert.ok(lines.some((l) => l.includes(whole)), whole);
});

test('R4: the wrap flag comes from xterm; a break at a space keeps the space; table, menu, box and short lines never join', async () => {
  const p = await chronicle();
  // a fake xterm buffer: translateToString(true) trims the end, as xterm does; isWrapped as xterm sets it
  const rows = [
    ['W'.repeat(41) + ' The whispers follow you wherever your ', false],
    ['way goes.', true],
    ['Name'.padEnd(21) + 'Level  Class      Gold'.padEnd(49) + 'Online'.padStart(10), false],
    ['Ragnar Bloodaxe'.padEnd(21) + '12     Warrior    12,000'.padEnd(49) + 'Yes'.padStart(10), false],
    ['║ [A] Attack'.padEnd(79) + '║', false],
    ['║ [D] Defend'.padEnd(79) + '║', false],
    ['╔' + '═'.repeat(78) + '╗', false],
    ['╚' + '═'.repeat(78) + '╝', false],
    [' [D] Dungeons     [I] Inn     [W] Weapon Shop     [A] Armor Shop    [M] Magic Sh', false],
    ['op', false],
    ['Short line.', false],
    ['Another short line.', false],
    ['Your choice: ', false],
  ];
  assert.ok(rows.slice(0, 1).concat(rows.slice(2, 9)).every(([s]) => s.length === 80), 'the wrapped row and the negatives fill all 80 columns');
  p.ctx.__buf = {
    baseY: 0, cursorY: rows.length - 1,
    getLine(y) { const r = rows[y]; return r ? { isWrapped: r[1], translateToString(trim) { const s = r[0].padEnd(80); return trim ? s.replace(/\s+$/, '') : s; } } : undefined; },
  };
  const got = Array.from(p.run('UsurperPlay.screenOf(__buf, -1)').rows, (segs) => Array.from(segs, (r) => r.t).join(''));
  assert.strictEqual(got[0], 'W'.repeat(41) + ' The whispers follow you wherever your way goes.', 'the space at column 80 stays');
  assert.deepStrictEqual(got.slice(1), rows.slice(2).map(([s]) => s.replace(/\s+$/, '')), 'every row after a real line end stays its own line');
  assert.deepStrictEqual(p.errors, []);
});

// ---------------------------------------------------------------- A1: block art

const SPLASH = [
  '          ███████████████▄▄█▄▄▄▀███▀█▄▀▀███▀▄▄█',
  '          ████████████▄■▐█▄▄███▄▄▄▀██▌▄▄███',
  '░'.repeat(80),
];
test('A1: block art rows are set solid; frames, bars and text are not art', async () => {
  const p = await chronicle();
  screen(p, ['Welcome to the Usurper Reborn online server!'].concat(SPLASH, [
    '██                 ╔══════════╗                 ██',
    '╔' + '═'.repeat(78) + '╗',
    'HP: [████████████████████] 41/41  Potions: 20/20  Gold: 2,000  Lv 1  XP 10/200',
    '                     v1.3.8 "Reckoning"', '                      Press any key...']));
  const cls = p.el('c-screen').children.map((l) => l.getAttribute('class'));
  assert.deepStrictEqual(cls, ['ln', 'ln art', 'ln art', 'ln art', 'ln', 'ln', 'ln', 'ln', 'ln']);
  assert.match(CSS, /^\.chron \.screen \.ln\.art \{ line-height: 1; min-height: 0; white-space: pre; overflow-wrap: normal; letter-spacing: -\.04em; font-size: min\(1em, calc\(100cqi \/ 46\)\); \}$/m);
});

// ---------------------------------------------------------------- P1, P2: the phone

test('P1: on a phone the keys follow the text on every layout; nothing in the book is sticky or fixed', () => {
  const phone = block('@media (max-width: 600px) {');
  assert.match(phone, /^ {2}\.chron \.dock \{ order: 5; position: static;/m);
  assert.doesNotMatch(CSS, /position:\s*(sticky|fixed)/, 'no layout pins the dock over the text');
});

test('P2: a phone fight shows every foe\'s card and bar above the log and the keys; the log keeps whole lines in 40vh', async () => {
  const p = await chronicle({ matchMedia: (q) => ({ matches: /max-width: (759|600)px/.test(q) }) });
  await fight(p);
  frame(p, 'Char.Combat.Enemies', { enemies: [{ name: 'Pixie', level: 7, hp: 30, maxHp: 40, alive: true }, { name: 'Pixie', level: 7, hp: 40, maxHp: 40, alive: true }, { name: 'Kobold', level: 6, hp: 12, maxHp: 50, alive: true }], round: 2 });
  screen(p, ROUND2);
  const foes = p.el('c-hud').querySelectorAll('.hcol')[1].querySelectorAll('.card');
  assert.deepStrictEqual(foes.map((c) => c.querySelector('.nm').textContent), ['Pixie', 'Pixie', 'Kobold']);
  assert.ok(foes.every((c) => c.querySelectorAll('.m').length === 1), 'each foe has its HP bar');
  const phone = block('@media (max-width: 600px) {');
  assert.match(phone, /^ {2}\.chron\[data-layout="fight"\] \.cbody \{ flex: none; min-height: 0; \}$/m);
  assert.match(phone, /^ {2}\.chron\[data-layout="fight"\] \.clog \{ max-height: 40vh; \}$/m);
  assert.match(phone, /^ {2}\.chron \.hud \{ order: 3; margin: 0; \}$/m, 'the cards come before the log and the keys');
  assert.deepStrictEqual(p.errors, []);
});
