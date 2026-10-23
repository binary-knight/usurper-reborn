"use strict";
// 1.3.1 wiki depth: the Characters tables are generated from the export, never typed in.
const { test } = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");
const { build, loadData, renderers, textOf } = require("../build");
const dataDir =
  process.env.WIKI_DATA_DIR || path.resolve(__dirname, "../../../wiki-data");
const load = () => loadData(dataDir).data;
const render = (directive, data = load()) =>
  renderers(data).directive(directive);
// the cells of the row whose header cell holds the text
function row(html, header) {
  const found = html
    .split("</tr>")
    .find((r) => textOf((r.match(/<th scope="row">(.*?)<\/th>/s) || [])[1] || "") === header);
  assert.ok(found, `row ${header}`);
  return [...found.matchAll(/<td[^>]*>(.*?)<\/td>/gs)].map((m) => textOf(m[1]));
}
const headers = (html) =>
  [...html.matchAll(/<th scope="col">(.*?)<\/th>/g)].map((m) => textOf(m[1]));

test("classes table: HP, Defense and mana per level and the roles the game lists", () => {
  const data = load();
  const html = render("table:classes", data);
  assert.deepEqual(headers(html), [
    "Class",
    "Type",
    "HP per level",
    "Defense per level",
    "Mana per level",
    "Roles the game lists",
    "Specializations",
  ]);
  assert.deepEqual(row(html, "Warrior").slice(0, 5), ["Starting", "17", "3", "0", "DPS, Tank"]);
  assert.deepEqual(row(html, "Magician").slice(1, 4), ["11", "2", "15"]);
  assert.deepEqual(row(html, "Tidesworn").slice(0, 5), ["Prestige", "18", "3", "8", "Tank, DPS"]);
  for (const c of data.classes) {
    const cells = row(html, c.name.en);
    assert.equal(cells[1], String(c.growthPerLevel.maxHP), c.id);
    assert.equal(cells[2], String(c.growthPerLevel.defence), c.id);
    assert.equal(cells[3], String(c.growthPerLevel.maxMana), c.id);
  }
});

test("abilities table: cooldown and the base values of the template", () => {
  const data = load();
  const html = render("table:abilities", data);
  assert.deepEqual(headers(html), [
    "Ability",
    "Classes",
    "Required level",
    "Stamina / mana",
    "Cooldown (rounds)",
    "Base damage / healing",
    "Attack / defense bonus",
    "Duration (rounds)",
    "Effect",
  ]);
  assert.deepEqual(row(html, "Thundering Roar").slice(1, 4), ["20", "40 / 0", "3"]);
  assert.deepEqual(row(html, "Shield Wall").slice(3, 7), ["3", "0 / 0", "0 / 30", "3"]);
  assert.deepEqual(row(html, "Inspiring Tune").slice(5, 7), ["25 / 20", "5"]);
  assert.equal(row(html, "Power Strike")[4], "35 / 0");
  for (const a of data.abilities)
    assert.equal(row(html, a.name.en)[3], String(a.cooldown), a.id);
});

test("class-equipment table: armor weights and usable templates per weapon type, no bows", () => {
  const data = load();
  const html = render("table:class-equipment", data);
  const head = headers(html);
  assert.deepEqual(head.slice(0, 2), ["Class", "Armor weights"]);
  assert.ok(!head.includes("Bow"), "bows are left out");
  for (const type of ["Sword", "Axe", "Staff", "Instrument", "Shield", "Tower Shield"])
    assert.ok(head.includes(type), type);
  const cell = (cls, type) => row(html, cls)[head.indexOf(type) - 1];
  assert.equal(cell("Magician", "Armor weights"), "Light");
  assert.equal(cell("Ranger", "Armor weights"), "Light, Medium");
  assert.equal(cell("Warrior", "Armor weights"), "Light, Medium, Heavy");
  assert.equal(cell("Bard", "Instrument"), "10 of 10");
  assert.equal(cell("Jester", "Instrument"), "0 of 10");
  assert.equal(cell("Barbarian", "Axe"), "4 of 4");
  assert.equal(cell("Warrior", "Axe"), "2 of 4");
  assert.equal(cell("Paladin", "Tower Shield"), "4 of 5");
  assert.match(textOf(html), /Plate is Heavy/);
  assert.match(textOf(html), /Chain is Medium/);
  for (const c of data.classes)
    for (const w of [...c.equipment.weapons, ...c.equipment.shields])
      assert.ok(w.usable >= 0 && w.usable <= w.total, `${c.id} ${w.type}`);
});

