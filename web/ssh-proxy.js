// Usurper Reborn - WebSocket to SSH Bridge + Stats API + NPC Analytics Dashboard
// Proxies browser terminal connections to the game's SSH server
// Serves /api/stats endpoint with live game statistics
// Serves /api/dash/* endpoints for NPC analytics dashboard

const http = require('http');
const https = require('https');
const crypto = require('crypto');
const { Server } = require('ws');
const { Client } = require('ssh2');

let Database;
try {
  Database = require('better-sqlite3');
} catch (err) {
  console.error(`[usurper-web] better-sqlite3 not available: ${err.message}`);
}

const net = require('net');
const path = require('path');
const { execFile } = require('child_process');
const fs = require('fs');
const os = require('os');

let bcrypt;
try {
  bcrypt = require('bcryptjs');
} catch (err) {
  console.warn(`[usurper-web] bcryptjs not available, falling back to SHA256: ${err.message}`);
}

const WS_PORT = 3000;
const SSH_HOST = '127.0.0.1';
const SSH_PORT = 4000;
const SSH_USER = 'usurper';
const SSH_PASS = 'play';
const DB_PATH = process.env.DB_PATH || '/var/usurper/usurper_online.db';

// MUD mode: connect directly to MUD TCP server instead of through SSH
// Default ON since the MUD server now listens directly on port 4001.
// Connects to 4001 directly (bypassing sslh on 4000) so X-IP forwarding works.
// Set MUD_MODE=0 to fall back to legacy SSH mode if needed.
const MUD_MODE = process.env.MUD_MODE !== '0';
const MUD_HOST = process.env.MUD_HOST || '127.0.0.1';
const MUD_PORT = parseInt(process.env.MUD_PORT || '4001', 10);
const CACHE_TTL = 30000; // 30 seconds
let _ghReleasesCache = null;
let _ghReleasesCacheTime = 0;
const FEED_POLL_MS = 5000; // SSE feed polls DB every 5 seconds
const GITHUB_PAT = process.env.GITHUB_PAT || '';
const SPONSORS_CACHE_TTL = 3600000; // 1 hour

// Balance dashboard auth
const BALANCE_USER = process.env.BALANCE_USER || 'admin';
const BALANCE_DEFAULT_PASS = process.env.BALANCE_PASS || 'changeme';
const BALANCE_SECRET = process.env.BALANCE_SECRET || crypto.randomBytes(32).toString('hex');
const BALANCE_TOKEN_TTL = 24 * 60 * 60 * 1000; // 24 hours

// Admin dashboard auth (shares credentials with balance dashboard)
// Same user/pass — BALANCE_USER, BALANCE_PASS, BALANCE_SECRET are used for both

// --- Login Rate Limiting ---
const loginAttempts = new Map(); // IP -> { count, resetTime }
const MAX_LOGIN_ATTEMPTS = 5;
const LOCKOUT_DURATION = 15 * 60 * 1000; // 15 minutes

function checkRateLimit(ip) {
  const now = Date.now();
  const attempt = loginAttempts.get(ip);
  if (!attempt) return true; // no prior attempts
  if (now > attempt.resetTime) {
    loginAttempts.delete(ip);
    return true; // lockout expired
  }
  return attempt.count < MAX_LOGIN_ATTEMPTS;
}

function recordFailedLogin(ip) {
  const now = Date.now();
  const attempt = loginAttempts.get(ip) || { count: 0, resetTime: now + LOCKOUT_DURATION };
  attempt.count++;
  attempt.resetTime = now + LOCKOUT_DURATION;
  loginAttempts.set(ip, attempt);
}

function clearLoginAttempts(ip) {
  loginAttempts.delete(ip);
}

// Prune stale rate limit entries every 10 minutes
setInterval(() => {
  const now = Date.now();
  for (const [ip, attempt] of loginAttempts) {
    if (now > attempt.resetTime) loginAttempts.delete(ip);
  }
}, 600000);

// Peak player tracking
let peakOnlinePlayers = 0;
let peakOnlinePlayersTime = null;
let sessionPeakOnline = 0;
let sessionPeakTime = null;
const SERVER_START_TIME = Date.now();

const BCRYPT_ROUNDS = 12;

// Legacy SHA256 hash (for migration detection)
function hashPasswordSha256(password) {
  return crypto.createHash('sha256').update(password).digest('hex');
}

// Hash password with bcrypt (preferred) or SHA256 (fallback)
function hashPassword(password) {
  if (bcrypt) return bcrypt.hashSync(password, BCRYPT_ROUNDS);
  return hashPasswordSha256(password);
}

// Detect if a stored hash is legacy SHA256 (64 hex chars) vs bcrypt ($2a$/$2b$)
function isBcryptHash(hash) {
  return hash && (hash.startsWith('$2a$') || hash.startsWith('$2b$'));
}

// Get stored password hash from DB, or fall back to default
function getBalancePasswordHash() {
  if (!dbWrite) return hashPasswordSha256(BALANCE_DEFAULT_PASS);
  try {
    dbWrite.exec(`CREATE TABLE IF NOT EXISTS balance_config (
      key TEXT PRIMARY KEY,
      value TEXT NOT NULL
    )`);
    const row = dbWrite.prepare("SELECT value FROM balance_config WHERE key = 'password_hash'").get();
    if (row) return row.value;
  } catch (e) {
    console.error(`[usurper-web] Balance config read error: ${e.message}`);
  }
  return hashPasswordSha256(BALANCE_DEFAULT_PASS);
}

function setBalancePasswordHash(hash) {
  if (!dbWrite) return false;
  try {
    dbWrite.exec(`CREATE TABLE IF NOT EXISTS balance_config (
      key TEXT PRIMARY KEY,
      value TEXT NOT NULL
    )`);
    dbWrite.prepare("INSERT OR REPLACE INTO balance_config (key, value) VALUES ('password_hash', ?)").run(hash);
    return true;
  } catch (e) {
    console.error(`[usurper-web] Balance config write error: ${e.message}`);
    return false;
  }
}

// Verify password — supports both bcrypt and legacy SHA256, auto-migrates to bcrypt
function verifyBalancePassword(password) {
  const storedHash = getBalancePasswordHash();
  if (isBcryptHash(storedHash)) {
    // Modern bcrypt hash
    return bcrypt ? bcrypt.compareSync(password, storedHash) : false;
  }
  // Legacy SHA256 hash — verify then auto-migrate to bcrypt
  if (hashPasswordSha256(password) === storedHash) {
    if (bcrypt) {
      const newHash = bcrypt.hashSync(password, BCRYPT_ROUNDS);
      setBalancePasswordHash(newHash);
      console.log('[security] Migrated password hash from SHA256 to bcrypt');
    }
    return true;
  }
  return false;
}

function createBalanceToken() {
  const payload = JSON.stringify({ user: BALANCE_USER, exp: Date.now() + BALANCE_TOKEN_TTL });
  const sig = crypto.createHmac('sha256', BALANCE_SECRET).update(payload).digest('hex');
  return Buffer.from(payload).toString('base64') + '.' + sig;
}

function verifyBalanceToken(token) {
  if (!token) return false;
  const parts = token.split('.');
  if (parts.length !== 2) return false;
  try {
    const payload = Buffer.from(parts[0], 'base64').toString();
    const sig = crypto.createHmac('sha256', BALANCE_SECRET).update(payload).digest('hex');
    if (sig !== parts[1]) return false;
    const data = JSON.parse(payload);
    return data.exp > Date.now();
  } catch { return false; }
}

// --- Admin Dashboard Auth (reuses balance credentials) ---
// Admin login/token uses the same balance auth functions:
// verifyBalancePassword(), createBalanceToken(), verifyBalanceToken()
// This means the same username/password works for both dashboards.

// --- Admin Metrics Caching ---
let adminOverviewCache = null;
let adminOverviewCacheTime = 0;
let adminServicesCache = null;
let adminServicesCacheTime = 0;
let adminDbCache = null;
let adminDbCacheTime = 0;
let adminSslCache = null;
let adminSslCacheTime = 0;

function execFileAsync(cmd, args, opts = {}) {
  return new Promise((resolve, reject) => {
    execFile(cmd, args, { timeout: 5000, ...opts }, (err, stdout, stderr) => {
      if (err) reject(err);
      else resolve(stdout);
    });
  });
}

// --- Peak Player Tracking ---
function initPeakTracking() {
  // Restore peak from DB on startup
  if (dbWrite) {
    try {
      dbWrite.exec(`CREATE TABLE IF NOT EXISTS admin_config (
        key TEXT PRIMARY KEY,
        value TEXT NOT NULL
      )`);
      const row = dbWrite.prepare("SELECT value FROM admin_config WHERE key = 'peak_online'").get();
      if (row) {
        const data = JSON.parse(row.value);
        peakOnlinePlayers = data.count || 0;
        peakOnlinePlayersTime = data.time || null;
      }
    } catch (e) { /* first run, no data yet */ }
  }

  // Poll every 30 seconds
  setInterval(() => {
    if (!db) return;
    try {
      const row = db.prepare(
        "SELECT COUNT(*) as cnt FROM online_players WHERE last_heartbeat >= datetime('now', '-120 seconds')"
      ).get();
      const current = row ? row.cnt : 0;

      // Session peak
      if (current > sessionPeakOnline) {
        sessionPeakOnline = current;
        sessionPeakTime = new Date().toISOString();
      }

      // All-time peak
      if (current > peakOnlinePlayers) {
        peakOnlinePlayers = current;
        peakOnlinePlayersTime = new Date().toISOString();
        if (dbWrite) {
          try {
            dbWrite.prepare("INSERT OR REPLACE INTO admin_config (key, value) VALUES (?, ?)")
              .run('peak_online', JSON.stringify({ count: peakOnlinePlayers, time: peakOnlinePlayersTime }));
          } catch (e) { /* non-critical */ }
        }
      }
    } catch (e) { /* DB may be locked */ }
  }, 30000);
}

// Dashboard cache constants
const DASH_NPC_CACHE_TTL = 10000; // 10 seconds
const DASH_EVENTS_CACHE_TTL = 5000; // 5 seconds
const DASH_HOURLY_CACHE_TTL = 60000; // 60 seconds
const DASH_SNAPSHOT_INTERVAL = 30000; // 30 seconds for NPC snapshots

const CLASS_NAMES = {
  0: 'Alchemist', 1: 'Assassin', 2: 'Barbarian', 3: 'Bard',
  4: 'Cleric', 5: 'Jester', 6: 'Magician', 7: 'Paladin',
  8: 'Ranger', 9: 'Sage', 10: 'Warrior',
  11: 'Tidesworn', 12: 'Wavecaller', 13: 'Cyclebreaker',
  14: 'Abysswarden', 15: 'Voidreaver'
};

const RACE_NAMES = {
  0: 'Human', 1: 'Hobbit', 2: 'Elf', 3: 'Half-Elf', 4: 'Dwarf',
  5: 'Troll', 6: 'Orc', 7: 'Gnome', 8: 'Gnoll', 9: 'Mutant'
};

const FACTION_NAMES = {
  '-1': 'None', 0: 'The Crown', 1: 'The Shadows', 2: 'The Faith'
};

const GOD_TITLES = [
  'Lesser Spirit', 'Minor Spirit', 'Spirit', 'Major Spirit',
  'Minor Deity', 'Deity', 'Major Deity', 'DemiGod', 'God'
];

const ALLOWED_ORIGINS = [
  'https://usurper-reborn.net',
  'https://www.usurper-reborn.net',
  'http://usurper-reborn.net',
  'http://www.usurper-reborn.net',
  'http://localhost',
];

// --- Database Setup ---
let db = null;
let dbWrite = null;
if (Database) {
  try {
    db = new Database(DB_PATH, { readonly: true, fileMustExist: true });
    db.pragma('journal_mode = WAL');
    console.log(`[usurper-web] Database connected (read-only): ${DB_PATH}`);
  } catch (err) {
    console.error(`[usurper-web] Database not available: ${err.message}`);
  }

  // Writable connection for dashboard auth tables only
  try {
    dbWrite = new Database(DB_PATH, { fileMustExist: true });
    dbWrite.pragma('journal_mode = WAL');
    console.log(`[usurper-web] Database connected (writable): ${DB_PATH}`);
  } catch (err) {
    console.error(`[usurper-web] Writable database not available: ${err.message}`);
  }
}

// Initialize peak tracking after DB is available
initPeakTracking();

function readBody(req) {
  return new Promise((resolve, reject) => {
    let body = '';
    req.on('data', chunk => {
      body += chunk;
      if (body.length > 10000) { reject(new Error('Body too large')); req.destroy(); }
    });
    req.on('end', () => {
      try { resolve(JSON.parse(body)); } catch (e) { reject(e); }
    });
    req.on('error', reject);
  });
}

function sendJson(res, status, data, extraHeaders) {
  res.setHeader('Content-Type', 'application/json');
  if (extraHeaders) {
    for (const [k, v] of Object.entries(extraHeaders)) res.setHeader(k, v);
  }
  res.writeHead(status);
  res.end(JSON.stringify(data));
}

// --- Dashboard NPC Data Cache ---
let dashNpcCache = null;
let dashNpcCacheTime = 0;
let dashEventsCache = null;
let dashEventsCacheTime = 0;
let dashHourlyCache = null;
let dashHourlyCacheTime = 0;

function getDashNpcs() {
  const now = Date.now();
  if (dashNpcCache && (now - dashNpcCacheTime) < DASH_NPC_CACHE_TTL) return dashNpcCache;
  if (!db) return [];
  try {
    const row = db.prepare("SELECT value FROM world_state WHERE key = 'npcs'").get();
    if (!row || !row.value) return [];
    dashNpcCache = JSON.parse(row.value);
    dashNpcCacheTime = now;
    return dashNpcCache;
  } catch (err) {
    console.error(`[usurper-web] Dashboard NPC query error: ${err.message}`);
    return dashNpcCache || [];
  }
}

