"use strict";
// 1.3.2 wiki depth: gods, the town's institutions, online play and the NPC world. The god rank, divine boon,
// alignment, faction, world event, Old God mechanics, monster guard and world boss tables are generated from
// realm.json, the races table carries the NPC lifespan, and the Old God table folds itself.
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

test("god ranks: experience, deeds and the Favors budget", () => {
  const html = render("table:god-ranks");
  assert.deepEqual(headers(html), [
    "Rank",
    "Title",
    "Divine experience needed",
    "Deeds a day",
    "Experience per believer a day",
    "Favors budget (no believers)",
    "Favors budget (10 or more believers)",
  ]);
  assert.deepEqual(row(html, "Rank 1"), ["Lesser Spirit", "0", "3", "2", "30", "10"]);
  assert.deepEqual(row(html, "Rank 4"), ["Major Spirit", "50,000", "6", "8", "60", "40"]);
  assert.deepEqual(row(html, "Rank 9"), ["God", "1,000,000", "15", "18", "110", "90"]);
});

test("divine boons: group, alignments, cost and the three tiers in words", () => {
  const html = render("table:divine-boons");
  assert.deepEqual(row(html, "Warrior's Fury"), [
    "Combat",
    "Light, Balance",
    "8",
    "+5 percent damage",
    "+10 percent damage",
    "+15 percent damage",
  ]);
  assert.deepEqual(row(html, "Battle Rage").slice(1, 3), ["Any", "6"]);
  assert.equal(row(html, "Lifedrain")[5], "heals 10 percent of damage dealt");
  assert.equal(load().realm.divineBoons.length, 13);
  // a new effect field fails the build instead of printing nothing
  const data = load();
  data.realm.divineBoons[0].tiers[0].effects.NewEffect = 1;
  assert.throws(() => render("table:divine-boons", data), /Unknown divine boon effect: NewEffect/);
});

test("alignment types: the combat effects of each", () => {
  const html = render("table:alignment-types");
  assert.deepEqual(row(html, "Evil"), ["x1.2", "x0.9", "+5", "6", "10", "None", "None"]);
  assert.deepEqual(row(html, "Holy"), ["x1", "x1.1", "+5", "None", "None", "4", "10"]);
  assert.deepEqual(row(html, "Balanced"), ["x1.05", "x1.05", "None", "None", "None", "None", "None"]);
});

test("faction ranks and the rank-scaled bonuses", () => {
  const ranks = render("table:faction-ranks");
  assert.deepEqual(row(ranks, "Rank 3"), ["500", "Knight", "Operative", "Priest/Priestess"]);
  assert.deepEqual(row(ranks, "Rank 8"), ["5,000", "Prince-Regent", "Faceless", "Voice of the Seven"]);
  const bonuses = render("table:faction-bonuses");
  assert.deepEqual(row(bonuses, "Rank 0"), ["5", "20", "10", "+15", "0", "x1.25", "5"]);
  assert.deepEqual(row(bonuses, "Rank 8"), ["15", "60", "30", "+45", "40", "x1.75", "25"]);
});

test("world events: days, factors and when the daily roll draws them", () => {
  const html = render("table:world-events");
  assert.deepEqual(row(html, "Inflation Crisis"), ["5 to 9", "x2", "None", "None", "No"]);
  assert.deepEqual(row(html, "Tournament Day"), ["1 to 2", "None", "x1.3", "None", "summer"]);
  assert.deepEqual(row(html, "Economic Boom"), ["5 to 11", "x0.75", "None", "x1.2", "Any season"]);
  assert.deepEqual(row(html, "Holy Day"), ["1", "None", "None", "None", "Any season"]);
  assert.equal(row(html, "Harvest Festival")[4], "autumn");
});

test("Old God mechanics fold themselves and show the fight setup", () => {
  const html = render("table:old-god-mechanics");
  assert.match(html, /^<details class="spoiler"><summary>Spoiler: Old God fight mechanics<\/summary>/);
  assert.equal(textOf(html), "", "nothing of the table reaches public text");
  const manwe = row(html, "Manwe, The Weary Creator");
  assert.deepEqual(manwe, [
    "50",
    "10",
    "25",
    "every 2 rounds, 1,500",
    "every 3 rounds, 4,000",
    "70",
    "2",
    "physical, magical",
    "50",
  ]);
  const maelketh = row(html, "Maelketh, The Broken Blade");
  assert.deepEqual(maelketh.slice(2, 5), ["40", "None", "None"]);
  assert.equal(maelketh[6], "None");
});

test("monster guards: price, stats and feeding by King.cs", () => {
  const html = render("table:monster-guards");
  assert.deepEqual(row(html, "Elder Drake"), ["40", "100,000", "3,000", "270", "170", "650"]);
  assert.deepEqual(row(html, "War Hound"), ["5", "2,000", "450", "55", "35", "100"]);
  assert.equal(load().realm.monsterGuards.length, 15);
});

test("world boss rewards by level", () => {
  const html = render("table:world-boss-rewards");
  assert.deepEqual(row(html, "Level 30").slice(1, 2), ["24,648"]);
  assert.deepEqual(row(html, "Level 100").slice(0, 2), ["450,000", "150,000"]);
});

test("races show the NPC lifespan in game years and real days", () => {
  const html = render("table:races");
  assert.deepEqual(row(html, "Elf").slice(4), ["200", "80"]);
  assert.deepEqual(row(html, "Gnoll").slice(4), ["50", "20"]);
});

test("realm constants print and do not reuse another dataset's name", () => {
  const r = renderers(load());
  assert.equal(r.directive("balance:WorldBossSpawnHourEastern"), "20");
  assert.equal(r.directive("balance:MurderWeightPerDeliberateMurder"), "3");
  assert.equal(r.directive("percent:PvPGoldStealPercent"), "10");
  assert.equal(r.directive("balance:MaxNPCGuards"), "5");
  const data = load();
  data.realm.constants.MaxLevel = 1;
  assert.throws(() => renderers(data), /Duplicate constant: MaxLevel/);
});
