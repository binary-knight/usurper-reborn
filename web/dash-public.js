'use strict';
// Public projections for the /api/dash endpoints. The dashboard is a public page, so what it
// sends is an allow-list of fields the page draws, never a raw world_state value.

// world_state 'children' holds the game's full wrapper (childrenRaw with parent ids, kidnapper
// and ransom; location; kidnapped). The page gets each child's name, age, sex, royal flag and
// soul description, and a parent's name only when that parent is an NPC. A parent counts as an
// NPC only when its id in childrenRaw (Child.MotherID / FatherID, the parent's Character.ID) is
// an NPC's characterID; a matching name alone is not enough, since a player may share an NPC's
// name. children[i] and childrenRaw[i] come from the same list in the same order (FamilySystem),
// and a pair is used only when the raw entry's name matches. The count is the number sent.
function publicChildren(wrapper, npcIds) {
  const kids = wrapper && Array.isArray(wrapper.children) ? wrapper.children : [];
  const raw = wrapper && Array.isArray(wrapper.childrenRaw) ? wrapper.childrenRaw : [];
  const str = (v) => (typeof v === 'string' ? v : '');
  const pick = (o, a, b) => (o && typeof o === 'object' ? str(o[a] !== undefined ? o[a] : o[b]) : '');
  const children = [];
  kids.forEach((c, i) => {
    if (!c || typeof c !== 'object') return;
    const name = str(c.name);
    const r = name && raw[i] && typeof raw[i] === 'object' && pick(raw[i], 'name', 'Name') === name ? raw[i] : null;
    const npcParent = (idA, idB) => {
      const id = pick(r, idA, idB);
      return id.length > 0 && npcIds.has(id);
    };
    children.push({
      name,
      age: typeof c.age === 'number' && isFinite(c.age) ? c.age : null,
      sex: c.sex === 'Male' || c.sex === 'Female' ? c.sex : '',
      royal: c.royal === true || c.royal === 1 || c.royal === 2,   // Child.Royal: 0 none, 1 half, 2 full
      soulDesc: str(c.soulDesc),
      mother: npcParent('motherID', 'MotherID') ? str(c.mother) : '',
      father: npcParent('fatherID', 'FatherID') ? str(c.father) : '',
    });
  });
  return { count: children.length, children };
}

module.exports = { publicChildren };
