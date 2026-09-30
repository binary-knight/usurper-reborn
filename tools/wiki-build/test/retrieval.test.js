"use strict";
// Retrieval quality against the real built index: guides first, exact names still win.
const { test, before, after } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { build } = require("../build");
const { search, excerpt } = require("../../../web/wiki-search");
const dataDir =
  process.env.WIKI_DATA_DIR || path.resolve(__dirname, "../../../wiki-data");
let temp, pages;
before(() => {
  temp = fs.mkdtempSync(path.join(os.tmpdir(), "usurper-wiki-retrieval-"));
  const out = path.join(temp, "site");
  build({ dataDir, outDir: out });
  pages = JSON.parse(
    fs.readFileSync(path.join(out, "search-index.json"), "utf8"),
  ).pages;
});
after(() => fs.rmSync(temp, { recursive: true, force: true }));

const top = (query, n) => search(pages, query, n).map((p) => p.path);
const W = "/wiki/en/";

// Questions whose guide page was missing from the excerpts sent to the model.
const misses = [
  ["What does praying at the Temple do?", `${W}gods/favor/`, 3],
  ["How do I choose a god?", `${W}gods/`, 3],
  ["How do I choose a god?", `${W}gods/favor/`, 5],
  ["What monster families are in the dungeon?", `${W}monsters/`, 3],
  ["Where can I buy armor?", `${W}items/shops/`, 3],
  ["Where can I buy armor?", `${W}items/armor/`, 3],
  ["What stats matter for a warrior?", `${W}characters/stats/`, 3],
  ["What are the main weapon slots?", `${W}items/weapons/`, 3],
];
// Questions that already retrieved the right guide; they must keep doing so.
const good = [
  ["How does Favor work?", `${W}gods/favor/`],
  ["What classes can I play?", `${W}characters/classes/`],
  ["How do I raise my Mental attribute?", `${W}characters/mental/`],
  ["What happens when Mental gets low?", `${W}characters/mental/`],
  ["What races are there?", `${W}characters/races/`],
  ["How does the Sage class work?", `${W}characters/classes/Sage/`],
  ["What are god boons?", `${W}gods/boons/`],
  ["What does the Inn do?", `${W}world/rest/`],
  ["What spells does the Cleric get?", `${W}characters/spells/`],
  ["What is the Healer for?", `${W}world/healer/`],
];

// 1.2.1 guides: each question must retrieve its guide in the top 3.
const guides121 = [
  ["How do I become king?", `${W}world/castle/`],
  ["How do I choose a god?", `${W}gods/`],
  ["What equipment slots are there?", `${W}items/equipment-slots/`],
  ["How does NG+ work?", `${W}characters/new-game-plus/`],
  ["Where is the Arena?", `${W}online/arena/`],
];
for (const [question, expected] of guides121)
  test(`1.2.1 guide: "${question}" finds ${expected} in the top 3`, () => {
    assert.ok(top(question, 3).includes(expected), top(question, 5).join(" "));
  });

// 1.2.2: bug reporting questions must retrieve the reporting guide in the top 3.
const bugReports = [
  "I found a bug, where do I report it?",
  "how do I report a bug",
  "bug report",
  "where do I submit an issue",
];
for (const question of bugReports)
  test(`bug guide: "${question}" finds ${W}getting-started/reporting-bugs/ in the top 3`, () => {
    assert.ok(
      top(question, 3).includes(`${W}getting-started/reporting-bugs/`),
      top(question, 5).join(" "),
    );
  });

// 1.2.2: dungeon feature and event questions must retrieve the new guides in the top 3.
const dungeonGuides = [
  ["is there a wiki page on the various random events and features found in the dungeon?", `${W}world/dungeon-features/`],
  ["dungeon random events", `${W}world/dungeon-features/`],
  ["what are dungeon shrines", `${W}world/dungeon-features/`],
  ["traps in the dungeon", `${W}world/dungeon-features/`],
  ["rare encounters", `${W}world/rare-encounters/`],
];
for (const [question, expected] of dungeonGuides)
  test(`dungeon guide: "${question}" finds ${expected} in the top 3`, () => {
    assert.ok(top(question, 3).includes(expected), top(question, 5).join(" "));
  });