test("class-equipment and class-roles: every class has its row, the Mystic Shaman with its own limits", () => {
  const data = load();
  const shamanData = data.classes.find((c) => c.id === "MysticShaman");
  assert.deepEqual(shamanData.equipment.armorWeights, ["Light", "Medium"]);
  const equipment = render("table:class-equipment", data);
  const shamanRow = row(equipment, "Mystic Shaman");
  assert.ok(shamanRow, "a Mystic Shaman row");
  assert.equal(shamanRow[0], "Light, Medium");
  assert.ok(row(equipment, "Warrior"));
  assert.equal((equipment.match(/<tr>/g) || []).length - 1, data.classes.length);
  const sections = render("table:class-roles", data).split("<h4 ").slice(1);
  const shaman = textOf("<h4 " + sections.find((s) => s.includes(">Mystic Shaman role numbers</h4>")));
  assert.match(shaman, /Mystic Shaman gains \d+ maximum HP/);
  assert.ok(shaman.includes("Armor weights it can wear: Light, Medium."), "its armor weight line");
  assert.equal(sections.filter((s) => s.includes("Armor weights it can wear")).length, data.classes.length);
});

test("weapon-templates: one heading per weapon type, each template with its class list", () => {
  const data = load();
  const html = render("table:weapon-templates", data);
  const headings = [...html.matchAll(/<h3 id="([^"]+)">(.*?)<\/h3>/g)].map((m) => [m[1], textOf(m[2])]);
  assert.ok(headings.some(([id, text]) => id === "axe-classes-that-can-use-each-one" && text === "Axe: classes that can use each one"));
  for (const type of ["Staff", "Instrument", "Greataxe", "Shield", "Tower Shield", "Buckler"])
    assert.ok(headings.some(([, text]) => text === `${type}: classes that can use each one`), type);
  assert.ok(!headings.some(([, text]) => /^Bow/.test(text)), "bows are left out");
  assert.equal(new Set(headings.map(([id]) => id)).size, headings.length, "heading ids are unique");
  assert.deepEqual(row(html, "Hand Axe"), ["One", "1 to 30", "Warrior, Barbarian, Ranger, Mystic Shaman"]);
  assert.equal(row(html, "Dagger")[2], "Every class");
  assert.equal(row(html, "Wooden Flute")[2], "Bard");
  assert.deepEqual(row(html, "Tower Shield"), ["25 to 60", "Warrior, Paladin, Barbarian"]);
  assert.equal(row(html, "Quarterstaff")[0], "Two");
  for (const t of data.characters.weaponTemplates) assert.ok(row(html, t.name), t.name);
});

