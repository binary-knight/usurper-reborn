'use strict';
// admin.html: player names and other stored text in tables, panels and buttons.
// Buttons carry the username in data-user and run through one delegated listener.
const test = require('node:test');
const assert = require('node:assert');
const { HOSTILE, loadPage, assertSafe, elements } = require('./harness');

const STUBS = ['openPlayerPanel', 'quickUnban', 'showBanModal', 'showKickModal', 'showSlayModal', 'startSnoop',
  'showMessageModal', 'queueCommand', 'showResetPwModal', 'showDeleteModal', 'liftIpBan', 'saveServerSetting',
  'makeEditable', 'resetDailies', 'saveRawJson', 'loadPlayers'];

async function page(fetchMap) {
  const p = await loadPage('admin.html', { fetch: (url) => (fetchMap && fetchMap(url)) || {} });
  const calls = [];
  p.real = {};
  for (const name of STUBS) {
    p.real[name] = p.ctx[name];
    p.ctx[name] = (...args) => { calls.push([name, ...args.map((a) => (a && a.tagName ? a.tagName : a))]); };
  }
  return { p, calls };
}

async function both(fn, fetchFor) {
  const h = await page(fetchFor && fetchFor(HOSTILE));
  const b = await page(fetchFor && fetchFor('Alice'));
  await fn(h.p, HOSTILE);
  await fn(b.p, 'Alice');
  await h.p.settle();
  await b.p.settle();
  return { h, b };
}

function check(h, b, id, label) {
  const html = h.p.el(id).innerHTML;
  assert.ok(html.length > 0, label + ' rendered');
  assertSafe(assert, html, b.p.el(id).innerHTML, label);
  return html;
}

function panelData(v) {
  return {
    username: v, displayName: v, isOnline: true, isBanned: false, banReason: v,
    settings: { isFrozen: false, isMuted: true, autoHeal: true, compactMode: false },
    stats: { level: v, experience: v, className: v, raceName: v, hp: v, maxHP: v },
    resources: { gold: v, bankGold: v, potions: v, lockpicks: v, herbs: { healingHerb: v } },
    social: { chivalry: v, darkness: v, alignment: v, faction: v, isKing: false, teamName: v, isMarried: true, spouseName: v },
    story: { awakeningLevel: v, isImmortal: true, divineName: v, godLevel: v, completedEndings: [v], collectedSeals: [] },
    statistics: {},
    daily: { fightCount: v },
    equipment: { dynamicEquipment: [{ equippedSlot: 1, name: v, weaponPower: v }] },
    inventory: [{ name: v, type: v }],
    playtimeMinutes: 61, createdAt: null, lastLogin: null,
  };
}

test('admin page loads escape.js, has no private escapeHtml or esc, runs without errors', async () => {
  const { p } = await page();
  assert.deepStrictEqual(p.loaded, ['escape.js']);
  assert.deepStrictEqual(p.errors, []);
  assert.strictEqual(p.run('escapeHtml'), p.run('UsurperEscape.escapeHtml'));
  assert.strictEqual(p.run('typeof esc'), 'undefined');
});

test('sink admin online players (959): name, level, class, location, ip, connection type escaped', async () => {
  const { h, b } = await both((p, v) => p.ctx.renderOnlinePlayers({ onlinePlayers: [{ name: v, level: v, className: v, location: v, connectionType: v, ip: v, connectedAt: null }] }));
  check(h, b, 'online-body', 'online players');
  assert.ok(h.p.el('online-body').textContent.includes(HOSTILE));
});

test('sink admin recent logins (1316): name, level, class escaped', async () => {
  const { h, b } = await both((p, v) => p.ctx.renderLogins({ recentLogins: [{ name: v, level: v, className: v, time: null }] }));
  check(h, b, 'logins-body', 'recent logins');
});

test('sink admin read-only fields (1589 via 1720, 1722, 1736): team, spouse, divine name escaped', async () => {
  const { h, b } = await both((p, v) => { p.ctx.renderSocialTab(panelData(v)); p.ctx.renderStoryTab(panelData(v)); });
  check(h, b, 'panel-social', 'social tab');
  check(h, b, 'panel-story', 'story tab');
  const text = h.p.el('panel-social').textContent + h.p.el('panel-story').textContent;
  assert.ok(text.split(HOSTILE).length >= 4, 'team, spouse, divine name shown as text');
});

