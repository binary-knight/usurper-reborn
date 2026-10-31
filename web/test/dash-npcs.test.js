'use strict';
// web/dash-npcs.js builds the public NPC list behind the Observatory (/api/dash/profile and ties): an
// allow-list of the fields the page reads, and nothing about a player character.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { publicNpcs, TRAITS } = require('../dash-npcs');
const { loadPage } = require('./harness');

// Player characters (display names) and logins, lower-cased as the proxy builds them. "Torvin Ash" is a
// player who took an NPC's name.
const PLAYERS = new Set(['hrothgarzq', 'acct_hroth', 'torvin ash', 'mirabelxq', 'acct_mira', 'xy']);

// Shaped as OnlineStateManager.SaveSharedNPCs writes NPCData (camelCase).
const NPCS = [
  {
    id: '1b2c-guid', characterID: 'npc_elara_vane', name: 'Elara Vane', level: 41, class: 4, race: 2, npcFaction: 2,
    team: 'The Wave Guild', location: 'Temple', gold: 900, bankGold: 12000, age: 33, hp: 300, maxHP: 410,
    isDead: false, isPermaDead: false, isAgedDeath: false, isKing: false, isMarried: true, married: true,
    spouseName: 'HrothgarZq', marriedTimes: 2, pregnancyDueDate: '2026-11-05T00:00:00Z', pregnancyFatherName: 'HrothgarZq',
    motherName: 'MirabelXq', fatherName: 'Old Brann', originalMotherName: 'MirabelXq', motherID: 'acct_mira',
    lastOnPlayerTeamAt: '2026-10-30T12:00:00Z', recentDialogueIds: ['d1', 'd2'], wasRaisedByPlayer: true,
    personalityProfile: { aggression: 0.2, loyalty: 0.8, intelligence: 0.6, greed: 0.5, compassion: 0.7, courage: 0.4,
      honesty: 0.7, ambition: 0.6, vengefulness: 0.2, impulsiveness: 0.6, caution: 0.6, mysticism: 0.4, patience: 0.4,
      romanticism: 0.9, sensuality: 0.7, exhibitionism: 0.04, voyeurism: 0.24 },
    emotionalState: { happiness: 0.5, anger: 0.5, fear: 0.6, trust: 0.7, confidence: 0.5, sadness: 0.2, greed: 0.5,
      loneliness: 0.1, envy: 0.4, pride: 0.4, hope: 0.4, peace: 0.3 },
    relationships: { 'Sir Galahad': -100, HrothgarZq: 95, 'Torvin Ash': 40, 'Mord Necro': 12 },
    knownCharacters: ['HrothgarZq'], enemies: ['MirabelXq'],
    currentGoals: [
      { name: 'Make Friends', type: 'Social', priority: 0.7, isActive: true, targetCharacter: '' },
      { name: 'Court HrothgarZq', type: 'Romance', priority: 0.9, isActive: true, targetCharacter: 'HrothgarZq' },
      { name: 'Avenge the insult', type: 'Revenge', priority: 0.5, isActive: true, targetCharacter: 'Torvin Ash' },
      { name: 'Watch MirabelXq', type: 'Social', priority: 0.4, isActive: true, targetCharacter: '' },
    ],
    memories: [
      { description: 'I helped Sir Galahad', type: 'Helped', importance: 0.5, timestamp: '2026-10-31T01:00:00Z', involvedCharacter: 'Sir Galahad' },
      { description: 'Suspect HrothgarZq of an affair', type: 'Suspicion', importance: 0.9, timestamp: '2026-10-31T02:00:00Z', involvedCharacter: 'HrothgarZq' },
      { description: 'Saw MirabelXq steal from Sir Galahad', type: 'Witnessed', importance: 0.6, timestamp: '2026-10-31T03:00:00Z', involvedCharacter: 'Sir Galahad' },
      { description: 'Fought Torvin Ash', type: 'Fight', importance: 0.6, timestamp: '2026-10-31T04:00:00Z', involvedCharacter: 'Torvin Ash' },
    ],
  },
  { characterID: 'npc_sir_galahad', name: 'Sir Galahad', level: 60, class: 7, race: 0, npcFaction: 0, isMarried: true, spouseName: 'Mord Necro',
    relationships: { 'Elara Vane': 30 } },
  { characterID: 'npc_mord', name: 'Mord Necro', level: 50, class: 6, race: 0, npcFaction: 1 },
  { characterID: 'npc_torvin', name: 'Torvin Ash', level: 20, class: 10, race: 4, npcFaction: -1 },
];

