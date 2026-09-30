"use strict";
// Wiki drift check: flags guides whose source files changed since the release
// named in their `checked:` field, and game locations no guide covers.
const fs = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const { readPages } = require("./build.js");

const buildRoot = path.resolve(__dirname, "../..");

function gameVersion(root) {
  const file = path.join(root, "Scripts/Core/GameConfig.cs");
  const m = fs
    .readFileSync(file, "utf8")
    .match(/public const string Version = "([^"]+)";/);
  if (!m) throw Error(`No Version constant in ${file}`);
  return m[1];
}

function compareVersions(a, b) {
  const pa = String(a).split(".").map(Number);
  const pb = String(b).split(".").map(Number);
  if ([...pa, ...pb].some((n) => !Number.isInteger(n)))
    throw Error(`Not a version: ${a} or ${b}`);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const d = (pa[i] || 0) - (pb[i] || 0);
    if (d) return Math.sign(d);
  }
  return 0;
}

function git(root, args) {
  return spawnSync("git", args, { cwd: root, encoding: "utf8" });
}

function tagExists(root, tag) {
  return git(root, ["rev-parse", "-q", "--verify", `refs/tags/${tag}`])
    .status === 0;
}

// Returns "changed", "added" (absent at the tag) or null (unchanged).
function sourceChange(root, tag, file) {
  if (git(root, ["cat-file", "-e", `${tag}:${file}`]).status !== 0)
    return "added";
  const r = git(root, ["diff", "--quiet", tag, "HEAD", "--", file]);
  if (r.status === 0) return null;
  if (r.status === 1) return "changed";
  throw Error(`git diff failed for ${file} at ${tag}: ${r.stderr.trim()}`);
}

function readAllowlist(file) {
  if (!fs.existsSync(file)) return new Map();
  const entries = new Map();
  fs.readFileSync(file, "utf8")
    .split("\n")
    .forEach((raw, i) => {
      const line = raw.trim();
      if (!line || line.startsWith("#")) return;
      const m = line.match(/^(\S+\.cs)\s+(\S.*)$/);
      if (!m)
        throw Error(
          `${path.basename(file)}:${i + 1}: expected "<File>.cs <reason>"`,
        );
      entries.set(m[1], m[2]);
    });
  return entries;
}

function check(options = {}) {
  const root = options.root || buildRoot;
  const contentDir = options.contentDir || path.join(root, "DOCS/wiki");
  const locationsDir =
    options.locationsDir || path.join(root, "Scripts/Locations");
  const allowlistPath =
    options.allowlistPath ||
    path.join(root, "tools/wiki-build/coverage-allowlist.txt");
  const version = options.version || gameVersion(root);
  const stale = [];
  const errors = [];
  const guides = readPages(contentDir).map((p) => ({
    ...p,
    file: path
      .relative(root, path.resolve(buildRoot, p.sourceFile))
      .split(path.sep)
      .join("/"),
    sourceList: p.sources.split(/,\s*/),
  }));
  for (const g of guides) {
    const order = compareVersions(g.checked, version);
    const changes = [];
    for (const s of g.sourceList)
      if (!fs.existsSync(path.join(root, s)))
        changes.push({ file: s, change: "deleted" });
    if (order > 0) {
      errors.push({
        guide: g.file,
        message: `checked: ${g.checked} is newer than the game version ${version}. Set checked to a released or the current version, or bump GameConfig.Version (or pass --version) when preparing the release.`,
      });
    } else if (order < 0) {
      const tag = `v${g.checked}`;
      if (!tagExists(root, tag)) {
        errors.push({
          guide: g.file,
          message: `checked: ${g.checked} has no git tag ${tag}, so its sources cannot be compared. Fetch tags (git fetch --tags) or, if ${g.checked} was never released, re-check the guide and set checked to ${version}.`,
        });
      } else {
        for (const s of g.sourceList) {
          if (!fs.existsSync(path.join(root, s))) continue;
          const change = sourceChange(root, tag, s);
          if (change) changes.push({ file: s, change });
        }
      }
    }
    if (changes.length) stale.push({ guide: g.file, checked: g.checked, changes });
  }
  const allow = readAllowlist(allowlistPath);
  const covered = new Set(
    guides.flatMap((g) => g.sourceList.map((s) => path.posix.basename(s))),
  );
  const uncovered = fs
    .readdirSync(locationsDir)
    .filter((f) => f.endsWith("Location.cs"))
    .sort()
    .filter((f) => !covered.has(f) && !allow.has(f));
  const staleAllow = [...allow.keys()].filter(
    (f) => !fs.existsSync(path.join(locationsDir, f)),
  );
  for (const f of staleAllow)
    errors.push({
      guide: path.relative(root, allowlistPath).split(path.sep).join("/"),
      message: `allowlist entry ${f} names a file that does not exist in ${path.relative(root, locationsDir)}`,
    });
  return { version, stale, errors, uncovered };
}

