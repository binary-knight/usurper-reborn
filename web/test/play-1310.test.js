'use strict';
// web/play/index.html and chronicle.css: the play page items of the 1.3.10 play test list (r200 play test, NEXT-RELEASE.md).
// Boxes drawn as boxes (R5, R14), the fight log without frame (R7), labels cut at a word (R8), a text page that fits its
// rows and its window (R9), the last screen quieter (R10), a taller terminal never adds old rows to the screen (R11), the
// Combat dot (R12), one key per action (R15, B10), no text line as a key (B6), how keys reach the game (N2), two text
// columns read down (N5), the map's keys (N8), caps (X1, X2), the rank chip (B12), low HP (C4), the death page (C5), the
// street's text (P4), scenes for every place (A2) and the monster sprites (A3).
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { loadPage, readPage } = require('./harness');

const PAGE = 'play/index.html';
const LANGS = ['en', 'es', 'fr', 'hu', 'it'];
const WEB = path.join(__dirname, '..');
const ROOT = path.join(WEB, '..');
const CSS = fs.readFileSync(path.join(WEB, 'play', 'chronicle.css'), 'utf8');
const lang = (l) => JSON.parse(fs.readFileSync(path.join(WEB, 'lang', l + '.json'), 'utf8'));
const gameLang = (l) => JSON.parse(fs.readFileSync(path.join(ROOT, 'Localization', l + '.json'), 'utf8'));

