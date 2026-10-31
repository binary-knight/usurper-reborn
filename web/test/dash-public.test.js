'use strict';
// The dashboard's children list is public: web/dash-public.js sends an allow-list only.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { publicChildren } = require('../dash-public');

// A wrapper as the game writes it (OnlineStateManager.SaveSharedChildrenNow), with private values in every
// place they can sit: a player parent, kidnap state and location, and childrenRaw with ids, kidnapper and ransom.
const WRAPPER = {
  count: 2,
  updatedAt: '2026-10-31T07:00:00Z',
  children: [
    { name: 'Ada', age: 4, sex: 'Female', mother: 'Elara Vane', father: 'PlayerDadZq', soul: 120, soulDesc: 'angel',
      health: 77, royal: false, kidnapped: true, birthDate: '2026-10-01T00:00:00Z', location: 'Kidnapped' },
    { name: 'Bram', age: 9, sex: 'Male', mother: 'PlayerMomXq', father: 'Torvin Ash', soul: -40, soulDesc: 'naughty',
      health: 90, royal: true, kidnapped: false, birthDate: '2026-09-01T00:00:00Z', location: 'Orphanage' },
  ],
  childrenRaw: [
    { Name: 'Ada', MotherID: 'npc_17', FatherID: 'acct_playerdadzq', Kidnapped: true, KidnapperName: 'Grimsby Knave', RansomDemanded: 98765 },
  ],
};
const NPCS = new Set(['Elara Vane', 'Torvin Ash', 'Grimsby Knave']);

test('only the drawn fields go out; no private value appears anywhere in the JSON', () => {
  const out = publicChildren(WRAPPER, NPCS);
  const json = JSON.stringify(out);
  for (const secret of ['PlayerDadZq', 'PlayerMomXq', 'Grimsby Knave', '98765', 'acct_playerdadzq', 'npc_17', 'Kidnapped',
    'Orphanage', 'childrenRaw', 'kidnapped', 'location', 'birthDate', 'health', '"soul"', '2026-10-01', 'updatedAt']) {
    assert.ok(!json.includes(secret), secret);
  }
  assert.deepStrictEqual(out, {
    count: 2,
    children: [
      { name: 'Ada', age: 4, sex: 'Female', royal: false, soulDesc: 'angel', mother: 'Elara Vane', father: '' },
      { name: 'Bram', age: 9, sex: 'Male', royal: true, soulDesc: 'naughty', mother: '', father: 'Torvin Ash' },
    ],
  });
});

test('odd input gives an empty list, never a throw or a raw value', () => {
  for (const w of [null, undefined, 5, 'x', {}, { children: 'x' }, { children: [null, 3, 'y'] }]) {
    assert.deepStrictEqual(publicChildren(w, NPCS), { count: 0, children: [] });
  }
  const odd = publicChildren({ children: [{ name: { a: 1 }, age: 'old', sex: 'X', royal: 'yes', soulDesc: 7, mother: 'Elara Vane' }] }, NPCS);
  assert.deepStrictEqual(odd.children[0], { name: '', age: null, sex: '', royal: false, soulDesc: '', mother: 'Elara Vane', father: '' });
});

test('getDashSummary sends the projection, not the world_state value', () => {
  const src = fs.readFileSync(path.join(__dirname, '..', 'ssh-proxy.js'), 'utf8');
  const start = src.indexOf('function getDashSummary()');
  const body = src.slice(start, src.indexOf('\nfunction ', start + 10));
  assert.match(body, /children = publicChildren\(JSON\.parse\(childRow\.value\), npcNames\);/);
  assert.ok(!/children = JSON\.parse\(childRow\.value\)/.test(body), 'the raw wrapper is never assigned');
  assert.match(src, /const \{ publicChildren \} = require\('\.\/dash-public'\);/);
});

test('the dashboard no longer reads kidnap state or location for a child', () => {
  const html = fs.readFileSync(path.join(__dirname, '..', 'dashboard.html'), 'utf8');
  const fn = html.slice(html.indexOf('function updateChildren()'), html.indexOf('// World Map'));
  assert.ok(fn.length > 100);
  assert.ok(!/c\.kidnapped|c\.location/.test(fn));
});
