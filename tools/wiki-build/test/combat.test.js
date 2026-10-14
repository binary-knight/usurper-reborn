"use strict";
// 1.3.2 wiki depth: the combat tables (statuses, monster target weights, regeneration) are generated
// from the export, and the balance table no longer carries the values no game code reads.
const { test } = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");
const { loadData, renderers, textOf } = require("../build");
const dataDir =
  process.env.WIKI_DATA_DIR || path.resolve(__dirname, "../../../wiki-data");
const load = () => loadData(dataDir).data;
const render = (directive, data = load()) =>
  renderers(data).directive(directive);
// the cells of the row whose header cell holds the text, in the first table that has one
function row(html, header) {
  const found = html
    .split("</tr>")
    .find(
      (r) =>
        textOf((r.match(/<th scope="row">(.*?)<\/th>/s) || [])[1] || "") ===
        header,
    );
  assert.ok(found, `row ${header}`);
  return [...found.matchAll(/<td[^>]*>(.*?)<\/td>/gs)].map((m) => textOf(m[1]));
}
const headers = (html) =>
  [...html.matchAll(/<th scope="col">(.*?)<\/th>/g)].map((m) => textOf(m[1]));

test("statuses table: flags, damage per round by level and whether it ends", () => {
  const html = render("table:statuses");
  assert.deepEqual(headers(html), [
    "Status",
    "Kind",
    "Stops you acting",
    "Stops spells",
    "Damage per round at level 1",
    "Damage per round at level 25",
    "Damage per round at level 50",
    "Damage per round at level 100",
    "Ends by itself",
  ]);
  // poison: 2 to 5 plus a tenth of the level
  assert.deepEqual(row(html, "Poisoned"), [
    "Damage Over Time",
    "No",
    "No",
    "2 to 5",
    "4 to 7",
    "7 to 10",
    "12 to 15",
    "Yes",
  ]);
  assert.deepEqual(row(html, "Frozen").slice(0, 4), [
    "Damage Over Time",
    "Yes",
    "No",
    "1 to 3",
  ]);
  assert.deepEqual(row(html, "Diseased").slice(3, 7), ["1", "2", "4", "7"]);
  assert.deepEqual(row(html, "Stunned"), [
    "Control",
    "Yes",
    "Yes",
    "None",
    "None",
    "None",
    "None",
    "Yes",
  ]);
  assert.deepEqual(row(html, "Silenced").slice(1, 3), ["No", "Yes"]);
  assert.equal(row(html, "Stoneskin")[7], "No");
  assert.equal(row(html, "Power Stance")[0], "Buff");
  assert.equal(load().combat.statuses.length, 35);
});

test("target weights: class base weight, Defend adds 40, heaviest first", () => {
  const html = render("table:target-weights");
  assert.deepEqual(headers(html), [
    "Class",
    "Weight",
    "Weight while you Defend",
  ]);
  assert.deepEqual(row(html, "Paladin"), ["180", "220"]);
  assert.deepEqual(row(html, "Magician"), ["60", "100"]);
  assert.deepEqual(row(html, "Tidesworn"), ["190", "230"]);
  const order = [...html.matchAll(/<th scope="row">(.*?)<\/th>/gs)].map((m) =>
    textOf(m[1]),
  );
  assert.equal(order[0], "Tidesworn");
  assert.equal(order[order.length - 1], "Magician");
  assert.equal(order.length, 17);
});

test("regeneration: mana 2 plus a tenth of Wisdom up to 15, stamina by armor", () => {
  const html = render("table:combat-regeneration");
  assert.deepEqual(headers(html), [
    "Stat",
    "Mana per round (Wisdom)",
    "Combat stamina per round (Stamina), light or no armor",
    "Medium armor",
    "Heavy armor",
  ]);
  assert.deepEqual(row(html, "50"), ["7", "8", "7", "6"]);
  assert.deepEqual(row(html, "30"), ["5", "7", "6", "5"]);
  assert.equal(row(html, "130")[0], "15");
  assert.equal(row(html, "200")[0], "15");
});

test("balance table leaves out the values no game code reads", () => {
  const data = load();
  const html = render("table:balance", data);
  for (const gone of [
    "Critical Hit Chance",
    "Critical Hit Multiplier",
    "Backstab Multiplier",
    "Berserk Multiplier",
    "Boss Doom Default Rounds",
    "Boss Tank Ao EAbsorption",
  ])
    assert.ok(!textOf(html).includes(gone), gone);
  assert.ok(textOf(html).includes("Boss Potion Cooldown Rounds"));
  for (const key of [
    "criticalHitChance",
    "criticalHitMultiplier",
    "backstabMultiplier",
    "berserkMultiplier",
    "bossDoomDefaultRounds",
    "bossTankAoEAbsorption",
  ])
    assert.ok(!(key in data.balance.moddableDefaults), key);
  for (const key of ["CriticalHitChance", "CriticalHitMultiplier", "BackstabMultiplier", "BerserkMultiplier"])
    assert.ok(!(key in data.balance.combatConstants), key);
});

test("combat constants print through the balance directive and reuse no name", () => {
  const data = load();
  const r = renderers(data);
  assert.equal(r.directive("balance:PlayerHitMinNeeded"), "2");
  assert.equal(r.directive("balance:PlayerHitMaxNeededAbove"), "8");
  assert.equal(r.directive("percent:BossFirstRoundsDamageCapPercent"), "15");
  assert.equal(r.directive("balance:VenomChancePercent"), "35");
  assert.equal(r.directive("balance:PredatorBonus"), "60");
  for (const key of Object.keys(data.combat.constants))
    for (const other of [
      data.balance.moddableDefaults,
      data.balance.characterConstants,
      data.balance.combatConstants,
      data.balance.godConstants,
      data.mental.constants,
      data.gods.favorRules,
      data.characters.constants,
      data.party.constants,
      data.economy.constants,
    ])
      assert.ok(!(key in other), key);
});
