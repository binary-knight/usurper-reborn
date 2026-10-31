'use strict';
// Reads the monster and boss names, the name templates and the no-monster killer texts from the
// game's C# data and English strings. web/dash-monster-names.js must equal what this returns.
const fs = require('node:fs');
const path = require('node:path');

function monsterSources(root) {
  const read = (...p) => fs.readFileSync(path.join(root, ...p), 'utf8');
  const all = (text, re) => [...text.matchAll(re)].map((m) => m[1]);
  const topLevel = /^\s*Name = "([^"]+)",/gm;
  const en = JSON.parse(read('Localization', 'en.json'));
  const enWhere = (test) => Object.keys(en).filter(test).map((k) => en[k]);
  const sources = {
    families: all(read('Scripts', 'Data', 'MonsterFamilies.cs'), /new\(\)\s*\{\s*Name = "([^"]+)"/g),
    beasts: all(read('Scripts', 'Data', 'BeastData.cs'), topLevel),
    oldGods: all(read('Scripts', 'Data', 'OldGodsData.cs'), topLevel),
    secretBosses: all(read('Scripts', 'Data', 'SecretBosses.cs'), topLevel),
    worldBosses: all(read('Scripts', 'Data', 'WorldBossData.cs'), /^\s*Id = "[^"]+",\s*\n\s*Name = "([^"]+)",/gm),
    loc: enWhere((k) => k.startsWith('monster.name.')),
  };
  // An Old God is also called by the part of its name before the title ("Maelketh").
  const godShort = sources.oldGods.map((n) => n.split(',')[0].trim());
  const names = [...new Set([...Object.values(sources).flat(), ...godShort])].sort();
  const affixes = [...new Set(enWhere((k) => /^monster\.(champion\.|elite\.|role\.|leader$)/.test(k)))].sort();
  const other = [...new Set(['an unknown end', en['combat.killer_unknown_end']])].filter(Boolean).sort();
  return { sources, names, affixes, other };
}

module.exports = { monsterSources };
