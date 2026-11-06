'use strict';
// web/play/index.html and chronicle.css: the play page items of the 1.3.11 list.
// B6, N8: every wording the game prints that asks for Enter or any key is known to the page as an Enter screen.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { loadPage } = require('./harness');

const PAGE = 'play/index.html';
const LANGS = ['en', 'es', 'fr', 'hu', 'it'];
const WEB = path.join(__dirname, '..');
const ROOT = path.join(WEB, '..');
const lang = (l) => JSON.parse(fs.readFileSync(path.join(WEB, 'lang', l + '.json'), 'utf8'));
const gameLang = (l) => JSON.parse(fs.readFileSync(path.join(ROOT, 'Localization', l + '.json'), 'utf8'));

async function load(opts = {}) {
  const p = await loadPage(PAGE, Object.assign({ fetch: (url) => (url.startsWith('/lang/') ? lang('en') : url === '/api/languages' ? LANGS : {}) }, opts));
  await p.settle();
  return p;
}

// A value that starts with a press verb and names Enter or a key is a prompt for it. The key hints in brackets
// ("Press [A] to Attack"), the text about Ctrl and a hint of two sentences are help text, not a wait.
const VERB = /^\[?\s*(press|presiona|pulsa|appuyez|appuie|nyomj|nyomd|premi)\b/i;
const NAMES_KEY = /enter|entr|intro|invio|key|tecla|touche|tasto|gomb|billenty/i;
const HINT = /\[[^\]\s]{1,4}\]|[Cc]trl\+|\b[Uu] para\b|\. [A-Z]/;
// The one wording that names Enter and a Q to quit: a menu line, not a plain Enter screen.
const MENU_Q = /\bQ\b/;

test('B6, N8: no wording that asks for Enter or any key in a language file is missed by the page', async () => {
  const p = await load();
  const asked = [];
  const missed = [];
  const menus = [];
  for (const l of LANGS) {
    for (const [k, v] of Object.entries(gameLang(l))) {
      if (typeof v !== 'string' || v.includes('\n')) continue;
      const s = v.trim();
      const body = s.replace(/^\[/, '');
      if (!VERB.test(s) || !NAMES_KEY.test(s) || HINT.test(body)) continue;
      p.ctx.__w = s;
      const got = p.run('UsurperPlay.asksEnter(__w)');
      if (MENU_Q.test(s)) { menus.push([l, k, got]); continue; }
      asked.push(s);
      if (!got) missed.push(l + ' ' + k + ': ' + s);
    }
  }
  assert.deepStrictEqual(missed, [], 'the page does not read these as an Enter screen');
  assert.ok(asked.length > 150, 'the scan found the wordings: ' + asked.length);
  assert.ok(menus.length >= 4 && menus.every((m) => m[2] === false), 'an Enter or Q line is a menu: ' + JSON.stringify(menus));
  for (const w of ['Nyomj Entert a folytatáshoz...', 'Nyomj ENTER-t a folytatáshoz...', 'Nyomj egy gombot...', 'Pulsa Enter para continuar...', 'Appuie sur Entrée pour commencer le combat...']) {
    p.ctx.__w = w;
    assert.strictEqual(p.run('UsurperPlay.asksEnter(__w)'), true, w);
  }
  for (const w of ['Choose action: ', 'Pulsa [A] para Atacar.', 'Press [F] to Fight the monsters in a room. In combat:', '(Press Enter on a blank line to cancel.)']) {
    p.ctx.__w = w;
    assert.strictEqual(p.run('UsurperPlay.asksEnter(__w)'), false, w);
  }
});

// ---------------------------------------------------------------- K2: 256 colours and true colour

// A buffer row of cells: [char, {fg: [mode, value], bg: [mode, value], bold}], modes 'p' palette, 'r' rgb, 'd' default.
function cellsLine(cells) {
  return {
    length: cells.length,
    isWrapped: false,
    translateToString: () => cells.map((c) => c[0]).join(''),
    getCell(x) {
      const [ch, o] = cells[x];
      const fg = (o && o.fg) || ['d', 0];
      const bg = (o && o.bg) || ['d', 0];
      return {
        getWidth: () => 1, getChars: () => ch, isBold: () => !!(o && o.bold),
        isFgPalette: () => fg[0] === 'p', isFgRGB: () => fg[0] === 'r', getFgColor: () => fg[1],
        isBgPalette: () => bg[0] === 'p', isBgRGB: () => bg[0] === 'r', getBgColor: () => bg[1],
      };
    },
  };
}
function bufferOf(lines) {
  return { baseY: 0, cursorY: lines.length - 1, cursorX: 0, getLine: (y) => lines[y] };
}
const runsRead = (p, lines) => {
  p.ctx.__buf = bufferOf(lines);
  return JSON.parse(p.run('JSON.stringify(UsurperPlay.screenOf(__buf, -1).rows)'));
};

test('K2: a cell in 256 colours or true colour keeps its colour, foreground and background; the 16 colours stay the inks', async () => {
  const p = await load({ storage: { playView: 'chronicle' } });
  const rows = runsRead(p, [cellsLine([
    ['a', { fg: ['p', 196] }], ['b', { fg: ['p', 21], bg: ['p', 226] }], ['c', { fg: ['p', 244] }], ['d', { fg: ['r', 0x12ab9f], bg: ['r', 0x010203] }],
    ['e', { fg: ['p', 1] }], ['f', { fg: ['p', 15] }], ['g', { fg: ['p', 5], bg: ['p', 17] }], ['h'],
  ])]);
  assert.deepStrictEqual(rows, [[
    { t: 'a', c: '', fg: '#ff0000' },
    { t: 'b', c: '', fg: '#0000ff', bg: '#ffff00' },
    { t: 'c', c: '', fg: '#808080' },
    { t: 'd', c: '', fg: '#12ab9f', bg: '#010203' },
    { t: 'e', c: 'k-red' },
    { t: 'f', c: 'k-wht' },
    { t: 'g', c: 'k-pur', bg: '#00005f' },
    { t: 'h', c: '' },
  ]]);
});

test('K2: the page draws a run\'s own colours, and refuses a value that is not a colour', async () => {
  const p = await load({ storage: { playView: 'chronicle' } });
  p.ctx.__rows = [[{ t: 'hot', c: '', fg: '#ff0000', bg: '#00005f' }, { t: 'x', c: '', fg: 'red; background:url(//e)', bg: 'javascript:1' }, { t: 'plain', c: 'k-grn' }]];
  p.run('UsurperPlay.renderChron(__rows, 0, -1)');
  const spans = p.el('c-screen').children[0].children;
  assert.strictEqual(spans[0].style.color, '#ff0000');
  assert.strictEqual(spans[0].style.backgroundColor, '#00005f');
  assert.strictEqual(spans[1].style.color, undefined);
  assert.strictEqual(spans[1].style.backgroundColor, undefined);
  assert.strictEqual(spans[2].getAttribute('class'), 'k-grn');
});

test('B6: the two wordings the old exact list pinned (ui.press_enter, ui.press_any_key) stay in the widened match, in every language', async () => {
  const p = await load();
  for (const l of LANGS) {
    for (const k of ['ui.press_enter', 'ui.press_any_key']) {
      const w = gameLang(l)[k];
      assert.strictEqual(typeof w, 'string', l + ' ' + k);
      p.ctx.__w = w;
      assert.strictEqual(p.run('UsurperPlay.asksEnter(__w)'), true, l + ' ' + k + ': ' + w);
    }
  }
});
