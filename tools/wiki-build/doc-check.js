"use strict";
// Documentation gate (1.3.4): every Markdown file at the repository root and in DOCS/ opens with one marker comment, either
//   <!-- doc: checked 1.3.4; sources: Scripts/Server/MudServer.cs, web/ssh-proxy.js -->   (a current doc)
//   <!-- doc: historical -->                                                              (a plan or record kept as it was)
// It fails on a file with no marker or a bad one, a listed source that does not exist, and, while a release
// is being prepared (no tag yet for the game version), a current doc with a source changed since the
// previous release and a checked version older than the game version. DOCS/wiki and DOCS/release-notes are
// outside it (the wiki has its own drift check).
const fs = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const { compareVersions } = require("./build.js");

const buildRoot = path.resolve(__dirname, "../..");
const MARKER = /^<!-- doc: (?:historical|checked (\d+\.\d+\.\d+); sources: ([^<>]+?)) -->$/;

function gameVersion(root) {
  const file = path.join(root, "Scripts/Core/GameConfig.cs");
  const m = fs.readFileSync(file, "utf8").match(/public const string Version = "([^"]+)";/);
  if (!m) throw Error(`No Version constant in ${file}`);
  return m[1];
}

function git(root, args) {
  return spawnSync("git", args, { cwd: root, encoding: "utf8" });
}

function tagExists(root, tag) {
  return git(root, ["rev-parse", "-q", "--verify", `refs/tags/${tag}`]).status === 0;
}

// The previous release: the highest v<x.y.z> tag below the version, by number.
function previousTag(root, version) {
  const r = git(root, ["tag", "--list", "v*"]);
  if (r.status !== 0) throw Error(`git tag failed: ${r.stderr.trim()}`);
  const lower = r.stdout
    .split("\n")
    .map((t) => t.trim())
    .filter((t) => /^v\d+\.\d+\.\d+$/.test(t))
    .filter((t) => compareVersions(t.slice(1), version) < 0)
    .sort((a, b) => compareVersions(a.slice(1), b.slice(1)));
  return lower.length ? lower[lower.length - 1] : null;
}

// The files the gate covers: the Markdown files at the repository root (README, PRIVACY, CONTRIBUTING) and the top
// level of DOCS.
function docFiles(root) {
  const out = [];
  for (const name of fs.readdirSync(root).sort())
    if (name.endsWith(".md") && fs.statSync(path.join(root, name)).isFile()) out.push(name);
  const docs = path.join(root, "DOCS");
  if (fs.existsSync(docs))
    for (const name of fs.readdirSync(docs).sort())
      if (name.endsWith(".md") && fs.statSync(path.join(docs, name)).isFile()) out.push(`DOCS/${name}`);
  return out;
}

function readMarker(root, file) {
  const first = fs.readFileSync(path.join(root, file), "utf8").split("\n", 1)[0].replace(/\r$/, "");
  const m = first.match(MARKER);
  if (!m) return { bad: first };
  if (m[1] === undefined) return { historical: true };
  const sources = m[2].split(",").map((s) => s.trim()).filter((s) => s.length > 0);
  return { checked: m[1], sources };
}

// True when the source (a file or a folder) differs between the tag and HEAD, or did not exist at the tag.
function changedSince(root, tag, source) {
  const r = git(root, ["diff", "--quiet", tag, "HEAD", "--", source]);
  if (r.status === 0) return false;
  if (r.status === 1) return true;
  throw Error(`git diff failed for ${source} at ${tag}: ${r.stderr.trim()}`);
}

function check({ root = buildRoot, version = gameVersion(root) } = {}) {
  const problems = [];
  const tag = previousTag(root, version);
  const preparing = !tagExists(root, `v${version}`) && tag !== null;
  let current = 0;
  let historical = 0;
  for (const file of docFiles(root)) {
    const marker = readMarker(root, file);
    if (marker.bad !== undefined) {
      problems.push(`${file}: no doc marker on the first line (found: ${marker.bad.slice(0, 80)})`);
      continue;
    }
    if (marker.historical) {
      historical++;
      continue;
    }
    current++;
    if (marker.sources.length === 0) problems.push(`${file}: a current doc names no sources`);
    if (compareVersions(marker.checked, version) > 0)
      problems.push(`${file}: checked ${marker.checked} is newer than the game version ${version}`);
    const missing = marker.sources.filter((s) => !fs.existsSync(path.join(root, s)));
    for (const s of missing) problems.push(`${file}: source ${s} does not exist`);
    if (preparing && marker.checked !== version) {
      const changed = marker.sources.filter((s) => !missing.includes(s) && changedSince(root, tag, s));
      if (changed.length > 0)
        problems.push(`${file}: checked ${marker.checked}, sources changed since ${tag}: ${changed.join(", ")}`);
    }
  }
  return { version, tag, preparing, current, historical, problems };
}

if (require.main === module) {
  const r = check();
  console.log(`doc check: version ${r.version}, previous release ${r.tag || "none"}, ${r.preparing ? "release being prepared" : "no release being prepared"}; ${r.current} current, ${r.historical} historical`);
  for (const p of r.problems) console.log(`  ${p}`);
  process.exit(r.problems.length ? 1 : 0);
}

module.exports = { check, readMarker, docFiles, MARKER };
