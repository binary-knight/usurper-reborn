'use strict';
// web/play/index.html: the full page terminal at /play/. GMCP values come from other players and
// from the game, so every one is drawn as text.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { HOSTILE, loadPage, readPage, pageScripts } = require('./harness');

const PAGE = 'play/index.html';
const LANGS = ['en', 'es', 'fr', 'hu', 'it'];
const lang = (l) => JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'lang', l + '.json'), 'utf8'));

function descendants(el, out = []) {
  for (const c of el.children || []) { out.push(c); descendants(c, out); }
  return out;
}
async function load() {
  const p = await loadPage(PAGE, { fetch: (url) => (url.startsWith('/lang/') ? lang('en') : url === '/api/languages' ? LANGS : {}) });
  await p.settle();
  return p;
}
function frame(p, gmcp, data) {
  p.ctx.__f = JSON.stringify({ gmcp, data });
  p.run('UsurperPlay.handleFrame(__f)');
}

test('the page script never writes HTML', () => {
  const code = pageScripts(readPage(PAGE)).filter((s) => s.code).map((s) => s.code).join('\n');
  assert.ok(code.length > 1000);
  for (const sink of ['innerHTML', 'outerHTML', 'insertAdjacentHTML', 'document.write', 'eval(', 'new Function']) {
    assert.ok(!code.includes(sink), sink);
  }
  assert.ok(!readPage(PAGE).includes('data-i18n-html'));
});

test('the page opens the split stream and pins xterm 5.5.0', () => {
  const html = readPage(PAGE);
  assert.match(html, /new WebSocket\(proto \+ '\/\/' \+ location\.host \+ '\/ws\?gmcp=1'\)/);
  assert.match(html, /<script src="https:\/\/cdn\.jsdelivr\.net\/npm\/@xterm\/xterm@5\.5\.0\/lib\/xterm\.min\.js"><\/script>/);
  assert.match(html, /<link rel="stylesheet" href="https:\/\/cdn\.jsdelivr\.net\/npm\/@xterm\/xterm@5\.5\.0\/css\/xterm\.css">/);
});

test('a chat line with HTML stays plain text', async () => {
  const p = await load();
  assert.deepStrictEqual(p.errors, []);
  frame(p, 'Comm.Channel.Text', { channel: 'gossip', talker: HOSTILE, text: HOSTILE });
  frame(p, 'Comm.Channel.Text', { channel: '<b>x</b>', talker: 'Ana', text: 'hi' });
  p.run("UsurperPlay.setDrawer('chat')");
  const list = p.el('chat-list');
  assert.strictEqual(list.children.length, 2);
  assert.ok(list.textContent.includes(HOSTILE));
  assert.ok(list.textContent.includes('<b>x</b>'));
  const tags = descendants(list).map((e) => e.tagName);
  assert.ok(tags.every((t) => t === 'DIV' || t === 'SPAN'), tags.join(','));
  assert.deepStrictEqual(p.errors, []);
});

test('combat names, the room and the killer stay plain text', async () => {
  const p = await load();
  const HOSTILE = '<img src=x onerror=alert(2)><script>a()</script>&amp;'; // under the 60 character name cap
  frame(p, 'Room.Info', { name: HOSTILE });
  frame(p, 'Char.Combat.Start', { enemies: [{ name: HOSTILE, level: 3, hp: 10, maxHp: 20, isBoss: true }] });
  frame(p, 'Char.Combat.Party', { members: [{ name: HOSTILE, hp: 5, maxHp: 9, isAlive: true }] });
  frame(p, 'Char.Death', { killer: HOSTILE });
  assert.strictEqual(p.el('room').textContent, HOSTILE);
  for (const id of ['enemy-list', 'party-list', 'combat-result']) {
    assert.ok(p.el(id).textContent.includes(HOSTILE), id);
    const tags = descendants(p.el(id)).map((e) => e.tagName);
    assert.ok(tags.every((t) => ['DIV', 'SPAN', 'I', 'B'].includes(t)), id + ': ' + tags.join(','));
  }
  assert.deepStrictEqual(p.errors, []);
});

