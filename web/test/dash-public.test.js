'use strict';
// The dashboard's children list is public: web/dash-public.js sends an allow-list only.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { publicChildren } = require('../dash-public');

// Shaped as the game writes it (OnlineStateManager.SaveSharedChildrenNow, camelCase): children[] for the page,
// childrenRaw[] (FamilySystem.SerializeChildren) in the same order with the parents' Character.IDs, royal as an
// integer (0 none, 1 half, 2 full). Private values sit in every place they can: a player parent, a player who
// shares an NPC's name, kidnap state and location, and childrenRaw with ids, kidnapper and ransom.
const WRAPPER = {
  count: 3,
  updatedAt: '2026-10-31T07:00:00Z',
  children: [
    { name: 'Ada', age: 4, sex: 'Female', mother: 'Elara Vane', father: 'PlayerDadZq', soul: 120, soulDesc: 'angel',
      health: 77, royal: 0, kidnapped: true, birthDate: '2026-10-01T00:00:00Z', location: 'Kidnapped' },
    { name: 'Bram', age: 9, sex: 'Male', mother: 'PlayerMomXq', father: 'Torvin Ash', soul: -40, soulDesc: 'naughty',
      health: 90, royal: 2, kidnapped: false, birthDate: '2026-09-01T00:00:00Z', location: 'Orphanage' },
    { name: 'Cora', age: 1, sex: 'Female', mother: 'Torvin Ash', father: 'Elara Vane', soul: 0, soulDesc: 'normal',
      health: 100, royal: 1, kidnapped: false, birthDate: '2026-10-30T00:00:00Z', location: 'Home' },
  ],
  childrenRaw: [
    { name: 'Ada', mother: 'Elara Vane', father: 'PlayerDadZq', motherID: 'npc_elara_vane', fatherID: 'acct_playerdadzq',
      kidnapped: true, kidnapperName: 'Grimsby Knave', ransomDemanded: 98765, royal: 0 },
    { name: 'Bram', mother: 'PlayerMomXq', father: 'Torvin Ash', motherID: 'acct_playermomxq', fatherID: 'npc_torvin_ash', royal: 2 },
    // Cora's parents carry NPC names, but her mother is a player who took the name "Torvin Ash": her id is not an NPC's.
    { name: 'Cora', mother: 'Torvin Ash', father: 'Elara Vane', motherID: 'acct_namesake', fatherID: 'npc_elara_vane', royal: 1 },
  ],
};
const NPC_IDS = new Set(['npc_elara_vane', 'npc_torvin_ash', 'npc_grimsby_knave']);

test('only the drawn fields go out; no private value appears anywhere in the JSON', () => {
  const out = publicChildren(WRAPPER, NPC_IDS);
  const json = JSON.stringify(out);
  for (const secret of ['PlayerDadZq', 'PlayerMomXq', 'Grimsby Knave', '98765', 'acct_', 'npc_', 'Kidnapped', 'Orphanage',
    'childrenRaw', 'kidnapped', 'location', 'birthDate', 'health', '"soul"', '2026-10-01', 'updatedAt', 'ransom', 'kidnapper']) {
    assert.ok(!json.includes(secret), secret);
  }
  assert.deepStrictEqual(out, {
    count: 3,
    children: [
      { name: 'Ada', age: 4, sex: 'Female', royal: false, soulDesc: 'angel', mother: 'Elara Vane', father: '' },
      { name: 'Bram', age: 9, sex: 'Male', royal: true, soulDesc: 'naughty', mother: '', father: 'Torvin Ash' },
      { name: 'Cora', age: 1, sex: 'Female', royal: true, soulDesc: 'normal', mother: '', father: 'Elara Vane' },
    ],
  });
});

test('a player who shares an NPC name is not shown as a parent', () => {
  const cora = publicChildren(WRAPPER, NPC_IDS).children[2];
  assert.strictEqual(cora.mother, '', 'the name matches an NPC but the id is a player\'s');
  assert.strictEqual(cora.father, 'Elara Vane');
});

test('royal reads the integer the game stores, 1 and 2 both royal', () => {
  const r = publicChildren(WRAPPER, NPC_IDS).children.map((c) => c.royal);
  assert.deepStrictEqual(r, [false, true, true]);
});

test('without a matching raw entry, no parent name goes out', () => {
  const noRaw = publicChildren({ children: WRAPPER.children }, NPC_IDS);
  assert.ok(noRaw.children.every((c) => c.mother === '' && c.father === ''));
  const shifted = publicChildren({ children: WRAPPER.children, childrenRaw: WRAPPER.childrenRaw.slice(1) }, NPC_IDS);
  assert.ok(shifted.children.every((c) => c.mother === '' && c.father === ''), 'a raw entry for another child is not used');
});

test('odd input gives an empty list, never a throw or a raw value', () => {
  for (const w of [null, undefined, 5, 'x', {}, { children: 'x' }, { children: [null, 3, 'y'] }]) {
    assert.deepStrictEqual(publicChildren(w, NPC_IDS), { count: 0, children: [] });
  }
  const odd = publicChildren({ children: [{ name: { a: 1 }, age: 'old', sex: 'X', royal: 'yes', soulDesc: 7, mother: 'Elara Vane' }], childrenRaw: [{ name: '', motherID: 'npc_elara_vane' }] }, NPC_IDS);
  assert.deepStrictEqual(odd.children[0], { name: '', age: null, sex: '', royal: false, soulDesc: '', mother: '', father: '' });
});

test('getDashSummary sends the projection, keyed on NPC character ids', () => {
  const src = fs.readFileSync(path.join(__dirname, '..', 'ssh-proxy.js'), 'utf8');
  const start = src.indexOf('function getDashSummary()');
  const body = src.slice(start, src.indexOf('\nfunction ', start + 10));
  assert.match(body, /const npcIds = new Set\(npcs\.map\(\(n\) => n && \(n\.characterID \|\| n\.CharacterID\)\)\.filter\(Boolean\)\);/);
  assert.match(body, /children = publicChildren\(JSON\.parse\(childRow\.value\), npcIds\);/);
  assert.ok(!/children = JSON\.parse\(childRow\.value\)/.test(body), 'the raw wrapper is never assigned');
  // Every response that carries the summary goes through getDashSummary.
  assert.match(src, /dashBroadcast\('npc-snapshot', JSON\.stringify\(\{ npcs: snapshot, summary \}\)\);/);
  assert.match(src, /event: npc-snapshot\\ndata: \$\{JSON\.stringify\(\{ npcs: snapshot, summary \}\)\}/);
});

test('the dashboard no longer reads kidnap state or location for a child', () => {
  const html = fs.readFileSync(path.join(__dirname, '..', 'dashboard.html'), 'utf8');
  const fn = html.slice(html.indexOf('function updateChildren()'), html.indexOf('// World Map'));
  assert.ok(fn.length > 100);
  assert.ok(!/c\.kidnapped|c\.location/.test(fn));
});
