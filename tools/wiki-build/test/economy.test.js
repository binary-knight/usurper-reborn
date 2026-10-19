"use strict";
// 1.3.2 wiki depth: the items and town economy tables are generated from the export.
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

test("materials table: each band and what spends the material", () => {
  const html = render("table:materials");
  assert.deepEqual(headers(html), ["Material", "Dungeon floors", "Spent on"]);
  assert.deepEqual(row(html, "Crimson War Shard"), [
    "20 to 35",
    "Phoenix Fire enchant",
  ]);
  assert.deepEqual(row(html, "Heart of the Ocean"), [
    "50 to 100",
    "Legendary enchant, Godbane enchant, 4th Inn stat training, 5th Inn stat training",
  ]);
  assert.deepEqual(row(html, "Withered Heart Petal"), [
    "35 to 50",
    "Sunbane enchant, Godbane enchant",
  ]);
  // the god column is not in the open table
  assert.ok(!/Maelketh/.test(html));
});

test("old god rewards fold the god column and the artifacts", () => {
  const html = render("table:old-god-rewards");
  assert.ok(html.startsWith('<details class="spoiler">'));
  assert.equal(textOf(html), "");
  assert.deepEqual(row(html, "Maelketh"), ["25", "Crimson War Shard"]);
  const artifacts = html.slice(html.indexOf("<caption>Artifacts"));
  assert.deepEqual(row(artifacts, "The Creator's Eye"), [
    "Maelketh, floor 25",
    "+35 Wisdom, +25 Dexterity",
  ]);
  assert.equal(
    row(artifacts, "The Void Key")[0],
    "Appears when you hold the other six",
  );
});

test("enchants table: costs, levels, effects and materials", () => {
  const html = render("table:enchants");
  assert.deepEqual(headers(html), [
    "Number",
    "Enchant",
    "Effect",
    "Cost before modifiers",
    "Level",
    "Materials",
  ]);
  assert.deepEqual(row(html, "12"), [
    "Godforged",
    "+38 to one stat you choose",
    "500,000 + 5,000 per level",
    "75",
    "1 Eye of Manwe, 1 Terravok's Heartstone",
  ]);
  assert.deepEqual(row(html, "1").slice(1), [
    "+2 to one stat you choose",
    "3,000 + 100 per level",
    "1",
    "None",
  ]);
  assert.equal(row(html, "6")[1], "+6 Intelligence, +4 Wisdom");
  assert.equal(
    row(html, "8")[1],
    "+5 percent critical hit chance, +10 percent critical damage",
  );
  assert.equal(
    row(html, "13")[1],
    "a chance of extra fire damage on a hit, +20 weapon power",
  );
});

test("enchants table: a counter row names the god and the two abilities", () => {
  const html = render("table:enchants");
  assert.equal(
    row(html, "15")[1],
    "In Aurelion's fight it holds back Light's Embrace and Sacrifice and removes its divine armor",
  );
  assert.equal(
    row(html, "16")[1],
    "In Terravok's fight it holds back Stone Skin and Entomb and removes its divine armor",
  );
  assert.equal(
    row(html, "17")[1],
    "In Manwe's fight it holds back Word of Creation and Time Stop and removes its divine armor",
  );
  assert.equal(
    row(html, "13")[1],
    "a chance of extra fire damage on a hit, +20 weapon power",
  );
});

test("gear sets, loot rarity, herbs and home upgrades", () => {
  const sets = render("table:gear-sets");
  assert.equal(
    row(sets, "Silk")[0],
    "2 pieces: +15 Max Mana; 4 pieces: +3 Intelligence, +3 Wisdom; 5 pieces: +40 Max Mana, +2 Defence",
  );
  const rarity = render("table:loot-rarity");
  assert.deepEqual(row(rarity, "Legendary"), ["Legendary", "3", "20", "2 to 3", "10"]);
  assert.deepEqual(row(rarity, "Common"), ["Nothing", "1", "1", "0", "0"]);
  assert.deepEqual(row(rarity, "Uncommon").slice(0, 1), ["Fine"]);
  const herbs = render("table:herbs");
  assert.deepEqual(row(herbs, "Swiftthistle").slice(0, 2), ["4", "3"]);
  const home = render("table:home-upgrades");
  const quarters = home.slice(home.indexOf("<caption>Living Quarters"));
  assert.deepEqual(row(quarters, "1"), ["25,000", "40", "2"]);
  assert.deepEqual(row(quarters, "0"), ["Start", "25", "1"]);
  const training = home.slice(home.indexOf("<caption>Training Room"));
  assert.deepEqual(row(training, "10"), ["3,500,000"]);
});

test("economy constants print through the balance directive and reuse no name", () => {
  const data = load();
  const r = renderers(data);
  assert.equal(r.directive("percent:MaterialDropChanceRegular"), "3");
  assert.equal(r.directive("percent:FourthEnchantFailChance"), "25");
  assert.equal(r.directive("balance:MaxEnchantments"), "5");
  for (const key of Object.keys(data.economy.constants))
    for (const other of [
      data.balance.moddableDefaults,
      data.balance.characterConstants,
      data.balance.combatConstants,
      data.balance.godConstants,
      data.mental.constants,
      data.gods.favorRules,
      data.characters.constants,
      data.party.constants,
    ])
      assert.ok(!(key in other), key);
});
