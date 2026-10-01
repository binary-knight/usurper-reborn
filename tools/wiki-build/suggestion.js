"use strict";
const fs = require("node:fs");
const path = require("node:path");
function validateSuggestion(input, root) {
  if (!/^\d+$/.test(input.id)) throw Error("Suggestion ID must be numeric");
  if (!/^(?:[a-z0-9-]+\/)*[a-z0-9-]+\.md$/.test(input.page))
    throw Error("Select an existing DOCS/wiki Markdown page");
  const file = path.join(root, "DOCS/wiki", input.page);
  if (
    !fs.existsSync(file) ||
    !fs
      .realpathSync(file)
      .startsWith(fs.realpathSync(path.join(root, "DOCS/wiki")) + path.sep)
  )
    throw Error("Page must exist inside DOCS/wiki");
  if (
    !input.markdown ||
    input.markdown.length > 100000 ||
    !input.markdown.startsWith("---\n")
  )
    throw Error(
      "Provide a complete Markdown page with frontmatter, up to 100000 characters",
    );
  // A reviewed draft keeps every history line the page already has.
  const historyLines = (markdown) => {
    const m = markdown.match(/^---\n([\s\S]*?)\n---\n/);
    return m ? m[1].split("\n").filter((l) => l.startsWith("history: ")) : [];
  };
  const kept = new Set(historyLines(input.markdown));
  for (const line of historyLines(fs.readFileSync(file, "utf8")))
    if (!kept.has(line))
      throw Error(`The draft must keep the page's history line: ${line}`);
  if (!input.suggestion || input.suggestion.length > 800)
    throw Error("Provide the recorded suggestion, up to 800 characters");
  if (!input.evidence || input.evidence.length > 4000)
    throw Error("Provide source evidence");
  const references =
    input.evidence.match(/(?:Scripts|DOCS)\/[\w/-]+\.(?:cs|md):\d+/g) || [];
  if (!references.length)
    throw Error("Evidence requires source file:line references");
  for (const ref of references) {
    const [source, line] = ref.split(":");
    const sourceFile = path.join(root, source);
    if (
      !fs.existsSync(sourceFile) ||
      +line < 1 ||
      +line > fs.readFileSync(sourceFile, "utf8").split("\n").length
    )
      throw Error(`Invalid evidence: ${ref}`);
  }
  return { ...input, file };
}
function assertDocsOnly(files) {
  if (
    !files.length ||
    files.some((f) => !/^DOCS\/wiki\/(?:[a-z0-9-]+\/)*[a-z0-9-]+\.md$/.test(f))
  )
    throw Error("Suggestion PRs may change only DOCS/wiki Markdown");
}
module.exports = { validateSuggestion, assertDocsOnly };