test('no player name or login appears anywhere in the response', () => {
  const json = JSON.stringify(publicNpcs(NPCS, PLAYERS));
  for (const secret of ['HrothgarZq', 'MirabelXq', 'acct_', 'affair']) assert.ok(!json.includes(secret), secret);
  // "Torvin Ash" is also an NPC, so its own record stays; it may not appear as anyone's tie, spouse, goal or memory.
  const elara = publicNpcs(NPCS, PLAYERS)[0];
  const aboutTorvin = JSON.stringify([elara.relationships, elara.currentGoals, elara.memories, elara.spouseName]);
  assert.ok(!aboutTorvin.includes('Torvin Ash'), 'an NPC name that a player shares is never shown as someone\'s tie');
});

test('NPC-only entries the page draws are kept', () => {
  const [elara, galahad] = publicNpcs(NPCS, PLAYERS);
  assert.deepStrictEqual(elara.relationships, { 'Sir Galahad': -100, 'Mord Necro': 12 });
  assert.deepStrictEqual(elara.currentGoals, [{ name: 'Make Friends', type: 'Social', priority: 0.7, isActive: true }]);
  assert.deepStrictEqual(elara.memories, [{ description: 'I helped Sir Galahad', type: 'Helped', importance: 0.5, timestamp: '2026-10-31T01:00:00Z' }]);
  assert.strictEqual(elara.spouseName, '', 'a player spouse is not named');
  assert.strictEqual(elara.isMarried, true);
  assert.strictEqual(galahad.spouseName, 'Mord Necro', 'an NPC spouse is');
  assert.strictEqual(elara.pregnancyDueDate, '2026-11-05T00:00:00Z');
  assert.strictEqual(elara.level, 41);
  assert.strictEqual(elara.npcFaction, 2);
  assert.strictEqual(elara.maxHP, 410);
});

test('only allow-listed fields go out: no lineage, ids, internal flags or romance traits', () => {
  const elara = publicNpcs(NPCS, PLAYERS)[0];
  assert.deepStrictEqual(Object.keys(elara).sort(), ['age', 'class', 'currentGoals', 'emotionalState', 'gold', 'hp', 'isAgedDeath', 'isDead',
    'isKing', 'isMarried', 'isPermaDead', 'level', 'location', 'maxHP', 'memories', 'name', 'npcFaction', 'personalityProfile',
    'pregnancyDueDate', 'race', 'relationships', 'spouseName', 'team']);
  assert.deepStrictEqual(Object.keys(elara.personalityProfile).sort(), [...TRAITS].sort());
  const json = JSON.stringify(elara);
  for (const k of ['romanticism', 'sensuality', 'exhibitionism', 'voyeurism', 'motherName', 'fatherName', 'lastOnPlayerTeamAt',
    'recentDialogueIds', 'wasRaisedByPlayer', 'characterID', '"id"', 'bankGold', 'knownCharacters', 'enemies', 'pregnancyFather', 'involvedCharacter', 'targetCharacter']) {
    assert.ok(!json.includes(k), k);
  }
});

test('without the player list, nothing about other characters goes out', () => {
  const elara = publicNpcs(NPCS, null)[0];
  assert.deepStrictEqual(elara.relationships, {});
  assert.deepStrictEqual(elara.currentGoals, []);
  assert.deepStrictEqual(elara.memories, []);
  assert.strictEqual(elara.spouseName, '');
  assert.strictEqual(elara.name, 'Elara Vane');
});

test('odd input never throws and never passes a raw value', () => {
  assert.deepStrictEqual(publicNpcs(null, PLAYERS), []);
  assert.deepStrictEqual(publicNpcs('x', PLAYERS), []);
  const [odd] = publicNpcs([{ name: 5, level: 'x', relationships: [1], currentGoals: 'g', memories: [null, 3], personalityProfile: 'p' }], PLAYERS);
  assert.strictEqual(odd.name, '');
  assert.strictEqual(odd.level, 0);
  assert.deepStrictEqual(odd.relationships, {});
  assert.deepStrictEqual(odd.currentGoals, []);
  assert.deepStrictEqual(odd.memories, []);
});

