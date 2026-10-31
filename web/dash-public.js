'use strict';
// Public projections for the /api/dash endpoints. The dashboard is a public page, so what it
// sends is an allow-list of fields the page draws, never a raw world_state value.

// world_state 'children' holds the game's full wrapper (childrenRaw with parent ids, kidnapper
// and ransom; location; kidnapped). The page gets each child's name, age, sex, royal flag and
// soul description, and a parent's name only when that parent is an NPC: a player character's
// name is left out. The count is the number of children sent.
function publicChildren(wrapper, npcNames) {
  const kids = wrapper && Array.isArray(wrapper.children) ? wrapper.children : [];
  const isNpc = (n) => typeof n === 'string' && n.length > 0 && npcNames.has(n);
  const str = (v) => (typeof v === 'string' ? v : '');
  const children = kids
    .filter((c) => c && typeof c === 'object')
    .map((c) => ({
      name: str(c.name),
      age: typeof c.age === 'number' && isFinite(c.age) ? c.age : null,
      sex: c.sex === 'Male' || c.sex === 'Female' ? c.sex : '',
      royal: c.royal === true,
      soulDesc: str(c.soulDesc),
      mother: isNpc(c.mother) ? c.mother : '',
      father: isNpc(c.father) ? c.father : '',
    }));
  return { count: children.length, children };
}

module.exports = { publicChildren };