test("class-roles: one section per class from the class numbers and its abilities", () => {
  const data = load();
  const html = render("table:class-roles", data);
  assert.ok(!html.includes("<h3"), "class names stay out of the indexed headings");
  const sections = html.split("<h4 ").slice(1);
  assert.equal(sections.length, data.classes.length);
  const section = (cls) => textOf("<h4 " + sections.find((s) => s.includes(`>${cls} role numbers</h4>`)));
  assert.match(section("Warrior"), /Warrior gains 17 maximum HP, 3 Defense and 0 maximum mana per level/);
  assert.match(section("Warrior"), /It has no mana and pays for abilities with combat stamina\./);
  assert.match(section("Warrior"), /Armor weights it can wear: Light, Medium, Heavy\./);
  assert.match(section("Warrior"), /Arms is DPS, Protection is Tank\./);
  assert.match(section("Warrior"), /Taunt abilities: Thundering Roar \(level 20, 40 stamina, cooldown 3 rounds\); Shield Wall Formation \(level 40, 45 stamina, cooldown 5 rounds, needs a shield\)\./);
  assert.match(section("Warrior"), /Defense abilities: Shield Wall \(level 8, 25 stamina, cooldown 3 rounds, needs a shield\)/);
  assert.match(section("Magician"), /It has mana\. Armor weights it can wear: Light\./);
  assert.match(section("Magician"), /Taunt abilities: none\./);
  assert.match(section("Tidesworn"), /The class has no specialization; where the game lists roles for it, it lists Tank and DPS\./);
  assert.match(section("Cleric"), /Its abilities heal 25 percent more\./);
  assert.match(section("Alchemist"), /Its abilities heal 50 percent more\./);
  assert.doesNotMatch(section("Warrior"), /heal \d+ percent more/);
  assert.doesNotMatch(textOf(html), /\b(best|strong|weak|good)\b/i);
});

test("proficiency table: every tier from the training functions", () => {
  const data = load();
  const html = render("table:proficiency", data);
  assert.equal(data.characters.proficiency.length, 9);
  assert.deepEqual(row(html, "Untrained"), ["0.50", "-2", "25", "1", "15"]);
  assert.deepEqual(row(html, "Average"), ["1.00", "0", "10", "6", "7"]);
  assert.deepEqual(row(html, "Good"), ["1.15", "1", "7", "8", "5"]);
  assert.deepEqual(row(html, "Legendary"), ["2.00", "7", "0", "None", "0"]);
  const cost = (id) => Number(row(html, id)[3]);
  assert.equal(["Average", "Good", "Skilled", "Expert", "Superb", "Master"].reduce((n, id) => n + cost(id), 0), 165);
});

test("xp-levels table: the experience of every level", () => {
  const data = load();
  const html = render("table:xp-levels", data);
  assert.equal(data.characters.experienceByLevel.length, 100);
  assert.deepEqual(row(html, "Level 1"), ["0", "0", "3"]);
  assert.deepEqual(row(html, "Level 2"), ["200", "200", "3"]);
  assert.deepEqual(row(html, "Level 3"), ["650", "450", "3"]);
  assert.deepEqual(row(html, "Level 10").slice(0, 1), ["19200"]);
  assert.equal(row(html, "Level 10")[2], "2");
  assert.equal(row(html, "Level 40")[2], "1");
  for (const e of data.characters.experienceByLevel)
    assert.equal(row(html, `Level ${e.level}`)[0], String(e.totalExperience));
});

test("mental-recovery table: each source prints its exported constant", () => {
  const data = load();
  const html = render("table:mental-recovery", data);
  const c = data.mental.constants;
  assert.deepEqual(row(html, "Home sleep"), [String(c.MentalHomeSleepGain)]);
  assert.deepEqual(row(html, "Inn table rest with a friend present"), [String(c.MentalInnFriendGain)]);
  assert.deepEqual(row(html, "A new day"), [String(c.MentalDailyReset)]);
  assert.deepEqual(row(html, "Addiction Rehab at the Healer"), [String(c.MentalRehabGain)]);
  assert.equal((html.match(/<tr>/g) || []).length - 1, 17);
  delete data.mental.constants.MentalHomeSleepGain;
  assert.throws(() => render("table:mental-recovery", data), /Unknown Mental constant: MentalHomeSleepGain/);
});

