"use strict";
// 1.3.7: the built pages spell the stat "defense", as the game text does. The built output (web/wiki, made by
// npm run build) must exist; a missing build fails here, never passes. WIKI_SITE_DIR points the test elsewhere.
const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const siteDir =
  process.env.WIKI_SITE_DIR || path.resolve(__dirname, "../../../web/wiki");
// Kept text: a pattern that may say Defence, by page.
// items/armor: the gear set bonus rows are the game's own en.json item.set.bonus.defence ("+{0} Defence").
// reference/balance: the name of the C# constant GodBoonJudicarDefencePct.
const kept = [
  ["en/items/armor/index.html", /\+\d+ Defence/g],
  ["en/reference/balance/index.html", /God Boon Judicar Defence Pct/g],
];
function htmlFiles(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) htmlFiles(p, out);
    else if (p.endsWith(".html")) out.push(p);
  }
  return out;
}

test("built pages say defense, not defence, outside the kept list", () => {
  assert.ok(
    fs.existsSync(siteDir),
    `the build output ${siteDir} is missing: run npm run build first`,
  );
  const files = htmlFiles(siteDir);
  assert.ok(files.length > 0, "the build output holds no page");
  const bad = [];
  for (const f of files) {
    const rel = path.relative(siteDir, f).split(path.sep).join("/");
    let text = fs
      .readFileSync(f, "utf8")
      .replace(/<(script|style)[\s\S]*?<\/\1>/gi, " ")
      .replace(/<[^>]*>/g, " ");
    for (const [file, re] of kept) if (file === rel) text = text.replace(re, "");
    if (/defence/i.test(text)) bad.push(rel);
  }
  assert.deepEqual(bad, []);
});