function report(result, mode, locationsRel = "Scripts/Locations") {
  const level = mode === "fail" ? "error" : "warning";
  const lines = [
    `Wiki drift check for game version ${result.version} (${mode} mode)`,
  ];
  const annotations = [];
  if (result.stale.length) {
    lines.push("", `Stale guides: ${result.stale.length}`);
    for (const s of result.stale) {
      lines.push(`  ${s.guide} (checked ${s.checked})`);
      for (const c of s.changes) {
        const how =
          c.change === "deleted"
            ? "no longer exists; update sources:"
            : c.change === "added"
              ? `did not exist at v${s.checked}; see: git log v${s.checked}..HEAD -- ${c.file}`
              : `see: git diff v${s.checked} HEAD -- ${c.file}`;
        lines.push(`    ${c.change} ${c.file}  ${how}`);
      }
      annotations.push(
        `::${level} file=${s.guide},line=1::Wiki guide is stale: ${s.changes.map((c) => `${c.file} ${c.change}`).join("; ")} since v${s.checked}. Re-check the prose and set checked to ${result.version}.`,
      );
    }
  }
  if (result.uncovered.length) {
    lines.push("", `Locations not covered by any guide: ${result.uncovered.length}`);
    for (const u of result.uncovered) {
      lines.push(
        `  ${locationsRel}/${u}  add it to a guide's sources or to tools/wiki-build/coverage-allowlist.txt with a reason`,
      );
      annotations.push(
        `::${level} file=${locationsRel}/${u},line=1::No wiki guide lists ${u} in sources, and it is not in the coverage allowlist.`,
      );
    }
  }
  if (result.errors.length) {
    lines.push("", `Errors: ${result.errors.length}`);
    for (const e of result.errors) {
      lines.push(`  ${e.guide}: ${e.message}`);
      annotations.push(`::${level} file=${e.guide},line=1::${e.message}`);
    }
  }
  const clean =
    !result.stale.length && !result.uncovered.length && !result.errors.length;
  if (clean) lines.push("", "All guides are current and every location is covered.");
  return { text: lines.join("\n"), annotations, clean };
}

function main(argv) {
  let mode = "warn";
  let version;
  let root;
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--fail") mode = "fail";
    else if (a === "--warn") mode = "warn";
    else if (a === "--mode") mode = argv[++i];
    else if (a === "--version") version = argv[++i];
    else if (a === "--root") root = path.resolve(argv[++i]);
    else throw Error(`Unknown argument: ${a}`);
  }
  if (!["warn", "fail"].includes(mode)) throw Error(`Unknown mode: ${mode}`);
  const result = check({ root, version });
  const out = report(result, mode);
  console.log(out.text);
  if (process.env.GITHUB_ACTIONS === "true")
    for (const a of out.annotations) console.log(a);
  return mode === "fail" && !out.clean ? 1 : 0;
}

if (require.main === module) {
  try {
    process.exitCode = main(process.argv.slice(2));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 2;
  }
}
module.exports = { check, report, main, compareVersions, gameVersion };
