'use strict';
// Public NPC dashboard (dashboard.html): every place NPC, child or relationship
// text reaches HTML. Names travel in data-npc and come back through the delegated
// click listener; hostile names must render as text and click through unchanged.
const test = require('node:test');
const assert = require('node:assert');
const { HOSTILE, loadPage, assertSafe, elements } = require('./harness');

function npc(name, extra) {
  return Object.assign({
    name, level: name === HOSTILE ? HOSTILE : 'Alice', class: 1, race: 2, age: 30, gold: 9000,
    hp: 50, maxHP: 100, location: 'MainStreet', npcFaction: 0, isDead: false,
    isMarried: true, spouseName: name,
    relationships: { [name]: 55 },
    personalityProfile: { aggression: 0.9, ambition: 0.1 },
    emotionalState: { happiness: 0.5, anger: 0.9, fear: 0.2 },
    currentGoals: [{ name, type: name, isActive: true, priority: 0.5 }],
    memories: [{ description: name, type: name, importance: 0.5, timestamp: '' }],
  }, extra || {});
}

async function page() {
  const p = await loadPage('dashboard.html', { fetch: () => [] });
  const picked = [];
  p.ctx.selectNotableNpc = (n) => picked.push(['notable', n]);
  p.ctx.selectNpcFromList = (n) => picked.push(['list', n]);
  return { p, picked };
}

async function render(fn) {
  const hostile = await page();
  const benign = await page();
  fn(hostile.p, HOSTILE);
  fn(benign.p, 'Alice');
  return { hostile, benign };
}

test('dashboard page loads escape.js and runs without errors', async () => {
  const { p } = await page();
  assert.deepStrictEqual(p.loaded, ['escape.js']);
  assert.deepStrictEqual(p.errors, []);
});

test('sink dashboard notable chips (2433): name in data-npc, detail escaped, click passes the raw name', async () => {
  const { hostile, benign } = await render((p, name) => {
    p.ctx.__npcs = [npc(name)];
    p.run('allNpcs = __npcs; updateNotableHighlights();');
  });
  const html = hostile.p.el('notable-bar').innerHTML;
  assertSafe(assert, html, benign.p.el('notable-bar').innerHTML, 'notable chips');
  const chips = hostile.p.doc.querySelectorAll('[data-action="select-npc"]');
  assert.ok(chips.length >= 2, 'chips rendered');
  hostile.p.click(chips[0].querySelector('.nc-value'));
  assert.deepStrictEqual(hostile.picked, [['notable', HOSTILE]]);
});

test('sink dashboard NPC table rows (2577): row click passes the raw name', async () => {
  const { hostile, benign } = await render((p, name) => {
    p.ctx.__npcs = [npc(name)];
    p.run('allNpcs = __npcs; renderNpcTable(allNpcs);');
  });
  assertSafe(assert, hostile.p.el('npc-table-body').innerHTML, benign.p.el('npc-table-body').innerHTML, 'npc table');
  const row = hostile.p.doc.querySelector('[data-action="select-npc-list"]');
  hostile.p.click(row.querySelector('td'));
  assert.deepStrictEqual(hostile.picked, [['list', HOSTILE]]);
});

test('sink dashboard location popup (2684): popup entry click passes the raw name', async () => {
  const { hostile, benign } = await render((p, name) => {
    p.ctx.__npcs = [npc(name)];
    p.run('allNpcs = __npcs; showLocationPopup("MainStreet", 0, 0);');
  });
  assertSafe(assert, hostile.p.el('loc-popup').innerHTML, benign.p.el('loc-popup').innerHTML, 'location popup');
  const entry = hostile.p.el('loc-popup').querySelector('[data-action="select-npc"]');
  hostile.p.click(entry);
  assert.deepStrictEqual(hostile.picked, [['notable', HOSTILE]]);
});

test('sink dashboard NPC detail relationships (3305) and detail fields: rel click passes the raw name', async () => {
  const { hostile, benign } = await render((p, name) => {
    p.ctx.__n = npc(name, { age: name, hp: name, maxHP: name });
    p.run('showNpcDetail(__n);');
  });
  assertSafe(assert, hostile.p.el('detail-content').innerHTML, benign.p.el('detail-content').innerHTML, 'npc detail');
  const rel = hostile.p.el('detail-content').querySelector('.rel-name');
  assert.strictEqual(rel.getAttribute('title'), HOSTILE);
  hostile.p.click(rel);
  assert.deepStrictEqual(hostile.picked, [['notable', HOSTILE]]);
  // the original (overridden) detail renderer is escaped the same way
  const { hostile: h2, benign: b2 } = await render((p, name) => {
    p.ctx.__n = npc(name, { age: name, hp: name, maxHP: name });
    p.run('_origShowNpcDetail(__n);');
  });
  assertSafe(assert, h2.p.el('detail-content').innerHTML, b2.p.el('detail-content').innerHTML, 'original npc detail');
});