// Read royal court data from world_state (the authoritative source).
// The world sim maintains the 'royal_court' key 24/7. Player sessions write to it
// when they change king state (throne challenges, tax changes, treasury ops).
// Falls back to 'economy' key if 'royal_court' doesn't exist yet.
function getRoyalCourtFromWorldState() {
  if (!db) return null;
  try {
    // Primary: dedicated royal_court key (full court data)
    const row = db.prepare(`SELECT value FROM world_state WHERE key = 'royal_court'`).get();
    if (row && row.value) return JSON.parse(row.value);
  } catch (e) { /* royal_court key may not exist yet */ }
  try {
    // Fallback: economy key (has king name, treasury, tax rates)
    const row = db.prepare(`SELECT value FROM world_state WHERE key = 'economy'`).get();
    if (row && row.value) return JSON.parse(row.value);
  } catch (e) { /* economy key may not exist yet */ }
  return null;
}

// Derive city controller info from NPC data (isTeamLeader maps to CTurf in-game)
function getCityControllerFromNpcs(npcs) {
  if (!npcs || npcs.length === 0) return null;
  const controllers = npcs.filter(n => {
    const isLeader = n.isTeamLeader || n.IsTeamLeader;
    const hasTeam = n.team || n.Team;
    const alive = !(n.isDead || n.IsDead);
    return isLeader && hasTeam && alive;
  });
  if (controllers.length === 0) return null;

  const teamName = controllers[0].team || controllers[0].Team;
  // All members of the controlling team (not just leaders)
  const teamMembers = npcs.filter(n => (n.team || n.Team) === teamName && !(n.isDead || n.IsDead));
  const teamLeader = teamMembers.sort((a, b) => (b.level || b.Level || 0) - (a.level || a.Level || 0))[0];
  const totalPower = teamMembers.reduce((sum, n) => {
    return sum + (n.level || n.Level || 0) + (n.strength || n.Strength || 0) + (n.defence || n.Defence || 0);
  }, 0);

  return {
    teamName,
    memberCount: teamMembers.length,
    totalPower,
    leaderName: teamLeader ? (teamLeader.name || teamLeader.Name || 'None') : 'None',
    leaderBank: teamLeader ? (teamLeader.bankGold || teamLeader.BankGold || 0) : 0
  };
}

function getDashSummary() {
  const npcs = getDashNpcs();
  if (!npcs || npcs.length === 0) return { total: 0 };

  const alive = npcs.filter(n => !n.isDead && !n.IsDead);
  const dead = npcs.filter(n => n.isDead || n.IsDead);
  const permadead = npcs.filter(n => n.isPermaDead || n.IsPermaDead);
  const agedDeath = npcs.filter(n => n.isAgedDeath || n.IsAgedDeath);
  const married = npcs.filter(n => n.isMarried || n.IsMarried);
  const pregnant = npcs.filter(n => n.pregnancyDueDate || n.PregnancyDueDate);
  const teams = new Set(npcs.filter(n => n.team || n.Team).map(n => n.team || n.Team).filter(t => t));

  // Class distribution (alive NPCs only — exclude permadead)
  const classDist = {};
  alive.forEach(n => {
    const cls = CLASS_NAMES[n.class] || CLASS_NAMES[n.Class] || 'Unknown';
    classDist[cls] = (classDist[cls] || 0) + 1;
  });

  // Race distribution (alive only)
  const raceDist = {};
  alive.forEach(n => {
    const race = RACE_NAMES[n.race] || RACE_NAMES[n.Race] || 'Unknown';
    raceDist[race] = (raceDist[race] || 0) + 1;
  });

  // Faction distribution (alive only)
  const factionDist = {};
  alive.forEach(n => {
    const fid = String(n.npcFaction !== undefined ? n.npcFaction : (n.NPCFaction !== undefined ? n.NPCFaction : -1));
    const fname = FACTION_NAMES[fid] || 'None';
    factionDist[fname] = (factionDist[fname] || 0) + 1;
  });

  // Location distribution (alive only)
  const locationDist = {};
  alive.forEach(n => {
    const loc = n.location || n.Location || 'Unknown';
    locationDist[loc] = (locationDist[loc] || 0) + 1;
  });

  // Level distribution (alive only)
  const levelDist = {};
  alive.forEach(n => {
    const lvl = n.level || n.Level || 1;
    const bucket = Math.floor(lvl / 10) * 10;
    const key = `${bucket}-${bucket + 9}`;
    levelDist[key] = (levelDist[key] || 0) + 1;
  });

  // Age distribution (alive only)
  const ageDist = {};
  alive.forEach(n => {
    const age = n.age || n.Age || 0;
    if (age > 0) {
      const bucket = Math.floor(age / 10) * 10;
      const key = `${bucket}-${bucket + 9}`;
      ageDist[key] = (ageDist[key] || 0) + 1;
    }
  });

  // Average level (alive only)
  const levels = alive.map(n => n.level || n.Level || 1);
  const avgLevel = levels.length > 0 ? (levels.reduce((a, b) => a + b, 0) / levels.length).toFixed(1) : 0;

  // Total gold (alive only)
  const totalGold = alive.reduce((sum, n) => sum + (n.gold || n.Gold || 0), 0);

  // King - from NPC data (which NPC has isKing flag)
  const kingNpc = npcs.find(n => n.isKing || n.IsKing);

  // Economy data: read from world_state (the authoritative source).
  // The world sim maintains the 'royal_court' and 'economy' keys 24/7.
  // Player sessions write to 'royal_court' when they change king state.
  let economy = null;
  try {
    const royalCourt = getRoyalCourtFromWorldState();
    const cityController = getCityControllerFromNpcs(npcs);

    // Economy summary from world sim (daily income/expenses/revenue counters)
    let econSummary = {};
    try {
      const econRow = db.prepare("SELECT value FROM world_state WHERE key = 'economy'").get();
      if (econRow && econRow.value) econSummary = JSON.parse(econRow.value);
    } catch (e) {}

    if (royalCourt || cityController || Object.keys(econSummary).length > 0) {
      economy = {
        kingName: royalCourt?.kingName || econSummary.kingName || 'None',
        kingIsActive: true,
        treasury: royalCourt?.treasury ?? econSummary.treasury ?? 0,
        taxRate: royalCourt?.taxRate ?? econSummary.taxRate ?? 0,
        kingTaxPercent: royalCourt?.kingTaxPercent ?? econSummary.kingTaxPercent ?? 0,
        cityTaxPercent: royalCourt?.cityTaxPercent ?? econSummary.cityTaxPercent ?? 0,
        dailyTaxRevenue: econSummary.dailyTaxRevenue || 0,
        dailyCityTaxRevenue: econSummary.dailyCityTaxRevenue || 0,
        dailyIncome: econSummary.dailyIncome || 0,
        dailyExpenses: econSummary.dailyExpenses || 0,
        cityControlTeam: cityController?.teamName || econSummary.cityControlTeam || 'None',
        cityControlMembers: cityController?.memberCount || econSummary.cityControlMembers || 0,
        cityControlPower: cityController?.totalPower || econSummary.cityControlPower || 0,
        cityControlLeader: cityController?.leaderName || econSummary.cityControlLeader || 'None',
        cityControlLeaderBank: cityController?.leaderBank || econSummary.cityControlLeaderBank || 0
      };
    }
  } catch (e) { /* economy data may not be available */ }

  // Children data from world_state (serialized by WorldSimService)
  let children = null;
  try {
    const childRow = db.prepare("SELECT value FROM world_state WHERE key = 'children'").get();
    if (childRow && childRow.value) children = JSON.parse(childRow.value);
  } catch (e) { /* children data may not exist yet */ }

  return {
    total: npcs.length,
    alive: alive.length,
    dead: dead.length,
    permadead: permadead.length,
    agedDeath: agedDeath.length,
    married: married.length,
    pregnant: pregnant.length,
    teams: economy?.cityControlTeam && economy.cityControlTeam !== 'None' && !teams.has(economy.cityControlTeam)
      ? teams.size + 1 : teams.size,
    avgLevel: parseFloat(avgLevel),
    totalGold,
    king: economy?.kingName || (kingNpc ? (kingNpc.name || kingNpc.Name) : null),
    classDist,
    raceDist,
    factionDist,
    locationDist,
    levelDist,
    ageDist,
    economy,
    children
  };
}

function getDashEvents() {
  const now = Date.now();
  if (dashEventsCache && (now - dashEventsCacheTime) < DASH_EVENTS_CACHE_TTL) return dashEventsCache;
  if (!db) return [];
  try {
    dashEventsCache = db.prepare(`
      SELECT id, message, category, player_name, created_at
      FROM news ORDER BY created_at DESC LIMIT 500
    `).all().map(row => ({
      id: row.id,
      message: row.message,
      category: row.category,
      playerName: row.player_name,
      time: row.created_at
    }));
    dashEventsCacheTime = now;
    return dashEventsCache;
  } catch (err) {
    return dashEventsCache || [];
  }
}

function getDashEventsHourly() {
  const now = Date.now();
  if (dashHourlyCache && (now - dashHourlyCacheTime) < DASH_HOURLY_CACHE_TTL) return dashHourlyCache;
  if (!db) return [];
  try {
    dashHourlyCache = db.prepare(`
      SELECT
        strftime('%Y-%m-%d %H:00', created_at) as hour,
        category,
        COUNT(*) as count
      FROM news
      WHERE created_at >= datetime('now', '-24 hours')
      GROUP BY hour, category
      ORDER BY hour ASC
    `).all().map(row => ({
      hour: row.hour,
      category: row.category,
      count: row.count
    }));
    dashHourlyCacheTime = now;
    return dashHourlyCache;
  } catch (err) {
    return dashHourlyCache || [];
  }
}

// --- Dashboard SSE Feed ---
const dashSseClients = new Set();
let dashLastNewsId = 0;
let lastNpcSnapshotTime = 0;
let lastNpcSnapshotHash = '';

function initDashFeedIds() {
  if (!db) return;
  try {
    const row = db.prepare(`SELECT MAX(id) as maxId FROM news`).get();
    if (row && row.maxId) dashLastNewsId = row.maxId;
    console.log(`[usurper-web] Dashboard feed initialized: newsId=${dashLastNewsId}`);
  } catch (e) { /* table may not exist */ }
}
initDashFeedIds();

function dashPollFeed() {
  if (dashSseClients.size === 0 || !db) return;

  try {
    // New news events (all categories)
    const newsRows = db.prepare(`
      SELECT id, message, category, created_at FROM news
      WHERE id > ? ORDER BY id ASC LIMIT 20
    `).all(dashLastNewsId);

    if (newsRows.length > 0) {
      dashLastNewsId = newsRows[newsRows.length - 1].id;
      const items = newsRows.map(r => ({ message: r.message, category: r.category, time: r.created_at }));
      dashBroadcast('news', JSON.stringify({ items }));
    }

    // NPC snapshot (every 30 seconds)
    const now = Date.now();
    if (now - lastNpcSnapshotTime >= DASH_SNAPSHOT_INTERVAL) {
      lastNpcSnapshotTime = now;
      const npcs = getDashNpcs();
      if (npcs && npcs.length > 0) {
        // Compact snapshot for SSE (just key fields per NPC)
        const snapshot = npcs.map(n => ({
          name: n.name || n.Name,
          level: n.level || n.Level || 1,
          location: n.location || n.Location || 'Unknown',
          alive: !(n.isDead || n.IsDead),
          married: !!(n.isMarried || n.IsMarried),
          faction: n.npcFaction !== undefined ? n.npcFaction : (n.NPCFaction !== undefined ? n.NPCFaction : -1),
          isKing: !!(n.isKing || n.IsKing),
          pregnant: !!(n.pregnancyDueDate || n.PregnancyDueDate),
          emotion: n.emotionalState || n.EmotionalState || null,
          hp: n.hp || n.HP || 0,
          maxHp: n.maxHP || n.MaxHP || 1
        }));
        const hash = crypto.createHash('md5').update(JSON.stringify(snapshot)).digest('hex');
        if (hash !== lastNpcSnapshotHash) {
          lastNpcSnapshotHash = hash;
          const summary = getDashSummary();
          dashBroadcast('npc-snapshot', JSON.stringify({ npcs: snapshot, summary }));
        }
      }
    }
  } catch (e) {
    // Silently ignore
  }
}

function dashBroadcast(eventType, data) {
  const msg = `event: ${eventType}\ndata: ${data}\n\n`;
  for (const client of dashSseClients) {
    try { client.write(msg); } catch (e) { dashSseClients.delete(client); }
  }
}

// Dashboard heartbeat
function dashHeartbeat() {
  if (dashSseClients.size === 0) return;
  const msg = `:heartbeat ${Date.now()}\n\n`;
  for (const client of dashSseClients) {
    try { client.write(msg); } catch (e) { dashSseClients.delete(client); }
  }
}

// --- Sponsors Cache ---
let sponsorsCache = null;
let sponsorsCacheTime = 0;

