"use strict";
const fs = require("node:fs");
const path = require("node:path");
const MarkdownIt = require("markdown-it");
const root = path.resolve(__dirname, "../..");
const sections = [
  ["getting-started", "Getting started"],
  ["characters", "Characters"],
  ["combat", "Combat"],
  ["gods", "Gods and faith"],
  ["world", "World"],
  ["items", "Items and economy"],
  ["monsters", "Monsters and bosses"],
  ["online", "Online world"],
  ["reference", "Reference"],
];
const escape = (value) =>
  String(value ?? "")
    .replace(/[\u2013\u2014]/g, "-")
    .replace(/\p{Extended_Pictographic}/gu, "")
    .replace(
      /[&<>"']/g,
      (c) =>
        ({
          "&": "&amp;",
          "<": "&lt;",
          ">": "&gt;",
          '"': "&quot;",
          "'": "&#39;",
        })[c],
    );
const name = (e) => e.name?.en || e.id;
const label = (value) =>
  String(value)
    .replace(/([a-z\d])([A-Z])/g, "$1 $2")
    .replace(/_/g, " ")
    .replace(/\bhp\b/gi, "HP")
    .replace(/^./, (c) => c.toUpperCase());
const slug = (value) =>
  String(value)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "");
const url = (kind, id) => {
  // Class:slot spell keys become Class-slot URLs so directory names also work on Windows.
  const segment = String(id).replace(/:/g, "-");
  if (!/^[A-Za-z0-9_-]+$/.test(segment)) throw Error(`Unsafe entity ID: ${id}`);
  return `/wiki/en/${kind}/${segment}/`;
};
const link = (kind, e) => `<a href="${url(kind, e.id)}">${escape(name(e))}</a>`;
function publicHtml(html) {
  let depth = 0,
    position = 0,
    publicText = "";
  for (const match of html.matchAll(/<\/?details\b[^>]*>/gi)) {
    if (depth === 0) publicText += html.slice(position, match.index);
    depth += /^<\//.test(match[0]) ? -1 : 1;
    if (depth < 0) throw Error("Unbalanced spoiler HTML");
    position = match.index + match[0].length;
  }
  if (depth) throw Error("Unclosed spoiler HTML");
  return publicText + html.slice(position);
}
const textOf = (html) =>
  publicHtml(html)
    .replace(/<[^>]*>/g, " ")
    .replace(
      /&(?:amp|lt|gt|quot|#39);/g,
      (s) =>
        ({
          "&amp;": "&",
          "&lt;": "<",
          "&gt;": ">",
          "&quot;": '"',
          "&#39;": "'",
        })[s],
    )
    .replace(/\s+/g, " ")
    .trim();
function table(headers, rows, caption) {
  return `<div class="table-wrap" role="region" aria-label="${escape(caption)}" tabindex="0"><table><caption>${escape(caption)}</caption><thead><tr>${headers.map((h) => `<th scope="col">${escape(h)}</th>`).join("")}</tr></thead><tbody>${rows.map((row) => `<tr>${row.map((c, i) => (i === 0 ? `<th scope="row">${c}</th>` : `<td>${c}</td>`)).join("")}</tr>`).join("")}</tbody></table></div>`;
}
function values(obj, caption) {
  return table(
    ["Rule", "Value"],
    Object.entries(obj).map(([k, v]) => [
      escape(label(k)),
      escape(Array.isArray(v) ? v.join(", ") : v),
    ]),
    caption,
  );
}
const spoiler = (summary, body) =>
  `<details class="spoiler"><summary>${escape(summary)}</summary>${body}</details>`;
function loadData(dir) {
  const meta = JSON.parse(fs.readFileSync(path.join(dir, "meta.json"), "utf8"));
  if (meta.schemaVersion !== 1 || !meta.gameVersion)
    throw Error("Unsupported wiki metadata");
  const data = {};
  for (const kind of [
    "items",
    "monsters",
    "classes",
    "races",
    "spells",
    "abilities",
    "gods",
    "mental",
    "balance",
    "achievements",
    "bosses",
    "characters",
    "party",
    "economy",
    "combat",
    "world",
    "realm",
  ]) {
    const file = JSON.parse(
      fs.readFileSync(path.join(dir, kind + ".json"), "utf8"),
    );
    if (file.schemaVersion !== 1 || file.gameVersion !== meta.gameVersion)
      throw Error(`Mismatched schema/version: ${kind}`);
    data[kind] = file.data;
  }
  for (const kind of [
    "items",
    "monsters",
    "classes",
    "races",
    "spells",
    "abilities",
    "achievements",
  ]) {
    if (!Array.isArray(data[kind]) || !data[kind].length)
      throw Error(`Empty dataset: ${kind}`);
    const ids = new Set();
    for (const e of data[kind]) {
      if (e.id == null || !name(e) || ids.has(String(e.id)))
        throw Error(`Invalid or duplicate ID: ${kind}/${e.id}`);
      ids.add(String(e.id));
    }
  }
  const classes = new Set(data.classes.map((e) => e.id));
  const races = new Set(data.races.map((e) => e.id));
  for (const c of data.classes) {
    if (!Array.isArray(c.allowedRaces) || !c.allowedRaces.length)
      throw Error(`Missing race compatibility: ${c.id}`);
    for (const race of c.allowedRaces)
      if (!races.has(race)) throw Error(`Unknown race: ${race}`);
  }
  for (const spell of data.spells)
    if (!classes.has(spell.classId))
      throw Error(`Unknown spell class: ${spell.classId}`);
  for (const e of [...data.items, ...data.abilities])
    for (const c of e.classes)
      if (!classes.has(c)) throw Error(`Unknown class: ${c}`);
  // 1.3.1: character rules exported from public functions and the weapon and shield templates
  const ch = data.characters;
  for (const key of [
    "proficiency",
    "experienceByLevel",
    "weaponTemplates",
    "shieldTemplates",
    "armorTypeWeights",
  ])
    if (!Array.isArray(ch[key]) || !ch[key].length)
      throw Error(`Empty dataset: characters/${key}`);
  if (!ch.constants || !Object.keys(ch.constants).length)
    throw Error("Empty dataset: characters/constants");
  for (const t of [...ch.weaponTemplates, ...ch.shieldTemplates])
    for (const c of t.classes)
      if (c !== "All" && !classes.has(c))
        throw Error(`Unknown template class: ${t.name}/${c}`);
  for (const c of data.classes)
    if (
      !c.equipment ||
      !Array.isArray(c.equipment.weapons) ||
      !Array.isArray(c.equipment.shields) ||
      !Array.isArray(c.equipment.armorWeights)
    )
      throw Error(`Missing class equipment: ${c.id}`);
  // 1.3.2: the party: beasts, story companions, ally stances and the constants the party guides print
  const party = data.party;
  for (const key of ["beasts", "companions", "stances"])
    if (!Array.isArray(party[key]) || !party[key].length)
      throw Error(`Empty dataset: party/${key}`);
  if (!party.constants || !Object.keys(party.constants).length)
    throw Error("Empty dataset: party/constants");
  for (const c of party.companions)
    if (!classes.has(c.combatClass))
      throw Error(`Unknown companion class: ${c.id}/${c.combatClass}`);
  // 1.3.2: items and the town economy
  const economy = data.economy;
  for (const key of [
    "materials",
    "materialGods",
    "enchants",
    "gearSets",
    "lootRarities",
    "herbs",
    "artifacts",
  ])
    if (!Array.isArray(economy[key]) || !economy[key].length)
      throw Error(`Empty dataset: economy/${key}`);
  if (
    !economy.homeUpgrades ||
    !economy.homeUpgrades.levels?.length ||
    !economy.homeUpgrades.trainingRoom?.length
  )
    throw Error("Empty dataset: economy/homeUpgrades");
  if (!economy.constants || !Object.keys(economy.constants).length)
    throw Error("Empty dataset: economy/constants");
  const materialIds = new Set(economy.materials.map((m) => m.id));
  const enchantNumbers = new Set(economy.enchants.map((e) => e.number));
  for (const e of economy.enchants)
    for (const m of e.materials)
      if (!materialIds.has(m.id))
        throw Error(`Unknown enchant material: ${e.number}/${m.id}`);
  for (const m of economy.materials)
    for (const n of m.enchants)
      if (!enchantNumbers.has(n))
        throw Error(`Unknown material enchant: ${m.id}/${n}`);
  for (const g of economy.materialGods)
    if (!materialIds.has(g.material))
      throw Error(`Unknown Old God material: ${g.god}/${g.material}`);
  // 1.3.2: combat: statuses, monster target weights by class, regeneration and the combat constants
  const combat = data.combat;
  for (const key of ["statuses", "targetWeights", "regeneration"])
    if (!Array.isArray(combat[key]) || !combat[key].length)
      throw Error(`Empty dataset: combat/${key}`);
  if (!combat.constants || !Object.keys(combat.constants).length)
    throw Error("Empty dataset: combat/constants");
  for (const w of combat.targetWeights)
    if (!classes.has(w.classId))
      throw Error(`Unknown target weight class: ${w.classId}`);
  // 1.3.2: the dungeon and the world beyond the town
  const world = data.world;
  for (const key of [
    "monsterAbilities",
    "floors",
    "outposts",
    "wildernessRegions",
    "druidShrines",
    "gauntletTiers",
    "gauntletChampions",
    "mercRanks",
    "mercContracts",
  ])
    if (!Array.isArray(world[key]) || !world[key].length)
      throw Error(`Empty dataset: world/${key}`);
  if (!world.constants || !Object.keys(world.constants).length)
    throw Error("Empty dataset: world/constants");
  const regionIds = new Set(world.wildernessRegions.map((r) => r.id));
  for (const s of world.druidShrines)
    if (!regionIds.has(s.region))
      throw Error(`Unknown shrine region: ${s.id}/${s.region}`);
  // 1.3.2: gods, the town's institutions, online play and the NPC world
  const realm = data.realm;
  for (const key of [
    "godRanks",
    "divineBoons",
    "alignmentTypes",
    "factionRanks",
    "factionBonuses",
    "worldEvents",
    "oldGodMechanics",
    "monsterGuards",
    "worldBossRewards",
  ])
    if (!Array.isArray(realm[key]) || !realm[key].length)
      throw Error(`Empty dataset: realm/${key}`);
  if (!realm.constants || !Object.keys(realm.constants).length)
    throw Error("Empty dataset: realm/constants");
  if (realm.factionRanks.length !== realm.factionBonuses.length)
    throw Error("Faction ranks and bonuses differ in length");
  return { meta, data };
}
function renderers(data) {
  const find = (list, id) => {
    const e = list.find(
      (e) =>
        String(e.id).toLowerCase() === id.toLowerCase() ||
        name(e).toLowerCase() === id.toLowerCase(),
    );
    if (!e) throw Error(`Unknown entity: ${id}`);
    return e;
  };
  const constants = {
    ...data.balance.moddableDefaults,
    ...data.balance.characterConstants,
    ...data.balance.combatConstants,
    ...data.balance.godConstants,
    ...data.mental.constants,
    ...data.gods.favorRules,
    ...data.characters.constants,
  };
  // 1.3.2: a party or economy constant may not reuse a name another dataset already prints
  for (const [key, value] of Object.entries(data.party.constants)) {
    if (key in constants) throw Error(`Duplicate constant: ${key}`);
    constants[key] = value;
  }
  for (const [key, value] of Object.entries(data.economy.constants)) {
    if (key in constants) throw Error(`Duplicate constant: ${key}`);
    constants[key] = value;
  }
  for (const [key, value] of Object.entries(data.combat.constants)) {
    if (key in constants) throw Error(`Duplicate constant: ${key}`);
    constants[key] = value;
  }
  for (const [key, value] of Object.entries(data.world.constants)) {
    if (key in constants) throw Error(`Duplicate constant: ${key}`);
    constants[key] = value;
  }
  for (const [key, value] of Object.entries(data.realm.constants)) {
    if (key in constants) throw Error(`Duplicate constant: ${key}`);
    constants[key] = value;
  }
  const className = (id) => name(data.classes.find((c) => c.id === id));
  // "All" in a template's class list means no class list; every class may use it
  const templateClasses = (t) =>
    t.classes.includes("All")
      ? "Every class"
      : escape(t.classes.map(className).join(", "));
  const typeLabel = (type) => label(type);
  // 1.3.2 economy tables
  const num = (v) => Number(v).toLocaleString("en-US");
  const ordinal = (n) =>
    n + (n % 100 >= 11 && n % 100 <= 13 ? "th" : ["th", "st", "nd", "rd"][n % 10] || "th");
  const enchantName = (n) =>
    name(data.economy.enchants.find((e) => e.number === n));
  // what a named enchant adds, by the item field it changes (MagicShopLocation.ApplyNamedEnchant)
  const enchantFields = {
    StrengthBonus: "Strength",
    DexterityBonus: "Dexterity",
    DefenceBonus: "Defence",
    WisdomBonus: "Wisdom",
    IntelligenceBonus: "Intelligence",
    WeaponPower: "weapon power",
    ArmorClass: "armor class",
    MagicResistance: "magic resistance",
    CriticalChanceBonus: "percent critical hit chance",
    CriticalDamageBonus: "percent critical damage",
    LifeSteal: "percent life steal",
    HasFireEnchant: "a chance of extra fire damage on a hit",
    HasFrostEnchant: "a chance to slow on a hit",
  };
  const enchantEffect = ([field, value]) => {
    if (!(field in enchantFields)) throw Error(`Unknown enchant effect: ${field}`);
    return value === true ? enchantFields[field] : `+${value} ${enchantFields[field]}`;
  };
  // 1.3.2 world tables. The words for each monster ability effect the export probes (WikiDataExporter.
  // AbilityEffects), in column order; null marks a field shown in its own column. A new field fails the build.
  // a probed range whose lowest value is 0 happens only some of the time
  const upTo = (v, n) => (typeof v === "object" && v.min === 0 ? `up to ${num(v.max)}` : n(v));
  const abilityEffectLabels = {
    attackPowerMultiplier: null,
    directDamage: null,
    status: null,
    statusChance: null,
    statusRounds: null,
    lifeStealPercent: (v, n) => `heals ${upTo(v, n)} percent of the damage it deals`,
    manaDrain: (v, n) => `drains ${n(v)} of your mana`,
    summons: (v, n) => `calls ${n(v)} more monsters`,
    endsTurn: (v) => (v === true ? "ends its turn" : "sometimes ends its turn"),
    monsterHP: (v, n) =>
      typeof v === "number" && v < 0
        ? `loses ${num(-v)} percent of its maximum HP`
        : `heals ${upTo(v, n)} percent of its maximum HP`,
    monsterStrength: (v, n) => (typeof v === "number" ? `+${num(v)} Strength` : `Strength changes by ${n(v)}`),
    monsterArmPow: (v, n) => (typeof v === "number" ? `+${num(v)} armor` : `armor changes by ${n(v)}`),
    monsterEvasionMissChance: (v, n, e) =>
      `${upTo(v, n)} percent of attacks on it miss for ${upTo(e.monsterEvasionRounds, n)} rounds`,
    monsterEvasionRounds: null,
    monsterThornsPercent: (v, n, e) =>
      `melee hits on it cost the attacker ${upTo(v, n)} percent of their damage for ${upTo(e.monsterThornsRounds, n)} rounds`,
    monsterThornsRounds: null,
  };
  const abilityUserKinds = {
    monster: "",
    signature: " (signature)",
    shaman: " Shaman",
    wilderness: " (wilderness)",
    gauntlet: " (Gauntlet)",
  };
  // the foraging results of WildernessLocation.ApplyForagingResult, by effect token
  const forageEffects = {
    herb_healing: "a herb",
    herb_ironbark: "a herb",
    herb_firebloom: "a herb",
    herb_starbloom: "a herb",
    herb_swift: "a herb",
    heal_small: "small heal",
    heal_medium: "medium heal",
    gold_small: "small gold",
    gold_medium: "medium gold",
    gold_large: "large gold",
    nothing: "nothing",
  };
  const discoveryKinds = { combat: "fight", shrine: "shrine", ruins: "ruins", traveler: "traveller" };
  // what a Sellsword contract objective asks (QuestObjectiveType), with its target
  const mercObjectives = {
    KillSpecificMonster: (t) => `slay ${t} monsters`,
    KillMonsters: () => "slay any monsters",
    ExploreRooms: () => "explore new dungeon rooms",
    VisitLocation: (t) => `visit the ${label(t)}`,
  };
  // Abilities whose effect turns monsters onto the user: CombatEngine.ApplyAbilityEffectsMultiMonster
  // cases aoe_taunt, undertow, abyssal_anchor and eternal_vigil, and ApplyTankTauntAndBuff for the
  // three level 40 abilities. A list here, because the template holds only the effect name.
  const tauntEffects = new Set([
    "aoe_taunt",
    "shield_wall_formation",
    "divine_mandate",
    "rage_challenge",
    "undertow",
    "abyssal_anchor",
    "eternal_vigil",
  ]);
  // The class healing bonus on abilities (ClassAbilitySystem.UseAbility), by class
  // classes whose equipment limits the guides do not state yet (none now)
  const heldEquipmentClasses = new Set([]);
  const classHealBonus = {
    Alchemist: "AlchemistPotionMasteryBonus",
    Cleric: "ClericDivineGraceBonus",
    Tidesworn: "TideswornOceansBlessingBonus",
    Wavecaller: "WavecallerHarmonicResonanceBonus",
  };
  // Mental gains by source, each a GameConfig constant; the label says where the game applies it
  const mentalRecovery = [
    ["MentalDailyReset", "A new day"],
    ["MentalDungeonCampGain", "Dungeon camp"],
    ["MentalSafeHavenGain", "Safe Haven rest"],
    ["MentalInnTableGain", "Inn table rest"],
    ["MentalInnFriendGain", "Inn table rest with a friend present"],
    ["MentalInnSleepGain", "Inn sleep or a rented room"],
    ["MentalHomeRestGain", "Home rest"],
    ["MentalHomeSleepGain", "Home sleep"],
    ["MentalSpouseGain", "Time with your spouse"],
    ["MentalTemplePrayerGain", "Temple daily prayer"],
    ["MentalConfessionGain", "Church confession"],
    ["MentalFriendTalkGain", "Talking with a friend"],
    ["MentalWildernessGain", "First wilderness exploration of the day"],
    ["MentalLearningGain", "Learning: a new spell, a training session or Library reading"],
    ["MentalWillowDraughtGain", "Willow Draught, drunk in the dungeon"],
    ["MentalRehabGain", "Addiction Rehab at the Healer"],
    ["MentalGriefAcceptanceGain", "A grief reaching Acceptance"],
  ];
  const abilityCost = (a) =>
    [
      a.staminaCost ? `${a.staminaCost} stamina` : "",
      a.manaCost ? `${a.manaCost} mana` : "",
    ]
      .filter(Boolean)
      .join(" and ") || "no cost";
  const abilityLine = (a) =>
    `${link("characters/abilities", a)} (level ${a.levelRequired}, ${abilityCost(a)}, cooldown ${a.cooldown} rounds${a.requiresShield ? ", needs a shield" : ""})`;
  function classRoles() {
    return data.classes
      .map((c) => {
        const mine = data.abilities
          .filter((a) => a.classes.includes(c.id))
          .sort((a, b) => a.levelRequired - b.levelRequired);
        const list = (items) =>
          items.length ? items.map(abilityLine).join("; ") : "none";
        const g = c.growthPerLevel;
        const roles = c.specializations.length
          ? `The game lists these roles through its specializations: ${c.specializations.map((s) => `${escape(name(s))} is ${escape(s.role)}`).join(", ")}.`
          : `The class has no specialization; where the game lists roles for it, it lists ${escape(c.defaultRoles.join(" and "))}.`;
        const heal = classHealBonus[c.id]
          ? ` Its abilities heal ${Math.round(constants[classHealBonus[c.id]] * 100)} percent more.`
          : "";
        return (
          // level four: the search index weighs h2 and h3 words, and a class name in seventeen
          // headings would pull every question that names a class to this page
          `<h4 id="${slug(name(c))}-role-numbers">${escape(name(c))} role numbers</h4>` +
          `<p>${escape(name(c))} gains ${g.maxHP} maximum HP, ${g.defence} Defence and ${g.maxMana} maximum mana per level, before specialization. ` +
          `${c.usesMana ? "It has mana." : "It has no mana and pays for abilities with combat stamina."} ` +
          `${heldEquipmentClasses.has(c.id) ? "" : `Armor weights it can wear: ${escape(c.equipment.armorWeights.join(", "))}. `}${roles}${heal}</p>` +
          `<p>Taunt abilities: ${list(mine.filter((a) => tauntEffects.has(a.specialEffect)))}.</p>` +
          `<p>Defense abilities: ${list(mine.filter((a) => a.type === "Defense" && !tauntEffects.has(a.specialEffect)))}.</p>` +
          `<p>Heal abilities: ${list(mine.filter((a) => a.type === "Heal"))}.</p>`
        );
      })
      .join("");
  }
  function templateTables(templates, order, hands) {
    return order
      .map((type) => {
        const rows = templates.filter((t) => t.weaponType === type);
        if (!rows.length) return "";
        const heading = `${typeLabel(type)}: classes that can use each one`;
        return (
          `<h3 id="${slug(heading)}">${escape(heading)}</h3>` +
          table(
            [typeLabel(type), ...(hands ? ["Hands"] : []), "Levels", "Classes"],
            rows.map((t) => [
              escape(t.name),
              ...(hands ? [t.handedness === "TwoHanded" ? "Two" : "One"] : []),
              `${t.minLevel} to ${t.maxLevel}`,
              templateClasses(t),
            ]),
            `${typeLabel(type)} templates`,
          )
        );
      })
      .join("");
  }
  function god(e) {
    const boons = Object.fromEntries(
      Object.entries(e.boonConstants).map(([k, v]) => [
        k.replace("GodBoon" + name(e), ""),
        v,
      ]),
    );
    return (
      `<p>Domain: ${escape(e.domainName.en)}. Wards: ${escape(e.wards.join(", ") || "None")}.</p>` +
      "<p>Boon constants below are base values. Favor strength and an active prayer change their effective size. Percent fields are percentages, not additional flat attributes.</p>" +
      values(boons, "Base boon rules") +
      table(
        ["Deed or taboo", "Favor change"],
        e.deedsAndTaboos.map((d) => [escape(label(d.act)), escape(d.favor)]),
        "Deeds and taboos",
      ) +
      spoiler(
        "Spoiler: divine lore",
        `<p>${escape(e.description)}</p><p>Old God echo: ${escape(e.echoesOldGod || "None")}.</p>`,
      )
    );
  }
  function monster(e) {
    return (
      `<p>Attack type: ${escape(e.attackType)}. Samples are normal non-boss monsters at the tier endpoints. NG+, server mods and special encounters change live values.</p>` +
      table(
        [
          "Tier",
          "Level range",
          "HP at min / max",
          "Strength at min / max",
          "Defence at min / max",
          "Abilities",
        ],
        e.tiers.map((t) => [
          escape(name(t)),
          `${t.minLevel} - ${t.maxLevel}`,
          `${t.normalStatsAtMinLevel.hp} / ${t.normalStatsAtMaxLevel.hp}`,
          `${t.normalStatsAtMinLevel.strength} / ${t.normalStatsAtMaxLevel.strength}`,
          `${t.normalStatsAtMinLevel.defence} / ${t.normalStatsAtMaxLevel.defence}`,
          escape(t.abilities.join(", ")),
        ]),
        "Monster tier samples",
      ) +
      spoiler("Spoiler: family lore", `<p>${escape(e.description)}</p>`)
    );
  }
  function boss(e, heading = 2) {
    const fields = Object.fromEntries(
      Object.entries(e).filter(
        ([k]) =>
          k.startsWith("base") ||
          [
            "attacksPerRound",
            "dungeonFloor",
            "floorLevel",
            "element",
            "rewardXP",
            "rewardGold",
            "asksQuestion",
          ].includes(k),
      ),
    );
    const phases = e.phases
      ? e.phases
          .map(
            (p, i) =>
              `<h${heading}>Phase ${i + 1}</h${heading}><ul>${p.map((a) => `<li>${escape(typeof a === "string" ? a : a.name + ": " + a.description)}</li>`).join("")}</ul>`,
          )
          .join("")
      : "";
    return (
      `<p>${escape(e.title)}. These are base definitions, not a guarantee of encounter stats.</p>` +
      values(fields, "Boss base definition") +
      phases
    );
  }
  function directive(raw) {
    const match = raw.trim().match(/^([\w-]+):([\w:-]+)(?:\s+(.+))?$/);
    if (!match) throw Error(`Invalid directive: ${raw}`);
    const [, kind, id, filter] = match;
    if (filter && kind !== "table") throw Error(`Unexpected filter: ${raw}`);
    if (kind === "balance") {
      if (!(id in constants)) throw Error(`Unknown balance constant: ${id}`);
      return escape(constants[id]);
    }
    if (kind === "percent") {
      // a fraction constant as whole percent: 0.25 prints 25
      if (!(id in constants) || typeof constants[id] !== "number")
        throw Error(`Unknown percent constant: ${id}`);
      return escape(Math.round(constants[id] * 100));
    }
    if (kind === "god") return god(find(data.gods.gods, id));
    if (kind === "monster-family") return monster(find(data.monsters, id));
    if (kind !== "table") throw Error(`Unknown directive: ${kind}`);
    let rows = data[id];
    if (filter) {
      const m = filter.match(/^(slot|classId)=([\w]+)$/);
      if (!m || !Array.isArray(rows) || !rows.some((r) => r[m[1]] === m[2]))
        throw Error(`Invalid table filter: ${raw}`);
      rows = rows.filter((r) => r[m[1]] === m[2]);
    }
    if (id === "items")
      return table(
        ["Item", "Slot", "Level", "Rarity", "Base value", "Weapon / armor"],
        rows.map((e) => [
          link("items", e),
          escape(e.slot),
          e.minLevel,
          escape(e.rarity),
          e.value,
          `${e.stats.weaponPower} / ${e.stats.armorClass}`,
        ]),
        "Built-in equipment",
      );
    if (id === "classes")
      return table(
        [
          "Class",
          "Type",
          "HP per level",
          "Defence per level",
          "Mana per level",
          "Roles the game lists",
          "Specializations",
        ],
        rows.map((e) => [
          link("characters/classes", e),
          e.prestige ? "Prestige" : "Starting",
          e.growthPerLevel.maxHP,
          e.growthPerLevel.defence,
          e.growthPerLevel.maxMana,
          escape(e.defaultRoles.join(", ")),
          escape(e.specializations.map(name).join(", ")),
        ]),
        "Classes",
      );
    if (id === "class-roles") return classRoles();
    if (id === "class-equipment") {
      const types = data.classes[0].equipment.weapons.map((w) => w.type);
      const shields = data.classes[0].equipment.shields.map((w) => w.type);
      return (
        `<p>Armor weight by armor type: ${data.characters.armorTypeWeights.map((a) => `${escape(a.armorType)} is ${escape(a.weightClass)}`).join(", ")}.</p>` +
        table(
          ["Class", "Armor weights", ...types.map(typeLabel), ...shields.map(typeLabel)],
          data.classes.filter((c) => !heldEquipmentClasses.has(c.id)).map((c) => [
            link("characters/classes", c),
            escape(c.equipment.armorWeights.join(", ")),
            ...[...c.equipment.weapons, ...c.equipment.shields].map(
              (w) => `${w.usable} of ${w.total}`,
            ),
          ]),
          "Weapon and shield templates each class can use",
        )
      );
    }
    if (id === "weapon-templates")
      return (
        templateTables(
          data.characters.weaponTemplates,
          data.classes[0].equipment.weapons.map((w) => w.type),
          true,
        ) +
        templateTables(
          data.characters.shieldTemplates,
          data.classes[0].equipment.shields.map((w) => w.type),
          false,
        )
      );
    if (id === "proficiency")
      return table(
        [
          "Tier",
          "Effect multiplier",
          "Roll modifier",
          "Spell fumble chance (%)",
          "Training points to the next tier",
          "Chance to improve by use (%)",
        ],
        data.characters.proficiency.map((e) => [
          escape(e.id),
          e.effectMultiplier.toFixed(2),
          e.rollModifier,
          e.failureChance,
          e.trainingPointsToNext ?? "None",
          e.improveByUseChance,
        ]),
        "Proficiency tiers",
      );
    if (id === "xp-levels")
      return table(
        [
          "Level",
          "Total experience",
          "Experience from the level before",
          "Kill experience multiplier",
        ],
        data.characters.experienceByLevel.map((e, i, all) => [
          `Level ${e.level}`,
          e.totalExperience,
          i ? e.totalExperience - all[i - 1].totalExperience : 0,
          e.killExperienceMultiplier,
        ]),
        "Experience for each level",
      );
    if (id === "mental-recovery")
      return table(
        ["Source", "Mental restored"],
        mentalRecovery.map(([key, text]) => {
          if (!(key in data.mental.constants))
            throw Error(`Unknown Mental constant: ${key}`);
          return [escape(text), data.mental.constants[key]];
        }),
        "Mental recovery by source",
      );
    if (id === "races")
      return table(
        [
          "Race",
          "HP bonus",
          "Strength bonus",
          "Defence bonus",
          "Stamina bonus",
          "NPC lifespan (game years)",
          "NPC lifespan (real days)",
        ],
        rows.map((e) => [
          link("characters/races", e),
          ...["hpBonus", "strengthBonus", "defenceBonus", "staminaBonus"].map(
            (k) => e.bonusesAndCreationRanges[k],
          ),
          e.npcLifespanYears,
          // a game year of an NPC is NpcLifecycleHoursPerYear real hours (WorldSimulator.ProcessNPCAging)
          num(Math.round((e.npcLifespanYears * constants.NpcLifecycleHoursPerYear) / 24)),
        ]),
        "Race creation bonuses and NPC lifespans",
      );
    if (id === "monsters")
      return table(
        ["Family", "Attack type", "Tiers"],
        rows.map((e) => [
          link("monsters/families", e),
          escape(e.attackType),
          e.tiers.length,
        ]),
        "Monster families",
      );
    if (id === "gods")
      return table(
        ["God", "Domain", "Wards"],
        data.gods.gods.map((e) => [
          link("gods", e),
          escape(e.domainName.en),
          escape(e.wards.join(", ")),
        ]),
        "Canonical gods",
      );
    if (id === "favor")
      return table(
        ["Tier", "Minimum Favor", "Boon strength (%)"],
        data.gods.tiers.map((e) => [
          escape(e.name),
          e.minFavor,
          e.strengthPercent,
        ]),
        "Favor tiers",
      );
    if (id === "mental")
      return table(
        ["Band", "Minimum", "Maximum"],
        data.mental.bands.map((e) => [escape(e.name), e.min, e.max]),
        "Mental bands",
      );
    if (id === "spells")
      return table(
        // levelRequired is the level that opens the spell; manaCost is what a cast
        // charges before the caster's Wisdom lowers it (WikiDataExporter.Spells)
        ["Spell", "Class", "Required level", "Mana (before Wisdom)", "Effect"],
        rows.map((e) => [
          link("characters/spells", e),
          escape(e.classId),
          e.levelRequired,
          e.manaCost,
          escape(e.description.en),
        ]),
        "Spell definitions",
      );
    if (id === "abilities")
      return table(
        [
          "Ability",
          "Classes",
          "Required level",
          "Stamina / mana",
          "Cooldown (rounds)",
          "Base damage / healing",
          "Attack / defense bonus",
          "Duration (rounds)",
          "Effect",
        ],
        rows.map((e) => [
          link("characters/abilities", e),
          escape(e.classes.join(", ")),
          e.levelRequired,
          `${e.staminaCost} / ${e.manaCost}`,
          e.cooldown,
          `${e.baseDamage} / ${e.baseHealing}`,
          `${e.attackBonus} / ${e.defenseBonus}`,
          e.duration,
          escape(e.description.en),
        ]),
        "Ability definitions",
      );
    if (id === "achievements")
      return (
        table(
          ["Achievement", "Category", "Tier", "Points", "Goal"],
          rows
            .filter((e) => !e.isSecret)
            .map((e) => [
              escape(name(e)),
              escape(e.category),
              escape(e.tier),
              e.pointValue,
              escape(e.description.en),
            ]),
          "Public achievements",
        ) +
        spoiler(
          "Spoiler: secret achievements",
          table(
            ["Achievement", "Goal"],
            rows
              .filter((e) => e.isSecret)
              .map((e) => [escape(name(e)), escape(e.description.en)]),
            "Secret achievements",
          ),
        )
      );
    if (id === "bosses")
      return table(
        ["Boss", "Element", "Base level"],
        data.bosses.world.map((e) => [
          link("monsters/bosses", e),
          escape(e.element),
          e.baseLevel,
        ]),
        "World bosses",
      );
    if (id === "secret-bosses")
      return spoiler(
        "Spoiler: story and secret bosses",
        [...data.bosses.oldGods, ...data.bosses.secret]
          .map((e) => `<h2>${escape(name(e))}</h2>${boss(e, 3)}`)
          .join(""),
      );
    if (id === "beasts")
      return table(
        [
          "Beast",
          "Species",
          "Kind",
          "Region",
          "Level to meet it",
          "Taming difficulty",
          "Base HP / attack / defence",
        ],
        data.party.beasts.map((b) => [
          escape(name(b)),
          escape(b.species.en),
          b.role === "Combat" ? "Fights with you" : "Passive bonus",
          `${escape(b.region.name.en)} (${escape(String(b.region.direction).toLowerCase())})`,
          b.sightedFromLevel ?? "Never",
          b.tameDifficulty,
          b.combatBase
            ? `${b.combatBase.hp} / ${b.combatBase.attack} / ${b.combatBase.defence}`
            : "None",
        ]),
        "Tameable beasts",
      );
    if (id === "companions")
      return (
        table(
          [
            "Companion",
            "Title",
            "Role",
            "Fights as",
            "Recruit level",
            "Main hand weapon types",
            "Romance",
          ],
          data.party.companions.map((c) => [
            escape(name(c)),
            escape(c.title.en),
            escape(c.role),
            link(
              "characters/classes",
              data.classes.find((x) => x.id === c.combatClass),
            ),
            c.recruitLevel,
            escape(c.mainHandWeaponTypes.map(typeLabel).join(", ")),
            c.romanceAvailable ? "Yes" : "No",
          ]),
          "Story companions",
        ) +
        table(
          ["Companion", "HP", "Attack", "Defence", "Magic", "Speed", "Healing"],
          data.party.companions.map((c) => [
            escape(name(c)),
            ...["hp", "attack", "defence", "magic", "speed", "healing"].map(
              (k) => c.baseStats[k],
            ),
          ]),
          "Story companion starting values at level 1",
        )
      );
    if (id === "ally-stances") {
      const pct = (v) => Math.round(v * 100);
      return table(
        [
          "Stance",
          "Drinks its own potion first below (% HP)",
          "Gives a potion to the most hurt member below (% HP)",
          "Uses a defensive ability first below (% HP)",
          "Braces below (% HP)",
          "Starts a taunt",
          "Weight as a monster's target (%)",
        ],
        data.party.stances.map((s) => [
          escape(s.id),
          pct(s.emergencySelfPotion),
          pct(s.potionMostInjured),
          pct(s.defensiveFirst),
          pct(s.brace),
          s.mayTaunt ? "Yes" : "No",
          pct(s.targetWeightMultiplier),
        ]),
        "Ally stances",
      );
    }
    if (id === "materials")
      return table(
        ["Material", "Dungeon floors", "Spent on"],
        data.economy.materials.map((m) => [
          escape(name(m)),
          `${m.floorMin} to ${m.floorMax}`,
          escape(
            [
              ...m.enchants.map((n) => `${enchantName(n)} enchant`),
              ...m.statTrainings.map((n) => `${ordinal(n)} Inn stat training`),
            ].join(", ") || "Nothing spends it",
          ),
        ]),
        "Crafting materials",
      );
    if (id === "old-god-rewards") {
      const materialName = (id) =>
        name(data.economy.materials.find((m) => m.id === id));
      const grant = (g) => `+${g.amount} ${label(g.stat)}`;
      return spoiler(
        "Spoiler: what each Old God leaves",
        table(
          ["Old God", "Floor", "Material"],
          data.economy.materialGods.map((g) => [
            escape(g.god),
            g.floor,
            escape(materialName(g.material)),
          ]),
          "Old God materials",
        ) +
          table(
            ["Artifact", "From", "Grants when collected"],
            data.economy.artifacts.map((a) => [
              escape(name(a)),
              a.oldGod
                ? `${escape(a.oldGod)}, floor ${a.floor}`
                : "Appears when you hold the other six",
              escape(
                [
                  ...a.grants.map(grant),
                  a.weaponPower ? `+${a.weaponPower} weapon power` : "",
                  a.chivalry ? `+${a.chivalry} Chivalry` : "",
                  a.darkness ? `+${a.darkness} Darkness` : "",
                ]
                  .filter(Boolean)
                  .join(", "),
              ),
            ]),
            "Artifacts",
          ),
      );
    }
    if (id === "enchants")
      return table(
        ["Number", "Enchant", "Effect", "Cost before modifiers", "Level", "Materials"],
        data.economy.enchants.map((e) => [
          e.number,
          escape(name(e)),
          escape(
            e.oneChosenStat
              ? `+${e.statBonus} to one stat you choose`
              : e.counter
                ? `In ${e.counter.god}'s fight it holds back ${e.counter.abilities.join(" and ")} and removes its divine armor`
                : Object.entries(e.effects).map(enchantEffect).join(", "),
          ),
          `${num(e.baseCost)} + ${num(e.costPerLevel)} per level`,
          e.minLevel,
          escape(
            e.materials
              .map(
                (m) =>
                  `${m.count} ${name(data.economy.materials.find((x) => x.id === m.id))}`,
              )
              .join(", ") || "None",
          ),
        ]),
        "Magic Shop enchants",
      );
    if (id === "gear-sets")
      return table(
        ["Set", "Bonuses by pieces worn", "Pieces that count"],
        data.economy.gearSets.map((s) => [
          escape(name(s)),
          escape(
            s.tiers
              .map(
                (t) =>
                  `${t.pieces} pieces: ${t.bonuses.map((b) => b.text).join(", ")}`,
              )
              .join("; "),
          ),
          escape(s.families.join(", ")),
        ]),
        "Gear sets",
      );
    if (id === "loot-rarity")
      return table(
        [
          "Rarity",
          "Name starts with",
          "Power multiplier",
          "Value multiplier",
          "Special effects",
          "Chance to be cursed (%)",
        ],
        data.economy.lootRarities.map((r) => [
          escape(r.id),
          escape(r.namePrefix ?? "Nothing"),
          r.powerMultiplier,
          r.valueMultiplier,
          r.minEffects === r.maxEffects
            ? r.minEffects
            : `${r.minEffects} to ${r.maxEffects}`,
          Math.round(r.curseChance * 100),
        ]),
        "Dropped item rarity",
      );
    if (id === "home-upgrades") {
      const h = data.economy.homeUpgrades;
      const pct = (v) => Math.round(v * 100);
      const cost = (v) => (v == null ? "Start" : num(v));
      const part = (heading, headers, row) =>
        `<h3 id="${slug(heading)}">${escape(heading)}</h3>` +
        table(
          ["Level", "Price", ...headers],
          h.levels.map((l) => [l.level, ...row(l)]),
          heading,
        );
      return (
        part("Living Quarters", ["Rest heals (% of max HP and mana)", "Rests a day"], (l) => [
          cost(l.livingQuartersCost),
          pct(l.restRecovery),
          l.restsPerDay,
        ]) +
        part("Bed", ["Chance of a child (x)"], (l) => [
          cost(l.bedCost),
          Math.round((1 + l.bedFertility) * 100) / 100,
        ]) +
        part("Chest", ["Items it holds"], (l) => [
          cost(l.chestCost),
          l.chestCapacity,
        ]) +
        part("Hearth", ["Well-Rested bonus (%)", "Fights it lasts"], (l) => [
          cost(l.hearthCost),
          pct(l.hearthBonus),
          l.hearthFights,
        ]) +
        part("Herb Garden", ["Herbs a day"], (l) => [
          cost(l.gardenCost),
          l.herbsPerDay,
        ]) +
        `<h3 id="training-room">Training Room</h3>` +
        table(
          ["Level", "Price"],
          h.trainingRoom.map((t) => [t.level, num(t.cost)]),
          "Training Room",
        )
      );
    }
    if (id === "herbs")
      return table(
        ["Herb", "Garden level", "Most you can carry", "Effect"],
        data.economy.herbs.map((e) => [
          escape(name(e)),
          e.gardenLevel,
          e.maxCarry,
          escape(e.effect),
        ]),
        "Herbs",
      );
    if (id === "statuses") {
      // the damage one tick does at a level, as "min to max", or the single number when they are equal
      const tick = (s, level) => {
        const d = s.damagePerRound.find((x) => x.level === level);
        if (!d) return "None";
        return d.min === d.max ? `${d.min}` : `${d.min} to ${d.max}`;
      };
      const levels = data.combat.statuses.find((s) => s.damagePerRound.length)
        .damagePerRound.map((d) => d.level);
      return table(
        [
          "Status",
          "Kind",
          "Stops you acting",
          "Stops spells",
          ...levels.map((l) => `Damage per round at level ${l}`),
          "Ends by itself",
        ],
        data.combat.statuses.map((s) => [
          escape(label(s.id)),
          escape(label(s.category)),
          s.preventsAction ? "Yes" : "No",
          s.preventsSpellcasting ? "Yes" : "No",
          ...levels.map((l) => tick(s, l)),
          s.countsDownEachRound ? "Yes" : "No",
        ]),
        "Combat statuses",
      );
    }
    if (id === "target-weights")
      return table(
        ["Class", "Weight", "Weight while you Defend"],
        [...data.combat.targetWeights]
          .sort((a, b) => b.weight - a.weight || a.classId.localeCompare(b.classId))
          .map((w) => [
            link(
              "characters/classes",
              data.classes.find((c) => c.id === w.classId),
            ),
            w.weight,
            w.weightWhileDefending,
          ]),
        "Monster target weight by class",
      );
    if (id === "combat-regeneration")
      return table(
        [
          "Stat",
          "Mana per round (Wisdom)",
          "Combat stamina per round (Stamina), light or no armor",
          "Medium armor",
          "Heavy armor",
        ],
        data.combat.regeneration.map((r) => [
          r.stat,
          r.manaPerRound,
          r.staminaPerRoundLight,
          r.staminaPerRoundMedium,
          r.staminaPerRoundHeavy,
        ]),
        "Mana and combat stamina regained at the end of each round",
      );
    // 1.3.2: the dungeon and the world beyond the town (world.json)
    if (id === "monster-abilities") {
      // a probed number: one value, or the lowest and highest of the runs
      const n = (v) =>
        v == null ? null : typeof v === "object" && !Array.isArray(v) ? `${num(v.min)} to ${num(v.max)}` : num(v);
      return table(
        [
          "Ability",
          "Hit (x attack power)",
          "Direct damage",
          "Status inflicted",
          "Other effects",
          "Changes when",
          "Used by",
        ],
        data.world.monsterAbilities.map((a) => {
          const e = a.effects;
          for (const key of Object.keys(e))
            if (!(key in abilityEffectLabels)) throw Error(`Unknown monster ability effect: ${a.id}/${key}`);
          const other = Object.entries(abilityEffectLabels)
            .filter(([key, f]) => f && key in e)
            .map(([key, f]) => f(e[key], n, e))
            .join(", ");
          // a fixed direct damage replaces the hit (CombatEngine.TryMonsterSpecialAbility)
          const direct =
            typeof e.directDamage === "number" ? e.directDamage > 0 : e.directDamage?.min > 0;
          const changes = [
            a.changesWhenMonsterHurt ? "the monster is badly hurt" : "",
            a.changesWhenTargetHurt ? "the target is badly hurt" : "",
            a.changesOnSecondUse ? "it is used again" : "",
          ].filter(Boolean);
          return [
            escape(label(a.id)),
            direct ? "No" : e.attackPowerMultiplier === 0 ? "None" : `x${n(e.attackPowerMultiplier)}`,
            e.directDamage ? n(e.directDamage) : "None",
            e.status
              ? escape(
                  `${[].concat(e.status).map(label).join(" or ")}, ${n(e.statusChance)} percent, ${n(e.statusRounds)} rounds`,
                )
              : "None",
            escape(other || "None"),
            escape(changes.join(", ") || "No"),
            escape(
              a.users
                .map((u) => `${u.name}${abilityUserKinds[u.kind] ?? (() => { throw Error(`Unknown ability user kind: ${u.kind}`); })()}`)
                .join(", "),
            ),
          ];
        }),
        "Monster special abilities",
      );
    }
    if (id === "dungeon-floors")
      return table(
        ["Floor", "Theme", "Rooms", "Floor boss"],
        data.world.floors.map((f) => [
          f.floor,
          escape(label(f.theme)),
          f.rooms,
          escape(f.boss),
        ]),
        "Dungeon floors",
      );
    if (id === "outposts")
      return table(
        ["Floor", "Outpost", "Healing", "Supplies", "Lore"],
        data.world.outposts.map((o) => [
          o.floor,
          escape(name(o)),
          o.healing
            ? escape(`${num(o.healCost)} gold, restores ${Math.round(o.healShareOfMissing * 100)} percent of missing HP and mana`)
            : "None",
          escape(o.goods.map((g) => `${g.name} ${num(g.price)}`).join(", ") || "None"),
          o.lore ? "Yes" : "No",
        ]),
        "Dungeon outposts",
      );
    if (id === "wilderness-regions")
      return table(
        ["Region", "Direction", "Level needed", "Monsters", "Foraging (one of five)", "Discoveries"],
        data.world.wildernessRegions.map((r) => [
          escape(name(r)),
          escape(r.direction.en),
          r.minLevel,
          escape(r.monsters.join(", ")),
          escape(
            r.foraging
              .map((f) => {
                if (!(f.effect in forageEffects)) throw Error(`Unknown foraging effect: ${r.id}/${f.effect}`);
                return f.effect === "nothing" ? "nothing" : `${f.name} (${forageEffects[f.effect]})`;
              })
              .join(", "),
          ),
          escape(
            r.discoveries
              .map((d) => {
                if (!(d.kind in discoveryKinds)) throw Error(`Unknown discovery kind: ${d.id}/${d.kind}`);
                return `${name(d)} (level ${d.minLevel}, ${discoveryKinds[d.kind]})`;
              })
              .join(", "),
          ),
        ]),
        "Wilderness regions",
      );
    if (id === "druid-shrines")
      return table(
        ["Shrine", "Region", "Attunement", "Alignment per visit"],
        data.world.druidShrines.map((sh) => [
          escape(name(sh)),
          escape(name(data.world.wildernessRegions.find((r) => r.id === sh.region))),
          escape(sh.passive),
          sh.chivalryShift > 0
            ? `${sh.chivalryShift} toward Chivalry`
            : sh.chivalryShift < 0
              ? `${-sh.chivalryShift} toward Darkness`
              : "None",
        ]),
        "Druid shrines",
      );
    if (id === "gauntlet-tiers")
      return table(
        ["Title", "Your level", "Gold (x level)", "Experience (x level)", "Fame"],
        data.world.gauntletTiers.map((t) => [
          escape(t.title),
          `${t.minLevel} to ${t.maxLevel}`,
          num(t.goldPerLevel),
          num(t.experiencePerLevel),
          t.fame,
        ]),
        "Gauntlet final bonus by level",
      );
    if (id === "gauntlet-champions")
      return table(
        ["Wave", "Champion", "Role", "Level", "HP", "Strength", "Defence", "Abilities", "Drop"],
        data.world.gauntletChampions.map((c) => [
          c.wave,
          escape(`${name(c)}, ${c.title}`),
          escape(label(c.role)),
          `your level + ${c.levelBonus}`,
          `x${c.hpMultiplier}`,
          `x${c.attackMultiplier}`,
          `x${c.defenseMultiplier}`,
          escape(c.abilities.map(label).join(", ")),
          escape(label(c.dropSlot)),
        ]),
        "Gauntlet champions",
      );
    if (id === "merc-ranks")
      return table(
        ["Rank", "Contracts completed", "Contracts per faction", "Pay factor"],
        data.world.mercRanks.map((r) => [
          escape(name(r)),
          r.contractsRequired,
          r.contractsPerFaction,
          `x${r.payMultiplier}`,
        ]),
        "Sellsword ranks",
      );
    if (id === "merc-contracts")
      return table(
        ["Contract", "Faction", "Tier", "Objective", "Count", "Days", "Pay factor"],
        data.world.mercContracts.map((c) => {
          if (!(c.objective in mercObjectives)) throw Error(`Unknown contract objective: ${c.id}/${c.objective}`);
          return [
            escape(c.title.en),
            escape(label(c.faction.replace(/^The/, ""))),
            c.tier,
            escape(mercObjectives[c.objective](c.target)),
            c.flatProgress ? `${c.baseCount}` : `${c.baseCount} + your level / 5`,
            c.days,
            `x${c.payMultiplier}`,
          ];
        }),
        "Sellsword Hall contracts",
      );
    // 1.3.2 realm tables (realm.json): gods, the town's institutions, online play and the NPC world
    if (id === "god-ranks")
      return table(
        [
          "Rank",
          "Title",
          "Divine experience needed",
          "Deeds a day",
          "Experience per believer a day",
          "Favors budget (no believers)",
          "Favors budget (10 or more believers)",
        ],
        data.realm.godRanks.map((r) => [
          `Rank ${r.rank}`,
          escape(r.title.en),
          num(r.experience),
          r.deedsPerDay,
          r.experiencePerBeliever,
          r.favorBudgetNoBelievers,
          r.favorBudget,
        ]),
        "Player-god ranks",
      );
    if (id === "divine-boons") {
      // the words for each effect field of a divine boon (DivineBoonRegistry.BoonDefinition); a new field fails the build
      const boonWords = {
        DamagePercent: (v) => `+${Math.round(v * 100)} percent damage`,
        DefensePercent: (v) => `+${Math.round(v * 100)} percent defence`,
        CritPercent: (v) => `+${Math.round(v * 100)} critical hit chance`,
        LifestealPercent: (v) => `heals ${Math.round(v * 100)} percent of damage dealt`,
        XPPercent: (v) => `+${Math.round(v * 100)} percent combat experience`,
        GoldPercent: (v) => `+${Math.round(v * 100)} percent combat gold`,
        ShopDiscountPercent: (v) => `${Math.round(v * 100)} percent off at the Weapon and Armor shops`,
        MaxHPPercent: (v) => `+${Math.round(v * 100)} percent maximum HP`,
        MaxManaPercent: (v) => `+${Math.round(v * 100)} percent maximum mana`,
        FleePercent: (v) => `+${Math.round(v * 100)} flee chance`,
        LuckPercent: (v) => `+${Math.round(v * 100)} to a monster's equipment drop chance`,
        FlatAttack: (v) => `+${v} attack`,
        FlatDefense: (v) => `+${v} defence`,
      };
      const tierText = (t) =>
        Object.entries(t.effects)
          .map(([k, v]) => {
            if (!(k in boonWords)) throw Error(`Unknown divine boon effect: ${k}`);
            return boonWords[k](v);
          })
          .join(", ");
      return table(
        ["Favor", "Group", "Alignments", "Points a tier", "Tier 1", "Tier 2", "Tier 3"],
        data.realm.divineBoons.map((b) => [
          escape(name(b)),
          escape(b.category),
          escape(b.alignments.length ? b.alignments.join(", ") : "Any"),
          b.costPerTier,
          ...b.tiers.map((t) => escape(tierText(t))),
        ]),
        "Favors a player-god can give",
      );
    }
    if (id === "alignment-types")
      return table(
        [
          "Alignment",
          "Attack",
          "Defence",
          "Critical hit chance",
          "Melee heals you (% of damage)",
          "Fear on a hit (%)",
          "HP regained a round (% of max)",
          "Extra damage to undead and demons (%)",
        ],
        data.realm.alignmentTypes.map((a) => [
          escape(name(a)),
          `x${a.attackMultiplier}`,
          `x${a.defenceMultiplier}`,
          a.critChanceBonus ? `+${a.critChanceBonus}` : "None",
          a.soulDrainPercent || "None",
          a.fearOnHitPercent || "None",
          a.regenPercentPerRound || "None",
          a.holySmitePercent || "None",
        ]),
        "Alignment effects in combat",
      );
    if (id === "faction-ranks")
      return table(
        ["Rank", "Reputation needed", "The Crown", "The Shadows", "The Faith"],
        data.realm.factionRanks.map((r) => [
          `Rank ${r.rank}`,
          num(r.reputation),
          escape(r.titles.TheCrown),
          escape(r.titles.TheShadows),
          escape(r.titles.TheFaith),
        ]),
        "Faction ranks",
      );
    if (id === "faction-bonuses")
      return table(
        [
          "Rank",
          "Crown: shop discount (%)",
          "Crown: guard help (%)",
          "Shadows: sell price bonus (%)",
          "Shadows: flee bonus",
          "Shadows: Black Market discount (%)",
          "Faith: blessing length",
          "Faith: rescue from death (%)",
        ],
        data.realm.factionBonuses.map((r) => [
          `Rank ${r.rank}`,
          r.crownShopDiscountPercent,
          r.crownGuardHelpPercent,
          r.shadowsSellBonusPercent,
          `+${r.shadowsFleeBonus}`,
          r.shadowsBlackMarketDiscountPercent,
          `x${r.faithBlessingLengthMultiplier}`,
          r.faithRescueChancePercent,
        ]),
        "Faction bonuses by rank",
      );
    if (id === "world-events") {
      const seasonNames = ["spring", "summer", "autumn", "winter"];
      const factor = (v) => (v == null ? "None" : `x${v}`);
      return table(
        ["Event", "Days", "Shop prices", "Kill experience", "Kill gold", "Drawn by the daily roll"],
        data.realm.worldEvents.map((e) => [
          escape(name(e)),
          e.minDays === e.maxDays ? e.minDays : `${e.minDays} to ${e.maxDays}`,
          factor(e.price),
          factor(e.experience),
          factor(e.gold),
          !e.randomSeasons.length
            ? "No"
            : e.randomSeasons.length === 4
              ? "Any season"
              : escape(e.randomSeasons.map((n) => seasonNames[n]).join(", ")),
        ]),
        "World events",
      );
    }
    if (id === "old-god-mechanics") {
      const every = (n, dmg) => (n ? `every ${n} rounds, ${num(dmg)}` : "None");
      const immune = (g) =>
        [g.physicalImmunityPhase ? "physical" : "", g.magicalImmunityPhase ? "magical" : ""]
          .filter(Boolean)
          .join(", ") || "None";
      return spoiler(
        "Spoiler: Old God fight mechanics",
        table(
          [
            "Old God",
            "Phase 2 at HP (%)",
            "Phase 3 at HP (%)",
            "Enrages at round",
            "Area attack",
            "Channel (lands 2 rounds later)",
            "Corruption per stack a round",
            "Doom rounds (phase 3)",
            "Immunity phases",
            "Cut to unenchanted damage (%)",
          ],
          data.realm.oldGodMechanics.map((g) => [
            escape(name(g)),
            g.phase2AtHpPercent,
            g.phase3AtHpPercent,
            g.enrageRound,
            every(g.areaEveryRounds, g.areaDamage),
            every(g.channelEveryRounds, g.channelDamage),
            g.corruptionPerStack || "None",
            g.doomRounds || "None",
            immune(g),
            g.unenchantedDamageCutPercent || "None",
          ]),
          "Old God fight mechanics",
        ),
      );
    }
    if (id === "monster-guards")
      return table(
        ["Monster guard", "Level", "Price", "HP", "Strength", "Defence", "Feeding a day"],
        data.realm.monsterGuards.map((m) => [
          escape(name(m)),
          m.level,
          num(m.price),
          num(m.hp),
          num(m.strength),
          num(m.defence),
          num(m.dailyFeeding),
        ]),
        "Monster guards",
      );
    if (id === "world-boss-rewards")
      return table(
        [
          "Your level",
          "Experience at full score",
          "Gold at full score",
          "Experience at the qualifying score",
          "Gold at the qualifying score",
          "Experience cap",
        ],
        data.realm.worldBossRewards.map((r) => [
          `Level ${r.level}`,
          num(r.fullScoreExperience),
          num(r.fullScoreGold),
          num(r.qualifyingScoreExperience),
          num(r.qualifyingScoreGold),
          num(r.experienceCap),
        ]),
        "World boss kill pay for one player",
      );
    if (id === "balance")
      return (
        values(data.balance.moddableDefaults, "Moddable balance defaults") +
        spoiler(
          "Spoiler: exact combat and hidden-effect rules",
          values(data.balance.combatConstants, "Combat constants") +
            values(data.balance.godConstants, "Faith constants") +
            values(data.mental.constants, "Mental constants"),
        )
      );
    throw Error(`Unknown table: ${id}`);
  }
  return { directive, god, monster, boss };
}
function compareVersions(a, b) {
  const pa = String(a).split(".").map(Number);
  const pb = String(b).split(".").map(Number);
  if ([...pa, ...pb].some((n) => !Number.isInteger(n)))
    throw Error(`Not a version: ${a} or ${b}`);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const d = (pa[i] || 0) - (pb[i] || 0);
    if (d) return Math.sign(d);
  }
  return 0;
}
// One `history:` line per version: "<x.y.z> | <one sentence>" or "<x.y.z> | none".
function parseHistory(lines, file, gameVersion) {
  const history = [];
  const seen = new Map();
  for (const raw of lines) {
    const m = raw.match(/^(\S+)\s*\|\s*(.*)$/);
    if (!m) throw Error(`Invalid history line in ${file}: expected "<x.y.z> | <sentence>" or "<x.y.z> | none": ${raw}`);
    const [, version, text] = m;
    const sentence = text.trim();
    if (!/^\d+\.\d+\.\d+$/.test(version))
      throw Error(`Invalid history version in ${file}: ${version} is not x.y.z`);
    if (!sentence)
      throw Error(`Empty history sentence in ${file} for ${version}`);
    const none = sentence === "none";
    if (seen.has(version))
      throw Error(
        seen.get(version) !== none
          ? `History for ${version} in ${file} has both none and a sentence`
          : `Two history lines for ${version} in ${file}`,
      );
    seen.set(version, none);
    if (gameVersion && compareVersions(version, gameVersion) > 0)
      throw Error(`History version ${version} in ${file} is newer than the game version ${gameVersion}`);
    history.push({ version, sentence: none ? null : sentence });
  }
  return history;
}
function readPages(dir, options = {}) {
  const result = [];
  for (const entry of fs
    .readdirSync(dir, { withFileTypes: true })
    .sort((a, b) => a.name.localeCompare(b.name))) {
    const file = path.join(dir, entry.name);
    if (entry.isDirectory()) result.push(...readPages(file, options));
    else if (entry.name.endsWith(".md")) {
      const source = fs.readFileSync(file, "utf8");
      const match = source.match(/^---\n([\s\S]*?)\n---\n([\s\S]*)$/);
      if (!match) throw Error(`Missing frontmatter: ${file}`);
      const fields = {};
      const historyLines = [];
      for (const line of match[1].split("\n")) {
        const h = line.match(/^history: (.*)$/);
        if (h) {
          historyLines.push(h[1]);
          continue;
        }
        const m = line.match(/^(title|path|checked|sources): (.+)$/);
        if (!m) throw Error(`Unknown frontmatter: ${line}`);
        fields[m[1]] = m[2];
      }
      if (
        !fields.title ||
        !fields.checked ||
        !fields.sources ||
        !/^\/wiki\/en\/(?:[a-z0-9-]+\/)*$/.test(fields.path)
      )
        throw Error(`Invalid page metadata: ${file}`);
      result.push({
        ...fields,
        history: parseHistory(historyLines, file, options.gameVersion),
        markdown: match[2],
        sourceFile: path.relative(root, file).split(path.sep).join("/"),
      });
    }
  }
  return result;
}
// Shown history entries, newest version first; `none` markers are never shown.
const shownHistory = (history = []) =>
  history
    .filter((h) => h.sentence)
    .sort((a, b) => compareVersions(b.version, a.version));
