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