test('a fight opens the combat drawer; a kill marks that enemy dead; the end shows the result', async () => {
  const p = await load();
  assert.strictEqual(p.el('drawer').hidden, true);
  frame(p, 'Char.Combat.Start', { enemies: [{ name: 'Zombie', level: 1, hp: 65, maxHp: 65 }, { name: 'Zombie', level: 1, hp: 65, maxHp: 65 }] });
  assert.strictEqual(p.el('drawer').hidden, false);
  assert.strictEqual(p.el('pane-combat').hidden, false);
  frame(p, 'Char.Combat.Enemies', { enemies: [{ name: 'Zombie', level: 1, hp: 27, maxHp: 65, alive: true }, { name: 'Zombie', level: 1, hp: 65, maxHp: 65, alive: true }], round: 1 });
  frame(p, 'Char.Combat.Killed', { name: 'Zombie', level: 1 });
  const rows = p.el('enemy-list').children;
  assert.ok(rows[0].classList.contains('out'));
  assert.ok(rows[0].textContent.includes('0/65'));
  assert.ok(!rows[1].classList.contains('out'));
  assert.strictEqual(p.el('combat-round').textContent, 'Round 1');
  frame(p, 'Char.Combat.End', { outcome: 'Victory', kills: 1, xpGained: 45, goldGained: 73 });
  assert.strictEqual(p.el('combat-result').hidden, false);
  assert.ok(p.el('combat-result').textContent.startsWith('Victory'));
  assert.ok(p.el('combat-result').textContent.includes('45 XP, 73 gold. Kills: 1'));
});

test('chat while the drawer is closed marks the Chat button; opening chat clears it', async () => {
  const p = await load();
  frame(p, 'Comm.Channel.Text', { channel: 'tell', talker: 'Ana', text: 'psst' });
  assert.ok(p.el('rail-chat').classList.contains('new'));
  p.run("UsurperPlay.setDrawer('chat')");
  assert.ok(!p.el('rail-chat').classList.contains('new'));
  assert.strictEqual(p.el('chat-list').children.length, 1);
});

test('vitals, fight stamina, level and gold come from GMCP; the Stamina stat (sp) is not drawn', async () => {
  const p = await load();
  assert.strictEqual(p.el('hp-meter').hidden, true);
  frame(p, 'Char.Vitals', { hp: 38, maxHp: 44, mp: 0, maxMp: 0, sp: 13 });
  frame(p, 'Char.Status', { level: 2, gold: 3073 });
  assert.strictEqual(p.el('hp-meter').hidden, false);
  assert.strictEqual(p.el('mp-meter').hidden, true, 'no MP bar without max MP');
  assert.strictEqual(p.el('sp-wrap').hidden, true, 'a server without combatSp leaves stamina hidden');
  assert.strictEqual(p.el('hp-num').textContent, '38/44');
  assert.strictEqual(p.el('level').textContent, 'Level 2');
  assert.ok(!p.el('vitals').textContent.includes('13'));
  frame(p, 'Char.Vitals', { hp: 38, maxHp: 44, mp: 0, maxMp: 0, sp: 13, combatSp: 100, maxCombatSp: 99 });
  assert.strictEqual(p.el('sp-wrap').hidden, false);
  assert.strictEqual(p.el('sp-num').textContent, '100/99');
  assert.ok(!p.el('vitals').textContent.includes('13'));
});

test('bad frames are ignored', async () => {
  const p = await load();
  for (const f of ['not json', '[]', 'null', '{"gmcp":5}', '{"gmcp":"Char.Vitals","data":"x"}', '{"gmcp":"Comm.Channel.Text","data":[1]}']) {
    p.ctx.__f = f;
    p.run('UsurperPlay.handleFrame(__f)');
  }
  assert.deepStrictEqual(p.errors, []);
});

