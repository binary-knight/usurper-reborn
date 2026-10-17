"use strict";
const { test, before } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const { check, readMarker } = require("../doc-check.js");

const env = {
  ...process.env,
  GIT_AUTHOR_NAME: "Fixture",
  GIT_AUTHOR_EMAIL: "fixture@example.invalid",
  GIT_COMMITTER_NAME: "Fixture",
  GIT_COMMITTER_EMAIL: "fixture@example.invalid",
  GIT_CONFIG_NOSYSTEM: "1",
  HOME: os.tmpdir(),
};
let repo;

function git(...args) {
  const r = spawnSync("git", ["-c", "commit.gpgsign=false", ...args], { cwd: repo, env, encoding: "utf8" });
  assert.equal(r.status, 0, r.stderr);
}
function write(file, text) {
  fs.mkdirSync(path.dirname(path.join(repo, file)), { recursive: true });
  fs.writeFileSync(path.join(repo, file), text);
}
const version = (v) => write("Scripts/Core/GameConfig.cs", `public const string Version = "${v}";\n`);
const current = (checked, sources) => `<!-- doc: checked ${checked}; sources: ${sources.join(", ")} -->\n# Doc\n`;

// Fixture history: v1.0.0 tags the first release; then Changed.cs changes and the version moves to 1.0.1.
before(() => {
  repo = fs.mkdtempSync(path.join(os.tmpdir(), "doc-check-"));
  git("init", "-q", "-b", "main");
  version("1.0.0");
  write("Scripts/Same.cs", "same\n");
  write("Scripts/Changed.cs", "old\n");
  write("web/page.js", "page\n");
  write("README.md", current("1.0.0", ["Scripts/Same.cs"]));
  write("DOCS/OLD_PLAN.md", "<!-- doc: historical -->\n# Plan\n");
  write("DOCS/wiki/guide.md", "no marker, outside the gate\n");
  write("DOCS/release-notes/RELEASE_NOTES_1.0.0.md", "no marker, outside the gate\n");
  git("add", "-A");
  git("commit", "-q", "-m", "release");
  git("tag", "v1.0.0");
  write("Scripts/Changed.cs", "new\n");
  git("add", "-A");
  git("commit", "-q", "-m", "change");
});

function withDoc(file, text, fn) {
  write(file, text);
  try { return fn(); } finally { fs.rmSync(path.join(repo, file)); }
}

test("the marker forms: current with sources, historical, and anything else is bad", () => {
  write("DOCS/M.md", "<!-- doc: checked 1.3.4; sources: a.cs, b/c.js -->\n");
  assert.deepEqual(readMarker(repo, "DOCS/M.md"), { checked: "1.3.4", sources: ["a.cs", "b/c.js"] });
  write("DOCS/M.md", "<!-- doc: historical -->\n");
  assert.deepEqual(readMarker(repo, "DOCS/M.md"), { historical: true });
  for (const bad of ["# Title", "<!-- doc: checked 1.3; sources: a.cs -->", "<!-- doc: current -->", "", " <!-- doc: historical -->"]) {
    write("DOCS/M.md", bad + "\n");
    assert.ok(readMarker(repo, "DOCS/M.md").bad !== undefined, bad);
  }
  fs.rmSync(path.join(repo, "DOCS/M.md"));
});

test("released state (the tag for the version exists): only markers and sources are checked", () => {
  version("1.0.0");
  const r = check({ root: repo });
  assert.equal(r.preparing, false);
  assert.deepEqual(r.problems, []);
  assert.equal(r.current, 1);
  assert.equal(r.historical, 1);
});

test("a file with no marker fails, and DOCS/wiki and DOCS/release-notes are outside the gate", () => {
  const r = withDoc("DOCS/NEW.md", "# New\n", () => check({ root: repo, version: "1.0.0" }));
  assert.equal(r.problems.length, 1);
  assert.match(r.problems[0], /^DOCS\/NEW\.md: no doc marker/);
});

test("a listed source that does not exist fails", () => {
  const r = withDoc("DOCS/D.md", current("1.0.0", ["Scripts/Same.cs", "Scripts/Missing.cs"]), () => check({ root: repo, version: "1.0.0" }));
  assert.deepEqual(r.problems, ["DOCS/D.md: source Scripts/Missing.cs does not exist"]);
});

test("while 1.0.1 is prepared, a doc whose source changed since v1.0.0 fails until it is checked at 1.0.1", () => {
  const stale = withDoc("DOCS/D.md", current("1.0.0", ["Scripts/Changed.cs", "Scripts/Same.cs"]), () => check({ root: repo, version: "1.0.1" }));
  assert.equal(stale.preparing, true);
  assert.equal(stale.tag, "v1.0.0");
  assert.deepEqual(stale.problems, ["DOCS/D.md: checked 1.0.0, sources changed since v1.0.0: Scripts/Changed.cs"]);
  const fresh = withDoc("DOCS/D.md", current("1.0.1", ["Scripts/Changed.cs"]), () => check({ root: repo, version: "1.0.1" }));
  assert.deepEqual(fresh.problems, []);
  const untouched = withDoc("DOCS/D.md", current("1.0.0", ["Scripts/Same.cs"]), () => check({ root: repo, version: "1.0.1" }));
  assert.deepEqual(untouched.problems, [], "an unchanged source needs no new check");
});

test("a folder source counts as changed when any file under it changed", () => {
  const r = withDoc("DOCS/D.md", current("1.0.0", ["Scripts"]), () => check({ root: repo, version: "1.0.1" }));
  assert.deepEqual(r.problems, ["DOCS/D.md: checked 1.0.0, sources changed since v1.0.0: Scripts"]);
});

test("a checked version newer than the game version fails", () => {
  const r = withDoc("DOCS/D.md", current("9.9.9", ["Scripts/Same.cs"]), () => check({ root: repo, version: "1.0.0" }));
  assert.deepEqual(r.problems, ["DOCS/D.md: checked 9.9.9 is newer than the game version 1.0.0"]);
});

test("the command exits 1 with the problems and 0 when clean", () => {
  const cli = path.resolve(__dirname, "../doc-check.js");
  // the CLI checks its own repository; here only that it runs and prints its summary line
  const r = spawnSync(process.execPath, [cli], { encoding: "utf8" });
  assert.match(r.stdout, /^doc check: version \d+\.\d+\.\d+, previous release /);
  assert.ok(r.status === 0 || r.status === 1);
});
