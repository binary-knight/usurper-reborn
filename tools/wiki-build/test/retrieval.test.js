"use strict";
// Retrieval quality against the real built index: guides first, exact names still win.
const { test, before, after } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { build } = require("../build");
const { search, excerpt, queryTerms } = require("../../../web/wiki-search");
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

// 1.2.4: questions asked in Discord about immortals must return the immortals guide first.
const immortals = [
  "what info do you have on immortals",
  "how do I become an immortal",
  "immortals",
];
for (const question of immortals)
  test(`immortals guide: "${question}" returns ${W}gods/player-gods/ first`, () => {
    assert.equal(top(question, 1)[0], `${W}gods/player-gods/`, top(question, 5).join(" "));
  });

// 1.2.4: question filler words are stop words, so they change neither the results nor the scores.
const fillers = ["info", "information", "know", "anything", "something", "please", "tell", "about"];
for (const word of fillers)
  test(`filler word "${word}" is ignored by search`, () => {
    const plain = search(pages, "immortals", 8).map((p) => [p.path, p.score]);
    const filled = search(pages, `${word} immortals`, 8).map((p) => [p.path, p.score]);
    assert.deepEqual(filled, plain);
  });

// Synthetic pages: A has both query words in its body, B and C one each, D neither.
const synthetic = [
  { path: "/a/", title: "Page A", headings: [], text: "alpha beta" },
  { path: "/b/", title: "Page B", headings: [], text: "alpha gamma" },
  { path: "/c/", title: "Page C", headings: [], text: "beta delta" },
  { path: "/d/", title: "Page D", headings: [], text: "epsilon" },
];
test("the fallback fills the requested count with partial matches, never unrelated pages", () => {
  assert.deepEqual(search(synthetic, "alpha beta", 8).map((p) => p.path), ["/a/", "/b/", "/c/"]);
  assert.deepEqual(search(synthetic, "alpha beta", 2).map((p) => p.path), ["/a/", "/b/"]);
});
test("the fallback ranks pages by how many distinct query words they match", () => {
  const pages3 = [
    { path: "/one/", title: "One", headings: [], text: "alpha alpha alpha alpha" },
    { path: "/two/", title: "Two", headings: [], text: "alpha beta" },
    { path: "/all/", title: "All", headings: [], text: "alpha beta gamma" },
  ];
  // "/two/" matches two words and passes the first pass; "/one/" matches one and is a fallback.
  assert.deepEqual(search(pages3, "alpha beta gamma", 8).map((p) => p.path), ["/all/", "/two/", "/one/"]);
});
test("a page matching every query word outranks any page matching only some", () => {
  // A title match on one word scores far above a body match on both, but must rank below it.
  const titled = [
    { path: "/title/", title: "Alpha", headings: ["Alpha"], text: "alpha alpha alpha", guide: true },
    { path: "/full/", title: "Other", headings: [], text: "alpha beta" },
  ];
  assert.deepEqual(search(titled, "alpha beta", 8).map((p) => p.path), ["/full/", "/title/"]);
  // The same order holds on the real index for every question in this file.
  const questions = [
    ...misses.map((m) => m[0]), ...good.map((g) => g[0]), ...guides121.map((g) => g[0]),
    ...bugReports, ...dungeonGuides.map((d) => d[0]), ...immortals,
  ];
  for (const question of questions) {
    const n = queryTerms(question).length;
    const full = search(pages, question, 20).map((p) => p.matched === n);
    const firstPartial = full.indexOf(false);
    assert.ok(firstPartial < 0 || !full.slice(firstPartial).includes(true), question);
  }
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
  // 1.3.2: plain trap, chest, shrine and rare encounter numbers left the folds (the user's ruling A7);
  // the folds that remain hold the story: guardian, seal and secret boss floors and the Old God fights
  for (const file of ["world/dungeon-features.md", "world/dungeon.md", "monsters/bosses.md"])
    for (const sentence of spoilerSentences(file)) {
      assert.ok(!indexed.includes(sentence), `${file}: ${sentence}`);
      checked++;
    }
  assert.ok(checked >= 15, `${checked} spoiler sentences checked`);
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

// 1.3.2 (W8): a guide's own path segment is a search word below its title, so a question about
// a changelog finds the page at reference/changelog/ (titled "Release history"); "wiki" is a
// stop word, so with no such page the question returns nothing instead of the home page.
test("a guide's path word finds it, and a title still outranks a path word", () => {
  for (const question of ["does the wiki have a changelog", "changelog", "is there a changelog"])
    assert.equal(top(question, 1)[0], `${W}reference/changelog/`, `${question}: ${top(question, 5).join(" ")}`);
  const without = pages.filter((p) => p.path !== `${W}reference/changelog/`);
  assert.deepEqual(search(without, "does the wiki have a changelog", 5).map((p) => p.path), []);
  assert.deepEqual(top("wiki", 5), []);
  const synthetic = [
    { path: "/wiki/en/reference/changelog/", title: "History", headings: [], text: "notes", guide: true },
    { path: "/wiki/en/reference/history/", title: "Changelog", headings: [], text: "notes", guide: true },
    { path: "/wiki/en/items/1/", title: "Item", headings: [], text: "notes" },
  ];
  assert.deepEqual(search(synthetic, "changelog", 5).map((p) => p.path), [
    "/wiki/en/reference/history/",
    "/wiki/en/reference/changelog/",
  ]);
  // Generated entity pages have numeric IDs in their path; those never count.
  assert.deepEqual(search(synthetic, "1", 5).map((p) => p.path), []);
});

// 1.3.2 (W8): the excerpt opens on the heading of the section that answers the question and
// keeps its numbers, instead of a window chosen by words common on the whole page.
test("the excerpt opens on the answering section and keeps its numbers", () => {
  const combat = pages.find((p) => p.path === `${W}combat/`);
  assert.match(excerpt(combat, "what is my dodge chance", 1500), /Dodge: 5 percent plus 1 percent per 2 Agility above 10, at most 35 percent/);
  assert.match(excerpt(combat, "what does power attack do", 420), /^\.\.\.Power Attack \(P\) /);
  assert.match(excerpt(combat, "what does power attack do", 420), /Power Attack costs 15 combat stamina/);
  const consumables = pages.find((p) => p.path === `${W}items/consumables/`);
  assert.match(excerpt(consumables, "how many potions can i carry", 1500), /You can carry 20 \+ \(level - 1\) healing potions/);
});

// 1.3.2 (W8): spoiler folds. The index carries each guide's fold titles (the summary line
// only); the fold's text stays out of the index and so out of the bot prompt.
const { buildLlmRequest, createWikiBot, LLM_SYSTEM_PROMPT, SUPPRESS_EMBEDS } = require("../../../web/wiki-bot");
const ORIGIN = "https://usurper-reborn.net";
const FOLD_NOTE = "[Spoiler folds on this page, text not given: ";
const foldFiles = ["world/story.md", "world/dungeon.md", "world/dungeon-features.md", "monsters/bosses.md", "characters/new-game-plus.md", "gods/player-gods.md", "items/materials.md"];
const foldSentences = () => foldFiles.flatMap(spoilerSentences);
test("the index lists each guide's spoiler fold titles and nothing else from the fold", () => {
  const story = pages.find((p) => p.path === `${W}world/story/`);
  assert.deepEqual(story.folds, [
    "Spoiler: seal floors and the Old Gods",
    "Spoiler: what each answer and outcome gives",
    "Spoiler: ending conditions",
    "Spoiler: story chapters and the moral choices",
  ]);
  assert.equal(pages.find((p) => p.path === `${W}combat/companions/`).folds, undefined);
  for (const p of pages) if (p.folds) assert.ok(p.guide, `${p.path} has folds but is not a guide`);
  const titles = pages.flatMap((p) => p.folds || []).join("\n");
  for (const sentence of foldSentences()) assert.ok(!titles.includes(sentence), sentence);
});

const table = require("./acceptance-table");
const foldRows = table.filter((r) => r[4] === "fold");
test("a FOLD question links the fold's page and the prompt names the fold, never its text", () => {
  assert.ok(foldRows.length >= 10, `${foldRows.length} fold rows`);
  assert.match(LLM_SYSTEM_PROMPT, /behind that spoiler fold on the linked page/);
  const hidden = foldSentences();
  for (const [id, question, page, title] of foldRows) {
    const matches = search(pages, question, 5);
    assert.ok(matches.some((p) => p.path === page), `${id}: ${matches.map((p) => p.path).join(" ")}`);
    const content = buildLlmRequest(question, matches, ORIGIN, "1.3.1").messages[0].content;
    const block = content.split("<excerpt ").find((b) => b.startsWith(`title=`) && b.includes(`url="${ORIGIN}${page}"`));
    assert.ok(block, `${id}: page sent to the model`);
    assert.ok(block.includes(FOLD_NOTE) && block.includes(title), `${id}: fold ${title} named`);
    for (const sentence of hidden) assert.ok(!content.includes(sentence), `${id}: ${sentence}`);
  }
});
test("the excerpt reply points to the fold, links its page and sends no link previews", async () => {
  const sent = [];
  const bot = createWikiBot({ channels: ["wiki"], index: { schemaVersion: 1, gameVersion: "1.3.1", pages }, logger: { error() {}, log() {} } });
  const ask = (content) => bot.handle({ id: "1", guildId: "g", channelId: "wiki", content: `<@9> ${content}`, author: { id: "u" + sent.length, bot: false }, channel: { send: async (m) => sent.push(m) } }, "9");
  await ask("how do i get each ending");
  assert.match(sent[0].content, /behind the spoiler fold Spoiler: ending conditions on this page/);
  assert.ok(sent[0].content.includes(`${ORIGIN}${W}world/story/`));
  assert.equal(sent[0].flags, SUPPRESS_EMBEDS);
  for (const sentence of foldSentences()) assert.ok(!sent[0].content.includes(sentence), sentence);
  // Companion recruitment is open text (no fold): the bot claims no fold for it.
  await ask("where do i recruit companions");
  const block = sent[1].content.split("\n\n").find((b) => b.endsWith(`${ORIGIN}${W}combat/companions/`));
  assert.ok(block, sent[1].content);
  assert.doesNotMatch(block, /spoiler fold/);
  const companions = buildLlmRequest("where do i recruit companions", search(pages, "where do i recruit companions", 5), ORIGIN, "1.3.1").messages[0].content
    .split("<excerpt ").find((b) => b.includes(`${W}combat/companions/"`));
  assert.ok(companions && !companions.includes(FOLD_NOTE));
});

// The acceptance set: every "first" row ranks its page first with the fact in its excerpt.
test("acceptance set: each answered question ranks its page first and its excerpt holds the fact", () => {
  assert.equal(table.length, 659);
  const first = table.filter((r) => r[4] === "first");
  const failed = [];
  for (const [id, question, page, fact] of first) {
    const at = top(question, 1)[0];
    const text = at === page ? excerpt(pages.find((p) => p.path === page), question, 1500).replace(/\s+/g, " ") : "";
    if (at !== page || !text.includes(fact)) failed.push(`${id} ${question}: ${at}`);
  }
  assert.deepEqual(failed, []);
  assert.ok(first.length >= 375, `${first.length} rows first`);
  const by = (re) => table.filter((r) => re.test(r[4])).length;
  console.log(`acceptance: ${first.length} first, ${foldRows.length} fold, ${by(/ranks first$/)} ranked lower, ${by(/outside the excerpt/)} excerpt misses, ${by(/fold page/)} fold misses, ${by(/^gap/)} gaps`);
});
