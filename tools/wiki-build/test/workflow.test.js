"use strict";
const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const YAML = require("yaml");
const root = path.resolve(__dirname, "../../..");
const load = (name) =>
  YAML.parse(
    fs.readFileSync(path.join(root, ".github/workflows", name), "utf8"),
  );
test("wiki runs on PRs and releases and its checked artifact gates deployment", () => {
  const pipeline = load("ci-cd.yml");
  assert.ok(pipeline.on.pull_request);
  assert.ok(pipeline.on.release);
  assert.ok(pipeline.jobs.wiki);
  assert.ok(pipeline.jobs["deploy-server"].needs.includes("wiki"));
  assert.ok(pipeline.jobs.wiki.steps.some((s) => s.with?.name === "wiki-site"));
  assert.ok(
    pipeline.jobs["deploy-server"].steps.some(
      (s) => s.with?.name === "wiki-site" && s.with.path === "web/wiki/",
    ),
  );
  const deploy = pipeline.jobs["deploy-server"].steps.find(
    (s) => s.name === "Deploy to production",
  ).run;
  assert.ok(deploy.includes("test ! -L /opt/usurper/web/wiki"));
  assert.ok(deploy.includes("sudo rm -rf -- /opt/usurper/web/wiki"));
});
test("suggestion workflow is manual, owner-verified, off-server and draft-only", () => {
  const workflow = load("wiki-suggestion.yml");
  assert.deepEqual(Object.keys(workflow.on), ["workflow_dispatch"]);
  assert.equal(workflow.jobs.draft.environment, "wiki-review");
  assert.match(workflow.jobs.draft.if, /evidence_verified/);
  assert.equal(workflow.jobs.draft.steps[0].with.ref, "main");
  assert.ok(
    workflow.jobs.draft.steps.some(
      (s) => s.run === "node tools/wiki-build/prepare-suggestion.js",
    ),
  );
  const guard = load("wiki-suggestion-guard.yml");
  assert.deepEqual(
    [...guard.on.pull_request_target.types].sort(),
    ["opened", "ready_for_review", "reopened", "synchronize"],
  );
  assert.deepEqual(Object.keys(guard.on), ["pull_request_target"]);
  assert.deepEqual(guard.permissions, {
    contents: "read",
    "pull-requests": "read",
  });
  assert.equal(guard.permissions.contents, "read");
  assert.ok(!guard.jobs["docs-only"].steps.some((s) => s.uses));
});
test("wiki job runs the drift check with full history, failing only for releases", () => {
  const pipeline = load("ci-cd.yml");
  const wiki = pipeline.jobs.wiki;
  const checkout = wiki.steps.find((s) => s.uses === "actions/checkout@v4");
  assert.equal(checkout.with?.["fetch-depth"], 0);
  const drift = wiki.steps.find((s) => s.name === "Wiki drift check");
  assert.ok(drift, "drift step missing");
  assert.equal(drift.run, 'node tools/wiki-build/drift.js --mode "$DRIFT_MODE"');
  const mode = drift.env.DRIFT_MODE;
  assert.match(mode, /github\.event_name == 'release'/);
  assert.match(mode, /workflow_dispatch/);
  assert.match(mode, /startsWith\(github\.head_ref, 'release-'\)/);
  assert.match(mode, /&& 'fail' \|\| 'warn'/);
  assert.ok(pipeline.jobs["deploy-server"].needs.includes("wiki"));
});