test('sink admin editable fields (1582): value escaped, click opens the editor with path and type', async () => {
  const { h, b } = await both((p, v) => { p.ctx.renderStatsTab(panelData(v)); p.ctx.renderSettingsTab(panelData(v)); });
  check(h, b, 'panel-stats', 'stats tab');
  const field = h.p.el('panel-stats').querySelector('[data-action="edit-field"]');
  h.p.click(field);
  assert.deepStrictEqual(h.calls, [['makeEditable', 'SPAN', 'player.level', 'number']]);
  h.calls.length = 0;
  h.p.click(h.p.el('panel-settings').querySelector('[data-action="edit-field"]'));
  assert.deepStrictEqual(h.calls, [['makeEditable', 'SPAN', 'player.autoHeal', 'boolean']]);
});

test('sink admin player table (1464-1477): row and buttons pass the raw username, buttons do not open the row', async () => {
  const data = (v) => ({ page: 1, totalPages: 1, total: 1, players: [
    { username: v, displayName: v, level: v, className: v, raceName: v, gold: 5, isOnline: true, isBanned: false, lastLogin: null },
    { username: v + 'b', displayName: v, level: v, className: v, raceName: v, gold: 5, isOnline: false, isBanned: true, lastLogin: null },
  ] });
  const { h, b } = await both((p, v) => p.ctx.renderPlayerTable(data(v)));
  check(h, b, 'player-table', 'player table');
  const t = h.p.el('player-table');
  const rows = t.querySelectorAll('tr.player-row');
  h.p.click(rows[0].querySelector('td'));
  const buttons = rows[0].querySelectorAll('button');
  for (const btn of buttons) h.p.click(btn);
  h.p.click(rows[1].querySelectorAll('button')[1]);
  assert.deepStrictEqual(h.calls, [
    ['openPlayerPanel', HOSTILE],
    ['openPlayerPanel', HOSTILE],
    ['showBanModal', HOSTILE],
    ['showKickModal', HOSTILE],
    ['quickUnban', HOSTILE + 'b'],
  ]);
});

test('sink admin player panel actions (1553-1571): every button passes the raw username', async () => {
  const { h, b } = await both((p, v) => p.ctx.renderPanelActions(panelData(v)));
  check(h, b, 'panel-actions', 'panel actions');
  for (const btn of h.p.el('panel-actions').querySelectorAll('button')) h.p.click(btn);
  assert.deepStrictEqual(h.calls, [
    ['showKickModal', HOSTILE], ['showSlayModal', HOSTILE], ['startSnoop', HOSTILE], ['showMessageModal', HOSTILE],
    ['queueCommand', 'freeze', HOSTILE], ['queueCommand', 'unmute', HOSTILE], ['showBanModal', HOSTILE],
    ['showResetPwModal', HOSTILE], ['showDeleteModal', HOSTILE],
  ]);
});

test('sink admin banned IP lift button (1054): passes the raw address', async () => {
  const fetchFor = (v) => (url) => (url.endsWith('/banned-ips') ? { entries: [{ ip_address: v, reason: v, associated_username: v, banned_at: v, banned_by: v }] } : null);
  const { h, b } = await both((p) => p.run('refreshBannedIps()'), fetchFor);
  check(h, b, 'banned-ips-body', 'banned ips');
  h.p.click(h.p.el('banned-ips-body').querySelector('[data-action="lift-ip"]'));
  assert.deepStrictEqual(h.calls, [['liftIpBan', HOSTILE]]);
});

test('sink admin server setting save (1185): key, type, min, max escaped; save passes them through', async () => {
  const row = (v) => ({ key: v, type: v, label: v, description: v, changeImpact: v, currentValue: v, minValue: v, maxValue: v, maxLength: v, isDefault: false, updatedBy: v, updatedAt: v });
  const { h, b } = await both((p, v) => { p.el('server-settings-body').innerHTML = '<table>' + p.ctx.renderSettingRow(row(v)) + '</table>'; });
  check(h, b, 'server-settings-body', 'server settings');
  h.p.click(h.p.el('server-settings-body').querySelector('[data-action="save-setting"]'));
  const inputId = 'setting-' + HOSTILE.replace(/[^a-z0-9_]/gi, '_');
  assert.deepStrictEqual(h.calls, [['saveServerSetting', HOSTILE, HOSTILE, inputId]]);
});