test('the raw and public NPC lists are not served whole; the Observatory reads the public list', () => {
  const src = fs.readFileSync(path.join(__dirname, '..', 'ssh-proxy.js'), 'utf8');
  assert.ok(!src.includes("'/api/dash/npcs'"), 'no /api/dash/npcs route');
  assert.ok(!src.includes("'/api/dash/summary'"), 'no /api/dash/summary route');
  assert.ok(!/npc-snapshot/.test(src), 'no NPC snapshot on the event stream');
  assert.match(src, /getPublicNpcs: getDashPublicNpcs,/);
  assert.match(src, /dashPublicNpcCache = publicNpcs\(getDashNpcs\(\), getDashPlayerNames\(\)\);/);
  assert.match(src, /SELECT username, display_name FROM players/);
});

test('a team name that holds a player name is left out; others stay', () => {
  const out = publicNpcs([{ name: 'Sir Galahad', team: 'HrothgarZq guard' }, { name: 'Mord Necro', team: 'The Wave Guild' }], PLAYERS);
  assert.deepStrictEqual(out.map((n) => n.team), ['', 'The Wave Guild']);
  assert.deepStrictEqual(publicNpcs([{ name: 'Mord Necro', team: 'The Wave Guild' }], null).map((n) => n.team), [''], 'no player list: no team names');
});

test('short player names are matched as whole words, not as parts of other words', () => {
  const out = publicNpcs([{ name: 'Mord Necro', currentGoals: [
    { name: 'Settle Score with Xy', isActive: true }, { name: 'Settle score with XY.', isActive: true },
    { name: 'Visit Xyleth Hall', isActive: true }, { name: 'Explore the Oxyd mines', isActive: true }] }], PLAYERS)[0];
  assert.deepStrictEqual(out.currentGoals.map((g) => g.name), ['Visit Xyleth Hall', 'Explore the Oxyd mines']);
});

test('a relationship or memory keyed by an NPC id is kept under that NPC name; unknown ids are dropped', () => {
  const npcs = [
    { id: 'guid-mord', name: 'Mord Necro', relationships: { 'guid-gal': 60, 'guid-unknown': 50, 'guid-torvin': 10 },
      memories: [{ description: 'Joined a gang', type: 'Joined', involvedCharacter: 'guid-gal', importance: 0.4 },
                 { description: 'Met someone', type: 'Met', involvedCharacter: 'guid-player', importance: 0.4 }] },
    { id: 'guid-gal', name: 'Sir Galahad' },
    { id: 'guid-torvin', name: 'Torvin Ash' },
  ];
  const mord = publicNpcs(npcs, PLAYERS)[0];
  assert.deepStrictEqual(mord.relationships, { 'Sir Galahad': 60 }, 'Torvin Ash shares a player name, so it is dropped too');
  assert.deepStrictEqual(mord.memories.map((m) => m.description), ['Joined a gang']);
  assert.ok(!JSON.stringify(mord).includes('guid-'));
});

test('the Observatory profile draws a public entry without errors (goals and memories, no player name)', async () => {
  const entry = publicNpcs(NPCS, PLAYERS)[0];
  const p = await loadPage('dashboard.html', {
    fetch: (url) => (url === '/api/dash/profile?name=' + encodeURIComponent('Elara Vane') ? entry : url === '/api/dash/history?hours=24' ? { status: 404, body: { error: 'Not found' } } : []),
  });
  await p.run("Observatory.openProfile('Elara Vane')");
  await p.settle();
  const text = p.el('profile').textContent;
  assert.ok(text.includes('Make Friends'), 'goal shown');
  assert.ok(text.includes('I helped Sir Galahad'), 'memory shown');
  assert.ok(!text.includes('HrothgarZq'));
  assert.deepStrictEqual(p.errors, []);
  assert.strictEqual(p.run('JSON.stringify(Observatory.state.errors)'), '[]');
});