// Sentences written inside :::spoiler blocks of the 1.2.1 guides, as plain text.
function spoilerSentences(file) {
  const md = fs.readFileSync(
    path.resolve(__dirname, "../../../DOCS/wiki", file),
    "utf8",
  );
  const bodies = [...md.matchAll(/^:::spoiler [^\n]+\n([\s\S]*?)^:::\s*$/gm)];
  assert.ok(bodies.length > 0, `${file} has spoiler blocks`);
  return bodies
    .flatMap((m) => m[1].replace(/\[([^\]]*)\]\([^)]*\)/g, "$1").split(/(?<=[.!?])\s+/))
    .map((t) => t.replace(/\s+/g, " ").trim())
    .filter((t) => t.length >= 20);
}
test("text inside the 1.2.1 guides' spoiler blocks never reaches the search index", () => {
  const indexed = pages
    .map((p) => [p.title, ...(p.headings || []), p.text].join(" ").replace(/\s+/g, " "))
    .join("\n");
  const story = pages.find((p) => p.path === `${W}world/story/`);
  assert.match(story.text, /wakes with no memory/, "public story text is indexed");
  let checked = 0;
  for (const file of ["world/story.md", "characters/new-game-plus.md"])
    for (const sentence of spoilerSentences(file)) {
      assert.ok(!indexed.includes(sentence), `${file}: ${sentence}`);
      checked++;
    }
  assert.ok(checked >= 10, `${checked} spoiler sentences checked`);
});

test("text inside the dungeon guides' spoiler blocks never reaches the search index", () => {
  const indexed = pages
    .map((p) => [p.title, ...(p.headings || []), p.text].join(" ").replace(/\s+/g, " "))
    .join("\n");
  const features = pages.find((p) => p.path === `${W}world/dungeon-features/`);
  assert.match(features.text, /Search for Traps is free/, "public feature text is indexed");
  let checked = 0;
  for (const file of ["world/dungeon-features.md", "world/rare-encounters.md"])
    for (const sentence of spoilerSentences(file)) {
      assert.ok(!indexed.includes(sentence), `${file}: ${sentence}`);
      checked++;
    }
  assert.ok(checked >= 40, `${checked} spoiler sentences checked`);
});

test("the index marks hand-written guides and only those", () => {
  const guides = pages.filter((p) => p.guide);
  assert.ok(guides.some((p) => p.path === `${W}gods/favor/`));
  assert.ok(guides.some((p) => p.path === `${W}monsters/`));
  for (const entity of [`${W}items/1000/`, `${W}gods/terran/`, `${W}characters/spells/Cleric-1/`])
    assert.equal(pages.find((p) => p.path === entity).guide, undefined, entity);
  assert.ok(guides.length >= 50 && guides.length < 100, `${guides.length} guides`);
});

for (const [question, expected, n] of misses)
  test(`former miss: "${question}" finds ${expected} in the top ${n}`, () => {
    assert.ok(top(question, n).includes(expected), top(question, 5).join(" "));
  });

for (const [question, expected] of good)
  test(`kept: "${question}" finds ${expected} in the top 3`, () => {
    assert.ok(top(question, 3).includes(expected), top(question, 5).join(" "));
  });

