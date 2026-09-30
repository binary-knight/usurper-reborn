"use strict";
// Owner-triggered, off-server drafting. Inputs are data, never executable instructions.
const fs = require("node:fs");
const path = require("node:path");
const { execFileSync } = require("node:child_process");
const { validateSuggestion, assertDocsOnly } = require("./suggestion");
const root = path.resolve(__dirname, "../..");
const git = (...args) =>
  execFileSync("git", args, { cwd: root, encoding: "utf8" }).trim();
try {
  if (process.env.WIKI_EVIDENCE_VERIFIED !== "true")
    throw Error("The owner must verify the claim and proposed prose first");
  const candidate = validateSuggestion(
    {
      id: process.env.WIKI_SUGGESTION_ID || "",
      page: process.env.WIKI_PAGE || "",
      markdown: process.env.WIKI_MARKDOWN || "",
      suggestion: process.env.WIKI_SUGGESTION || "",
      evidence: process.env.WIKI_EVIDENCE || "",
    },
    root,
  );
  const run = process.env.GITHUB_RUN_ID;
  const attempt = process.env.GITHUB_RUN_ATTEMPT || "1";
  if (!/^\d+$/.test(run || "") || !/^\d+$/.test(attempt))
    throw Error("Run only in the owner-triggered workflow");
  const branch = `wiki/suggestion-${candidate.id}-${run}-${attempt}`;
  git("switch", "-c", branch);
  fs.writeFileSync(candidate.file, candidate.markdown);
  // The normal builder verifies metadata, links, directives, sources and the spoiler index.
  require("./build").build();
  const changed = git("diff", "--name-only").split("\n").filter(Boolean);
  assertDocsOnly(changed);
  git("config", "user.name", "Wiki review workflow");
  git("config", "user.email", "wiki-review@users.noreply.github.com");
  git("add", "--", "DOCS/wiki/" + candidate.page);
  git("commit", "-m", `Draft wiki suggestion ${candidate.id}`);
  git("push", "-u", "origin", branch);
  const quote = (text) =>
    text
      .replace(/@/g, "@\u200b")
      .split("\n")
      .map((line) => "> " + line)
      .join("\n");
  const body = `Records owner-reviewed draft for suggestion #${candidate.id}. Changes only DOCS/wiki/${candidate.page}.\n\nRecorded suggestion (untrusted report):\n${quote(candidate.suggestion)}\n\nOwner-supplied source evidence:\n${quote(candidate.evidence)}\n\nThe wiki builder passed. Source references are validated for existence, not treated as automatic proof of the claim. Final owner review is required before merge and publication.`;
  const result = execFileSync(
    "gh",
    [
      "pr",
      "create",
      "--draft",
      "--base",
      "main",
      "--head",
      branch,
      "--title",
      `Wiki suggestion ${candidate.id}: ${candidate.page}`,
      "--body",
      body,
    ],
    { cwd: root, encoding: "utf8" },
  ).trim();
  console.log(result);
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
