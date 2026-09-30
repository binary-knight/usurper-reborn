"use strict";
const { test, before, after } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const { check } = require("../drift.js");

const cli = path.resolve(__dirname, "../drift.js");
const env = {
  ...process.env,
  GIT_AUTHOR_NAME: "Fixture",
  GIT_AUTHOR_EMAIL: "fixture@example.invalid",
  GIT_COMMITTER_NAME: "Fixture",
  GIT_COMMITTER_EMAIL: "fixture@example.invalid",
  GIT_CONFIG_NOSYSTEM: "1",
  HOME: os.tmpdir(),
};
delete env.GITHUB_ACTIONS;
let repo;

function git(...args) {
  const r = spawnSync("git", ["-c", "commit.gpgsign=false", ...args], {
    cwd: repo,
    env,
    encoding: "utf8",
  });
  assert.equal(r.status, 0, r.stderr);
}
function write(file, text) {
  fs.mkdirSync(path.dirname(path.join(repo, file)), { recursive: true });
  fs.writeFileSync(path.join(repo, file), text);
}
function guide(name, checked, sources) {
  write(
    `DOCS/wiki/world/${name}.md`,
    `---\ntitle: ${name}\npath: /wiki/en/world/${name}/\nchecked: ${checked}\nsources: ${sources.join(", ")}\n---\nText.\n`,
  );
}
const run = (version) =>
  check({
    root: repo,
    version,
    allowlistPath: path.join(repo, "allow.txt"),
  });
const staleNames = (r) => r.stale.map((s) => path.basename(s.guide));

// Fixture history: v1.0.0 tags the first release; a second commit changes
// Changed.cs, deletes Gone.cs and adds New.cs. Other files stay the same.
before(() => {
  repo = fs.mkdtempSync(path.join(os.tmpdir(), "wiki-drift-"));
  git("init", "-q", "-b", "main");
  write("Scripts/Locations/SameLocation.cs", "same\n");
  write("Scripts/Locations/ChangedLocation.cs", "old\n");
  write("Scripts/Locations/GoneLocation.cs", "gone\n");
  write("Scripts/Locations/HiddenLocation.cs", "hidden\n");
  write("Scripts/Locations/InternalLocation.cs", "internal\n");
  git("add", "-A");
  git("commit", "-q", "-m", "release");
  git("tag", "v1.0.0");
  write("Scripts/Locations/ChangedLocation.cs", "new\n");
  fs.rmSync(path.join(repo, "Scripts/Locations/GoneLocation.cs"));
  write("Scripts/Systems/New.cs", "added\n");
  write("allow.txt", "# comment\nInternalLocation.cs  operator menu, not a player place\n");
  guide("same", "1.0.0", ["Scripts/Locations/SameLocation.cs"]);
  guide("changed", "1.0.0", ["Scripts/Locations/ChangedLocation.cs"]);
  guide("gone", "1.0.0", ["Scripts/Locations/GoneLocation.cs"]);
  guide("added", "1.0.0", ["Scripts/Systems/New.cs"]);
  git("add", "-A");
  git("commit", "-q", "-m", "work");
});
after(() => fs.rmSync(repo, { recursive: true, force: true }));

test("drift: a guide whose sources are unchanged since its tag is current", () => {
  const r = run("1.1.0");
  assert.ok(!staleNames(r).includes("same.md"));
});

test("drift: a changed source makes its guide stale", () => {
  const r = run("1.1.0");
  const s = r.stale.find((x) => x.guide === "DOCS/wiki/world/changed.md");
  assert.ok(s, JSON.stringify(r.stale));
  assert.deepEqual(s.changes, [
    { file: "Scripts/Locations/ChangedLocation.cs", change: "changed" },
  ]);
  const a = r.stale.find((x) => x.guide === "DOCS/wiki/world/added.md");
  assert.deepEqual(a.changes, [{ file: "Scripts/Systems/New.cs", change: "added" }]);
});

test("drift: checked equal to the current version is current without a tag", () => {
  guide("rechecked", "1.1.0", ["Scripts/Locations/ChangedLocation.cs"]);
  try {
    const r = run("1.1.0");
    assert.ok(!staleNames(r).includes("rechecked.md"));
    assert.ok(!r.errors.some((e) => e.guide.endsWith("rechecked.md")));
    assert.ok(staleNames(r).includes("changed.md"));
  } finally {
    fs.rmSync(path.join(repo, "DOCS/wiki/world/rechecked.md"));
  }
  const tagged = run("1.0.0");
  assert.deepEqual(staleNames(tagged), ["gone.md"]);
  assert.deepEqual(tagged.errors, []);
});

test("drift: an older checked version without a tag is an error", () => {
  guide("untagged", "0.9.0", ["Scripts/Locations/SameLocation.cs"]);
  try {
    const r = run("1.1.0");
    const e = r.errors.find((x) => x.guide === "DOCS/wiki/world/untagged.md");
    assert.ok(e, JSON.stringify(r.errors));
    assert.match(e.message, /no git tag v0\.9\.0/);
    assert.ok(!staleNames(r).includes("untagged.md"));
  } finally {
    fs.rmSync(path.join(repo, "DOCS/wiki/world/untagged.md"));
  }
});

test("drift: a checked version newer than the game version is an error", () => {
  const r = run("0.5.0");
  assert.equal(r.errors.length, 4);
  assert.match(r.errors[0].message, /newer than the game version 0\.5\.0/);
});

test("drift: a source that no longer exists makes its guide stale", () => {
  for (const version of ["1.0.0", "1.1.0"]) {
    const s = run(version).stale.find(
      (x) => x.guide === "DOCS/wiki/world/gone.md",
    );
    assert.ok(s, version);
    assert.deepEqual(s.changes, [
      { file: "Scripts/Locations/GoneLocation.cs", change: "deleted" },
    ]);
  }
});

test("drift: a location in no guide's sources is reported", () => {
  assert.deepEqual(run("1.1.0").uncovered, ["HiddenLocation.cs"]);
});

test("drift: an allowlisted location is not reported", () => {
  const r = run("1.1.0");
  assert.ok(!r.uncovered.includes("InternalLocation.cs"));
  assert.ok(!r.uncovered.includes("SameLocation.cs"));
});

test("drift: warn mode exits 0 and fail mode exits non-zero with stale guides", () => {
  const args = ["--root", repo, "--version", "1.1.0"];
  const warn = spawnSync(process.execPath, [cli, "--warn", ...args], { env, encoding: "utf8" });
  const fail = spawnSync(process.execPath, [cli, "--fail", ...args], { env, encoding: "utf8" });
  assert.equal(warn.status, 0, warn.stderr);
  assert.equal(fail.status, 1, fail.stderr);
  for (const out of [warn.stdout, fail.stdout]) {
    assert.match(out, /Stale guides: 3/);
    assert.match(out, /git diff v1\.0\.0 HEAD -- Scripts\/Locations\/ChangedLocation\.cs/);
    assert.match(out, /Scripts\/Locations\/HiddenLocation\.cs/);
  }
  const annotated = spawnSync(process.execPath, [cli, "--warn", ...args], {
    env: { ...env, GITHUB_ACTIONS: "true" },
    encoding: "utf8",
  });
  assert.match(annotated.stdout, /^::warning file=DOCS\/wiki\/world\/changed\.md,line=1::/m);
});
