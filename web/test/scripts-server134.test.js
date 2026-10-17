'use strict';
// 1.3.4: the self-host server scripts: update-server.sh deploys Localization/ with the binary, and the MUD service
// file ships a placeholder admin name for the operator to replace, not a real account name.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.join(__dirname, '..', '..');
const read = (f) => fs.readFileSync(path.join(ROOT, 'scripts-server', f), 'utf8');

test('update-server.sh copies the Localization folder when the new build has one', () => {
  assert.match(read('update-server.sh'), /\nif \[ -d "\$NEW_FILES\/Localization" \]; then cp -r "\$NEW_FILES\/Localization" \/opt\/usurper\/; fi\n/);
});

test('usurper-mud.service names only the placeholder admin, with the line telling the operator to replace it', () => {
  const unit = read('usurper-mud.service');
  const exec = unit.split('\n').filter((l) => l.startsWith('ExecStart='));
  assert.equal(exec.length, 1);
  assert.deepEqual([...exec[0].matchAll(/--admin (\S+)/g)].map((m) => m[1]), ['YourName']);
  assert.match(unit, /\n# Put your own account name after --admin /);
  assert.match(unit, /--mud-port 4001 /);
  assert.match(unit, /^# Single-process TCP game server on port 4001\./m);
});
