'use strict';
// 1.3.11: the web unit reads /etc/usurper/web.env (optional, dash kept), and the service logs one startup line that
// names six variables as set or unset, never a value. Reads files and runs the cut helper only.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { slice, proxySource } = require('./balance-api-harness');

const ROOT = path.join(__dirname, '..', '..');
const read = (...p) => fs.readFileSync(path.join(ROOT, ...p), 'utf8');
const src = proxySource();
const ctx = {};
vm.createContext(ctx);
const [startupEnvLine, names] = vm.runInContext(
  slice(src, '// --- Startup env line (1.3.11) ---', '// --- End startup env line ---') +
    '\n[startupEnvLine, STARTUP_ENV_NAMES.slice()]', ctx);

test('startup env line lists the six names, set or unset', () => {
  assert.deepEqual([...names], ['BALANCE_PASS', 'BALANCE_SECRET', 'RESEND_API_KEY', 'MAIL_FROM', 'DISCORD_BOT_TOKEN', 'BUG_REPORT_WEBHOOK_URL']);
  assert.equal(startupEnvLine({}), '[usurper-web] Environment: BALANCE_PASS=unset BALANCE_SECRET=unset RESEND_API_KEY=unset MAIL_FROM=unset DISCORD_BOT_TOKEN=unset BUG_REPORT_WEBHOOK_URL=unset');
  const line = startupEnvLine({ BALANCE_PASS: 'x', MAIL_FROM: 'y', BALANCE_SECRET: '' });
  assert.match(line, /BALANCE_PASS=set /);
  assert.match(line, /BALANCE_SECRET=unset /);
  assert.match(line, /MAIL_FROM=set /);
  assert.match(line, /BUG_REPORT_WEBHOOK_URL=unset$/);
});

test('the line never carries a value', () => {
  const secret = 'zz-value-91';
  const env = {};
  for (const n of names) env[n] = secret + n;
  env.SITE_URL = secret;
  const line = startupEnvLine(env);
  assert.equal(line.includes('zz-value'), false);
  assert.equal(line.includes('SITE_URL'), false);
  assert.equal((line.match(/=set/g) || []).length, 6);
});

test('the service logs the line once at startup', () => {
  assert.match(src, /console\.log\(startupEnvLine\(process\.env\)\);/);
  assert.equal((src.match(/startupEnvLine\(process\.env\)/g) || []).length, 1);
});

test('the unit reads /etc/usurper/web.env and keeps the optional dash', () => {
  const unit = read('scripts-server', 'usurper-web.service');
  assert.match(unit, /^EnvironmentFile=-\/etc\/usurper\/web\.env$/m);
  assert.doesNotMatch(unit, /^EnvironmentFile=.*\/opt\/usurper\/web\/\.env/m);
});

test('every 404 guard stays: nginx rule, PRIVATE_FILES, tar exclude', () => {
  assert.match(read('scripts-server', 'nginx-private.conf'), /^location ~ \/\\\.\(\?!well-known\/\) \{ return 404; \}$/m);
  assert.match(src, /const PRIVATE_FILES = /);
  assert.match(read('.github', 'workflows', 'ci-cd.yml'), /--exclude=\.env /);
});

test('comments and the guide name the new path and the move steps', () => {
  assert.doesNotMatch(src, /loaded from \/opt\/usurper\/web\/\.env/);
  assert.doesNotMatch(src, /lives only in \/opt\/usurper\/web\/\.env/);
  const guide = read('DOCS', 'SERVER_DEPLOYMENT.md');
  assert.match(guide, /usurper-web\.service` reads `\/etc\/usurper\/web\.env`/);
  assert.match(guide, /#### Moving the environment file/);
  assert.match(guide, /cmp \/opt\/usurper\/web\/\.env \/etc\/usurper\/web\.env/);
});