test('sink admin pagination (1486-1488): page numbers in data-page, counts escaped', async () => {
  const { h, b } = await both((p, v) => p.ctx.renderPlayerTable({ page: 2, totalPages: 3, total: v, players: [{ username: 'x', level: 1 }] }));
  check(h, b, 'player-pagination', 'pagination');
  const [prev, next] = h.p.el('player-pagination').querySelectorAll('button');
  h.p.click(next);
  assert.strictEqual(h.p.run('playerPage'), 3);
  h.p.click(prev);
  assert.strictEqual(h.p.run('playerPage'), 1);
  assert.deepStrictEqual(h.calls.map((c) => c[0]), ['loadPlayers', 'loadPlayers']);
});

test('sink admin status panels: overview, services, database, ssl, version, news, equipment, bot stats, wizard log', async () => {
  const fetchFor = (v) => (url) => {
    if (url.endsWith('/bot-stats')) return { ageSeconds: 1, thresholds: { suspectMeanMs: v, suspectStdDevMs: v, suspectConsecutiveCount: v, fastThresholdMs: v, windowSize: v }, sessions: [{ username: v, suspect: false, totalFlags: 2, meanMs: 5, stddevMs: 5, consecFast: v }] };
    if (url.endsWith('/wizard-log?limit=50')) return { entries: [{ created_at: null, wizard_name: v, action: v, target: v, details: v }] };
    return null;
  };
  const { h, b } = await both(async (p, v) => {
    p.ctx.renderOverviewCards({ server: { systemUptime: 1, processUptime: 1, memoryPercent: v, memoryUsedMB: v, memoryTotalMB: v, cpuLoad: [1], cpuCount: 2 }, disk: { percent: v, usedGB: v, totalGB: v }, players: { online: v, peakOnline: v, sessionPeak: v, totalRegistered: v, totalSleeping: v, newToday: v, activeLast24h: v, activeLast7d: v, banned: v }, webProxy: { wsConnections: v, sseClients: v, dashSseClients: v } });
    p.ctx.renderServices({ services: [{ name: v, status: v, pid: v, memory: v, uptime: v }] });
    p.ctx.renderDatabase({ tables: { [v]: 3 }, sizeMB: v, walSizeMB: v, integrityCheck: v });
    p.ctx.renderSslVersion({ expiresAt: '2030-01-01', daysRemaining: v, issuer: v }, { version: v, sizeMB: v, modifiedAt: null });
    p.ctx.renderNews({ recentNews: [{ message: v, category: v, time: null }] });
    p.ctx.renderEquipmentTab(panelData(v));
    p.run('refreshBotStats(); loadWizardLog();');
  }, fetchFor);
  for (const id of ['overview-cards', 'services-body', 'db-body', 'ssl-version-body', 'news-body', 'panel-equipment', 'bot-stats-body', 'wizard-log-body']) {
    check(h, b, id, id);
  }
});

test('sink admin error messages (1444, 2093): a thrown message is shown as text', async () => {
  const thrower = (v) => (url) => { if (url.includes('/players?') || url.includes('/wizard-log')) throw new Error(v); return null; };
  const h = await page(thrower(HOSTILE));
  const b = await page(thrower('Alice'));
  for (const x of [h, b]) { await x.p.real.loadPlayers(); await x.p.run('loadWizardLog()'); }
  for (const id of ['player-table', 'wizard-log-body']) {
    assertSafe(assert, h.p.el(id).innerHTML, b.p.el(id).innerHTML, id);
    assert.ok(h.p.el(id).textContent.includes(HOSTILE), id + ' shows the message');
  }
});

test('admin raw JSON editor and reset dailies buttons run through the delegated listener', async () => {
  const fetchFor = () => (url) => (url.endsWith('/raw') ? { __raw: { status: 200, body: '{"a":"</textarea><img src=x onerror=1>"}' } } : null);
  const { h } = await both(async (p) => { await p.ctx.renderRawTab({ username: 'u' }); p.ctx.renderDailyTab(panelData('1')); }, fetchFor);
  assertSafe(assert, h.p.el('panel-raw').innerHTML, undefined, 'raw tab');
  h.p.click(h.p.el('panel-raw').querySelector('[data-action="save-raw"]'));
  h.p.click(h.p.el('panel-daily').querySelector('[data-action="reset-dailies"]'));
  assert.deepStrictEqual(h.calls.map((c) => c[0]), ['saveRawJson', 'resetDailies']);
});
