"use strict";
// 1.3.2 wiki depth: the dungeon and the world beyond the town. The monster ability, floor, outpost,
// wilderness, druid shrine, Gauntlet and Sellsword tables are generated from world.json, and a secret
// boss shows its reward.
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

test("monster abilities: hit, direct damage, status, other effects and who uses them", () => {
  const html = render("table:monster-abilities");
  assert.deepEqual(headers(html), [
    "Ability",
    "Hit (x attack power)",
    "Direct damage",
    "Status inflicted",
    "Other effects",
    "Changes when",
    "Used by",
  ]);
  const crushing = row(html, "Crushing Blow");
  assert.deepEqual(crushing.slice(0, 5), [
    "x2",
    "None",
    "Stunned, 25 percent, 2 rounds",
    "None",
    "No",
  ]);
  assert.match(crushing[5], /Hill Giant \(wilderness\)/);
  assert.deepEqual(row(html, "Life Drain").slice(0, 4), [
    "x0.7",
    "None",
    "None",
    "heals 50 percent of the damage it deals",
  ]);
  // a breath replaces the hit with its direct damage and ends the turn
  const fire = row(html, "Fire Breath");
  assert.equal(fire[0], "No");
  assert.match(fire[1], /^\d+ to \d+$/);
  assert.equal(fire[2], "Burning, 40 percent, 3 rounds");
  assert.match(fire[5], /Drake/);
  assert.deepEqual(row(html, "Stoneskin").slice(3, 5), [
    "ends its turn, +20 armor",
    "it is used again",
  ]);
  assert.equal(row(html, "Devour")[4], "the target is badly hurt");
  assert.equal(row(html, "Multiattack")[0], "x1");
  assert.match(row(html, "Rally")[5], /Goblin \(signature\)/);
  assert.match(row(html, "Fireball")[5], /Draconic Shaman/);
  // every ability some monster carries, none of the unused ones
  const ids = load().world.monsterAbilities.map((a) => a.id);
  assert.ok(ids.includes("VenomousBite") && !ids.includes("Flee"));
});

test("dungeon floors: theme, rooms and floor boss for all 100 floors", () => {
  const html = render("table:dungeon-floors");
  assert.deepEqual(headers(html), ["Floor", "Theme", "Rooms", "Floor boss"]);
  assert.deepEqual(row(html, "7"), ["Catacombs", "15", "Skull Revenant"]);
  assert.deepEqual(row(html, "40"), ["Ancient Ruins", "20", "Awakened Golem"]);
  assert.deepEqual(row(html, "100"), ["Abyssal Void", "25", "Void Horror"]);
  assert.equal(load().world.floors.length, 100);
});

test("outposts: the heal price and share, and supplies at the floor's price", () => {
  const html = render("table:outposts");
  assert.deepEqual(row(html, "10").slice(0, 2), [
    "The Bonewright's Forge",
    "30 gold, restores 30 percent of missing HP and mana",
  ]);
  assert.equal(row(html, "20")[1], "None");
  assert.match(row(html, "65")[2], /Healing Potion 175/);
  assert.match(row(html, "35")[1], /^74 gold, restores 40 percent/);
});

test("wilderness regions and druid shrines", () => {
  const regions = render("table:wilderness-regions");
  assert.deepEqual(row(regions, "Blackmire Swamp").slice(0, 2), ["South", "20"]);
  assert.match(row(regions, "Whispering Forest")[3], /Ironbark Root \(a herb\)/);
  assert.match(row(regions, "Stormbreak Coast")[3], /Pearl \(large gold\)/);
  assert.equal(
    row(regions, "Iron Mountains")[4],
    "Abandoned Mine (level 12, fight), Eagle's Eyrie (level 15, shrine)",
  );
  const shrines = render("table:druid-shrines");
  assert.deepEqual(row(shrines, "Moonwell of Noctura"), [
    "Blackmire Swamp",
    "+5% crit chance (24h)",
    "3 toward Darkness",
  ]);
  assert.equal(row(shrines, "Lantern Shrine of Aurelion")[2], "5 toward Chivalry");
});

test("Gauntlet tiers and champions", () => {
  const tiers = render("table:gauntlet-tiers");
  assert.deepEqual(row(tiers, "Arena Hopeful"), ["5 to 19", "200", "100", "30"]);
  assert.deepEqual(row(tiers, "Grand Champion"), ["80 to 100", "1,500", "700", "150"]);
  const champions = render("table:gauntlet-champions");
  assert.deepEqual(row(champions, "10").slice(1, 6), [
    "Tyrant",
    "your level + 14",
    "x4",
    "x1.8",
    "x2",
  ]);
  assert.equal(load().world.gauntletChampions.length, 7);
});

test("Sellsword ranks and contracts", () => {
  const ranks = render("table:merc-ranks");
  assert.deepEqual(row(ranks, "Legend"), ["150", "3", "x1.15"]);
  assert.deepEqual(row(ranks, "Sellsword"), ["10", "1", "x1"]);
  const contracts = render("table:merc-contracts");
  assert.deepEqual(row(contracts, "Unhallowed Dead"), [
    "Faith",
    "1",
    "slay Zombie monsters",
    "3 + your level / 5",
    "x1",
  ]);
  assert.equal(row(contracts, "Deep Survey")[2], "explore new dungeon rooms");
});

test("secret bosses show their reward and no unused stats", () => {
  const html = render("table:secret-bosses");
  assert.match(html, /<th scope="row">Reward XP<\/th><td>25000<\/td>/);
  assert.match(html, /<th scope="row">Asks Question<\/th><td>true<\/td>/);
  assert.doesNotMatch(html, /Base Speed|Base Crit Chance|<pre>/);
});

test("world constants print and do not reuse another dataset's name", () => {
  const r = renderers(load());
  assert.equal(r.directive("percent:EliteChanceFloors3To10"), "8");
  assert.equal(r.directive("balance:GauntletWaveCount"), "10");
  assert.equal(r.directive("balance:MendEveryRounds"), "3");
  assert.equal(r.directive("balance:Floor1MonsterStatMultiplier"), "0.5");
});
