'use strict';
// The public NPC list behind /api/dash/npcs. world_state 'npcs' holds everything the game keeps
// about each NPC, including memories, goals and feelings about player characters, a spouse or
// child's other parent who may be a player, and internal flags. The dashboard gets an allow-list:
// the fields its page reads, with every entry about another character kept only when that
// character is an NPC and no player character has the same name.

// The 13 personality traits the page draws. personalityProfile also holds romance traits,
// which are not sent.
const TRAITS = ['aggression', 'loyalty', 'intelligence', 'greed', 'compassion', 'courage', 'honesty',
  'ambition', 'vengefulness', 'impulsiveness', 'caution', 'mysticism', 'patience'];
const MOODS = ['happiness', 'anger', 'fear', 'trust', 'confidence', 'sadness', 'greed', 'loneliness',
  'envy', 'pride', 'hope', 'peace'];

const str = (v) => (typeof v === 'string' ? v : '');
const num = (v) => (typeof v === 'number' && isFinite(v) ? v : 0);
const bool = (v) => v === true;
const arr = (v) => (Array.isArray(v) ? v : []);
const obj = (v) => (v && typeof v === 'object' && !Array.isArray(v) ? v : {});

// playerNames: lower-cased player character names and login names, or null when they could not be
// read; then no entry about another character is sent. npcNames: NPC names as stored.
function makeGuards(npcNames, playerNames) {
  if (!playerNames) return { isSafeNpc: () => false, mentionsPlayer: () => true };
  const isPlayer = (name) => typeof name === 'string' && playerNames.has(name.toLowerCase());
  // Another character may be shown only when it is an NPC and no player shares its name.
  const isSafeNpc = (name) => typeof name === 'string' && name.length > 0 && npcNames.has(name) && !isPlayer(name);
  // Free text (a memory, a goal) is dropped when any player name appears in it.
  const playerList = [...playerNames].filter((p) => p.length >= 3);
  const mentionsPlayer = (text) => {
    const t = str(text).toLowerCase();
    return t.length > 0 && playerList.some((p) => t.includes(p));
  };
  return { isSafeNpc, mentionsPlayer };
}

function publicNpc(n, g) {
  n = obj(n);
  const pp = obj(n.personalityProfile);
  const emo = obj(n.emotionalState);
  const traits = {};
  TRAITS.forEach((k) => { traits[k] = num(pp[k]); });
  const mood = {};
  MOODS.forEach((k) => { mood[k] = num(emo[k]); });

  const relationships = {};
  Object.entries(obj(n.relationships)).forEach(([who, v]) => {
    if (g.isSafeNpc(who) && typeof v === 'number' && isFinite(v)) relationships[who] = v;
  });

  const currentGoals = arr(n.currentGoals)
    .filter((x) => x && typeof x === 'object')
    .filter((x) => !str(x.targetCharacter) || g.isSafeNpc(x.targetCharacter))
    .filter((x) => !g.mentionsPlayer(x.name) && !g.mentionsPlayer(x.description))
    .map((x) => ({ name: str(x.name), type: str(x.type), priority: num(x.priority), isActive: bool(x.isActive) }));

  const memories = arr(n.memories)
    .filter((x) => x && typeof x === 'object')
    .filter((x) => !str(x.involvedCharacter) || g.isSafeNpc(x.involvedCharacter))
    .filter((x) => !g.mentionsPlayer(x.description))
    .map((x) => ({ description: str(x.description), type: str(x.type), importance: num(x.importance), timestamp: str(x.timestamp) }));

  const spouse = g.isSafeNpc(n.spouseName) ? n.spouseName : '';
  return {
    name: str(n.name),
    level: num(n.level),
    class: num(n.class),
    race: num(n.race),
    npcFaction: typeof n.npcFaction === 'number' ? n.npcFaction : -1,
    team: str(n.team),
    location: str(n.location),
    gold: num(n.gold),
    age: num(n.age),
    hp: num(n.hp),
    maxHP: num(n.maxHP),
    isDead: bool(n.isDead),
    isPermaDead: bool(n.isPermaDead),
    isAgedDeath: bool(n.isAgedDeath),
    isKing: bool(n.isKing),
    isMarried: bool(n.isMarried) || bool(n.married),
    spouseName: spouse,
    pregnancyDueDate: str(n.pregnancyDueDate),
    personalityProfile: traits,
    emotionalState: mood,
    relationships,
    currentGoals,
    memories,
  };
}

function publicNpcs(npcs, playerNames) {
  const list = arr(npcs).filter((n) => n && typeof n === 'object');
  const npcNames = new Set(list.map((n) => n.name).filter((x) => typeof x === 'string'));
  const g = makeGuards(npcNames, playerNames);
  return list.map((n) => publicNpc(n, g));
}

module.exports = { publicNpcs, TRAITS, MOODS };