test('sink dashboard children panel (1389): sex, age and location are escaped', async () => {
  const kid = (v) => ({ name: v, sex: v, age: v, location: v, mother: v, father: v, soulDesc: v });
  const { hostile, benign } = await render((p, name) => {
    p.ctx.__s = { children: { count: 1, children: [kid(name)] } };
    p.run('summary = __s; updateChildren();');
  });
  assertSafe(assert, hostile.p.el('children-rows').innerHTML, benign.p.el('children-rows').innerHTML, 'children rows');
  assert.ok(hostile.p.el('children-rows').textContent.includes(HOSTILE));
});

test('sink dashboard world map dots (3062, 3093): name and level escaped, dot click uses data-npc', async () => {
  const { hostile, benign } = await render((p, name) => {
    p.ctx.__npcs = [npc(name), npc(name + '2', { location: 'Nowhere' })];
    p.run('allNpcs = __npcs; updateWorldMap();');
  });
  const html = hostile.p.el('world-map').innerHTML;
  assertSafe(assert, html, benign.p.el('world-map').innerHTML, 'world map');
  const dots = elements(html).filter((e) => e.attrs['data-npc'] !== undefined);
  assert.deepStrictEqual(dots.map((d) => d.attrs['data-npc']), [HOSTILE, HOSTILE + '2']);
  hostile.p.click(hostile.p.el('world-map').querySelector('.map-npc-dot'));
  assert.strictEqual(hostile.p.run('selectedNpc'), HOSTILE);
  // the original (overridden) map renderer
  const { hostile: h2, benign: b2 } = await render((p, name) => {
    p.ctx.__npcs = [npc(name), npc(name + '2', { location: 'Nowhere' })];
    p.run('allNpcs = __npcs; _origUpdateWorldMap();');
  });
  assertSafe(assert, h2.p.el('world-map').innerHTML, b2.p.el('world-map').innerHTML, 'original world map');
});

test('sink dashboard compare picker and columns (2722, 2774): level and age escaped', async () => {
  const { hostile, benign } = await render((p, name) => {
    p.ctx.__npcs = [npc(name, { age: name })];
    p.run('allNpcs = __npcs; openComparison(); document.getElementById("compare-npc-a").value = __npcs[0].name; updateComparison();');
  });
  assertSafe(assert, hostile.p.el('compare-npc-a').innerHTML, benign.p.el('compare-npc-a').innerHTML, 'compare options');
  assertSafe(assert, hostile.p.el('compare-body').innerHTML, benign.p.el('compare-body').innerHTML, 'compare columns');
  const opt = elements(hostile.p.el('compare-npc-a').innerHTML).find((e) => e.attrs.value === HOSTILE);
  assert.ok(opt, 'option value round-trips');
});

test('sink dashboard network tooltips (2152, 3556): level, child age and relationship values escaped', async () => {
  async function tooltipHtml(name) {
    const { p } = await page();
    p.ctx.__npcs = [npc(name, { team: name, relationships: { [name]: name } })];
    p.ctx.__s = { children: { children: [{ name: name + 'c', age: name }] } };
    p.run('allNpcs = __npcs; summary = __s;');
    const out = [];
    for (const fn of ['_origUpdateNetwork', 'updateNetwork']) {
      p.d3Handlers.length = 0;
      p.run(fn + '();');
      const enter = p.d3Handlers.filter(([n]) => n === 'mouseenter').map(([, h]) => h);
      assert.ok(enter.length >= 1, fn + ' registers a tooltip');
      const datum = { id: name, level: name, faction: '0', dead: false, perma: false, married: true, spouse: name, team: name, index: 0, cls: 'Warrior', race: 'Elf', relCount: 1, isChild: false };
      enter[0]({ clientX: 0, clientY: 0 }, datum);
      out.push(p.el('tooltip').innerHTML);
      enter[0]({ clientX: 0, clientY: 0 }, Object.assign({}, datum, { isChild: true, childAge: name }));
      out.push(p.el('tooltip').innerHTML);
    }
    return out;
  }
  const h = await tooltipHtml(HOSTILE);
  const b = await tooltipHtml('Alice');
  h.forEach((html, i) => assertSafe(assert, html, b[i], 'tooltip ' + i));
});