test("guides outrank generated entity pages that only share words", () => {
  for (const [question, guide] of [
    ["Where can I buy armor?", `${W}items/shops/`],
    ["What are the main weapon slots?", `${W}items/weapons/`],
    ["What monster families are in the dungeon?", `${W}monsters/`],
    ["What stats matter for a warrior?", `${W}characters/stats/`],
  ]) {
    const results = search(pages, question, 10);
    const guideAt = results.findIndex((p) => p.path === guide);
    const firstUnnamedEntity = results.findIndex(
      (p) => !p.guide && p.path !== `${W}characters/classes/Warrior/`,
    );
    assert.ok(guideAt >= 0, question);
    assert.ok(
      firstUnnamedEntity < 0 || guideAt < firstUnnamedEntity,
      `${question}: ${results.map((p) => p.path).join(" ")}`,
    );
  }
});

test("an exact entity name still returns that entity first", () => {
  assert.equal(top("Cure Light", 1)[0], `${W}characters/spells/Cleric-1/`);
  assert.equal(top("Warrior's Belt", 1)[0], `${W}items/10003/`);
  assert.equal(top("warriors belt", 1)[0], `${W}items/10003/`);
  assert.equal(top("Terran", 1)[0], `${W}gods/terran/`);
  // Names whose words also fill a guide: the guide must not take the named entity's place.
  assert.equal(top("Prayer of Mending", 1)[0], `${W}characters/abilities/prayer_of_mending/`);
  assert.equal(top("Boots of the Gods", 1)[0], `${W}items/9032/`);
  assert.equal(top("Song of Rest", 1)[0], `${W}characters/abilities/song_of_rest/`);
  // A class named inside a question outranks items that merely share its name.
  const warrior = top("What stats matter for a warrior?", 5);
  assert.ok(
    warrior.indexOf(`${W}characters/classes/Warrior/`) <
      warrior.indexOf(`${W}characters/stats/`),
  );
});

test("title and headings weigh more than body text", () => {
  // "Boons and prayer" is a Favor heading; the Healer page mentions prayer only in passing.
  const favor = search(pages, "prayer", 20);
  assert.equal(favor[0].path, `${W}gods/favor/`);
  const shops = search(pages, "auctions", 5);
  assert.equal(shops[0].path, `${W}items/shops/`);
});

test("a query phrase found as adjacent words outranks pages with the words apart", () => {
  // Only the monsters page has "Monster families"; the dungeon guides mention monsters
  // and the dungeon separately, and the home page has "family" in its title.
  assert.equal(top("What monster families are in the dungeon?", 1)[0], `${W}monsters/`);
  assert.equal(top("monster families", 1)[0], `${W}monsters/`);
});

test("plurals and -ing forms match their base word", () => {
  assert.ok(top("monster family", 3).includes(`${W}monsters/`));
  assert.ok(top("buying armour", 3).includes(`${W}items/shops/`));
  assert.ok(top("sleeping at the inn", 1).includes(`${W}world/rest/`));
});

test("the excerpt shows the part of the page that answers the question", () => {
  const favor = pages.find((p) => p.path === `${W}gods/favor/`);
  const text = excerpt(favor, "What does praying at the Temple do?", 360);
  assert.match(text, /Daily Temple prayer gives \d+ Favor/);
  // The best window, not the first mention: these terms appear early on each page too.
  assert.match(excerpt(favor, "switching gods", 200), /Leaving a god discards/);
  const mental = pages.find((p) => p.path === `${W}characters/mental/`);
  assert.match(
    excerpt(mental, "How does the Healer help Mental?", 200),
    /the Healer\. Inn sleep restores/,
  );
  const shops = pages.find((p) => p.path === `${W}items/shops/`);
  assert.match(excerpt(shops, "auction lots", 200), /Auction/);
  assert.ok(excerpt(shops, "auction lots", 200).length <= 206);
});

test("a warm query stays fast on the full index", () => {
  search(pages, "warm up the cache", 5);
  const started = process.hrtime.bigint();
  for (let i = 0; i < 20; i++) search(pages, good[i % good.length][0], 5);
  const ms = Number(process.hrtime.bigint() - started) / 1e6 / 20;
  assert.ok(ms < 17, `${ms.toFixed(2)} ms per query`);
});