async function getSponsors() {
  const now = Date.now();
  if (sponsorsCache && (now - sponsorsCacheTime) < SPONSORS_CACHE_TTL) {
    return sponsorsCache;
  }

  if (!GITHUB_PAT) {
    return { sponsors: [], note: 'GitHub PAT not configured' };
  }

  const query = JSON.stringify({ query: `{
    viewer {
      sponsorshipsAsMaintainer(first: 100, includePrivate: false, orderBy: {field: CREATED_AT, direction: ASC}) {
        totalCount
        nodes {
          sponsorEntity {
            ... on User { login name avatarUrl url }
            ... on Organization { login name avatarUrl url }
          }
          tier { name monthlyPriceInDollars isOneTime }
          createdAt
        }
      }
    }
  }` });

  return new Promise((resolve) => {
    const req = https.request('https://api.github.com/graphql', {
      method: 'POST',
      headers: {
        'Authorization': `bearer ${GITHUB_PAT}`,
        'Content-Type': 'application/json',
        'User-Agent': 'usurper-reborn-web',
        'Content-Length': Buffer.byteLength(query)
      }
    }, (res) => {
      let body = '';
      res.on('data', chunk => body += chunk);
      res.on('end', () => {
        try {
          const data = JSON.parse(body);
          const viewer = data.data && data.data.viewer;
          const nodes = (viewer &&
            viewer.sponsorshipsAsMaintainer &&
            viewer.sponsorshipsAsMaintainer.nodes) || [];

          sponsorsCache = {
            sponsors: nodes
              .filter(n => n.sponsorEntity)
              .map(n => ({
                login: n.sponsorEntity.login,
                name: n.sponsorEntity.name,
                avatarUrl: n.sponsorEntity.avatarUrl,
                url: n.sponsorEntity.url,
                tier: (n.tier && n.tier.name) || null,
                since: n.createdAt
              })),
            totalCount: (viewer &&
              viewer.sponsorshipsAsMaintainer &&
              viewer.sponsorshipsAsMaintainer.totalCount) || 0,
            cachedAt: new Date().toISOString()
          };
          sponsorsCacheTime = now;
          console.log(`[usurper-web] Sponsors refreshed: ${sponsorsCache.sponsors.length} public sponsors`);
          resolve(sponsorsCache);
        } catch (err) {
          console.error(`[usurper-web] Sponsors parse error: ${err.message}`);
          resolve(sponsorsCache || { sponsors: [] });
        }
      });
    });

    req.on('error', (err) => {
      console.error(`[usurper-web] Sponsors fetch error: ${err.message}`);
      resolve(sponsorsCache || { sponsors: [] });
    });

    req.write(query);
    req.end();
  });
}

// Pre-fetch sponsors on startup
if (GITHUB_PAT) {
  getSponsors().catch(() => {});
}

// --- Stats Cache ---
let statsCache = null;
let statsCacheTime = 0;

function getStats() {
  const now = Date.now();
  if (statsCache && (now - statsCacheTime) < CACHE_TTL) {
    return statsCache;
  }

  if (!db) {
    return { error: 'Database not available', online: [], stats: {}, highlights: {}, news: [], npcActivities: [] };
  }

  try {
    // Online players with class/level + immortal data
    const online = db.prepare(`
      SELECT op.username, op.display_name, op.location, op.connected_at,
             json_extract(p.player_data, '$.player.level') as level,
             json_extract(p.player_data, '$.player.class') as class_id,
             COALESCE(op.connection_type, 'Unknown') as connection_type,
             json_extract(p.player_data, '$.player.isImmortal') as is_immortal,
             json_extract(p.player_data, '$.player.divineName') as divine_name,
             json_extract(p.player_data, '$.player.godLevel') as god_level
      FROM online_players op
      LEFT JOIN players p ON LOWER(op.username) = LOWER(p.username)
      WHERE op.last_heartbeat >= datetime('now', '-120 seconds')
      ORDER BY op.display_name
    `).all().map(row => {
      const isImmortal = row.is_immortal === 1 || row.is_immortal === true;
      return {
        name: row.display_name || row.username,
        level: isImmortal ? (row.god_level || 1) : (row.level || 1),
        className: isImmortal ? 'Immortal' : (CLASS_NAMES[row.class_id] || 'Unknown'),
        location: row.location || 'Unknown',
        connectedAt: row.connected_at,
        connectionType: row.connection_type || 'Unknown',
        isImmortal: isImmortal,
        divineName: row.divine_name || null,
        godLevel: row.god_level || 0
      };
    });

    // Aggregate stats
    const agg = db.prepare(`
      SELECT
        COUNT(*) as totalPlayers,
        COALESCE(SUM(json_extract(player_data, '$.player.statistics.totalMonstersKilled')), 0) as totalKills,
        COALESCE(ROUND(AVG(json_extract(player_data, '$.player.level')), 1), 0) as avgLevel,
        COALESCE(MAX(json_extract(player_data, '$.player.level')), 0) as maxLevel,
        COALESCE(MAX(json_extract(player_data, '$.player.statistics.deepestDungeonLevel')), 0) as deepestFloor,
        COALESCE(SUM(json_extract(player_data, '$.player.gold')), 0)
          + COALESCE(SUM(json_extract(player_data, '$.player.bankGold')), 0) as totalGold
      FROM players WHERE is_banned = 0 AND username NOT LIKE 'emergency_%'
    `).get();

    // Top player (exclude immortals — they have their own section)
    const topPlayer = db.prepare(`
      SELECT display_name,
             json_extract(player_data, '$.player.level') as level,
             json_extract(player_data, '$.player.class') as class_id
      FROM players WHERE is_banned = 0 AND username NOT LIKE 'emergency_%'
        AND (json_extract(player_data, '$.player.isImmortal') IS NULL
             OR json_extract(player_data, '$.player.isImmortal') != 1)
      ORDER BY json_extract(player_data, '$.player.level') DESC LIMIT 1
    `).get();

    // Most popular class (exclude NULL class from empty/test accounts)
    const popClass = db.prepare(`
      SELECT json_extract(player_data, '$.player.class') as class_id, COUNT(*) as cnt
      FROM players WHERE is_banned = 0
        AND username NOT LIKE 'emergency_%'
        AND json_extract(player_data, '$.player.class') IS NOT NULL
      GROUP BY class_id ORDER BY cnt DESC LIMIT 1
    `).get();

    // Children count (from world_state - authoritative source maintained by world sim)
    let childrenCount = 0;
    try {
      const childRow = db.prepare("SELECT value FROM world_state WHERE key = 'children'").get();
      if (childRow && childRow.value) {
        const childData = JSON.parse(childRow.value);
        childrenCount = childData.count || 0;
      }
    } catch (e) { /* children data may not exist yet */ }

    // Marriage count (reuse cached NPC data to avoid re-parsing 19MB blob)
    let marriageCount = 0;
    try {
      const npcs = getDashNpcs();
      if (npcs && npcs.length > 0) {
        const marriedCount = npcs.filter(n => n.isMarried || n.IsMarried).length;
        marriageCount = Math.floor(marriedCount / 2);
      }
    } catch (e) { /* npcs may not exist */ }

    // NPC permadeath / aged death counts (from world_state NPC blob)
    let permadeadCount = 0;
    let agedDeathCount = 0;
    try {
      const npcs = getDashNpcs();
      if (npcs && npcs.length > 0) {
        permadeadCount = npcs.filter(n => n.isPermaDead || n.IsPermaDead).length;
        agedDeathCount = npcs.filter(n => n.isAgedDeath || n.IsAgedDeath).length;
      }
    } catch (e) { /* npcs may not exist */ }

    // Most wanted player (highest murder weight)
    let mostWanted = null;
    try {
      const wanted = db.prepare(`
        SELECT display_name,
               json_extract(player_data, '$.player.level') as level,
               json_extract(player_data, '$.player.class') as class_id,
               CAST(json_extract(player_data, '$.player.murderWeight') AS REAL) as murder_weight
        FROM players WHERE is_banned = 0
          AND username NOT LIKE 'emergency_%'
          AND json_extract(player_data, '$.player.murderWeight') IS NOT NULL
          AND CAST(json_extract(player_data, '$.player.murderWeight') AS REAL) > 0
        ORDER BY murder_weight DESC LIMIT 1
      `).get();
      if (wanted) {
        mostWanted = {
          name: wanted.display_name,
          level: wanted.level || 1,
          className: CLASS_NAMES[wanted.class_id] || 'Unknown',
          murderWeight: Math.round(wanted.murder_weight * 10) / 10
        };
      }
    } catch (e) { /* murder weight may not exist in older saves */ }

    // Current king (from world_state - the authoritative source maintained by world sim)
    let king = null;
    try {
      const royalCourt = getRoyalCourtFromWorldState();
      if (royalCourt && royalCourt.kingName) {
        king = royalCourt.kingName;
      }
    } catch (e) { /* king may not exist */ }

    // Immortals (ascended player-gods)
    let immortals = [];
    try {
      immortals = db.prepare(`
        SELECT
          p.display_name,
          json_extract(p.player_data, '$.player.divineName') as divine_name,
          json_extract(p.player_data, '$.player.godLevel') as god_level,
          json_extract(p.player_data, '$.player.godExperience') as god_xp,
          json_extract(p.player_data, '$.player.godAlignment') as god_alignment,
          json_extract(p.player_data, '$.player.worshippedGod') as worshipped_god,
          CASE WHEN op.username IS NOT NULL THEN 1 ELSE 0 END as is_online
        FROM players p
        LEFT JOIN online_players op ON LOWER(p.username) = LOWER(op.username)
          AND op.last_heartbeat >= datetime('now', '-120 seconds')
        WHERE p.is_banned = 0
          AND p.player_data != '{}'
          AND LENGTH(p.player_data) > 2
          AND json_extract(p.player_data, '$.player.isImmortal') = 1
          AND p.username NOT LIKE 'emergency_%'
        ORDER BY json_extract(p.player_data, '$.player.godExperience') DESC
      `).all().map(row => {
        const lvl = Math.max(1, Math.min(row.god_level || 1, 9));
        // Count NPC believers worshipping this god
        let believers = 0;
        try {
          const npcData = getDashNpcs();
          if (npcData && npcData.length > 0) {
            believers = npcData.filter(n => n.worshippedGod === row.divine_name || n.WorshippedGod === row.divine_name).length;
          }
        } catch (e) { /* npc data may not be available */ }
        // Count player believers
        try {
          const pCount = db.prepare(`
            SELECT COUNT(*) as cnt FROM players
            WHERE is_banned = 0 AND username NOT LIKE 'emergency_%'
              AND json_extract(player_data, '$.player.worshippedGod') = ?
          `).get(row.divine_name);
          believers += (pCount ? pCount.cnt : 0);
        } catch (e) { /* player believer count may fail */ }
        return {
          mortalName: row.display_name,
          divineName: row.divine_name || row.display_name,
          godLevel: lvl,
          godTitle: GOD_TITLES[lvl - 1] || 'Spirit',
          godExperience: row.god_xp || 0,
          godAlignment: row.god_alignment || 'Balance',
          believers: believers,
          isOnline: row.is_online === 1
        };
      });
    } catch (e) { /* immortals query may fail */ }

    // Player leaderboard (mortal players only, ranked by level then XP)
    let leaderboard = [];
    try {
      leaderboard = db.prepare(`
        SELECT
          p.display_name,
          json_extract(p.player_data, '$.player.level') as level,
          json_extract(p.player_data, '$.player.class') as class_id,
          json_extract(p.player_data, '$.player.experience') as xp,
          CASE WHEN op.username IS NOT NULL THEN 1 ELSE 0 END as is_online
        FROM players p
        LEFT JOIN online_players op ON LOWER(p.username) = LOWER(op.username)
          AND op.last_heartbeat >= datetime('now', '-120 seconds')
        WHERE p.is_banned = 0
          AND p.player_data != '{}'
          AND LENGTH(p.player_data) > 2
          AND json_extract(p.player_data, '$.player.level') IS NOT NULL
          AND p.username NOT LIKE 'emergency_%'
          AND (json_extract(p.player_data, '$.player.isImmortal') IS NULL
               OR json_extract(p.player_data, '$.player.isImmortal') != 1)
        ORDER BY json_extract(p.player_data, '$.player.level') DESC,
                 json_extract(p.player_data, '$.player.experience') DESC
        LIMIT 25
      `).all().map((row, idx) => ({
        rank: idx + 1,
        name: row.display_name,
        level: row.level || 1,
        className: CLASS_NAMES[row.class_id] || 'Unknown',
        experience: row.xp || 0,
        isOnline: row.is_online === 1
      }));
    } catch (e) { /* leaderboard query may fail */ }

    // Recent news
    let news = [];
    try {
      news = db.prepare(`
        SELECT message, category, player_name, created_at
        FROM news WHERE category != 'npc' ORDER BY created_at DESC LIMIT 10
      `).all().map(row => ({
        message: row.message,
        category: row.category,
        playerName: row.player_name,
        time: row.created_at
      }));
    } catch (e) { /* news table may not exist */ }

    // NPC world activity feed (from 24/7 world simulator)
    let npcActivities = [];
    try {
      npcActivities = db.prepare(`
        SELECT message, created_at
        FROM news
        WHERE category = 'npc'
        ORDER BY created_at DESC
        LIMIT 20
      `).all().map(row => ({
        message: row.message,
        time: row.created_at
      }));
    } catch (e) { /* npc activities may not exist yet */ }

    // Economy/tax data: from world_state (authoritative, maintained by world sim)
    let economy = null;
    try {
      const royalCourt = getRoyalCourtFromWorldState();
      let econSummary = {};
      try {
        const econRow = db.prepare(`SELECT value FROM world_state WHERE key = 'economy'`).get();
        if (econRow && econRow.value) econSummary = JSON.parse(econRow.value);
      } catch (e) {}

      if (royalCourt || Object.keys(econSummary).length > 0) {
        economy = {
          kingName: royalCourt?.kingName || econSummary.kingName || 'None',
          treasury: royalCourt?.treasury ?? econSummary.treasury ?? 0,
          taxRate: royalCourt?.taxRate ?? econSummary.taxRate ?? 0,
          kingTaxPercent: royalCourt?.kingTaxPercent ?? econSummary.kingTaxPercent ?? 0,
          cityTaxPercent: royalCourt?.cityTaxPercent ?? econSummary.cityTaxPercent ?? 0,
          dailyTaxRevenue: econSummary.dailyTaxRevenue || 0,
          dailyCityTaxRevenue: econSummary.dailyCityTaxRevenue || 0,
          dailyIncome: econSummary.dailyIncome || 0,
          dailyExpenses: econSummary.dailyExpenses || 0,
          cityControlTeam: econSummary.cityControlTeam || 'None',
          cityControlMembers: econSummary.cityControlMembers || 0,
          cityControlPower: econSummary.cityControlPower || 0,
          cityControlLeader: econSummary.cityControlLeader || 'None',
          cityControlLeaderBank: econSummary.cityControlLeaderBank || 0
        };
      }
    } catch (e) { /* economy data may not be available */ }

    // PvP leaderboard
    let pvpLeaderboard = [];
    try {
      pvpLeaderboard = db.prepare(`
        SELECT
          p.display_name,
          json_extract(p.player_data, '$.player.level') as level,
          json_extract(p.player_data, '$.player.class') as class_id,
          SUM(CASE WHEN LOWER(pvp.winner) = LOWER(pvp.player) THEN 1 ELSE 0 END) as wins,
          SUM(CASE WHEN LOWER(pvp.winner) != LOWER(pvp.player) THEN 1 ELSE 0 END) as losses,
          COALESCE(SUM(CASE WHEN LOWER(pvp.winner) = LOWER(pvp.player) THEN pvp.gold_stolen ELSE 0 END), 0) as gold_stolen
        FROM (
          SELECT attacker as player, attacker, defender, winner, gold_stolen FROM pvp_log
          UNION ALL
          SELECT defender as player, attacker, defender, winner, gold_stolen FROM pvp_log
        ) pvp
        JOIN players p ON LOWER(pvp.player) = LOWER(p.username)
        WHERE p.is_banned = 0
        GROUP BY pvp.player
        HAVING wins > 0
        ORDER BY wins DESC, gold_stolen DESC
        LIMIT 10
      `).all().map((row, idx) => ({
        rank: idx + 1,
        name: row.display_name,
        level: row.level || 1,
        className: CLASS_NAMES[row.class_id] || 'Unknown',
        wins: row.wins || 0,
        losses: row.losses || 0,
        goldStolen: row.gold_stolen || 0
      }));
    } catch (e) { /* pvp_log table may not exist yet */ }

    statsCache = {
      online: online,
      onlineCount: online.length,
      stats: {
        totalPlayers: agg ? agg.totalPlayers : 0,
        totalKills: agg ? agg.totalKills : 0,
        avgLevel: agg ? agg.avgLevel : 0,
        maxLevel: agg ? agg.maxLevel : 0,
        deepestFloor: agg ? agg.deepestFloor : 0,
        totalGold: agg ? agg.totalGold : 0,
        marriages: marriageCount,
        children: childrenCount,
        permadeadNpcs: permadeadCount,
        agedDeathNpcs: agedDeathCount
      },
      highlights: {
        topPlayer: topPlayer ? {
          name: topPlayer.display_name,
          level: topPlayer.level,
          className: CLASS_NAMES[topPlayer.class_id] || 'Unknown'
        } : null,
        king: king,
        popularClass: popClass ? CLASS_NAMES[popClass.class_id] || 'Unknown' : null,
        mostWanted: mostWanted
      },
      immortals: immortals,
      leaderboard: leaderboard,
      pvpLeaderboard: pvpLeaderboard,
      economy: economy,
      news: news,
      npcActivities: npcActivities,
      cachedAt: new Date().toISOString()
    };
    statsCacheTime = now;
    return statsCache;
  } catch (err) {
    console.error(`[usurper-web] Stats query error: ${err.message}`);
    return { error: 'Stats query failed', online: [], stats: {}, highlights: {}, news: [], npcActivities: [] };
  }
}