test('odd field types are dropped at the door, and later frames still draw', async () => {
  const p = await load();
  const odd = [{ toString: null }, { valueOf: null, toString: null }, [1, 2], { a: 1 }, null, true, 'NaN', Infinity, NaN];
  // Straight to the handler, past the frame catch, so each field check is what keeps it from throwing.
  for (const v of odd) {
    for (const [pkg, data] of [
      ['Char.Vitals', { hp: v, maxHp: v, mp: v, maxMp: v }],
      ['Char.Status', { level: v, gold: v }],
      ['Room.Info', { name: v }],
      ['Char.Combat.Start', { enemies: [{ name: v, level: v, hp: v, maxHp: v }, v] }],
      ['Char.Combat.Enemies', { enemies: v, round: v }],
      ['Char.Combat.Party', { members: [{ name: v, hp: v, maxHp: v, isAlive: v }] }],
      ['Char.Combat.Killed', { name: v }],
      ['Char.Combat.End', { outcome: v, kills: v, xpGained: v, goldGained: v }],
      ['Char.Death', { killer: v }],
      ['Comm.Channel.Text', { channel: v, talker: v, text: v }],
    ]) {
      p.ctx.__p = pkg;
      p.ctx.__d = data;
      p.run('UsurperPlay.handleGmcp(__p, __d)');
    }
  }
  // The real path: a frame whose nested value is an object with toString set to null.
  p.run(`UsurperPlay.handleFrame('{"gmcp":"Char.Vitals","data":{"hp":{"toString":null},"maxHp":10}}')`);
  p.run(`UsurperPlay.handleFrame('{"gmcp":"Char.Combat.End","data":{"outcome":{"toString":null},"xpGained":{"valueOf":null,"toString":null}}}')`);
  p.run(`UsurperPlay.handleFrame('{"gmcp":"Comm.Channel.Text","data":{"channel":{"toString":null},"talker":[1],"text":{"a":1}}}')`);
  assert.deepStrictEqual(p.errors, []);
  frame(p, 'Char.Vitals', { hp: 5, maxHp: 10, mp: 0, maxMp: 0 });
  frame(p, 'Room.Info', { name: 'Main Street' });
  frame(p, 'Char.Combat.End', { outcome: 'Victory', kills: 1, xpGained: 2, goldGained: 3 });
  assert.strictEqual(p.el('hp-num').textContent, '5/10');
  assert.strictEqual(p.el('room').textContent, 'Main Street');
  assert.ok(p.el('combat-result').textContent.includes('2 XP, 3 gold. Kills: 1'));
});

test('every play key the page uses is in all five languages', () => {
  const html = readPage(PAGE);
  const used = new Set();
  for (const m of html.matchAll(/(?:data-i18n="|t\('|setConn\(')(play\.[A-Za-z0-9_.]+)/g)) used.add(m[1]);
  for (const o of ['Victory', 'PlayerDied', 'PlayerEscaped', 'Stalemate', 'Interrupted', 'OpponentSpared']) used.add('play.result.' + o);
  for (const c of ['tell', 'gossip', 'shout', 'guild']) used.add('play.chat.channel.' + c);
  assert.ok(used.size > 30);
  const en = lang('en');
  const enKeys = Object.keys(en).filter((k) => k.startsWith('play.')).sort();
  assert.deepStrictEqual(enKeys, [...used].sort(), 'en play keys match the page');
  for (const l of LANGS) {
    const d = lang(l);
    for (const k of used) assert.ok(typeof d[k] === 'string' && d[k].length > 0, l + ' ' + k);
    for (const k of used) assert.strictEqual((d[k].match(/\{\d\}/g) || []).sort().join(), (en[k].match(/\{\d\}/g) || []).sort().join(), l + ' placeholders ' + k);
  }
});
