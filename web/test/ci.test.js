'use strict';
// The CI workflow and the deploy tarball, checked from the workflow file itself:
//  1. a web-test job runs npm ci and npm test in web/ on node 22, and nothing lets it
//     fail quietly (no continue-on-error, no || true);
//  2. the build and the server deploy both wait for that job;
//  3. the web tarball command, run for real on a scratch tree, leaves web/test out and
//     keeps everything that is served (including a nested folder that is also named test).
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

const WORKFLOW = path.join(__dirname, '..', '..', '.github', 'workflows', 'ci-cd.yml');
const text = fs.readFileSync(WORKFLOW, 'utf8');

// The text of one top-level job: from "  <name>:" to the next two-space-indented key.
function jobBlock(name) {
  const lines = text.split('\n');
  const start = lines.findIndex((l) => l === `  ${name}:`);
  assert.ok(start >= 0, `job ${name} is missing from ci-cd.yml`);
  let end = lines.length;
  for (let i = start + 1; i < lines.length; i++) {
    if (/^  [A-Za-z0-9_-]+:\s*$/.test(lines[i])) { end = i; break; }
  }
  return lines.slice(start, end).join('\n');
}

function needsOf(block) {
  const m = block.match(/^    needs:\s*\[([^\]]*)\]/m);
  assert.ok(m, 'job has no needs list');
  return m[1].split(',').map((s) => s.trim());
}

// The run: command of the step with the given name, in the given job text.
function stepRun(block, stepName) {
  const lines = block.split('\n');
  const i = lines.findIndex((l) => l.trim() === `- name: ${stepName}`);
  assert.ok(i >= 0, `step ${stepName} is missing`);
  for (let j = i + 1; j < lines.length; j++) {
    if (/^\s+- /.test(lines[j])) break;
    const m = lines[j].match(/^\s+run:\s*(.*)$/);
    if (m) return m[1];
  }
  assert.fail(`step ${stepName} has no run command`);
}

test('the web-test job runs npm ci and npm test in web/ on node 22', () => {
  const job = jobBlock('web-test');
  assert.match(job, /node-version: '22'/);
  assert.strictEqual(stepRun(job, 'Install web packages'), 'npm ci');
  assert.strictEqual(stepRun(job, 'Run web tests'), 'npm test');
  const wd = job.match(/working-directory: web\b/g) || [];
  assert.strictEqual(wd.length, 2, 'both the install and the test step run in web/');
});

test('the web-test job cannot fail quietly', () => {
  const job = jobBlock('web-test');
  assert.doesNotMatch(job, /continue-on-error/);
  assert.doesNotMatch(job, /\|\|\s*(true|:|exit 0)/);
  assert.doesNotMatch(job, /^\s+if:/m, 'the job runs on every trigger');
});

test('npm test in web/ is the node test runner over web/test', () => {
  const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'package.json'), 'utf8'));
  assert.match(pkg.scripts.test, /^node --test /);
});

test('the build and the server deploy wait for the web-test job', () => {
  assert.ok(needsOf(jobBlock('build')).includes('web-test'), 'build does not need web-test');
  assert.ok(needsOf(jobBlock('deploy-server')).includes('web-test'), 'deploy-server does not need web-test');
});

test('the web tarball leaves out web/test and keeps what is served', () => {
  const cmd = stepRun(jobBlock('deploy-server'), 'Create web assets tarball');
  assert.match(cmd, /^tar -czf web\.tar\.gz -C web /);

  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'webtar-'));
  try {
    const web = path.join(root, 'web');
    const files = [
      'index.html', 'admin.html', 'escape.js', 'ssh-proxy.js', 'lang/en.json',
      // a served folder that happens to be called test must survive
      'wiki/test/keep.html',
      // not shipped
      'test/ci.test.js', 'test/harness.js', 'node_modules/x/index.js', '.env', 'old.bak',
    ];
    for (const f of files) {
      fs.mkdirSync(path.dirname(path.join(web, f)), { recursive: true });
      fs.writeFileSync(path.join(web, f), 'x');
    }
    execFileSync('bash', ['-c', cmd], { cwd: root });
    const listed = execFileSync('tar', ['-tzf', path.join(root, 'web.tar.gz')], { encoding: 'utf8' })
      .split('\n').map((l) => l.replace(/^\.\//, '')).filter(Boolean);
    const inTar = (f) => listed.includes(f);

    for (const f of ['index.html', 'admin.html', 'escape.js', 'ssh-proxy.js', 'lang/en.json', 'wiki/test/keep.html']) {
      assert.ok(inTar(f), `${f} must be in the tarball`);
    }
    for (const f of ['test/ci.test.js', 'test/harness.js', '.env', 'old.bak', 'node_modules/x/index.js']) {
      assert.ok(!inTar(f), `${f} must not be in the tarball`);
    }
    assert.ok(!listed.some((l) => l === 'test' || l === 'test/' || l.startsWith('test/')), 'no test/ entry at all');
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});
