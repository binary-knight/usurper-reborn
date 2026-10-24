"use strict";
// 1.3.2 wiki depth: the party tables (beasts, story companions, ally stances) are generated from the export.
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

test("beasts table: the region's level opens a beast whose own level is lower", () => {
  const html = render("table:beasts");
  assert.deepEqual(headers(html), [
    "Beast",
    "Species",
    "Kind",
    "Region",
    "Level to meet it",
    "Taming difficulty",
    "Base HP / attack / defense",
  ]);
  assert.deepEqual(row(html, "Marsh Toad"), [
    "Amphibian",
    "Passive bonus",
    "Blackmire Swamp (south)",
    "20",
    "13",
    "None",
  ]);
  assert.equal(row(html, "Tidepool Sprite")[3], "30");
  assert.equal(row(html, "Forest Hawk")[3], "10");
  assert.deepEqual(row(html, "Storm Eagle").slice(1, 6), [
    "Fights with you",
    "Stormbreak Coast (west)",
    "50",
    "21",
    "110 / 30 / 12",
  ]);
  const data = load();
  assert.equal(data.party.beasts.length, 8);
  for (const b of data.party.beasts)
    assert.equal(
      b.sightedFromLevel,
      Math.max(b.region.minLevel, b.beastMinLevel),
      b.id,
    );
});

test("companions table: the class each fights as, recruit level and weapon types", () => {
  const html = render("table:companions");
  assert.deepEqual(headers(html).slice(0, 7), [
    "Companion",
    "Title",
    "Role",
    "Fights as",
    "Recruit level",
    "Main hand weapon types",
    "Romance",
  ]);
  assert.deepEqual(row(html, "Lyris").slice(1, 6), [
    "Damage",
    "Ranger",
    "15",
    "Sword, Dagger, Bow",
    "Yes",
  ]);
  assert.deepEqual(row(html, "Aldric").slice(1, 5), [
    "Tank",
    "Warrior",
    "10",
    "Sword, Axe, Mace, Hammer, Flail, Greatsword, Greataxe, Maul",
  ]);
  assert.equal(row(html, "Mira")[4], "Mace, Hammer, Flail, Staff");
  assert.equal(row(html, "Vex")[2], "Assassin");
  assert.equal(row(html, "Melodia")[4], "Dagger, Rapier, Instrument");
  // the second table: the values at level 1
  const stats = html.slice(html.indexOf("starting values at level 1"));
  assert.deepEqual(row(stats, "Aldric"), ["350", "45", "40", "5", "20", "0"]);
  assert.deepEqual(row(stats, "Mira"), ["180", "10", "12", "35", "25", "60"]);
});

test("ally stances table: the thresholds of each stance", () => {
  const html = render("table:ally-stances");
  assert.equal(headers(html)[0], "Stance");
  assert.deepEqual(row(html, "Balanced"), ["30", "50", "40", "35", "Yes", "100"]);
  assert.deepEqual(row(html, "Aggressive"), ["25", "45", "35", "30", "Yes", "100"]);
  assert.deepEqual(row(html, "Cautious"), ["40", "60", "55", "50", "No", "70"]);
});

test("party constants print through the balance directive and reuse no name", () => {
  const data = load();
  const r = renderers(data);
  assert.equal(r.directive("balance:BeastEncounterChancePercent"), "8");
  assert.equal(r.directive("balance:MaxActiveCompanions"), "4");
  assert.equal(r.directive("percent:PermadeathChanceDungeonTeam"), "2");
  for (const key of Object.keys(data.party.constants))
    for (const other of [
      data.balance.moddableDefaults,
      data.balance.characterConstants,
      data.balance.combatConstants,
      data.balance.godConstants,
      data.mental.constants,
      data.gods.favorRules,
      data.characters.constants,
    ])
      assert.ok(!(key in other), key);
});