// --- SSE Live Feed (public) ---
const sseClients = new Set();
let lastNpcId = 0;
let lastNewsId = 0;

// Initialize high-water marks from current DB state
function initFeedIds() {
  if (!db) return;
  try {
    const npcRow = db.prepare(`SELECT MAX(id) as maxId FROM news WHERE category = 'npc'`).get();
    if (npcRow && npcRow.maxId) lastNpcId = npcRow.maxId;
    const newsRow = db.prepare(`SELECT MAX(id) as maxId FROM news WHERE category != 'npc'`).get();
    if (newsRow && newsRow.maxId) lastNewsId = newsRow.maxId;
    console.log(`[usurper-web] Feed initialized: npcId=${lastNpcId}, newsId=${lastNewsId}`);
  } catch (e) { /* tables may not exist yet */ }
}
initFeedIds();

// Poll for new entries and push to all SSE clients
function pollFeed() {
  if (sseClients.size === 0 || !db) return;

  try {
    // New NPC activities since last check
    const npcRows = db.prepare(`
      SELECT id, message, created_at FROM news
      WHERE category = 'npc' AND id > ? ORDER BY id ASC LIMIT 10
    `).all(lastNpcId);

    // New player news since last check
    const newsRows = db.prepare(`
      SELECT id, message, created_at FROM news
      WHERE category != 'npc' AND id > ? ORDER BY id ASC LIMIT 10
    `).all(lastNewsId);

    if (npcRows.length > 0) {
      lastNpcId = npcRows[npcRows.length - 1].id;
      const items = npcRows.map(r => ({ message: r.message, time: r.created_at }));
      broadcast('npc', items);
    }

    if (newsRows.length > 0) {
      lastNewsId = newsRows[newsRows.length - 1].id;
      const items = newsRows.map(r => ({ message: r.message, time: r.created_at }));
      broadcast('news', items);
    }
  } catch (e) {
    // Silently ignore - table might not exist yet
  }
}

function broadcast(type, items) {
  const data = JSON.stringify({ type, items });
  const msg = `data: ${data}\n\n`;
  for (const client of sseClients) {
    try { client.write(msg); } catch (e) { sseClients.delete(client); }
  }
}

// Start feed polling (public + dashboard)
const feedTimer = setInterval(pollFeed, FEED_POLL_MS);
const dashFeedTimer = setInterval(dashPollFeed, FEED_POLL_MS);
const dashHeartbeatTimer = setInterval(dashHeartbeat, 15000);

