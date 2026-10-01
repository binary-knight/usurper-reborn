"use strict";
const { test, before, after } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { build, loadData, renderers, textOf } = require("../build");
const { search } = require("../../../web/wiki-search");
const dataDir =
  process.env.WIKI_DATA_DIR || path.resolve(__dirname, "../../../wiki-data");
let temp, result;
before(() => {
  temp = fs.mkdtempSync(path.join(os.tmpdir(), "usurper-wiki-test-"));
  result = build({ dataDir, outDir: path.join(temp, "site") });
});
after(() => fs.rmSync(temp, { recursive: true, force: true }));
test("stable IDs, generated numbers and complete references", () => {
  assert.ok(result.pages.length > 900);
  const item = result.pages.find((p) => p.path === "/wiki/en/items/1000/");
  assert.equal(item.title, "Rusty Dagger");
  assert.match(item.body, /Weapon Power<\/th><td>2/);
  const terran = result.pages.find((p) => p.path === "/wiki/en/gods/terran/");
  assert.match(terran.body, /Max HP Pct<\/th><td>10/);
  assert.match(
    result.pages.find((p) => p.path === "/wiki/en/characters/mental/").body,
    /Stable/,
  );
  assert.match(
    result.pages.find(
      (p) => p.path === "/wiki/en/characters/abilities/power_strike/",
    ).body,
    /Base Damage<\/th><td>35/,
  );
  assert.ok(
    result.pages.some((p) => p.path === "/wiki/en/characters/spells/Cleric-1/"),
  );
  assert.ok(fs.existsSync(path.join(temp, "site/en/items/1000/index.html")));
});
test("secret entities have no standalone pages or search text", () => {
  const { data } = loadData(dataDir);
  const index = JSON.stringify(result.search);
  for (const entity of [
    ...data.bosses.secret,
    ...data.bosses.oldGods,
    ...data.achievements.filter((a) => a.isSecret),
  ]) {
    assert.ok(
      !index.toLowerCase().includes(entity.name.en.toLowerCase()),
      entity.name.en,
    );
    assert.ok(
      !result.pages.some(
        (p) => p.path === `/wiki/en/monsters/bosses/${entity.id}/`,
      ),
    );
  }
  assert.ok(
    !result.search
      .find((p) => p.path === "/wiki/en/characters/mental/")
      .text.includes("Rescue sets Mental"),
  );
  assert.match(
    result.pages.find((p) => p.path === "/wiki/en/monsters/bosses/").body,
    /<details/,
  );
});
test("HTML injection is escaped and details excluded", () => {
  assert.equal(
    textOf("<p>public</p><details><summary>secret</summary>hidden</details>"),
    "public",
  );
  assert.equal(
    textOf(
      "public<details>outer<details>inner</details>after inner</details>end",
    ),
    "publicend",
  );
  const { data } = loadData(dataDir);
  data.gods.gods[0].description = "<img src=x onerror=alert(1)>";
  assert.match(renderers(data).god(data.gods.gods[0]), /&lt;img/);
});
test("all rendered pages use the agreed punctuation and semantic layout", () => {
  for (const page of result.pages) {
    assert.ok(
      !/[\u2013\u2014]|\p{Extended_Pictographic}/u.test(page.body),
      page.path,
    );
    assert.ok(!page.body.includes("<th>"), page.path);
    const html = fs.readFileSync(
      path.join(temp, "site", page.path.slice("/wiki/".length), "index.html"),
      "utf8",
    );
    assert.equal((html.match(/<h1>/g) || []).length, 1, page.path);
    assert.ok(
      html.includes('lang="en"') &&
        html.includes('aria-label="Breadcrumb"') &&
        html.includes('id="main"'),
      page.path,
    );
  }
});
test("invalid directives and filters fail closed", () => {
  const render = renderers(loadData(dataDir).data);
  for (const directive of [
    "oops:items",
    "god:missing",
    "balance:missing",
    "table:unknown",
    "table:items slot=NoSuchSlot",
    "table:items typo=x",
    "monster-family:missing",
  ])
    assert.throws(() => render.directive(directive));
  assert.match(
    render.directive("monster-family:Undead"),
    /Monster tier samples/,
  );
});
function invalidPage(body, extra = "") {
  const content = fs.mkdtempSync(path.join(temp, "content-"));
  fs.writeFileSync(
    path.join(content, "index.md"),
    `---\ntitle: Test\npath: /wiki/en/\nchecked: 1.2.0\nsources: Scripts/Core/GameConfig.cs\n${extra}---\n${body}`,
  );
  return () =>
    build({ dataDir, contentDir: content, outDir: path.join(temp, "invalid") });
}
test("broken local links, anchors, malformed directives and duplicate routes fail", () => {
  assert.throws(
    invalidPage("[Missing](/wiki/en/no-such-page/)"),
    /Broken wiki link/,
  );
  assert.throws(invalidPage("[Missing](#wrong-anchor)"), /Broken anchor/);
  assert.throws(invalidPage("{{balance:missing}}"), /Unknown balance/);
  assert.throws(invalidPage("{{table:items"), /Malformed directive/);
  assert.throws(
    invalidPage(":::spoiler Missing end\ncontent"),
    /Unclosed spoiler/,
  );
  assert.throws(
    invalidPage(":::spoiler Outer\n:::spoiler Inner\ncontent\n:::\n:::"),
    /Nested spoiler/,
  );
  assert.throws(invalidPage("# Extra title"), /level-two/);
  assert.throws(invalidPage("Text", "unknown: field\n"), /Unknown frontmatter/);
});
test("duplicate URLs and mismatched dataset versions are rejected", () => {
  const content = fs.mkdtempSync(path.join(temp, "duplicate-"));
  const prose = fs.readFileSync(
    path.resolve(__dirname, "../../../DOCS/wiki/index.md"),
  );
  fs.writeFileSync(path.join(content, "one.md"), prose);
  fs.writeFileSync(path.join(content, "two.md"), prose);
  assert.throws(
    () =>
      build({
        dataDir,
        contentDir: content,
        outDir: path.join(temp, "duplicate-output"),
      }),
    /Duplicate URL/,
  );
  const input = path.join(temp, "mismatch");
  fs.cpSync(dataDir, input, { recursive: true });
  const itemFile = path.join(input, "items.json");
  const items = JSON.parse(fs.readFileSync(itemFile));
  items.gameVersion = "0.0.0";
  fs.writeFileSync(itemFile, JSON.stringify(items));
  assert.throws(() => loadData(input), /Mismatched schema\/version/);
});
test("rebuild removes obsolete generated pages but preserves other assets", () => {
  const site = path.join(temp, "site");
  const manifest = JSON.parse(
    fs.readFileSync(path.join(site, "manifest.json")),
  );
  manifest.paths.push("/wiki/en/obsolete/");
  fs.writeFileSync(path.join(site, "manifest.json"), JSON.stringify(manifest));
  fs.mkdirSync(path.join(site, "en/obsolete"), { recursive: true });
  fs.writeFileSync(
    path.join(site, "en/obsolete/index.html"),
    "old generated page",
  );
  fs.writeFileSync(path.join(site, "keep.txt"), "unrelated asset");
  build({ dataDir, outDir: site });
  assert.ok(!fs.existsSync(path.join(site, "en/obsolete/index.html")));
  assert.equal(
    fs.readFileSync(path.join(site, "keep.txt"), "utf8"),
    "unrelated asset",
  );
});
test("search is deterministic and returns no answer for an unknown question", () => {
  assert.equal(
    search(result.search, "Terran")[0].path,
    "/wiki/en/gods/terran/",
  );
  assert.ok(
    search(result.search, "how does Favor work?").some(
      (p) => p.path === "/wiki/en/gods/favor/",
    ),
  );
  assert.deepEqual(search(result.search, "zzzzunknown"), []);
  assert.deepEqual(search(result.search, "the and what"), []);
  assert.equal(
    search(result.search, "What level can I specialize?")[0].path,
    "/wiki/en/characters/specializations/",
  );
  assert.equal(
    search(result.search, "how much does it cost to change specialization?")[0]
      .path,
    "/wiki/en/characters/specializations/",
  );
});
test("history lines: valid forms parse and each invalid form fails with a clear message", () => {
  const { parseHistory, readPages } = require("../build");
  assert.deepEqual(parseHistory(["1.2.3 | Shops sell more.", "1.2.2 | none"], "f.md", "1.2.3"), [
    { version: "1.2.3", sentence: "Shops sell more." },
    { version: "1.2.2", sentence: null },
  ]);
  assert.deepEqual(parseHistory([], "f.md", "1.2.3"), []);
  const cases = [
    ["history: 1.2 | Short version.\n", /1\.2 is not x\.y\.z/],
    ["history: v1.2.0 | Prefixed version.\n", /v1\.2\.0 is not x\.y\.z/],
    ["history: 1.2.0 |\n", /Empty history sentence in .* for 1\.2\.0/],
    ["history: 1.2.0 |   \n", /Empty history sentence/],
    ["history: 1.2.0 Missing bar.\n", /Invalid history line/],
    ["history: 1.2.0 | One.\nhistory: 1.2.0 | Two.\n", /Two history lines for 1\.2\.0/],
    ["history: 1.2.0 | none\nhistory: 1.2.0 | none\n", /Two history lines for 1\.2\.0/],
    ["history: 1.2.0 | none\nhistory: 1.2.0 | A sentence.\n", /both none and a sentence/],
    ["history: 1.2.0 | A sentence.\nhistory: 1.2.0 | none\n", /both none and a sentence/],
    ["history: 99.0.0 | Too new.\n", /99\.0\.0 .* newer than the game version/],
    ["history: 1.2.0 | Long \u2014 dash.\n", /Forbidden punctuation/],
    ["histories: 1.2.0 | Typo field.\n", /Unknown frontmatter/],
  ];
  for (const [extra, message] of cases)
    assert.throws(invalidPage("Text", extra), message, extra);
  // Without a game version (drift passes its own) the newer check is skipped.
  const dir = fs.mkdtempSync(path.join(temp, "history-read-"));
  fs.writeFileSync(
    path.join(dir, "a.md"),
    "---\ntitle: A\npath: /wiki/en/\nchecked: 1.2.0\nsources: README.md\nhistory: 99.0.0 | Later.\n---\nText\n",
  );
  assert.equal(readPages(dir)[0].history[0].version, "99.0.0");
  assert.throws(() => readPages(dir, { gameVersion: "1.2.2" }), /newer than the game version 1\.2\.2/);
});
function historySite() {
  const content = fs.mkdtempSync(path.join(temp, "history-content-"));
  fs.cpSync(path.resolve(__dirname, "../../../DOCS/wiki"), content, { recursive: true });
  const favor = path.join(content, "gods/favor.md");
  const version = loadData(dataDir).meta.gameVersion;
  fs.writeFileSync(
    favor,
    fs
      .readFileSync(favor, "utf8")
      .replace(
        /^(sources: .*)$/m,
        `$1\nhistory: 1.1.9 | Oldest qxoldword note.\nhistory: ${version} | Newest qxhistoryword note with <b>tags</b> & "quotes".\nhistory: 1.1.10 | Middle note.\nhistory: 1.2.1 | none`,
      ),
  );
  const out = path.join(temp, "history-site");
  const built = build({ dataDir, contentDir: content, outDir: out });
  const html = (p) => fs.readFileSync(path.join(out, p.slice("/wiki/".length), "index.html"), "utf8");
  return { built, out, html, version };
}
test("history section: newest first, escaped, none hidden, labelled, only where entries exist", () => {
  const { html, version } = historySite();
  const page = html("/wiki/en/gods/favor/");
  const section = page.match(/<section class="history"[\s\S]*?<\/section>/);
  assert.ok(section, "section rendered");
  assert.match(section[0], /<h2 id="changes-by-version">Changes by version<\/h2>/);
  assert.deepEqual(
    [...section[0].matchAll(/<dt>([^<]+)<\/dt>/g)].map((m) => m[1]),
    [version, "1.1.10", "1.1.9"],
  );
  assert.ok(section[0].includes("&lt;b&gt;tags&lt;/b&gt; &amp; &quot;quotes&quot;"));
  assert.ok(!section[0].includes("<b>"));
  assert.ok(!section[0].includes("1.2.1"), "none marker is not shown");
  assert.ok(!/>none</.test(section[0]));
  // Inside main, after the body and before the footer.
  assert.ok(page.indexOf("</section><footer>") > page.indexOf('id="main"'));
  for (const p of ["/wiki/en/gods/boons/", "/wiki/en/items/1000/", "/wiki/en/"])
    assert.ok(!html(p).includes('class="history"'), p);
});
test("history section: a guide with only none markers shows no section", () => {
  const content = fs.mkdtempSync(path.join(temp, "history-none-"));
  fs.cpSync(path.resolve(__dirname, "../../../DOCS/wiki"), content, { recursive: true });
  const favor = path.join(content, "gods/favor.md");
  fs.writeFileSync(
    favor,
    fs.readFileSync(favor, "utf8").replace(/^(sources: .*)$/m, "$1\nhistory: 1.2.0 | none\nhistory: 1.2.1 | none"),
  );
  const out = path.join(temp, "history-none-site");
  build({ dataDir, contentDir: content, outDir: out });
  const page = fs.readFileSync(path.join(out, "en/gods/favor/index.html"), "utf8");
  assert.ok(!page.includes('class="history"'));
  assert.ok(!page.includes("Changes by version"));
});
test("history entries stay out of search, page descriptions and Ask replies", async () => {
  const { built, out, html } = historySite();
  const entry = built.search.find((p) => p.path === "/wiki/en/gods/favor/");
  assert.ok(!entry.text.includes("qxhistoryword") && !entry.text.includes("qxoldword"));
  assert.ok(!entry.headings.includes("Changes by version"));
  assert.ok(!fs.readFileSync(path.join(out, "search-index.json"), "utf8").includes("qxhistoryword"));
  assert.deepEqual(search(built.search, "qxhistoryword"), []);
  assert.equal(search(built.search, "how does Favor work?")[0].path, "/wiki/en/gods/favor/");
  const meta = html("/wiki/en/gods/favor/").match(/<meta name="description" content="([^"]*)"/)[1];
  assert.ok(!meta.includes("qxhistoryword"));
  const { createWikiBot, loadIndex } = require("../../../web/wiki-bot");
  const sent = [];
  const bot = createWikiBot({
    channels: ["allowed"],
    index: loadIndex(path.join(out, "search-index.json")),
    logger: { error() {} },
  });
  const ask = (content) => ({
    id: String(sent.length + 1),
    guildId: "guild",
    channelId: "allowed",
    content,
    author: { id: "user" + sent.length, bot: false },
    member: { roles: { cache: new Set() } },
    channel: { send: async (payload) => sent.push(payload) },
  });
  assert.equal(await bot.handle(ask("<@123> how does Favor work?"), "123"), true);
  assert.match(sent[0].content, /wiki\/en\/gods\/favor\//);
  assert.ok(!sent[0].content.includes("qxhistoryword"));
  assert.equal(await bot.handle(ask("<@123> qxhistoryword"), "123"), true);
  assert.ok(!/wiki\/en\/gods\/favor\//.test(sent[1].content));
});