function socket(sent, state) {
  return class { constructor() { this.readyState = state === undefined ? 1 : state; } send(d) { sent.push(d); } close() {} };
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
function screen(p, rows, start, cursor) {
  p.ctx.__rows = rows;
  p.ctx.__start = start;
  p.ctx.__cursor = cursor;
  p.run('UsurperPlay.renderChron(__rows, __start, __cursor)');
}
const enter = (p) => p.run('UsurperPlay.drawStarted(1500)');
const lines = (p) => p.el('c-screen').children;
const cls = (e) => e.getAttribute('class');
const capKeys = (p) => p.el('c-keys').querySelectorAll('.kc').map((b) => b.getAttribute('data-key'));
const capTexts = (p) => p.el('c-keys').querySelectorAll('.kc').map((b) => b.querySelector('.cap').textContent);
const logText = (p) => p.el('c-body').querySelector('.clog').children.filter((e) => /\bev\b/.test(e.getAttribute('class'))).map((e) => e.textContent);
// A rule of the stylesheet from its selector to its closing brace.
function rule(sel) {
  const at = CSS.indexOf(sel + ' {');
  assert.ok(at !== -1, 'no rule ' + sel);
  return CSS.slice(at, CSS.indexOf('}', at) + 1);
}
const FRAME_CHARS = /[─━═│║┃┌┐└┘├┤╔╗╚╝╠╣]/;

// ---------------------------------------------------------------- R5, R14: boxes

const BOXES = [
  '╔══════════════════════════════════════╗',
  '║             MAIN STREET              ║',
  '╚══════════════════════════════════════╝',
  '┌─── TIP ──────────────────────────────',
  '│ Shop Tip',
  '│ Press S to Sell. Better equipment means better combat!',
  '└──────────────────────────────────────',
  '            │      █ Monsters',
  '██ ║ inner ║ ██',
  '  ╔═══════════════════╗',
  '  ║  [S] Flawless Victory   ║',
  '  ╠═══════════════════╣',
  '  ║  Total Rewards: +800 Gold ║',
  '  ╚═══════════════════╝',
];

test('R5, R14: a game box is drawn as a box: edges on its rule rows, both sides on every row, no loose bars', async () => {
  const p = await chronicle();
  screen(p, BOXES);
  const ln = lines(p);
  assert.strictEqual(ln.length, BOXES.length, 'one line per row, as before');
  assert.deepStrictEqual(ln.slice(0, 7).map(cls), ['ln bx bt bare', 'ln bx bs ctr', 'ln bx bb bare', 'ln bx bt', 'ln bx bs', 'ln bx bs', 'ln bx bb bare']);
  assert.strictEqual(ln[1].textContent, 'MAIN STREET', 'a centred title loses its bars and its padding');
  assert.strictEqual(ln[3].textContent, 'TIP', 'the title on the top edge');
  assert.strictEqual(ln[4].textContent, ' Shop Tip');
  // R14: the tip's rows have no right bar in the game's text; inside the box they take the box's width, so the right
  // edge is drawn
  assert.strictEqual(ln[4].getAttribute('data-bw'), ln[3].getAttribute('data-bw'));
  assert.strictEqual(ln[5].getAttribute('data-bw'), ln[3].getAttribute('data-bw'));
  for (let i = 0; i < 7; i++) assert.doesNotMatch(ln[i].textContent, FRAME_CHARS, 'no stray bar: ' + BOXES[i]);
  // a lone bar outside any box (a map's corridor) and a box drawn among art stay as printed
  assert.strictEqual(cls(ln[7]), 'ln');
  assert.strictEqual(ln[7].textContent, BOXES[7]);
  assert.strictEqual(cls(ln[8]), 'ln');
  assert.strictEqual(ln[8].textContent, BOXES[8]);
  // an indented box keeps its indent, and a middle edge
  assert.strictEqual(ln[9].getAttribute('data-bi'), '2');
  assert.deepStrictEqual(ln.slice(9).map(cls), ['ln bx bt bare', 'ln bx bs', 'ln bx bm bare', 'ln bx bs', 'ln bx bb bare']);
  assert.strictEqual(ln[10].textContent, '  [S] Flawless Victory   ');
  assert.match(rule('.chron .screen .ln.bx'), /border-left-width: 1px; border-right-width: 1px;/);
  assert.match(rule('.chron .screen .ln.bt'), /border-top-width: 1px;/);
  assert.match(rule('.chron .screen .ln.bb'), /border-bottom-width: 1px;/);
  assert.deepStrictEqual(p.errors, []);
});

test('R5: a box row whose box began above the screen keeps its bars only when it closes them; the frame colour goes to the edges', async () => {
  const p = await chronicle();
  screen(p, [[{ t: '║ ', c: 'k-cyn' }, { t: 'left over row', c: '' }, { t: ' ║', c: 'k-cyn' }], '│ an open bar with no box', [{ t: '╚══════════╝', c: 'k-cyn' }]]);
  const ln = lines(p);
  assert.strictEqual(cls(ln[0]), 'ln bx bs bk-cyn');
  assert.strictEqual(ln[0].textContent, ' left over row ');
  assert.strictEqual(cls(ln[1]), 'ln', 'an open bar outside a box is text');
  assert.strictEqual(cls(ln[2]), 'ln bx bb bare bk-cyn');
  assert.strictEqual(p.run('UsurperPlay.frameOf("── a heading ──")'), null, 'a heading rule with no corner is not a box');
});

// ---------------------------------------------------------------- R10, R11: the current screen

test('R10: the rows above the current screen are the last screen\'s, set quieter', async () => {
  const p = await chronicle();
  screen(p, ['old menu line', '[A] Armor', 'Your choice: W', 'WEAPON SHOP', 'Run by a troll.'], 3);
  assert.deepStrictEqual(lines(p).map((l) => /\bold\b/.test(cls(l))), [true, true, true, false, false]);
  assert.match(rule('.chron .screen .ln.old'), /opacity: \.5;/);
});

test('R11: the screen floor is the later of the last Enter\'s row and the row above the last clear', async () => {
  const p = await chronicle();
  assert.strictEqual(p.run('UsurperPlay.floorOf(10, 40)'), 39, 'a clear after the Enter: the screen starts at the clear');
  assert.strictEqual(p.run('UsurperPlay.floorOf(50, 40)'), 50, 'an Enter after the clear');
  assert.strictEqual(p.run('UsurperPlay.floorOf(-1, -1)'), -1);
  assert.strictEqual(p.run('UsurperPlay.floorOf(-1, 0)'), -1);
  assert.strictEqual(p.run('UsurperPlay.floorOf(null, "x")'), -1);
  // the clear's place in its chunk, so the chunk is written in two parts with the mark between them
  p.ctx.__b = Uint8Array.from([65, 0x1b, 0x5b, 0x32, 0x4a, 0x1b, 0x5b, 0x48, 66]);
  assert.strictEqual(p.run('UsurperPlay.seesClear(__b)'), true);
  assert.strictEqual(p.run('UsurperPlay.clearAt()'), 5);
  p.ctx.__b = Uint8Array.from([65, 66]);
  assert.strictEqual(p.run('UsurperPlay.seesClear(__b)'), false);
  assert.strictEqual(p.run('UsurperPlay.clearAt()'), -1);
  const code = readPage(PAGE);
  assert.match(code, /if \(cut >= 0\) terminal\.write\(bytes\.subarray\(0, cut\), markClear\);\n\s*terminal\.write\(cut >= 0 \? bytes\.subarray\(cut\) : bytes, /);
  assert.match(code, /var mark = given \? givenMark : screenFloor\(\);/);
  assert.match(code, /renderPad\(rowsFromBuffer\(terminal\.buffer && terminal\.buffer\.active, screenFloor\(\)\)/);
});

// ---------------------------------------------------------------- R9: the text page fits its rows

test('R9: a desktop text page sets its type to its longest row, so a table row never wraps; the window ends at a whole line', async () => {
  const p = await chronicle();
  const row = '  3. Hand Axe                   --   10 Axe        300  [War/Bar/Rng/Sha]';
  screen(p, ['  #  Name                      Lvl  Pow Type     Price Bonus', row, '[#] Buy item   [N] Next   [B] Back']);
  assert.strictEqual(p.el('c-screen').getAttribute('data-n'), String(row.length));
  assert.match(p.el('c-screen').getAttribute('class'), /\bfit\b/);
  screen(p, ['short', 'x'.repeat(130)]);
  assert.strictEqual(p.el('c-screen').getAttribute('data-n'), '80', 'a joined line longer than 80 columns wraps as prose');
  screen(p, ['short']);
  assert.strictEqual(p.el('c-screen').getAttribute('data-n'), '40');
  assert.match(CSS, /\.chron \.screen\.fit \{ font-size: clamp\(10px, calc\(\(100cqi - 18px\) \/ \(var\(--n, 80\) \* \.6\)\), 15px\); \}/);
  assert.match(readPage(PAGE), /function wholeLines\(pre\) \{/);
});

// ---------------------------------------------------------------- R8, X3, P5: labels

test('R8: a label longer than 40 ends at a word with an ellipsis; the cap keeps the whole label for its title and name', async () => {
  const p = await chronicle();
  const long = 'The Undercroft: the dark altars, and descend into the deep';
  const short = p.run('UsurperPlay.shortLabel(' + JSON.stringify(long) + ')');
  assert.match(short, /…$/);
  assert.ok(long.startsWith(short.slice(0, -1)), short);
  assert.ok(/\s/.test(long.charAt(short.length - 1)) || short.length - 1 === long.length, 'cut at a space: ' + short);
  assert.ok(short.length <= 41);
  assert.strictEqual(p.run('UsurperPlay.shortLabel("Remove Enchantment")'), 'Remove Enchantment');
  screen(p, ['[U] ' + long, '[R] Return']);
  const cap = p.el('c-keys').querySelectorAll('.kc')[0];
  assert.strictEqual(cap.querySelector('.lb').textContent, short);
  assert.strictEqual(cap.getAttribute('title'), long);
  assert.strictEqual(cap.getAttribute('aria-label'), 'U, ' + long);
  assert.strictEqual(p.el('c-keys').querySelectorAll('.kc')[1].getAttribute('title'), null, 'a whole label needs no title');
});

test('R8, X3, P5: labels are set in the book face, never broken inside a word, at most two lines', () => {
  const lb = rule('.chron .kc .lb');
  assert.match(lb, /font-family: var\(--book\);/);
  assert.match(lb, /overflow-wrap: normal; word-break: normal; hyphens: manual;/);
  assert.match(lb, /-webkit-line-clamp: 2;/);
  assert.match(lb, /font-variant-numeric: lining-nums;/);
  for (const m of CSS.matchAll(/\.kc \.lb[^{]*\{([^}]*)\}/g)) {
    assert.doesNotMatch(m[1], /var\(--display\)|hyphens:\s*auto|overflow-wrap:\s*(anywhere|break-word)/, m[0].slice(0, 80));
  }
});

// ---------------------------------------------------------------- X1, X2: caps

test('X1, X2: a digit or sign cap is set in the mono face; the shop\'s # cap reads No., names the number and sends nothing', async () => {
  const sent = [];
  const p = await chronicle({ WebSocket: socket(sent) });
  screen(p, ['[1] Rings   [%] Status   [A] Armor   [AUTO] Auto-Combat', '[#] Buy item   [N] Next   [B] Back']);
  const caps = p.el('c-keys').querySelectorAll('.cap');
  assert.deepStrictEqual(caps.map((c) => [c.textContent, cls(c)]), [['1', 'cap sym'], ['%', 'cap sym'], ['A', 'cap'], ['AUTO', 'cap wide'], ['No.', 'cap wide num'], ['N', 'cap'], ['B', 'cap']]);
  const num = p.el('c-keys').querySelectorAll('.kc')[4];
  assert.strictEqual(num.getAttribute('aria-label'), 'Type the number, then Enter: Buy item');
  p.click(num);
  assert.deepStrictEqual(sent, [], 'the # cap sends no #');
  p.click(p.el('c-keys').querySelectorAll('.kc')[5]);
  assert.deepStrictEqual(sent, ['N\r']);
  assert.match(rule('.chron .kc .cap.sym'), /font-family: var\(--term\);/);
});

// ---------------------------------------------------------------- N5, R15, B10: which keys

test('N5: three or more rows in two text columns read down the left column, then the right', async () => {
  const p = await chronicle();
  screen(p, ['  ═══ Shopping ═══                      ═══ Enchanting ═══', '',
    '  [1] Rings                             [E]nchant Equipment',
    '  [2] Necklaces                         [W] Remove Enchantment',
    '  [S]ell Accessories                    [C]urse Removal',
    '  [I]dentify Item', '',
    '  [H]ealing Potions                     [V] Love Spells',
    '  [M]ana Potions                        [K] Dark Arts',
    '  [D]ungeon Reset Scroll                [Y] Study Spells',
    '                                          [G] Scrying (NPC Info)', '',
    '  [T]alk to Ravanella                   [R]eturn to street', 'Your choice:']);
  assert.deepStrictEqual(capKeys(p), ['1', '2', 'S', 'I', 'E', 'W', 'C', 'H', 'M', 'D', 'V', 'K', 'Y', 'G', 'T', 'R']);
  // a packed row (three keys or more) keeps row order
  screen(p, [' [D]Dungeons     [I]Inn', ' [W]Weapon Shop  [A]Armor Shop   [M]Magic Shop', ' [1]Healer       [2]Quest Hall   [V]Level Master']);
  assert.deepStrictEqual(capKeys(p), ['D', 'I', 'W', 'A', 'M', '1', '2', 'V']);
});

test('R15: one key per action: the Inn\'s [S] Status stays, the quick [%] Status goes', async () => {
  const p = await chronicle();
  screen(p, ['[R] Return    [S] Status    [?] Help', 'Quick Commands: [%] Status   [R] Return   [*]Inventory    [?]Help', '[42hp 100st] The Inn >']);
  assert.deepStrictEqual(capKeys(p), ['R', 'S', '?', '*']);
});

const ROOM_MENU = { screen: 'dungeon_room', oneKey: true, groups: [{ label: 'Actions', items: [{ key: 'M', label: 'Map' }, { key: 'I', label: 'Inventory' }, { key: '=', label: 'Status' }, { key: 'Q', label: 'Leave dungeon' }] }] };
const ROOM_DETAIL = { name: 'Hall', level: 1, theme: 'Catacombs', themeId: 'Catacombs', header: 'Level 1 | Catacombs', danger: { rating: 1, max: 3 }, status: 'danger', exits: [], features: [] };

test('B10: a menu key shows as the key the screen prints for that action, and that key goes on one press', async () => {
  const sent = [];
  const p = await chronicle({ WebSocket: socket(sent) });
  frame(p, 'Room.Info', { num: 'room_1', name: 'Hall', area: 'Dungeon Floor 1' });
  screen(p, ['A hall.', 'Actions:', '  [M] Map  [*] Inventory  [%] Status  [Q] Leave dungeon', '[42hp 100st] Dungeon Fl.1 >'], 0, 3);
  frame(p, 'Room.Detail', ROOM_DETAIL);
  frame(p, 'Client.Menu', ROOM_MENU);
  assert.strictEqual(p.run('UsurperPlay.layout()'), 'room');
  assert.deepStrictEqual(capKeys(p), ['M', '*', '%', 'Q']);
  p.ctx.__k = { key: '*', repeat: false, isComposing: false, keyCode: 56, ctrlKey: false, metaKey: false, altKey: false, preventDefault() {} };
  p.run('document.getElementById("chron").listeners.keydown[0](Object.assign({ target: document.getElementById("chron") }, __k))');
  assert.deepStrictEqual(sent, ['*\r'], 'the shown key fires on one press');
  p.click(p.el('c-keys').querySelectorAll('.kc')[2]);
  assert.deepStrictEqual(sent, ['*\r', '%\r']);
  // a screen that prints no other key keeps the menu's
  screen(p, ['A hall.', '[42hp 100st] Dungeon Fl.1 >'], 0, 1);
  frame(p, 'Client.Menu', ROOM_MENU);
  assert.deepStrictEqual(capKeys(p), ['M', 'I', '=', 'Q']);
});

// ---------------------------------------------------------------- B6, N2, N8: Enter and how keys go

test('B6: on a screen that asks for Enter, a key printed in a box above (an achievement tag) is no key', async () => {
  const p = await chronicle({ WebSocket: socket([]) });
  screen(p, ['╔════════════════════════╗', '║  * 2 ACHIEVEMENTS UNLOCKED! *  ║', '╠════════════════════════╣',
    '║  [S] Flawless Victory  ║', '║  [B] First Blood       ║', '╚════════════════════════╝', '', 'The room is cleared!', 'Press Enter to continue...']);
  assert.deepStrictEqual(capKeys(p), []);
  assert.strictEqual(p.el('c-enterbar').hidden, false);
  assert.ok(p.el('c-enterbar').textContent.includes('Press Enter to continue...'));
  // the same in Spanish
  screen(p, ['║  [S] Victoria impecable  ║', 'Presiona Enter para continuar...']);
  assert.deepStrictEqual(capKeys(p), []);
});

test('B6: the Enter key shows only while the game waits for a line and the connection is open', async () => {
  const p = await chronicle({ WebSocket: socket([]) });
  frame(p, 'Client.Waiting', null);
  enter(p);
  screen(p, ['The stranger thanks you.', 'Your chivalry increases by 54!']);
  assert.strictEqual(p.el('c-enterbar').hidden, true, 'a line still being printed is no prompt');
  frame(p, 'Client.Waiting', null);
  assert.strictEqual(p.el('c-enterbar').hidden, false, 'the game now reads a line');
  frame(p, 'Client.Waiting', { poll: true });
  enter(p);
  screen(p, ['A key poll is no line read.']);
  assert.strictEqual(p.el('c-enterbar').hidden, true);
  const q = await chronicle({ WebSocket: socket([], 3) });
  screen(q, ['You wake with a gasp.', 'Press Enter to continue...']);
  assert.strictEqual(q.el('c-enterbar').hidden, true, 'a closed connection: no Enter key ("Connection closed")');
});

test('B6: the Enter prompts the page knows are the game\'s own, in every language', () => {
  const code = readPage(PAGE);
  const asks = JSON.parse('[' + /var ENTER_ASKS = \[([^\]]*)\];/.exec(code)[1].replace(/'/g, '"') + ']');
  const want = [];
  for (const l of LANGS) for (const k of ['ui.press_enter', 'ui.press_any_key']) want.push(gameLang(l)[k].replace(/\.+$/, ''));
  assert.deepStrictEqual([...asks].sort(), [...new Set(want)].sort());
});

test('N8: a prompt the game ends with a line end is this screen\'s: the map shows its [P] key and Enter', async () => {
  const p = await chronicle({ WebSocket: socket([]) });
  const rows = ['old screen', '[42hp 100st] Dungeon Fl.1 >', ' DUNGEON MAP', '   [P] Persistent mini-map: OFF', '   Press Enter to continue...', ''];
  p.ctx.__buf = { baseY: 0, cursorY: rows.length - 1, getLine: (y) => (rows[y] === undefined ? undefined : { translateToString: () => rows[y] }) };
  assert.deepStrictEqual(JSON.parse(p.run('JSON.stringify(UsurperPlay.rowsFromBuffer(__buf, -1))')), rows.slice(2), 'the blank cursor row does not end the read');
  screen(p, rows.slice(2));
  assert.deepStrictEqual(capKeys(p), ['P']);
  assert.strictEqual(p.el('c-enterbar').hidden, false);
});

test('N2: under the keys, how they reach the game: one press on a one-key menu, the key and Enter elsewhere', async () => {
  const p = await chronicle();
  frame(p, 'Room.Info', { num: 'room_1', name: 'Hall', area: 'Dungeon Floor 1' });
  screen(p, ['A hall.']);
  frame(p, 'Room.Detail', ROOM_DETAIL);
  frame(p, 'Client.Menu', ROOM_MENU);
  assert.strictEqual(p.el('c-keys').querySelector('.howto').textContent, lang('en')['play.chron.keys_one']);
  frame(p, 'Client.Menu', Object.assign({}, ROOM_MENU, { oneKey: false }));
  assert.strictEqual(p.el('c-keys').querySelector('.howto').textContent, lang('en')['play.chron.keys_enter']);
  enter(p);
  screen(p, ['[1] Male  [2] Female:']);
  assert.strictEqual(p.el('c-keys').querySelector('.howto').textContent, lang('en')['play.chron.keys_enter']);
  screen(p, ['Nothing to pick here.', 'Press Enter to continue...']);
  assert.strictEqual(p.el('c-keys').querySelector('.howto'), null, 'no keys, no line about them');
});

// ---------------------------------------------------------------- R7: the fight log

test('R7: the fight log leaves out a tip\'s edges and title and a heading between rules, and keeps the tip\'s lines', async () => {
  const p = await chronicle();
  frame(p, 'Char.Vitals', { hp: 40, maxHp: 40, mp: 0, maxMp: 0, combatSp: 70, maxCombatSp: 70 });
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Pixie', level: 1, hp: 43, maxHp: 43 }] });
  frame(p, 'Client.Menu', { screen: 'combat', oneKey: true, round: 1, groups: [{ label: 'Actions', items: [{ key: 'A', label: 'Attack' }] }] });
  screen(p, ['═══ COMBAT ═══', '', 'The Pixie says: "Let\'s play a game..."', '', '┌─── TIP ─────────────────', '│ Combat Tip', '│ Press [A] to Attack, [R] to Retreat.', '└─────────────────', '', 'Choose action: ']);
  assert.deepStrictEqual(logText(p), ['The Pixie says: "Let\'s play a game..."', 'Combat Tip', 'Press [A] to Attack, [R] to Retreat.']);
});

// ---------------------------------------------------------------- B12, C4, C5, R12: the fight panels

test('B12: the enemy chip comes from rank when the frame has it, and from isBoss when it does not', async () => {
  const p = await chronicle();
  frame(p, 'Char.Combat.Start', { enemies: [
    { name: 'Orc [CHAMPION]', level: 3, hp: 9, maxHp: 9, isBoss: true, rank: 'champion' },
    { name: 'Sentinel', level: 3, hp: 9, maxHp: 9, isBoss: true, rank: 'boss' },
    { name: 'Goblin', level: 3, hp: 9, maxHp: 9, isBoss: true, rank: 'normal' },
    { name: 'Old Boss', level: 3, hp: 9, maxHp: 9, isBoss: true },
    { name: 'Rat', level: 1, hp: 9, maxHp: 9, isBoss: true, rank: 'odd' }] });
  const chips = p.el('c-hud').querySelectorAll('.hcol')[1].querySelectorAll('.card').map((c) => c.querySelectorAll('.chipc').map((x) => x.textContent).join(','));
  assert.deepStrictEqual(chips, ['Champion', 'Boss', '', 'Boss', 'Boss'], 'a rank the page does not know is no rank: isBoss decides');
  const tags = p.el('enemy-list').querySelectorAll('.unit').map((u) => u.querySelectorAll('.tag').map((x) => x.textContent).filter((x) => !/^Lv/.test(x)).join(','));
  assert.deepStrictEqual(tags, ['Champion', 'Boss', '', 'Boss', 'Boss'], 'the drawer says the same');
});

test('C4: at a quarter of the HP or less, the card, its rule and the Classic meter say Low HP in words', async () => {
  const p = await chronicle();
  frame(p, 'Char.Status', { name: 'Brannoc', class: 'Warrior', level: 2, gold: 1, xp: 1 });
  frame(p, 'Char.Vitals', { hp: 11, maxHp: 45, mp: 0, maxMp: 0, combatSp: 70, maxCombatSp: 78 });
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Goblin', level: 3, hp: 40, maxHp: 58 }] });
  frame(p, 'Char.Combat.Party', { members: [{ name: 'Brannoc', level: 2, hp: 11, maxHp: 45, isAlive: true, isPlayer: true }, { name: 'Mira', level: 2, hp: 12, maxHp: 45, isAlive: true }] });
  const cards = p.el('c-hud').querySelectorAll('.hcol')[0].querySelectorAll('.card');
  assert.match(cls(cards[0]), /\blow\b/);
  assert.strictEqual(cards[0].querySelector('.chipc.low').textContent, 'Low HP');
  assert.match(cls(cards[0].querySelector('.m')), /\blow\b/);
  assert.doesNotMatch(cls(cards[1]), /\blow\b/, '12 of 45 is above a quarter');
  assert.strictEqual(p.el('hp-low').hidden, false);
  assert.match(cls(p.el('hp-meter')), /\blow\b/);
  assert.deepStrictEqual(p.el('party-list').querySelectorAll('.unit').map((u) => u.querySelectorAll('.tag.low').map((x) => x.textContent).join()), ['Low HP', ''], 'the drawer too');
  frame(p, 'Char.Vitals', { hp: 0, maxHp: 45, mp: 0, maxMp: 0, combatSp: 0, maxCombatSp: 78 });
  assert.strictEqual(p.el('hp-low').hidden, true, 'at 0 the player is down, not low');
  frame(p, 'Char.Vitals', { hp: 30, maxHp: 45, mp: 0, maxMp: 0, combatSp: 70, maxCombatSp: 78 });
  assert.strictEqual(p.el('hp-low').hidden, true);
  assert.match(rule('.chron .card.low'), /border-color: var\(--hp-ink\);/);
});

test('C5: a death ends the fight page: the death text is the text page, with no round marker', async () => {
  const p = await chronicle();
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Goblin', level: 3, hp: 40, maxHp: 58 }] });
  frame(p, 'Client.Menu', { screen: 'combat', oneKey: true, round: 2, groups: [{ label: 'Actions', items: [{ key: 'A', label: 'Attack' }] }] });
  screen(p, ['The goblin hits you!', 'Choose action: ']);
  assert.strictEqual(p.run('UsurperPlay.layout()'), 'fight');
  assert.strictEqual(p.el('combat-round').textContent, 'Round 2');
  frame(p, 'Char.Death', { killer: 'Goblin' });
  assert.strictEqual(p.run('UsurperPlay.layout()'), 'text');
  enter(p);
  screen(p, ['You were slain by Goblin.', 'Press Enter to continue...']);
  assert.strictEqual(p.el('c-body').querySelector('.rd'), null);
  assert.ok(p.el('c-screen').textContent.includes('You were slain by Goblin.'));
  assert.strictEqual(p.el('combat-round').textContent, '');
});

test('R12: the Combat button\'s dot goes when the fight ends', async () => {
  const p = await chronicle();
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Goblin', level: 3, hp: 40, maxHp: 58 }] });
  assert.match(cls(p.el('rail-combat')), /\bnew\b/);
  frame(p, 'Char.Combat.End', { outcome: 'Victory', kills: 1, xpGained: 1, goldGained: 1 });
  assert.doesNotMatch(cls(p.el('rail-combat')) || '', /\bnew\b/);
});

// ---------------------------------------------------------------- P4: the street's text

test('P4: the street\'s text leaves out its Online heading, the status line and the quick keys, which join the dock', async () => {
  const p = await chronicle();
  frame(p, 'Client.Footer', { hp: 44, maxHp: 45, potions: 20, maxPotions: 21 });
  frame(p, 'Room.Info', { num: 1, name: 'Main Street', area: 'Usurper Reborn' });
  screen(p, ['You are standing on the main street.', ' [D]Dungeons     [I]Inn', ' [S]Status', '', ' ═══ Online ═══', ' [3]Who\'s Online (1 player)', '',
    'HP: 44/45 | Gold: 3,788 | Stamina: 100/78 | Level 2 | Lives: 3/3 (0%)', '',
    'Quick Commands: [%] Status   [*]Inventory    [?]Help    [0] Talk (4)  [~]Prefs', '                [/]Cmds    [!]Bug', '',
    '│ The Quest Hall (Main Street: Quest Hall [2]) offers bounties', '[44hp 100st] Main Street >'], 0, 13);
  frame(p, 'Client.Menu', { screen: 'main_street_classic', oneKey: true, groups: [
    { label: 'Places', items: [{ key: 'D', label: 'Dungeons' }, { key: 'I', label: 'Inn' }, { key: '2', label: 'Quest Hall' }, { key: 'S', label: 'Status' }] },
    { label: 'Online', items: [{ key: '3', label: 'Who\'s Online' }] }, { label: 'System', items: [{ key: '~', label: 'Settings' }] }] });
  assert.strictEqual(p.run('UsurperPlay.layout()'), 'street');
  const text = p.el('c-body').querySelector('.screen').textContent;
  for (const gone of ['Online', 'Quick Commands', 'Stamina: 100/78', '[/]Cmds']) assert.ok(!text.includes(gone), gone);
  assert.ok(text.includes('The Quest Hall (Main Street: Quest Hall [2]) offers bounties'), 'a sentence naming a key stays');
  const groups = p.el('c-keys').querySelectorAll('.kgroup');
  assert.strictEqual(groups[groups.length - 1].getAttribute('aria-label'), 'More keys');
  assert.deepStrictEqual(groups[groups.length - 1].querySelectorAll('.kc').map((b) => b.getAttribute('data-key')), ['*', '?', '0', '/', '!']);
});

// ---------------------------------------------------------------- A2, A3: art

test('A2: a place with no scene of its own takes the nearest one, or a plain plate with its name; never the class portrait', async () => {
  const p = await chronicle();
  const bg = () => p.el('c-frame').querySelector('.scene').style.backgroundImage || '';
  frame(p, 'Char.Status', { name: 'Brannoc', class: 'Warrior', level: 2, gold: 1, xp: 1 });
  frame(p, 'Room.Info', { num: 18, name: 'Armor Shop', area: 'Usurper Reborn' });
  screen(p, ['ARMOR SHOP']);
  assert.strictEqual(bg(), 'url("/play/art/scenes/weapon-shop.png")');
  frame(p, 'Room.Info', { num: 7, name: 'Magic Shop', area: 'Usurper Reborn' });
  assert.strictEqual(bg(), '');
  assert.strictEqual(p.el('c-frame').querySelector('.place-name').textContent, 'Magic Shop');
  assert.strictEqual(p.el('c-cap').textContent, 'Plate: Magic Shop');
  assert.strictEqual(p.el('c-frame').querySelectorAll('img').length, 0);
});

test('A3: a monster drawn apart from its family is matched by its shown name in every language, as whole words', async () => {
  const p = await chronicle();
  const by = (n) => p.run('UsurperPlay.spriteByName(' + JSON.stringify(n) + ')');
  assert.strictEqual(by('Kobold'), 'goblin');
  assert.strictEqual(by('Coboldo [CAMPIONE]'), 'goblin');
  assert.strictEqual(by('Zombie Revenant'), '');
  assert.strictEqual(by('Zombi'), '');
  assert.strictEqual(by('Alpha Werewolf'), 'wolf-beast');
  assert.strictEqual(by('Vérfarkas'), 'wolf-beast');
  assert.strictEqual(by('Pixie'), '');
  assert.strictEqual(by('Folletto'), '');
  assert.strictEqual(by('Lich'), 'wraith');
  assert.strictEqual(by('Goblin'), null, 'the family decides the rest');
  assert.strictEqual(by('Spriteling'), null, 'whole words only');
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Kobold', hp: 1, maxHp: 1 }, { name: 'Alpha Wolf', hp: 1, maxHp: 1 }, { name: 'Zombie', hp: 1, maxHp: 1 }, { name: 'Ghoul', hp: 1, maxHp: 1 }] });
  frame(p, 'Client.Enemies', { enemies: [{ name: 'Kobold', family: 'Draconic' }, { name: 'Alpha Wolf', family: 'Beast' }, { name: 'Zombie', family: 'Undead' }, { name: 'Ghoul', family: 'Undead' }] });
  const row = p.el('c-frame').querySelector('.foes-in');
  assert.deepStrictEqual(row.children.map((c) => (c.tagName === 'IMG' ? c.getAttribute('src') : c.textContent)),
    ['/play/art/monsters/goblin.png', 'Alpha Wolf', 'Zombie', 'Ghoul']);
});

test('A3: every name the sprite table matches is the game\'s own name for that monster, and every sprite is a file', () => {
  const code = readPage(PAGE);
  const table = /var SPRITE_NAMES = \[([\s\S]*?)\n {4}\];/.exec(code)[1];
  const ids = { Zombie: 'zombie', Ghoul: 'ghoul', Wraith: 'wraith', Lich: 'lich', Kobold: 'kobold', Werewolf: 'werewolf', Pixie: 'pixie', Sprite: 'sprite',
    Treant: 'treant', 'Spider Queen': 'spider_queen', Broodmother: 'broodmother' };
  for (const m of table.matchAll(/\['([a-z-]*)', \[([^\]]*)\]\]/g)) {
    const names = JSON.parse('[' + m[2].replace(/'/g, '"') + ']');
    const id = ids[names[0]];
    assert.ok(id, names[0]);
    assert.deepStrictEqual([...new Set(names)].sort(), [...new Set(LANGS.map((l) => gameLang(l)['monster.name.' + id]))].sort(), id);
    if (m[1]) assert.ok(fs.existsSync(path.join(WEB, 'play', 'art', 'monsters', m[1] + '.png')), m[1]);
  }
});

// ---------------------------------------------------------------- R6: the room

test('R6: the room text is never cut: the prose takes its whole height and the right page scrolls', () => {
  assert.match(rule('.chron .cbody.b-room > .prose'), /flex: none; min-height: 0; overflow: visible;/);
  assert.match(rule('.chron[data-layout="room"] .recto'), /overflow-y: auto;/);
});

// ---------------------------------------------------------------- strings

test('the new strings are in all five languages, translated, with no dashes', () => {
  const keys = ['play.combat.champion', 'play.chron.low_hp', 'play.chron.type_number', 'play.chron.cap_number', 'play.chron.more_keys', 'play.chron.keys_one', 'play.chron.keys_enter'];
  const en = lang('en');
  for (const l of LANGS) {
    const d = lang(l);
    for (const k of keys) {
      assert.ok(typeof d[k] === 'string' && d[k].trim(), l + ' ' + k);
      assert.doesNotMatch(d[k], /[–—]/, l + ' ' + k);
      if (l !== 'en' && !(l === 'fr' && k === 'play.combat.champion')) assert.notStrictEqual(d[k], en[k], l + ' ' + k);
    }
    assert.match(d['play.chron.type_number'], /\{0\}/);
  }
});