test("percent and balance directives print the character constants", () => {
  const data = load();
  assert.equal(render("percent:AlchemistPotionMasteryBonus", data), "50");
  assert.equal(render("percent:TideswornOceansResiliencePercent", data), "3");
  assert.equal(render("percent:AwakeningBonusStage7Extra", data), "5");
  assert.equal(render("balance:BardInspirationChance", data), "15");
  assert.equal(render("balance:MagicianArcaneSpellBonus", data), "1.15");
  assert.equal(render("balance:RespecBaseGoldCost", data), "4000");
  assert.throws(() => render("percent:NoSuchConstant", data), /Unknown percent constant/);
  assert.throws(() => render("percent:AlchemistPotionMasteryBonus slot=Body", data), /Unexpected filter/);
});

test("a template class the export does not know fails the load", () => {
  const fs = require("node:fs");
  const os = require("node:os");
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "usurper-wiki-chars-"));
  try {
    for (const f of fs.readdirSync(dataDir)) fs.copyFileSync(path.join(dataDir, f), path.join(temp, f));
    const file = path.join(temp, "characters.json");
    const json = JSON.parse(fs.readFileSync(file, "utf8"));
    json.data.weaponTemplates[0].classes = ["Pirate"];
    fs.writeFileSync(file, JSON.stringify(json));
    assert.throws(() => loadData(temp), /Unknown template class: .*Pirate/);
    json.data.weaponTemplates = [];
    fs.writeFileSync(file, JSON.stringify(json));
    assert.throws(() => loadData(temp), /Empty dataset: characters\/weaponTemplates/);
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
});

test("the Characters guides print their numbers through the directives and keep the fold", () => {
  const fs = require("node:fs");
  const os = require("node:os");
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "usurper-wiki-guides-"));
  try {
    const result = build({ dataDir, outDir: path.join(temp, "site") });
    const data = load();
    const c = { ...data.mental.constants, ...data.characters.constants };
    const pageOf = (p) => result.pages.find((x) => x.path === p);
    const indexOf = (p) => result.search.find((x) => x.path === p);
    const classes = textOf(pageOf("/wiki/en/characters/classes/").body);
    assert.ok(classes.includes(`Potion Mastery makes every healing potion an Alchemist uses heal ${Math.round(c.AlchemistPotionMasteryBonus * 100)} percent more.`));
    assert.ok(classes.includes(`a chance of ${c.CyclebreakerPassiveEvadePercent} percent to evade`));
    assert.ok(classes.includes(`has a ${c.SoftTauntStickChance} percent chance to keep to it`));
    const headings = indexOf("/wiki/en/characters/classes/").headings;
    for (const h of [
      "Which classes can tank",
      "Which classes can heal",
      "Potion Mastery (Alchemist)",
      "Trickster's Luck (Jester)",
      "Role numbers for each class",
      "Axe: classes that can use each one",
      "Equipment by class: armor weight, weapons and shields",
    ])
      assert.ok(headings.includes(h), h);
    const leveling = textOf(pageOf("/wiki/en/characters/leveling/").body);
    assert.ok(leveling.includes("Leveling is automatic by default"));
    assert.ok(leveling.includes(`costs ${c.RespecBaseGoldCost} gold plus ${c.RespecGoldPerLevel} gold per level`));
    assert.ok(indexOf("/wiki/en/characters/leveling/").headings.includes("Training points per level"));
    const mental = textOf(pageOf("/wiki/en/characters/mental/").body);
    assert.ok(mental.includes(`You fled: ${c.MentalFleeLoss}.`));
    assert.ok(mental.includes(`Home sleep ${c.MentalHomeSleepGain}`));
    // the lasting NG+ unlocks stay inside the spoiler fold: in the page, not in the search text
    const ngPage = pageOf("/wiki/en/characters/new-game-plus/").body;
    assert.match(ngPage, /<details[\s\S]*Lasting unlocks from a finished life[\s\S]*<\/details>/);
    assert.ok(!indexOf("/wiki/en/characters/new-game-plus/").text.includes("Lasting unlocks"));
    assert.ok(indexOf("/wiki/en/characters/new-game-plus/").text.includes("starts at level 5"));
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
});