// --- Dashboard Route Handler ---
// --- Balance Dashboard API ---
async function handleBalanceRequest(req, res) {
  const url = req.url.split('?')[0];
  const method = req.method;
  const query = new URL(req.url, 'http://localhost').searchParams;

  // CORS preflight
  if (method === 'OPTIONS') {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');
    res.writeHead(204);
    res.end();
    return true;
  }

  // Login endpoint (no auth required)
  if (method === 'POST' && url === '/api/balance/login') {
    const ip = req.headers['x-real-ip'] || req.socket.remoteAddress || 'unknown';
    try {
      if (!checkRateLimit(ip)) {
        console.warn(`[security] Rate limited login attempt from ${ip} (balance)`);
        sendJson(res, 429, { error: 'Too many login attempts. Try again later.' });
        return true;
      }
      const body = await readBody(req);
      if (body.username === BALANCE_USER && verifyBalancePassword(body.password)) {
        clearLoginAttempts(ip);
        const isDefault = body.password === BALANCE_DEFAULT_PASS;
        console.log(`[security] Successful balance login from ${ip}`);
        sendJson(res, 200, { token: createBalanceToken(), mustChangePassword: isDefault });
      } else {
        recordFailedLogin(ip);
        console.warn(`[security] Failed balance login from ${ip} (user: ${String(body.username || '').substring(0, 20)})`);
        sendJson(res, 401, { error: 'Invalid credentials' });
      }
    } catch (e) {
      sendJson(res, 400, { error: 'Invalid request' });
    }
    return true;
  }

  // All other balance endpoints require auth
  const authHeader = req.headers['authorization'] || '';
  const token = authHeader.startsWith('Bearer ') ? authHeader.slice(7) : '';
  if (!verifyBalanceToken(token)) {
    sendJson(res, 401, { error: 'Unauthorized' });
    return true;
  }

  // POST /api/balance/change-password
  if (method === 'POST' && url === '/api/balance/change-password') {
    try {
      const body = await readBody(req);
      if (!body.newPassword || body.newPassword.length < 6) {
        sendJson(res, 400, { error: 'Password must be at least 6 characters' });
        return true;
      }
      if (setBalancePasswordHash(hashPassword(body.newPassword))) {
        sendJson(res, 200, { success: true });
      } else {
        sendJson(res, 500, { error: 'Failed to save password' });
      }
    } catch (e) {
      sendJson(res, 400, { error: 'Invalid request' });
    }
    return true;
  }

  if (!db) {
    sendJson(res, 503, { error: 'Database not available' });
    return true;
  }

  // GET /api/balance/overview
  if (method === 'GET' && url === '/api/balance/overview') {
    try {
      const total = db.prepare('SELECT COUNT(*) as c FROM combat_events').get();
      const victories = db.prepare("SELECT COUNT(*) as c FROM combat_events WHERE outcome = 'victory'").get();
      const deaths = db.prepare("SELECT COUNT(*) as c FROM combat_events WHERE outcome = 'death'").get();
      const fled = db.prepare("SELECT COUNT(*) as c FROM combat_events WHERE outcome = 'fled'").get();
      const avgRounds = db.prepare("SELECT AVG(rounds) as v FROM combat_events WHERE outcome = 'victory'").get();
      const avgDmg = db.prepare("SELECT AVG(damage_dealt) as v FROM combat_events WHERE outcome = 'victory'").get();
      const today = db.prepare("SELECT COUNT(DISTINCT player_name) as c FROM combat_events WHERE created_at >= datetime('now', '-24 hours')").get();
      const oneHitKills = db.prepare("SELECT COUNT(*) as c FROM combat_events WHERE rounds <= 1 AND outcome = 'victory'").get();
      const oneHitDeaths = db.prepare("SELECT COUNT(*) as c FROM combat_events WHERE rounds <= 1 AND outcome = 'death'").get();
      sendJson(res, 200, {
        totalCombats: total.c,
        victories: victories.c,
        deaths: deaths.c,
        fled: fled.c,
        winRate: total.c > 0 ? (victories.c / total.c * 100).toFixed(1) : 0,
        avgRounds: avgRounds.v ? avgRounds.v.toFixed(1) : 0,
        avgDamage: avgDmg.v ? Math.round(avgDmg.v) : 0,
        activePlayers24h: today.c,
        oneHitKills: oneHitKills.c,
        oneHitDeaths: oneHitDeaths.c
      });
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // GET /api/balance/class-performance
  if (method === 'GET' && url === '/api/balance/class-performance') {
    try {
      const rows = db.prepare(`
        SELECT player_class,
          COUNT(*) as total,
          SUM(CASE WHEN outcome = 'victory' THEN 1 ELSE 0 END) as wins,
          SUM(CASE WHEN outcome = 'death' THEN 1 ELSE 0 END) as deaths,
          SUM(CASE WHEN outcome = 'fled' THEN 1 ELSE 0 END) as fled,
          AVG(CASE WHEN outcome = 'victory' THEN damage_dealt END) as avg_damage,
          AVG(CASE WHEN outcome = 'victory' THEN xp_gained END) as avg_xp,
          AVG(CASE WHEN outcome = 'victory' THEN gold_gained END) as avg_gold,
          AVG(CASE WHEN outcome = 'victory' THEN rounds END) as avg_rounds,
          MAX(damage_dealt) as max_damage
        FROM combat_events
        GROUP BY player_class
        ORDER BY total DESC
      `).all();
      sendJson(res, 200, rows.map(r => ({
        ...r,
        winRate: r.total > 0 ? (r.wins / r.total * 100).toFixed(1) : 0,
        deathRate: r.total > 0 ? (r.deaths / r.total * 100).toFixed(1) : 0,
        avg_damage: r.avg_damage ? Math.round(r.avg_damage) : 0,
        avg_xp: r.avg_xp ? Math.round(r.avg_xp) : 0,
        avg_gold: r.avg_gold ? Math.round(r.avg_gold) : 0,
        avg_rounds: r.avg_rounds ? r.avg_rounds.toFixed(1) : 0,
      })));
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // GET /api/balance/one-hit-kills
  if (method === 'GET' && url === '/api/balance/one-hit-kills') {
    try {
      const rows = db.prepare(`
        SELECT * FROM combat_events
        WHERE rounds <= 1 AND (outcome = 'victory' OR outcome = 'death')
        ORDER BY created_at DESC
        LIMIT 200
      `).all();
      sendJson(res, 200, rows);
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // GET /api/balance/death-hotspots
  if (method === 'GET' && url === '/api/balance/death-hotspots') {
    try {
      const byMonster = db.prepare(`
        SELECT monster_name, monster_level, COUNT(*) as deaths,
          AVG(player_level) as avg_player_level,
          AVG(damage_taken) as avg_damage_taken
        FROM combat_events WHERE outcome = 'death' AND monster_name IS NOT NULL
        GROUP BY monster_name ORDER BY deaths DESC LIMIT 30
      `).all();
      const byFloor = db.prepare(`
        SELECT dungeon_floor, COUNT(*) as deaths,
          AVG(player_level) as avg_player_level
        FROM combat_events WHERE outcome = 'death' AND dungeon_floor > 0
        GROUP BY dungeon_floor ORDER BY dungeon_floor
      `).all();
      const byClass = db.prepare(`
        SELECT player_class, COUNT(*) as deaths
        FROM combat_events WHERE outcome = 'death'
        GROUP BY player_class ORDER BY deaths DESC
      `).all();
      sendJson(res, 200, { byMonster, byFloor, byClass });
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // GET /api/balance/boss-fights
  if (method === 'GET' && url === '/api/balance/boss-fights') {
    try {
      const rows = db.prepare(`
        SELECT * FROM combat_events WHERE is_boss = 1
        ORDER BY created_at DESC LIMIT 100
      `).all();
      sendJson(res, 200, rows);
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // GET /api/balance/player-activity?player=name
  if (method === 'GET' && url === '/api/balance/player-activity') {
    const player = query.get('player');
    if (!player) {
      // Return player summary list
      try {
        const rows = db.prepare(`
          SELECT player_name, player_class, MAX(player_level) as max_level,
            COUNT(*) as total_combats,
            SUM(CASE WHEN outcome = 'victory' THEN 1 ELSE 0 END) as wins,
            SUM(CASE WHEN outcome = 'death' THEN 1 ELSE 0 END) as deaths,
            SUM(xp_gained) as total_xp,
            SUM(gold_gained) as total_gold,
            MAX(created_at) as last_combat
          FROM combat_events
          GROUP BY player_name
          ORDER BY total_combats DESC
        `).all();
        sendJson(res, 200, rows);
      } catch (e) {
        sendJson(res, 500, { error: e.message });
      }
    } else {
      try {
        const rows = db.prepare(`
          SELECT * FROM combat_events WHERE player_name = ?
          ORDER BY created_at DESC LIMIT 200
        `).all(player);
        sendJson(res, 200, rows);
      } catch (e) {
        sendJson(res, 500, { error: e.message });
      }
    }
    return true;
  }

  // GET /api/balance/xp-economy
  if (method === 'GET' && url === '/api/balance/xp-economy') {
    try {
      const rows = db.prepare(`
        SELECT player_level,
          AVG(xp_gained) as avg_xp,
          AVG(gold_gained) as avg_gold,
          COUNT(*) as combats,
          AVG(rounds) as avg_rounds
        FROM combat_events WHERE outcome = 'victory'
        GROUP BY player_level ORDER BY player_level
      `).all();
      sendJson(res, 200, rows);
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // GET /api/balance/recent
  if (method === 'GET' && url === '/api/balance/recent') {
    try {
      const rows = db.prepare(`
        SELECT * FROM combat_events ORDER BY created_at DESC LIMIT 100
      `).all();
      sendJson(res, 200, rows);
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // GET /api/balance/suspects
  if (method === 'GET' && url === '/api/balance/suspects') {
    try {
      const rows = db.prepare(`
        SELECT player_name, player_class, MAX(player_level) as max_level,
          COUNT(*) as total,
          SUM(CASE WHEN outcome = 'victory' THEN 1 ELSE 0 END) as wins,
          ROUND(SUM(CASE WHEN outcome = 'victory' THEN 1.0 ELSE 0 END) / COUNT(*) * 100, 1) as win_pct,
          MAX(damage_dealt) as max_damage,
          AVG(CASE WHEN outcome = 'victory' THEN damage_dealt END) as avg_damage,
          AVG(CASE WHEN outcome = 'victory' THEN xp_gained END) as avg_xp,
          SUM(CASE WHEN rounds <= 1 AND outcome = 'victory' THEN 1 ELSE 0 END) as one_hit_kills
        FROM combat_events
        GROUP BY player_name
        HAVING total >= 10
        ORDER BY win_pct DESC, avg_damage DESC
      `).all();
      sendJson(res, 200, rows);
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  sendJson(res, 404, { error: 'Not found' });
  return true;
}

async function handleDashRequest(req, res) {
  const url = req.url;
  const method = req.method;

  // Dashboard SSE feed
  if (method === 'GET' && url === '/api/dash/feed') {
    res.setHeader('Content-Type', 'text/event-stream');
    res.setHeader('Cache-Control', 'no-cache');
    res.setHeader('Connection', 'keep-alive');
    res.setHeader('X-Accel-Buffering', 'no');
    res.writeHead(200);
    res.write('retry: 5000\n\n');

    dashSseClients.add(res);
    console.log(`[usurper-web] Dashboard SSE connected (${dashSseClients.size} total)`);

    // Send initial snapshot immediately
    try {
      const npcs = getDashNpcs();
      if (npcs && npcs.length > 0) {
        const snapshot = npcs.map(n => ({
          name: n.name || n.Name,
          level: n.level || n.Level || 1,
          location: n.location || n.Location || 'Unknown',
          alive: !(n.isDead || n.IsDead),
          married: !!(n.isMarried || n.IsMarried),
          faction: n.npcFaction !== undefined ? n.npcFaction : (n.NPCFaction !== undefined ? n.NPCFaction : -1),
          isKing: !!(n.isKing || n.IsKing),
          pregnant: !!(n.pregnancyDueDate || n.PregnancyDueDate),
          emotion: n.emotionalState || n.EmotionalState || null,
          hp: n.hp || n.HP || 0,
          maxHp: n.maxHP || n.MaxHP || 1
        }));
        const summary = getDashSummary();
        res.write(`event: npc-snapshot\ndata: ${JSON.stringify({ npcs: snapshot, summary })}\n\n`);
      }
    } catch (e) {}

    req.on('close', () => {
      dashSseClients.delete(res);
      console.log(`[usurper-web] Dashboard SSE disconnected (${dashSseClients.size} total)`);
    });
    return true;
  }

  // Data endpoints
  if (method === 'GET' && url === '/api/dash/npcs') {
    const npcs = getDashNpcs();
    sendJson(res, 200, npcs);
    return true;
  }

  if (method === 'GET' && url === '/api/dash/summary') {
    const summary = getDashSummary();
    sendJson(res, 200, summary);
    return true;
  }

  if (method === 'GET' && url === '/api/dash/events') {
    const events = getDashEvents();
    sendJson(res, 200, events);
    return true;
  }

  if (method === 'GET' && url === '/api/dash/events/hourly') {
    const hourly = getDashEventsHourly();
    sendJson(res, 200, hourly);
    return true;
  }

  return false; // Not a dash route
}

// --- Admin Dashboard API ---
const ADMIN_SERVICES = ['usurper-mud', 'sshd-usurper', 'usurper-web', 'nginx'];

async function getAdminOverview() {
  const now = Date.now();
  if (adminOverviewCache && (now - adminOverviewCacheTime) < 15000) return adminOverviewCache;

  // Server metrics from Node.js builtins
  const memUsage = process.memoryUsage();
  const totalMem = os.totalmem();
  const freeMem = os.freemem();
  const usedMem = totalMem - freeMem;

  const server = {
    processUptime: Math.floor(process.uptime()),
    systemUptime: Math.floor(os.uptime()),
    platform: os.platform(),
    nodeVersion: process.version,
    memoryRssMB: Math.round(memUsage.rss / 1024 / 1024),
    memoryTotalMB: Math.round(totalMem / 1024 / 1024),
    memoryFreeMB: Math.round(freeMem / 1024 / 1024),
    memoryUsedMB: Math.round(usedMem / 1024 / 1024),
    memoryPercent: Math.round((usedMem / totalMem) * 100),
    cpuLoad: os.loadavg(),
    cpuCount: os.cpus().length,
    hostname: os.hostname()
  };

  // Disk usage
  let disk = { total: 0, used: 0, available: 0, percent: 0 };
  try {
    const dfOut = await execFileAsync('df', ['-B1', '/']);
    const lines = dfOut.trim().split('\n');
    if (lines.length >= 2) {
      const parts = lines[1].split(/\s+/);
      const total = parseInt(parts[1]) || 0;
      const used = parseInt(parts[2]) || 0;
      const avail = parseInt(parts[3]) || 0;
      disk = {
        totalGB: (total / 1073741824).toFixed(1),
        usedGB: (used / 1073741824).toFixed(1),
        availableGB: (avail / 1073741824).toFixed(1),
        percent: total > 0 ? Math.round((used / total) * 100) : 0
      };
    }
  } catch (e) { /* df not available */ }

  // Player metrics from DB
  let players = { online: 0, peakOnline: peakOnlinePlayers, peakOnlineTime: peakOnlinePlayersTime,
    sessionPeak: sessionPeakOnline, sessionPeakTime: sessionPeakTime,
    totalRegistered: 0, totalSleeping: 0, newToday: 0, activeLast24h: 0, activeLast7d: 0, banned: 0 };
  if (db) {
    try {
      const onlineRow = db.prepare(
        "SELECT COUNT(*) as cnt FROM online_players WHERE last_heartbeat >= datetime('now', '-120 seconds')"
      ).get();
      players.online = onlineRow ? onlineRow.cnt : 0;

      const totalRow = db.prepare(
        "SELECT COUNT(*) as cnt FROM players WHERE username NOT LIKE 'emergency_%'"
      ).get();
      players.totalRegistered = totalRow ? totalRow.cnt : 0;

      const sleepRow = db.prepare("SELECT COUNT(*) as cnt FROM sleeping_players").get();
      players.totalSleeping = sleepRow ? sleepRow.cnt : 0;

      const newRow = db.prepare(
        "SELECT COUNT(*) as cnt FROM players WHERE created_at >= datetime('now', '-24 hours') AND username NOT LIKE 'emergency_%'"
      ).get();
      players.newToday = newRow ? newRow.cnt : 0;

      const active24Row = db.prepare(
        "SELECT COUNT(*) as cnt FROM players WHERE last_login >= datetime('now', '-24 hours') AND username NOT LIKE 'emergency_%'"
      ).get();
      players.activeLast24h = active24Row ? active24Row.cnt : 0;

      const active7dRow = db.prepare(
        "SELECT COUNT(*) as cnt FROM players WHERE last_login >= datetime('now', '-7 days') AND username NOT LIKE 'emergency_%'"
      ).get();
      players.activeLast7d = active7dRow ? active7dRow.cnt : 0;

      const bannedRow = db.prepare("SELECT COUNT(*) as cnt FROM players WHERE is_banned = 1").get();
      players.banned = bannedRow ? bannedRow.cnt : 0;
    } catch (e) {
      console.error(`[usurper-web] Admin player query error: ${e.message}`);
    }
  }

  // Web proxy stats
  const webProxy = {
    startTime: new Date(Date.now() - process.uptime() * 1000).toISOString(),
    wsConnections: wss ? wss.clients.size : 0,
    sseClients: sseClients ? sseClients.size : 0,
    dashSseClients: dashSseClients ? dashSseClients.size : 0
  };

  adminOverviewCache = { server, disk, players, webProxy };
  adminOverviewCacheTime = now;
  return adminOverviewCache;
}

async function getAdminServices() {
  const now = Date.now();
  if (adminServicesCache && (now - adminServicesCacheTime) < 30000) return adminServicesCache;

  const services = [];
  for (const name of ADMIN_SERVICES) {
    try {
      const out = await execFileAsync('systemctl', [
        'show', name,
        '--property=ActiveState,MainPID,MemoryCurrent,ExecMainStartTimestamp'
      ]);
      const props = {};
      for (const line of out.trim().split('\n')) {
        const [k, ...v] = line.split('=');
        props[k] = v.join('=');
      }

      const startTs = props.ExecMainStartTimestamp || '';
      let uptimeStr = 'unknown';
      if (startTs && startTs !== '') {
        // Parse systemd timestamp like "Mon 2026-03-03 10:00:00 UTC"
        const startDate = new Date(startTs.replace(/^\w+ /, ''));
        if (!isNaN(startDate.getTime())) {
          const secs = Math.floor((Date.now() - startDate.getTime()) / 1000);
          const d = Math.floor(secs / 86400);
          const h = Math.floor((secs % 86400) / 3600);
          const m = Math.floor((secs % 3600) / 60);
          uptimeStr = d > 0 ? `${d}d ${h}h` : h > 0 ? `${h}h ${m}m` : `${m}m`;
        }
      }

      const memBytes = parseInt(props.MemoryCurrent) || 0;
      const memStr = memBytes > 0 ? `${Math.round(memBytes / 1048576)}M` : 'N/A';

      services.push({
        name,
        status: props.ActiveState || 'unknown',
        pid: parseInt(props.MainPID) || 0,
        memory: memStr,
        uptime: uptimeStr
      });
    } catch (e) {
      services.push({ name, status: 'unknown', pid: 0, memory: 'N/A', uptime: 'unknown' });
    }
  }

  adminServicesCache = services;
  adminServicesCacheTime = now;
  return services;
}

async function getAdminDatabase() {
  const now = Date.now();
  if (adminDbCache && (now - adminDbCacheTime) < 60000) return adminDbCache;

  const result = { path: DB_PATH, sizeBytes: 0, sizeMB: '0', walSizeMB: '0', tables: {}, integrityCheck: 'unknown' };

  // File size
  try {
    const stat = fs.statSync(DB_PATH);
    result.sizeBytes = stat.size;
    result.sizeMB = (stat.size / 1048576).toFixed(1);
  } catch (e) { /* file may not exist locally */ }

  // WAL size
  try {
    const walStat = fs.statSync(DB_PATH + '-wal');
    result.walSizeMB = (walStat.size / 1048576).toFixed(1);
  } catch (e) { result.walSizeMB = '0'; }

  if (db) {
    // Table counts
    try {
      const tables = db.prepare("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").all();
      for (const t of tables) {
        try {
          const row = db.prepare(`SELECT COUNT(*) as cnt FROM "${t.name}"`).get();
          result.tables[t.name] = row ? row.cnt : 0;
        } catch (e) { result.tables[t.name] = -1; }
      }
    } catch (e) { /* schema query failed */ }

    // Integrity check (fast mode)
    try {
      const row = db.prepare("PRAGMA integrity_check(1)").get();
      result.integrityCheck = row ? Object.values(row)[0] : 'unknown';
    } catch (e) { result.integrityCheck = 'error'; }
  }

  adminDbCache = result;
  adminDbCacheTime = now;
  return result;
}

async function getAdminSsl() {
  const now = Date.now();
  if (adminSslCache && (now - adminSslCacheTime) < 3600000) return adminSslCache;

  const result = { domain: 'usurper-reborn.net', expiresAt: null, daysRemaining: null, issuer: null };

  function parseSslOutput(out) {
    for (const line of out.trim().split('\n')) {
      if (line.includes('notAfter=')) {
        const dateStr = line.split('notAfter=')[1].trim();
        const expDate = new Date(dateStr);
        if (!isNaN(expDate.getTime())) {
          result.expiresAt = expDate.toISOString();
          result.daysRemaining = Math.floor((expDate.getTime() - Date.now()) / 86400000);
        }
      }
      if (line.includes('issuer=') || line.includes('issuer =')) {
        const cn = line.match(/CN\s*=\s*([^,/\n]+)/);
        result.issuer = cn ? cn[1].trim() : null;
      }
    }
  }

  // Method 1: Read cert file directly (needs root access)
  try {
    const certPath = '/etc/letsencrypt/live/usurper-reborn.net/cert.pem';
    const out = await execFileAsync('openssl', ['x509', '-enddate', '-issuer', '-noout', '-in', certPath]);
    parseSslOutput(out);
  } catch (e) { /* permission denied — expected for usurper user */ }

  // Method 2: Connect to local HTTPS and read cert via Node.js TLS
  if (!result.expiresAt) {
    try {
      const tls = require('tls');
      const cert = await new Promise((resolve, reject) => {
        const sock = tls.connect(443, 'localhost', { servername: 'usurper-reborn.net', rejectUnauthorized: false }, () => {
          const peerCert = sock.getPeerCertificate();
          sock.destroy();
          resolve(peerCert);
        });
        sock.setTimeout(5000, () => { sock.destroy(); reject(new Error('timeout')); });
        sock.on('error', reject);
      });
      if (cert && cert.valid_to) {
        result.expiresAt = new Date(cert.valid_to).toISOString();
        result.daysRemaining = Math.floor((new Date(cert.valid_to) - Date.now()) / 86400000);
        result.issuer = cert.issuer && cert.issuer.O ? cert.issuer.O : (cert.issuer ? JSON.stringify(cert.issuer) : 'Unknown');
      }
    } catch (e2) { /* both methods failed */ }
  }

  adminSslCache = result;
  adminSslCacheTime = now;
  return result;
}

function getAdminActivity() {
  if (!db) return { recentLogins: [], recentNews: [], onlinePlayers: [] };

  let recentLogins = [];
  try {
    recentLogins = db.prepare(`
      SELECT display_name, last_login,
             json_extract(player_data, '$.player.level') as level,
             json_extract(player_data, '$.player.class') as class_id
      FROM players WHERE username NOT LIKE 'emergency_%'
      ORDER BY last_login DESC LIMIT 20
    `).all().map(r => ({
      name: r.display_name,
      time: r.last_login,
      level: r.level || 1,
      className: CLASS_NAMES[r.class_id] || 'Unknown'
    }));
  } catch (e) { /* query failed */ }

  let recentNews = [];
  try {
    recentNews = db.prepare(`
      SELECT message, category, created_at
      FROM news ORDER BY created_at DESC LIMIT 30
    `).all().map(r => ({
      message: r.message,
      category: r.category,
      time: r.created_at
    }));
  } catch (e) { /* news table may not exist */ }

  let onlinePlayers = [];
  try {
    onlinePlayers = db.prepare(`
      SELECT op.username, op.display_name, op.location, op.connected_at,
             json_extract(p.player_data, '$.player.level') as level,
             json_extract(p.player_data, '$.player.class') as class_id,
             COALESCE(op.connection_type, 'Unknown') as connection_type,
             COALESCE(op.ip_address, '') as ip_address
      FROM online_players op
      LEFT JOIN players p ON LOWER(op.username) = LOWER(p.username)
      WHERE op.last_heartbeat >= datetime('now', '-120 seconds')
      ORDER BY op.display_name
    `).all().map(r => ({
      name: r.display_name || r.username,
      level: r.level || 1,
      className: CLASS_NAMES[r.class_id] || 'Unknown',
      location: r.location || 'Unknown',
      connectionType: r.connection_type,
      connectedAt: r.connected_at,
      ip: r.ip_address || ''
    }));
  } catch (e) { /* query failed */ }

  return { recentLogins, recentNews, onlinePlayers };
}

function getAdminVersion() {
  const binaryPath = '/opt/usurper/UsurperReborn';
  const result = { binaryPath, sizeMB: null, modifiedAt: null, version: null };

  try {
    const stat = fs.statSync(binaryPath);
    result.sizeMB = (stat.size / 1048576).toFixed(1);
    result.modifiedAt = stat.mtime.toISOString();
  } catch (e) { /* binary not found (running locally) */ }

  // Read version from version.txt (written during deployment)
  try {
    const versionPath = '/opt/usurper/version.txt';
    result.version = fs.readFileSync(versionPath, 'utf8').trim();
  } catch (e) { /* version.txt not found */ }

  // Fallback: check the DLL for embedded version string
  if (!result.version) {
    try {
      const dllPath = '/opt/usurper/UsurperReborn.dll';
      const buf = fs.readFileSync(dllPath);
      const str = buf.toString('utf8', 0, Math.min(buf.length, 500000));
      const match = str.match(/0\.\d+\.\d+(?:-[a-zA-Z0-9]+)?/);
      if (match) result.version = match[0];
    } catch (e) { /* DLL not found or too large */ }
  }

  return result;
}

async function handleAdminRequest(req, res) {
  const url = req.url.split('?')[0];
  const method = req.method;

  // CORS preflight
  if (method === 'OPTIONS') {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, DELETE, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');
    res.writeHead(204);
    res.end();
    return true;
  }

  // Login endpoint (no auth required) — shares credentials with balance dashboard
  if (method === 'POST' && url === '/api/admin/login') {
    const ip = req.headers['x-real-ip'] || req.socket.remoteAddress || 'unknown';
    try {
      if (!checkRateLimit(ip)) {
        console.warn(`[security] Rate limited login attempt from ${ip} (admin)`);
        sendJson(res, 429, { error: 'Too many login attempts. Try again later.' });
        return true;
      }
      const body = await readBody(req);
      if (body.username === BALANCE_USER && verifyBalancePassword(body.password)) {
        clearLoginAttempts(ip);
        const isDefault = body.password === BALANCE_DEFAULT_PASS;
        console.log(`[security] Successful admin login from ${ip}`);
        sendJson(res, 200, { token: createBalanceToken(), mustChangePassword: isDefault });
      } else {
        recordFailedLogin(ip);
        console.warn(`[security] Failed admin login from ${ip} (user: ${String(body.username || '').substring(0, 20)})`);
        sendJson(res, 401, { error: 'Invalid credentials' });
      }
    } catch (e) {
      sendJson(res, 400, { error: 'Invalid request' });
    }
    return true;
  }

  // All other admin endpoints require auth (accepts balance tokens)
  // Accept token from Authorization header OR query string (for EventSource/SSE which can't send headers)
  const authHeader = req.headers['authorization'] || '';
  let token = authHeader.startsWith('Bearer ') ? authHeader.slice(7) : '';
  if (!token) {
    try { token = new URL(req.url, 'http://localhost').searchParams.get('token') || ''; } catch {}
  }
  if (!verifyBalanceToken(token)) {
    sendJson(res, 401, { error: 'Unauthorized' });
    return true;
  }

  // POST /api/admin/change-password — changes balance password (shared)
  if (method === 'POST' && url === '/api/admin/change-password') {
    try {
      const body = await readBody(req);
      if (!body.newPassword || body.newPassword.length < 6) {
        sendJson(res, 400, { error: 'Password must be at least 6 characters' });
        return true;
      }
      if (setBalancePasswordHash(hashPassword(body.newPassword))) {
        sendJson(res, 200, { success: true });
      } else {
        sendJson(res, 500, { error: 'Failed to save password' });
      }
    } catch (e) {
      sendJson(res, 400, { error: 'Invalid request' });
    }
    return true;
  }

  // GET /api/admin/overview
  if (method === 'GET' && url === '/api/admin/overview') {
    const data = await getAdminOverview();
    sendJson(res, 200, data);
    return true;
  }

  // GET /api/admin/services
  if (method === 'GET' && url === '/api/admin/services') {
    const data = await getAdminServices();
    sendJson(res, 200, { services: data });
    return true;
  }

  // GET /api/admin/database
  if (method === 'GET' && url === '/api/admin/database') {
    const data = await getAdminDatabase();
    sendJson(res, 200, data);
    return true;
  }

  // GET /api/admin/ssl
  if (method === 'GET' && url === '/api/admin/ssl') {
    const data = await getAdminSsl();
    sendJson(res, 200, data);
    return true;
  }

  // GET /api/admin/activity
  if (method === 'GET' && url === '/api/admin/activity') {
    const data = getAdminActivity();
    sendJson(res, 200, data);
    return true;
  }

  // GET /api/admin/version
  if (method === 'GET' && url === '/api/admin/version') {
    const data = getAdminVersion();
    sendJson(res, 200, data);
    return true;
  }

  // POST /api/admin/reset-peak
  if (method === 'POST' && url === '/api/admin/reset-peak') {
    peakOnlinePlayers = 0;
    peakOnlinePlayersTime = null;
    sessionPeakOnline = 0;
    sessionPeakTime = null;
    if (dbWrite) {
      try {
        dbWrite.prepare("INSERT OR REPLACE INTO admin_config (key, value) VALUES (?, ?)")
          .run('peak_online', JSON.stringify({ count: 0, time: null }));
      } catch (e) { /* non-critical */ }
    }
    sendJson(res, 200, { success: true });
    return true;
  }

  // GET /api/admin/geolocate — resolve IPs of online players to lat/lng/country/city
  if (method === 'GET' && url === '/api/admin/geolocate') {
    try {
      const rows = db.prepare(`
        SELECT display_name, ip_address, connection_type,
               json_extract(p.player_data, '$.player.level') as level,
               json_extract(p.player_data, '$.player.class') as class_id
        FROM online_players op
        LEFT JOIN players p ON LOWER(op.username) = LOWER(p.username)
        WHERE op.last_heartbeat >= datetime('now', '-120 seconds')
          AND op.ip_address IS NOT NULL AND op.ip_address != ''
      `).all();

      // Filter out localhost/relay IPs (127.x, ::1) — these are SSH relay connections
      const validRows = rows.filter(r => r.ip_address && !r.ip_address.startsWith('127.') && r.ip_address !== '::1');
      const uniqueIPs = [...new Set(validRows.map(r => r.ip_address))];

      if (uniqueIPs.length === 0) {
        sendJson(res, 200, { players: [] });
        return true;
      }

      // Use ip-api.com batch endpoint (free, no key, 15 req/min for batch)
      const geoRes = await fetch('http://ip-api.com/batch?fields=status,country,city,lat,lon,query', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(uniqueIPs.map(ip => ({ query: ip })))
      });
      const geoData = await geoRes.json();

      // Build IP -> geo lookup
      const geoMap = {};
      for (const g of geoData) {
        if (g.status === 'success') {
          geoMap[g.query] = { lat: g.lat, lon: g.lon, country: g.country, city: g.city };
        }
      }

      // Map players to geo data
      const players = validRows.map(r => ({
        name: r.display_name,
        level: r.level || 1,
        className: CLASS_NAMES[r.class_id] || 'Unknown',
        connectionType: r.connection_type || 'Unknown',
        ip: r.ip_address,
        geo: geoMap[r.ip_address] || null
      })).filter(p => p.geo);

      sendJson(res, 200, { players });
    } catch (e) {
      sendJson(res, 500, { error: 'Geolocation failed: ' + e.message });
    }
    return true;
  }

  // --- Player Management Endpoints ---

  // Parse :username from URL paths like /api/admin/players/SomeName/action
  const playerMatch = url.match(/^\/api\/admin\/players\/([^/]+)(\/.*)?$/);
  const playerUsername = playerMatch ? decodeURIComponent(playerMatch[1]) : null;
  const playerAction = playerMatch ? (playerMatch[2] || '') : '';

  // GET /api/admin/players — list with search/filter/pagination
  if (method === 'GET' && url.startsWith('/api/admin/players') && !playerMatch) {
    try {
      const params = new URL(req.url, 'http://localhost').searchParams;
      const search = params.get('search') || '';
      const filter = params.get('filter') || 'all'; // all, online, banned, frozen, muted
      const page = Math.max(1, parseInt(params.get('page')) || 1);
      const limit = Math.min(100, Math.max(10, parseInt(params.get('limit')) || 25));
      const offset = (page - 1) * limit;

      let where = "WHERE p.username NOT LIKE 'emergency_%'";
      const args = [];
      if (search) {
        where += " AND (LOWER(p.username) LIKE ? OR LOWER(p.display_name) LIKE ?)";
        args.push(`%${search.toLowerCase()}%`, `%${search.toLowerCase()}%`);
      }
      if (filter === 'online') where += " AND o.username IS NOT NULL";
      if (filter === 'banned') where += " AND p.is_banned = 1";
      if (filter === 'frozen') where += " AND json_extract(p.player_data, '$.player.isFrozen') = 1";
      if (filter === 'muted') where += " AND json_extract(p.player_data, '$.player.isMuted') = 1";

      const countRow = db.prepare(`SELECT COUNT(*) as total FROM players p LEFT JOIN online_players o ON LOWER(p.username) = LOWER(o.username) ${where}`).get(...args);

      const rows = db.prepare(`
        SELECT p.username, p.display_name, p.is_banned, p.last_login, p.created_at, p.total_playtime_minutes,
               json_extract(p.player_data, '$.player.level') as level,
               json_extract(p.player_data, '$.player.class') as class_id,
               json_extract(p.player_data, '$.player.gold') as gold,
               json_extract(p.player_data, '$.player.hp') as hp,
               json_extract(p.player_data, '$.player.maxHP') as maxHp,
               json_extract(p.player_data, '$.player.isFrozen') as is_frozen,
               json_extract(p.player_data, '$.player.isMuted') as is_muted,
               json_extract(p.player_data, '$.player.race') as race_id,
               o.username as online_username, o.location as online_location, o.connection_type
        FROM players p
        LEFT JOIN online_players o ON LOWER(p.username) = LOWER(o.username)
        ${where}
        ORDER BY CASE WHEN o.username IS NOT NULL THEN 0 ELSE 1 END, p.last_login DESC
        LIMIT ? OFFSET ?
      `).all(...args, limit, offset);

      const players = rows.map(r => ({
        username: r.username,
        displayName: r.display_name,
        level: r.level || 1,
        className: CLASS_NAMES[r.class_id] || 'Unknown',
        raceName: RACE_NAMES[r.race_id] || 'Unknown',
        gold: r.gold || 0,
        hp: r.hp || 0,
        maxHp: r.maxHp || 0,
        isOnline: !!r.online_username,
        isBanned: !!r.is_banned,
        isFrozen: !!r.is_frozen,
        isMuted: !!r.is_muted,
        location: r.online_location || null,
        connectionType: r.connection_type || null,
        lastLogin: r.last_login,
        createdAt: r.created_at,
        playtimeMinutes: r.total_playtime_minutes || 0,
      }));

      sendJson(res, 200, { players, total: countRow.total, page, limit, totalPages: Math.ceil(countRow.total / limit) });
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // GET /api/admin/players/:username — full player detail
  if (method === 'GET' && playerUsername && playerAction === '') {
    try {
      const row = db.prepare("SELECT * FROM players WHERE LOWER(username) = LOWER(?)").get(playerUsername);
      if (!row) { sendJson(res, 404, { error: 'Player not found' }); return true; }

      const online = db.prepare("SELECT * FROM online_players WHERE LOWER(username) = LOWER(?)").get(playerUsername);
      let data = {};
      try { data = JSON.parse(row.player_data || '{}'); } catch (e) { /* empty */ }
      const p = data.player || {};

      const detail = {
        username: row.username,
        displayName: row.display_name,
        isOnline: !!online,
        onlineLocation: online ? online.location : null,
        connectionType: online ? online.connection_type : null,
        isBanned: !!row.is_banned,
        banReason: row.ban_reason,
        lastLogin: row.last_login,
        lastLogout: row.last_logout,
        createdAt: row.created_at,
        playtimeMinutes: row.total_playtime_minutes || 0,
        stats: {
          level: p.level || 1,
          experience: p.experience || 0,
          className: CLASS_NAMES[p.class] || 'Unknown',
          classId: p.class || 0,
          raceName: RACE_NAMES[p.race] || 'Unknown',
          raceId: p.race || 0,
          hp: p.hp || 0, maxHP: p.maxHP || 0,
          mana: p.mana || 0, maxMana: p.maxMana || 0,
          stamina: p.stamina || 0, maxStamina: p.maxStamina || 100,
          strength: p.strength || 0, defense: p.defense || 0,
          agility: p.agility || 0, dexterity: p.dexterity || 0,
          constitution: p.constitution || 0, intelligence: p.intelligence || 0,
          wisdom: p.wisdom || 0, charisma: p.charisma || 0,
          fatigue: p.fatigue || 0,
        },
        resources: {
          gold: p.gold || 0, bankGold: p.bankGold || 0,
          potions: p.potions || 0, lockpicks: p.lockpicks || 0,
          herbs: {
            healingHerb: p.healingHerb || 0, ironbarkRoot: p.ironbarkRoot || 0,
            firebloomPetal: p.firebloomPetal || 0, swiftthistle: p.swiftthistle || 0,
            starbloomEssence: p.starbloomEssence || 0,
          },
        },
        social: {
          alignment: p.alignment || 'Neutral',
          chivalry: p.chivalry || 0, darkness: p.darkness || 0,
          isKing: p.isKing || false, kingName: p.kingName || null,
          daysInPrison: p.daysInPrison || 0,
          murderWeight: p.murderWeight || 0,
          teamName: p.teamName || null,
          faction: FACTION_NAMES[String(p.faction)] || FACTION_NAMES['-1'],
          factionId: p.faction ?? -1,
          isMarried: p.isMarried || false,
          spouseName: p.spouseName || null,
        },
        story: {
          awakeningLevel: p.awakeningLevel || 0,
          cycleNumber: p.cycleNumber || 1,
          cycleExpMultiplier: p.cycleExpMultiplier || 1.0,
          isImmortal: p.isImmortal || false,
          divineName: p.divineName || null,
          godLevel: p.godLevel || 0,
          completedEndings: p.completedEndings || [],
          collectedSeals: data.collectedSeals || [],
          heardLoreSongs: p.heardLoreSongs || [],
        },
        daily: {
          fightCount: p.fightCount || 0,
          thieveryCount: p.thieveryCount || 0,
          brawlCount: p.brawlCount || 0,
          questsCompletedToday: p.questsCompletedToday || 0,
          homeRestsToday: p.homeRestsToday || 0,
          herbsGatheredToday: p.herbsGatheredToday || 0,
          pvpAttacksToday: p.pvpAttacksToday || 0,
          gameTimeMinutes: p.gameTimeMinutes || 0,
        },
        settings: {
          autoHeal: p.autoHeal ?? true,
          compactMode: p.compactMode || false,
          combatSpeed: p.combatSpeed || 'normal',
          isFrozen: p.isFrozen || false,
          isMuted: p.isMuted || false,
        },
        statistics: p.statistics || {},
        equipment: p.equipment || {},
        inventory: p.inventory || [],
        spells: p.spells || [],
        abilities: p.abilities || [],
        companions: data.companions || [],
      };

      sendJson(res, 200, detail);
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // POST /api/admin/players/:username/edit — dot-path field editing
  if (method === 'POST' && playerUsername && playerAction === '/edit') {
    try {
      const body = await readBody(req);
      if (!body.changes || typeof body.changes !== 'object') {
        sendJson(res, 400, { error: 'Missing changes object' }); return true;
      }

      const row = dbWrite.prepare("SELECT player_data FROM players WHERE LOWER(username) = LOWER(?)").get(playerUsername);
      if (!row) { sendJson(res, 404, { error: 'Player not found' }); return true; }

      const online = db.prepare("SELECT username FROM online_players WHERE LOWER(username) = LOWER(?)").get(playerUsername);
      let data = JSON.parse(row.player_data || '{}');

      // Apply dot-path changes (e.g., "player.gold" -> data.player.gold = value)
      for (const [path, value] of Object.entries(body.changes)) {
        const parts = path.split('.');
        let obj = data;
        for (let i = 0; i < parts.length - 1; i++) {
          if (obj[parts[i]] === undefined) obj[parts[i]] = {};
          obj = obj[parts[i]];
        }
        obj[parts[parts.length - 1]] = value;
      }

      dbWrite.prepare("UPDATE players SET player_data = ? WHERE LOWER(username) = LOWER(?)").run(JSON.stringify(data), playerUsername);

      // Log the edit
      try {
        dbWrite.prepare("INSERT INTO wizard_log (wizard_name, action, target, details, created_at) VALUES (?, ?, ?, ?, datetime('now'))")
          .run('admin-web', 'edit_player', playerUsername, JSON.stringify(Object.keys(body.changes)));
      } catch (e) { /* non-critical */ }

      sendJson(res, 200, { success: true, isOnline: !!online, warning: online ? 'Player is online — changes may be overwritten by autosave. Consider kicking the player first.' : null });
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // GET /api/admin/players/:username/raw — raw JSON blob
  if (method === 'GET' && playerUsername && playerAction === '/raw') {
    try {
      const row = db.prepare("SELECT player_data FROM players WHERE LOWER(username) = LOWER(?)").get(playerUsername);
      if (!row) { sendJson(res, 404, { error: 'Player not found' }); return true; }
      res.setHeader('Content-Type', 'application/json');
      res.writeHead(200);
      res.end(row.player_data);
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // POST /api/admin/players/:username/raw — replace entire JSON blob
  if (method === 'POST' && playerUsername && playerAction === '/raw') {
    try {
      const body = await readBody(req);
      JSON.parse(JSON.stringify(body)); // validate it's valid JSON
      const exists = dbWrite.prepare("SELECT username FROM players WHERE LOWER(username) = LOWER(?)").get(playerUsername);
      if (!exists) { sendJson(res, 404, { error: 'Player not found' }); return true; }

      dbWrite.prepare("UPDATE players SET player_data = ? WHERE LOWER(username) = LOWER(?)").run(JSON.stringify(body), playerUsername);

      try {
        dbWrite.prepare("INSERT INTO wizard_log (wizard_name, action, target, details, created_at) VALUES (?, ?, ?, ?, datetime('now'))")
          .run('admin-web', 'raw_edit_player', playerUsername, 'Full JSON replacement');
      } catch (e) { /* non-critical */ }

      sendJson(res, 200, { success: true });
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // POST /api/admin/players/:username/ban
  if (method === 'POST' && playerUsername && playerAction === '/ban') {
    try {
      const body = await readBody(req).catch(() => ({}));
      const reason = body.reason || 'Banned by admin';
      dbWrite.prepare("UPDATE players SET is_banned = 1, ban_reason = ? WHERE LOWER(username) = LOWER(?)").run(reason, playerUsername);

      // Queue kick if online
      const online = db.prepare("SELECT username FROM online_players WHERE LOWER(username) = LOWER(?)").get(playerUsername);
      if (online) {
        dbWrite.prepare("INSERT INTO admin_commands (command, target_username, args, created_by) VALUES (?, ?, ?, ?)")
          .run('kick', playerUsername, JSON.stringify({ reason: 'You have been banned: ' + reason }), 'admin-web');
      }

      try {
        dbWrite.prepare("INSERT INTO wizard_log (wizard_name, action, target, details, created_at) VALUES (?, ?, ?, ?, datetime('now'))")
          .run('admin-web', 'ban_player', playerUsername, reason);
      } catch (e) { /* non-critical */ }

      sendJson(res, 200, { success: true, wasOnline: !!online });
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // POST /api/admin/players/:username/unban
  if (method === 'POST' && playerUsername && playerAction === '/unban') {
    try {
      dbWrite.prepare("UPDATE players SET is_banned = 0, ban_reason = NULL WHERE LOWER(username) = LOWER(?)").run(playerUsername);

      try {
        dbWrite.prepare("INSERT INTO wizard_log (wizard_name, action, target, details, created_at) VALUES (?, ?, ?, ?, datetime('now'))")
          .run('admin-web', 'unban_player', playerUsername, '');
      } catch (e) { /* non-critical */ }

      sendJson(res, 200, { success: true });
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // POST /api/admin/players/:username/reset-password
  if (method === 'POST' && playerUsername && playerAction === '/reset-password') {
    try {
      const body = await readBody(req);
      if (!body.newPassword || body.newPassword.length < 4) {
        sendJson(res, 400, { error: 'Password must be at least 4 characters' }); return true;
      }
      const hashed = hashPassword(body.newPassword);
      dbWrite.prepare("UPDATE players SET password_hash = ? WHERE LOWER(username) = LOWER(?)").run(hashed, playerUsername);

      try {
        dbWrite.prepare("INSERT INTO wizard_log (wizard_name, action, target, details, created_at) VALUES (?, ?, ?, ?, datetime('now'))")
          .run('admin-web', 'reset_password', playerUsername, '');
      } catch (e) { /* non-critical */ }

      sendJson(res, 200, { success: true });
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // DELETE /api/admin/players/:username
  if (method === 'DELETE' && playerUsername && playerAction === '') {
    try {
      // Kick if online first
      const online = db.prepare("SELECT username FROM online_players WHERE LOWER(username) = LOWER(?)").get(playerUsername);
      if (online) {
        dbWrite.prepare("INSERT INTO admin_commands (command, target_username, args, created_by) VALUES (?, ?, ?, ?)")
          .run('kick', playerUsername, JSON.stringify({ reason: 'Account deleted' }), 'admin-web');
      }

      // Delete from all related tables
      const tables = ['players', 'wizard_flags', 'sleeping_players', 'online_players', 'pvp_log'];
      for (const table of tables) {
        try {
          dbWrite.prepare(`DELETE FROM ${table} WHERE LOWER(username) = LOWER(?)`).run(playerUsername);
        } catch (e) { /* table might not have username column */ }
      }
      // Messages
      try {
        dbWrite.prepare("DELETE FROM messages WHERE LOWER(from_player) = LOWER(?) OR LOWER(to_player) = LOWER(?)").run(playerUsername, playerUsername);
      } catch (e) { /* non-critical */ }

      try {
        dbWrite.prepare("INSERT INTO wizard_log (wizard_name, action, target, details, created_at) VALUES (?, ?, ?, ?, datetime('now'))")
          .run('admin-web', 'delete_player', playerUsername, '');
      } catch (e) { /* non-critical */ }

      sendJson(res, 200, { success: true });
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // POST /api/admin/commands — queue a command for MUD server
  if (method === 'POST' && url === '/api/admin/commands') {
    try {
      const body = await readBody(req);
      if (!body.command) { sendJson(res, 400, { error: 'Missing command' }); return true; }

      const result = dbWrite.prepare("INSERT INTO admin_commands (command, target_username, args, created_by) VALUES (?, ?, ?, ?)")
        .run(body.command, body.target || null, body.args ? JSON.stringify(body.args) : null, 'admin-web');

      sendJson(res, 200, { id: result.lastInsertRowid, status: 'queued' });
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // GET /api/admin/commands/:id — check command status
  const cmdMatch = url.match(/^\/api\/admin\/commands\/(\d+)$/);
  if (method === 'GET' && cmdMatch) {
    try {
      const row = db.prepare("SELECT * FROM admin_commands WHERE id = ?").get(parseInt(cmdMatch[1]));
      if (!row) { sendJson(res, 404, { error: 'Command not found' }); return true; }
      sendJson(res, 200, row);
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // GET /api/admin/snoop/:username — SSE stream of snoop output
  const snoopMatch = url.match(/^\/api\/admin\/snoop\/([^/]+)$/);
  if (method === 'GET' && snoopMatch) {
    const snoopTarget = decodeURIComponent(snoopMatch[1]);
    try {
      // Queue snoop_start command
      const startResult = dbWrite.prepare("INSERT INTO admin_commands (command, target_username, args, created_by) VALUES (?, ?, ?, ?)")
        .run('snoop_start', snoopTarget, null, 'admin-web');

      res.writeHead(200, {
        'Content-Type': 'text/event-stream',
        'Cache-Control': 'no-cache',
        'Connection': 'keep-alive',
        'X-Accel-Buffering': 'no',
      });
      res.write(`data: ${JSON.stringify({ type: 'started', target: snoopTarget, commandId: startResult.lastInsertRowid })}\n\n`);

      let lastSnoopId = 0;
      const snoopInterval = setInterval(() => {
        try {
          const rows = db.prepare("SELECT id, line, created_at FROM snoop_buffer WHERE target_username = ? AND id > ? ORDER BY id LIMIT 50")
            .all(snoopTarget, lastSnoopId);
          for (const row of rows) {
            res.write(`data: ${JSON.stringify({ type: 'output', line: row.line, id: row.id })}\n\n`);
            lastSnoopId = row.id;
          }
        } catch (e) {
          res.write(`data: ${JSON.stringify({ type: 'error', message: e.message })}\n\n`);
        }
      }, 500);

      req.on('close', () => {
        clearInterval(snoopInterval);
        // Queue snoop_stop
        try {
          dbWrite.prepare("INSERT INTO admin_commands (command, target_username, args, created_by) VALUES (?, ?, ?, ?)")
            .run('snoop_stop', snoopTarget, null, 'admin-web');
        } catch (e) { /* best-effort */ }
      });
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  // GET /api/admin/wizard-log — recent audit trail
  if (method === 'GET' && url.startsWith('/api/admin/wizard-log')) {
    try {
      const params = new URL(req.url, 'http://localhost').searchParams;
      const limit = Math.min(200, Math.max(10, parseInt(params.get('limit')) || 50));
      const rows = db.prepare("SELECT * FROM wizard_log ORDER BY id DESC LIMIT ?").all(limit);
      sendJson(res, 200, { entries: rows });
    } catch (e) {
      sendJson(res, 500, { error: e.message });
    }
    return true;
  }

  sendJson(res, 404, { error: 'Not found' });
  return true;
}

// --- HTTP Handler ---
function handleHttpRequest(req, res) {
  // Admin dashboard routes
  if (req.url && req.url.startsWith('/api/admin/')) {
    handleAdminRequest(req, res).catch(err => {
      console.error(`[usurper-web] Admin API error: ${err.message}`);
      if (!res.headersSent) sendJson(res, 500, { error: 'Internal error' });
    });
    return;
  }

  // Balance dashboard routes
  if (req.url && req.url.startsWith('/api/balance/')) {
    handleBalanceRequest(req, res).catch(err => {
      console.error(`[usurper-web] Balance API error: ${err.message}`);
      if (!res.headersSent) sendJson(res, 500, { error: 'Internal error' });
    });
    return;
  }

  // Dashboard routes
  if (req.url && req.url.startsWith('/api/dash/')) {
    handleDashRequest(req, res).catch(err => {
      console.error(`[usurper-web] Dashboard error: ${err.message}`);
      if (!res.headersSent) sendJson(res, 500, { error: 'Internal error' });
    });
    return;
  }

  if (req.method === 'GET' && req.url === '/api/feed') {
    // SSE endpoint for live feed
    res.setHeader('Content-Type', 'text/event-stream');
    res.setHeader('Cache-Control', 'no-cache');
    res.setHeader('Connection', 'keep-alive');
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('X-Accel-Buffering', 'no'); // Prevent nginx buffering
    res.writeHead(200);
    res.write('retry: 5000\n\n'); // Auto-reconnect after 5s

    sseClients.add(res);
    console.log(`[usurper-web] SSE client connected (${sseClients.size} total)`);

    req.on('close', () => {
      sseClients.delete(res);
      console.log(`[usurper-web] SSE client disconnected (${sseClients.size} total)`);
    });
    return;
  } else if (req.method === 'GET' && req.url === '/api/sponsors') {
    getSponsors().then(data => {
      res.setHeader('Access-Control-Allow-Origin', '*');
      res.setHeader('Content-Type', 'application/json');
      res.setHeader('Cache-Control', 'public, max-age=300');
      res.writeHead(200);
      res.end(JSON.stringify(data));
    }).catch(err => {
      res.writeHead(500);
      res.end(JSON.stringify({ error: 'Failed to fetch sponsors' }));
    });
    return;
  } else if (req.method === 'GET' && req.url === '/api/releases/latest') {
    // Proxy GitHub releases API for clients that can't do TLS 1.2 (e.g. Win7)
    // Caches for 5 minutes to avoid GitHub rate limits
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Content-Type', 'application/json');
    res.setHeader('Cache-Control', 'public, max-age=300');

    const now = Date.now();
    if (_ghReleasesCache && (now - _ghReleasesCacheTime) < 300000) {
      res.writeHead(200);
      res.end(JSON.stringify(_ghReleasesCache));
      return;
    }

    const ghReq = https.get('https://api.github.com/repos/binary-knight/usurper-reborn/releases/latest', {
      headers: { 'User-Agent': 'usurper-web-proxy', 'Accept': 'application/vnd.github.v3+json' }
    }, (ghRes) => {
      let body = '';
      ghRes.on('data', chunk => body += chunk);
      ghRes.on('end', () => {
        try {
          const data = JSON.parse(body);
          _ghReleasesCache = { tag_name: data.tag_name, name: data.name, html_url: data.html_url, assets: (data.assets || []).map(a => ({ name: a.name, browser_download_url: a.browser_download_url, size: a.size })) };
          _ghReleasesCacheTime = now;
          res.writeHead(200);
          res.end(JSON.stringify(_ghReleasesCache));
        } catch (e) {
          res.writeHead(502);
          res.end(JSON.stringify({ error: 'Failed to parse GitHub response' }));
        }
      });
    });
    ghReq.on('error', (e) => {
      res.writeHead(502);
      res.end(JSON.stringify({ error: e.message }));
    });
    ghReq.end();
    return;
  } else if (req.method === 'GET' && req.url === '/api/stats') {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Content-Type', 'application/json');
    res.setHeader('Cache-Control', 'public, max-age=15');
    res.writeHead(200);
    res.end(JSON.stringify(getStats()));
  } else if (req.method === 'OPTIONS') {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');
    res.writeHead(204);
    res.end();
  } else if (req.method === 'GET') {
    // Static file serving for HTML pages (index, dashboard, balance, admin)
    const MIME_TYPES = { '.html': 'text/html', '.css': 'text/css', '.js': 'application/javascript', '.json': 'application/json', '.png': 'image/png', '.ico': 'image/x-icon' };
    let filePath = req.url.split('?')[0];
    if (filePath === '/') filePath = '/index.html';
    if (!path.extname(filePath)) filePath += '.html';
    const safeName = path.basename(filePath);
    const fullPath = path.join(__dirname, safeName);
    if (fs.existsSync(fullPath)) {
      const ext = path.extname(safeName);
      res.setHeader('Content-Type', MIME_TYPES[ext] || 'application/octet-stream');
      res.writeHead(200);
      fs.createReadStream(fullPath).pipe(res);
    } else {
      res.writeHead(404);
      res.end('{"error":"not found"}');
    }
  } else {
    res.writeHead(404);
    res.end('{"error":"not found"}');
  }
}

// --- HTTP + WebSocket Server ---
const httpServer = http.createServer(handleHttpRequest);
const wss = new Server({ server: httpServer });

httpServer.listen(WS_PORT, () => {
  console.log(`[usurper-web] HTTP + WebSocket server listening on port ${WS_PORT}`);
  console.log(`[usurper-web] Stats API: http://127.0.0.1:${WS_PORT}/api/stats`);
  console.log(`[usurper-web] Dashboard API: http://127.0.0.1:${WS_PORT}/api/dash/*`);
  console.log(`[usurper-web] Balance API: http://127.0.0.1:${WS_PORT}/api/balance/*`);
  console.log(`[usurper-web] Admin API: http://127.0.0.1:${WS_PORT}/api/admin/*`);
  console.log(`[usurper-web] Proxying SSH to ${SSH_HOST}:${SSH_PORT}`);
});

// --- WebSocket Connection Handler ---
wss.on('connection', (ws, req) => {
  const origin = req.headers.origin || '';
  const clientIP = req.headers['x-real-ip'] || req.socket.remoteAddress;

  // Origin check (allow if no origin header - direct WS clients)
  if (origin && !ALLOWED_ORIGINS.some(o => origin.startsWith(o))) {
    console.log(`[usurper-web] Rejected connection from origin: ${origin}`);
    ws.close(1008, 'Origin not allowed');
    return;
  }

  console.log(`[usurper-web] New connection from ${clientIP} (${MUD_MODE ? 'MUD TCP' : 'SSH'} mode)`);

  if (MUD_MODE) {
    // MUD mode: connect directly to TCP game server (lower latency, no SSH overhead)
    // No AUTH header is sent — the MUD server detects the raw connection and
    // presents an interactive login/register menu directly to the user.
    const tcp = net.connect({ host: MUD_HOST, port: MUD_PORT }, () => {
      console.log(`[usurper-web] TCP connected to MUD server for ${clientIP}`);
      // Forward real client IP to game server before interactive auth begins
      tcp.write(`X-IP:${clientIP}\n`);
    });

    // TCP → WebSocket
    tcp.on('data', (data) => {
      if (ws.readyState === ws.OPEN) {
        ws.send(data);
      }
    });

    tcp.on('close', () => {
      console.log(`[usurper-web] TCP closed for ${clientIP}`);
      ws.close(1000, 'Session ended');
    });

    tcp.on('error', (err) => {
      console.error(`[usurper-web] TCP error for ${clientIP}: ${err.message}`);
      if (ws.readyState === ws.OPEN) {
        ws.close(1011, 'Game server connection failed');
      }
    });

    // WebSocket → TCP
    ws.on('message', (data) => {
      if (!tcp.destroyed) {
        tcp.write(data);
      }
    });

    ws.on('close', () => {
      console.log(`[usurper-web] WebSocket closed for ${clientIP}`);
      tcp.destroy();
    });

    ws.on('error', (err) => {
      console.error(`[usurper-web] WebSocket error for ${clientIP}: ${err.message}`);
      tcp.destroy();
    });
  } else {
    // Legacy SSH mode: proxy through sshd-usurper
    const ssh = new Client();
    let sshStream = null;

    ssh.on('ready', () => {
      console.log(`[usurper-web] SSH connected for ${clientIP}`);

      ssh.shell({ term: 'xterm-256color', cols: 80, rows: 24 }, (err, stream) => {
        if (err) {
          console.error(`[usurper-web] Shell error: ${err.message}`);
          ws.close(1011, 'SSH shell failed');
          return;
        }

        sshStream = stream;

        // SSH stdout → WebSocket (send raw Buffer to preserve UTF-8 encoding)
        stream.on('data', (data) => {
          if (ws.readyState === ws.OPEN) {
            ws.send(data);
          }
        });

        // SSH stderr → WebSocket (send raw Buffer to preserve UTF-8 encoding)
        stream.stderr.on('data', (data) => {
          if (ws.readyState === ws.OPEN) {
            ws.send(data);
          }
        });

        stream.on('close', () => {
          console.log(`[usurper-web] SSH stream closed for ${clientIP}`);
          ws.close(1000, 'Session ended');
        });
      });
    });

    ssh.on('error', (err) => {
      console.error(`[usurper-web] SSH error for ${clientIP}: ${err.message}`);
      if (ws.readyState === ws.OPEN) {
        ws.close(1011, 'SSH connection failed');
      }
    });

    // WebSocket data → SSH stdin
    ws.on('message', (data) => {
      if (sshStream) {
        sshStream.write(data);
      }
    });

    ws.on('close', () => {
      console.log(`[usurper-web] WebSocket closed for ${clientIP}`);
      ssh.end();
    });

    ws.on('error', (err) => {
      console.error(`[usurper-web] WebSocket error for ${clientIP}: ${err.message}`);
      ssh.end();
    });

    // Initiate SSH connection
    ssh.connect({
      host: SSH_HOST,
      port: SSH_PORT,
      username: SSH_USER,
      password: SSH_PASS,
      readyTimeout: 10000,
    });
  }
});

// Graceful shutdown
function gracefulShutdown() {
  console.log('[usurper-web] Shutting down...');
  clearInterval(feedTimer);
  clearInterval(dashFeedTimer);
  clearInterval(dashHeartbeatTimer);
  for (const client of sseClients) { try { client.end(); } catch (e) {} }
  for (const client of dashSseClients) { try { client.end(); } catch (e) {} }
  sseClients.clear();
  dashSseClients.clear();
  if (db) db.close();
  if (dbWrite) dbWrite.close();
  wss.close(() => httpServer.close(() => process.exit(0)));
}
process.on('SIGTERM', gracefulShutdown);
process.on('SIGINT', gracefulShutdown);