function build(options = {}) {
  const dataDir = options.dataDir || path.join(root, "wiki-data");
  const contentDir = options.contentDir || path.join(root, "DOCS/wiki");
  const outDir = options.outDir || path.join(root, "web/wiki");
  const { meta, data } = loadData(dataDir);
  let sourceRef = meta.commit || process.env.GITHUB_SHA;
  if (!sourceRef) {
    try {
      sourceRef = require("node:child_process")
        .execFileSync("git", ["rev-parse", "HEAD"], {
          cwd: root,
          encoding: "utf8",
        })
        .trim();
    } catch (_) {
      sourceRef = "v" + meta.gameVersion;
    }
  }
  const render = renderers(data);
  const md = new MarkdownIt({
    html: false,
    linkify: false,
    typographer: false,
  });
  md.renderer.rules.heading_open = (tokens, idx, _opts, _env, self) => {
    tokens[idx].attrSet("id", slug(tokens[idx + 1].content));
    return self.renderToken(tokens, idx, _opts);
  };
  const pages = readPages(contentDir, { gameVersion: meta.gameVersion });
  for (const page of pages) {
    const disclosureLines = page.markdown
      .split("\n")
      .filter((line) => line.startsWith(":::"));
    let inside = false;
    for (const line of disclosureLines) {
      if (line.startsWith(":::spoiler ")) {
        if (inside) throw Error(`Nested spoiler: ${page.path}`);
        inside = true;
      } else if (line.trim() === ":::") {
        if (!inside) throw Error(`Unexpected spoiler end: ${page.path}`);
        inside = false;
      } else throw Error(`Invalid spoiler syntax: ${page.path}`);
    }
    if (inside) throw Error(`Unclosed spoiler: ${page.path}`);
    if (
      /[\u2013\u2014]|\p{Extended_Pictographic}/u.test(
        page.markdown +
          page.title +
          page.history.map((h) => h.sentence || "").join(" "),
      )
    )
      throw Error(`Forbidden punctuation or emoji: ${page.path}`);
    const blocks = [];
    let source = page.markdown.replace(
      /\{\{([^{}]+)\}\}/g,
      (_all, directive) => {
        const html = render.directive(directive);
        if (!html.startsWith("<")) return html;
        const token = `WIKIBLOCK${blocks.length}TOKEN`;
        blocks.push(html);
        return "\n\n" + token + "\n\n";
      },
    );
    if (/\{\{|\}\}/.test(source))
      throw Error(`Malformed directive: ${page.path}`);
    // Explicit Markdown spoiler blocks are rendered separately; raw HTML stays disabled.
    source = source.replace(
      /^:::spoiler ([^\n]+)\n([\s\S]*?)^:::\s*$/gm,
      (_all, summary, body) => {
        const token = `WIKIBLOCK${blocks.length}TOKEN`;
        blocks.push(
          spoiler(
            summary,
            md
              .render(body)
              .replace(/<p>WIKIBLOCK(\d+)TOKEN<\/p>/g, (_m, n) => blocks[n]),
          ),
        );
        return "\n\n" + token + "\n\n";
      },
    );
    if (source.includes(":::")) throw Error(`Unclosed spoiler: ${page.path}`);
    page.body = md
      .render(source)
      .replace(/<p>WIKIBLOCK(\d+)TOKEN<\/p>/g, (_m, n) => blocks[n]);
  }
  const add = (kind, e, body, sources) =>
    pages.push({
      path: url(kind, e.id),
      title: name(e),
      checked: meta.gameVersion,
      sources,
      body,
    });
  for (const e of data.items)
    add(
      "items",
      e,
      `<p>Built-in template ID ${e.id}. Randomly generated shop stock, modifiers and server mods are not included. Acquisition is not inferred from rarity or value.</p>` +
        (e.description
          ? spoiler("Spoiler: item lore", `<p>${escape(e.description)}</p>`)
          : "") +
        values(
          {
            slot: e.slot,
            handedness: e.handedness,
            weaponType: e.weaponType,
            armorType: e.armorType,
            weightClass: e.weightClass,
            rarity: e.rarity,
            minLevel: e.minLevel,
            strengthRequired: e.strengthRequired,
            baseValue: e.value,
            baseSellValue: e.sellValue,
            classes: e.classes.length
              ? e.classes
              : "No template class restriction",
            requiresGood: e.requiresGood,
            requiresEvil: e.requiresEvil,
          },
          "Equipment requirements and base values",
        ) +
        values(
          Object.fromEntries(
            Object.entries(e.stats).filter(([, v]) => v !== 0),
          ),
          "Nonzero equipment stats",
        ) +
        values(
          Object.fromEntries(
            Object.entries(e).filter(
              ([k]) => k.startsWith("has") || k.startsWith("is"),
            ),
          ),
          "Equipment flags",
        ),
      "Scripts/Data/EquipmentData.cs",
    );
  for (const e of data.classes)
    add(
      "characters/classes",
      e,
      `<p>${e.prestige ? "Prestige class. Unlock it through play, not the initial character selector." : "Starting class available at character creation when compatible with your race."} Starting class values combine with race creation bonuses; they are not the final equipped character sheet.</p>` +
        `<p>Compatible races: ${e.allowedRaces
          .map((id) =>
            link(
              "characters/races",
              data.races.find((r) => r.id === id),
            ),
          )
          .join(", ")}.</p>` +
        values(e.startingAttributes, "Class creation values") +
        values(e.growthPerLevel, "Base class growth per level") +
        e.specializations
          .map(
            (s) =>
              `<h2>${escape(name(s))}</h2><p>${escape(s.description)} Role: ${escape(s.role)}.</p>${values(s.bonusesPerLevel, "Specialization growth per level")}`,
          )
          .join("") +
        '<p><a href="/wiki/en/characters/specializations/">Choosing a specialization</a> | <a href="/wiki/en/characters/spells/">Spells</a> | <a href="/wiki/en/characters/abilities/">Abilities</a></p>' +
        values(
          Object.fromEntries(
            Object.entries(e.name).filter(([k]) => k !== "en"),
          ),
          "Verified translated class names",
        ),
      "Scripts/Core/GameConfig.cs, Scripts/Locations/LevelMasterLocation.cs, Scripts/Data/SpecializationData.cs",
    );
  for (const e of data.races)
    add(
      "characters/races",
      e,
      values(e.bonusesAndCreationRanges, "Race bonuses and creation ranges") +
        `<p>NPC lifespan setting: ${e.npcLifespanYears} years. This is an NPC simulation setting, not a promise about player survival.</p>` +
        values(
          Object.fromEntries(
            Object.entries(e.name).filter(([k]) => k !== "en"),
          ),
          "Verified translated race names",
        ),
      "Scripts/Core/GameConfig.cs",
    );
  for (const e of data.monsters)
    add(
      "monsters/families",
      e,
      render.monster(e),
      "Scripts/Data/MonsterFamilies.cs, Scripts/Systems/MonsterGenerator.cs",
    );
  for (const e of data.spells)
    add(
      "characters/spells",
      e,
      `<p>${escape(e.description.en)}</p><p>Class: ${link(
        "characters/classes",
        data.classes.find((c) => c.id === e.classId),
      )}. This is a fixed spell definition; your character must still learn it and meet the combat requirements.</p>` +
        values(
          {
            catalogSlot: e.level,
            requiredLevel: e.levelRequired,
            manaBeforeWisdom: e.manaCost,
            magicWords: e.magicWords,
            isMultiTarget: e.isMultiTarget,
            spellType: e.spellType,
          },
          "Spell values",
        ) +
        '<p>Wisdom lowers the mana a cast costs. See <a href="/wiki/en/characters/spells/#level-and-mana">level and mana</a>.</p>' +
        '<p><a href="/wiki/en/characters/spells/">All spells</a></p>',
      "Scripts/Systems/SpellSystem.cs",
    );
  for (const e of data.abilities)
    add(
      "characters/abilities",
      e,
      `<p>${escape(e.description.en)}</p><p>Classes: ${e.classes
        .map((id) =>
          link(
            "characters/classes",
            data.classes.find((c) => c.id === id),
          ),
        )
        .join(
          ", ",
        )}. Template numbers are not a prediction of final damage or healing after your attributes, equipment and combat effects.</p>` +
        values(
          Object.fromEntries(
            Object.entries(e).filter(
              ([key]) =>
                !["id", "name", "description", "classes"].includes(key),
            ),
          ),
          "Ability template and requirements",
        ) +
        '<p><a href="/wiki/en/characters/abilities/">All abilities</a></p>',
      "Scripts/Systems/ClassAbilitySystem.cs",
    );
  for (const e of data.gods.gods)
    add(
      "gods",
      e,
      render.god(e) +
        '<p><a href="/wiki/en/gods/favor/">Favor, prayer and sacrifice</a> | <a href="/wiki/en/characters/mental/">Mental and wards</a></p>',
      "Scripts/Systems/GodBoonSystem.cs, Scripts/Systems/GodDeedSystem.cs, Scripts/Core/GameConfig.cs",
    );
  for (const e of data.bosses.world)
    add("monsters/bosses", e, render.boss(e), "Scripts/Data/WorldBossData.cs");
  const urls = new Set();
  for (const p of pages) {
    if (urls.has(p.path)) throw Error(`Duplicate URL: ${p.path}`);
    urls.add(p.path);
  }
  const anchors = new Map(
    pages.map((p) => [
      p.path,
      new Set([...p.body.matchAll(/\bid="([^"]+)"/g)].map((m) => m[1])),
    ]),
  );
  for (const page of pages) {
    if (/<h1\b/i.test(page.body))
      throw Error(`Use level-two headings below the page title: ${page.path}`);
    for (const m of page.body.matchAll(/href="([^"]+)"/g)) {
      const href = m[1];
      if (/^(https?:\/\/|mailto:)/.test(href)) continue;
      const resolved = new URL(
        href.replace(/&amp;/g, "&"),
        "https://wiki.invalid" + page.path,
      );
      if (resolved.pathname.startsWith("/wiki/")) {
        if (!urls.has(resolved.pathname))
          throw Error(`Broken wiki link in ${page.path}: ${href}`);
        if (
          resolved.hash &&
          !anchors
            .get(resolved.pathname)
            .has(decodeURIComponent(resolved.hash.slice(1)))
        )
          throw Error(`Broken anchor: ${href}`);
      } else if (!["/", "/#connect"].includes(href))
        throw Error(`Non-wiki local link: ${href}`);
    }
    for (const source of page.sources.split(/,\s*/))
      if (
        !(
          source === "README.md" ||
          /^(Scripts|DOCS|web)\/[\w./-]+\.(cs|md|js)$/.test(source)
        ) ||
        source.includes("..") ||
        !fs.existsSync(path.join(root, source))
      )
        throw Error(`Missing or invalid source: ${source}`);
  }
  for (const [section] of sections)
    if (!urls.has(`/wiki/en/${section}/`))
      throw Error(`Missing section landing page: ${section}`);
  const secretNames = [
    ...data.bosses.secret,
    ...data.bosses.oldGods,
    ...data.achievements.filter((e) => e.isSecret),
  ]
    .map(name)
    .filter((n) => n.length > 3);
  const redact = (text) =>
    secretNames.reduce(
      (t, n) =>
        t.replace(
          new RegExp(n.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"), "gi"),
          "[spoiler]",
        ),
      text,
    );
  const foldTitles = (body) =>
    [...body.matchAll(/<details class="spoiler"><summary>([\s\S]*?)<\/summary>/g)].map(
      (m) => textOf(m[1]),
    );
  const search = pages.map((p) => ({
    title: redact(p.title),
    path: p.path,
    headings: [
      ...publicHtml(p.body).matchAll(/<h[23][^>]*>([\s\S]*?)<\/h[23]>/g),
    ].map((m) => redact(textOf(m[1]))),
    text: redact(textOf(p.body)),
    // Hand-written guides from DOCS/wiki rank above generated entity pages.
    ...(p.sourceFile ? { guide: true } : {}),
    // A guide's spoiler folds by their summary line only, so search and the bot can point to
    // a fold without its text ever entering the index.
    ...(p.sourceFile && foldTitles(p.body).length
      ? { folds: foldTitles(p.body).map(redact) }
      : {}),
  }));
  const strings = JSON.parse(
    fs.readFileSync(path.join(root, "web/lang/en.json"), "utf8"),
  );
  const t = (key) => {
    if (!strings["wiki." + key]) throw Error(`Missing wiki label: ${key}`);
    return escape(strings["wiki." + key]);
  };
  const nav = sections
    .map(([s]) => `<a href="/wiki/en/${s}/">${t("section." + s)}</a>`)
    .join("");
  function layout(p, preview) {
    const group = sections.find(([s]) => p.path.startsWith(`/wiki/en/${s}/`));
    const sourceLinks = p.sources
      .split(/,\s*/)
      .map(
        (s) =>
          `<a href="https://github.com/binary-knight/usurper-reborn/blob/${encodeURIComponent(sourceRef)}/${s}">${escape(s)}</a>`,
      )
      .join(", ");
    const entries = shownHistory(p.history);
    const history = entries.length
      ? `<section class="history" aria-labelledby="changes-by-version"><h2 id="changes-by-version">${t("history")}</h2><dl>${entries.map((h) => `<dt>${escape(h.version)}</dt><dd>${escape(h.sentence)}</dd>`).join("")}</dl></section>`
      : "";
    return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>${escape(p.title)} | Usurper Reborn Wiki</title><meta name="description" content="${escape(preview.slice(0, 180))}"><link rel="stylesheet" href="/wiki/wiki.css"><script defer src="/wiki/wiki-labels.js"></script><script defer src="/wiki/wiki-search.js"></script><script defer src="/wiki/wiki.js"></script></head><body><a class="skip" href="#main">${t("skip")}</a><header><a class="logo" href="/">USURPER REBORN</a><nav aria-label="Site"><a href="/wiki/en/">${t("home")}</a><a href="/#connect">${t("play")}</a><button id="theme" type="button" aria-pressed="false">${t("theme")}</button></nav></header><div class="layout"><aside><form role="search" id="search-form"><label for="search">${t("search")}</label><input type="search" id="search" placeholder="${t("searchPlaceholder")}" maxlength="200" autocomplete="off"><button type="submit">${t("find")}</button></form><p id="search-status" role="status"></p><div id="search-results"></div><nav aria-label="Wiki sections" class="sections">${nav}</nav></aside><main id="main" tabindex="-1"><nav class="breadcrumbs" aria-label="Breadcrumb"><a href="/wiki/en/">${t("home")}</a>${group ? ` / <a href="/wiki/en/${group[0]}/">${escape(group[1])}</a>` : ""}</nav><h1>${escape(p.title)}</h1><p class="version">${t("version")} ${escape(meta.gameVersion)}. ${t("checked")} ${escape(p.checked)}.</p>${p.body}${history}<footer><p>${t("fixedRules")}</p><p>${t("sources")}: ${sourceLinks}</p><p>${t("languageNote")}</p></footer></main></div></body></html>`;
  }
  // Remove only obsolete generated page files listed by our previous manifest.
  // No recursive output cleanup and no removal of hand-written assets.
  fs.mkdirSync(outDir, { recursive: true });
  const oldManifest = path.join(outDir, "manifest.json");
  if (fs.existsSync(oldManifest)) {
    const previous = JSON.parse(fs.readFileSync(oldManifest, "utf8"));
    for (const oldPath of previous.paths || []) {
      if (!/^\/wiki\/en\/(?:[\w%:-]+\/)*$/.test(oldPath))
        throw Error("Invalid prior wiki manifest path");
      if (!urls.has(oldPath)) {
        const obsolete = path.join(
          outDir,
          oldPath.slice("/wiki/".length),
          "index.html",
        );
        if (fs.existsSync(obsolete)) {
          if (
            !fs
              .realpathSync(path.dirname(obsolete))
              .startsWith(fs.realpathSync(outDir) + path.sep)
          )
            throw Error("Output path escaped wiki directory");
          fs.unlinkSync(obsolete);
        }
      }
    }
  }
  for (let i = 0; i < pages.length; i++) {
    const dest = path.join(
      outDir,
      pages[i].path.slice("/wiki/".length),
      "index.html",
    );
    fs.mkdirSync(path.dirname(dest), { recursive: true });
    fs.writeFileSync(dest, layout(pages[i], search[i].text));
  }
  fs.writeFileSync(
    path.join(outDir, "search-index.json"),
    JSON.stringify({
      schemaVersion: 1,
      gameVersion: meta.gameVersion,
      pages: search,
    }),
  );
  const clientLabels = Object.fromEntries(
    ["searching", "results", "noResults", "searchUnavailable"].map((key) => {
      t(key);
      return [key, strings["wiki." + key]];
    }),
  );
  fs.writeFileSync(
    path.join(outDir, "wiki-labels.js"),
    "globalThis.WikiLabels = " +
      JSON.stringify(clientLabels).replace(/</g, "\\u003c") +
      ";",
  );
  fs.writeFileSync(
    path.join(outDir, "index.html"),
    '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Usurper Reborn Wiki</title><meta http-equiv="refresh" content="0;url=/wiki/en/"></head><body><a href="/wiki/en/">Open the English player wiki</a></body></html>',
  );
  for (const asset of ["wiki.css", "wiki.js"])
    fs.copyFileSync(path.join(__dirname, asset), path.join(outDir, asset));
  fs.copyFileSync(
    path.join(root, "web/wiki-search.js"),
    path.join(outDir, "wiki-search.js"),
  );
  fs.writeFileSync(
    path.join(outDir, "manifest.json"),
    JSON.stringify(
      { gameVersion: meta.gameVersion, paths: pages.map((p) => p.path) },
      null,
      2,
    ),
  );
  return { pages, search, meta };
}
if (require.main === module) {
  try {
    const args = process.argv.slice(2);
    const result = build({
      dataDir: args[0] && path.resolve(args[0]),
      outDir: args[1] && path.resolve(args[1]),
    });
    console.log(
      `Built ${result.pages.length} wiki pages for ${result.meta.gameVersion}`,
    );
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
module.exports = {
  build,
  loadData,
  readPages,
  renderers,
  textOf,
  compareVersions,
  parseHistory,
  shownHistory,
};
