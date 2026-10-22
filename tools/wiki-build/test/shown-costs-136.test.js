"use strict";
// 1.3.6 X1: the class roles table states the stamina tier rule once, and the ability table shows the rounds
// Cutting Words and Legendary Shot hold (their templates hold 0).
const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { build, loadData, renderers, textOf } = require("../build");
const dataDir =
  process.env.WIKI_DATA_DIR || path.resolve(__dirname, "../../../wiki-data");
const load = () => loadData(dataDir).data;
const render = (directive, data = load()) =>
  renderers(data).directive(directive);
function row(html, header) {
  const found = html
    .split("</tr>")
    .find((r) => textOf((r.match(/<th scope="row">(.*?)<\/th>/s) || [])[1] || "") === header);
  assert.ok(found, `row ${header}`);
  return [...found.matchAll(/<td[^>]*>(.*?)<\/td>/gs)].map((m) => textOf(m[1]));
}

test("class-roles: the tier rule is stated once, ahead of the class sections, with the template costs", () => {
  const data = load();
  const html = render("table:class-roles", data);
  const [lead, ...sections] = html.split("<h4 ");
  assert.equal(sections.length, data.classes.length);
  assert.equal(
    textOf(lead),
    "Ability costs below are template costs. An ability that needs level 50 or more costs 25 percent more stamina, " +
      "and one that needs level 75 or more costs 40 percent more, rounded down. The combat screens show the cost charged.",
  );
  assert.equal((html.match(/rounded down/g) || []).length, 1, "the rule is stated once");
  const warrior = textOf("<h4 " + sections.find((s) => s.includes(">Warrior role numbers</h4>")));
  assert.match(warrior, /Iron Fortress \(level 85, 80 stamina, cooldown \d+ rounds, needs a shield\)/);
});

test("abilities table: Cutting Words and Legendary Shot show the rounds their effect lasts", () => {
  const data = load();
  const byId = (id) => data.abilities.find((a) => a.id === id);
  assert.equal(byId("cutting_words").duration, 0, "the template holds no duration");
  assert.equal(byId("legendary_shot").duration, 0, "the template holds no duration");
  const html = render("table:abilities", data);
  assert.equal(row(html, byId("cutting_words").name.en)[6], "4");
  assert.equal(row(html, byId("legendary_shot").name.en)[6], "1");
  for (const a of data.abilities.filter((x) => !["weaken", "legendary"].includes(x.specialEffect)))
    assert.equal(row(html, a.name.en)[6], String(a.duration), a.id);
});

test("ability pages: a page whose stamina cost the tier raises carries the tier note once", () => {
  const data = load();
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), "usurper-wiki-x1-"));
  try {
    const { pages } = build({ dataDir, outDir: path.join(temp, "site") });
    const page = (id) => pages.find((p) => p.path === `/wiki/en/characters/abilities/${id}/`);
    const note =
      "The stamina cost above is the template cost: an ability that needs level 50 or more costs 25 percent more stamina, " +
      "and one that needs level 75 or more costs 40 percent more, rounded down.";
    for (const a of data.abilities) {
      const text = textOf(page(a.id).body);
      const count = text.split(note).length - 1;
      assert.equal(count, a.staminaCost > 0 && a.levelRequired >= 50 ? 1 : 0, a.id);
    }
    assert.ok(textOf(page("champion_strike").body).includes(note));
    assert.ok(!textOf(page("savage_charge").body).includes(note), "level 47: the template is the charge");
    assert.ok(!textOf(page("windfury_totem").body).includes(note), "mana only");
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
});
