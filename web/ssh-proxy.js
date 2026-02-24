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

const WS_PORT = 3000;
const SSH_HOST = '127.0.0.1';
const SSH_PORT = 4000;
const SSH_USER = 'usurper';
const SSH_PASS = 'play';
const DB_PATH = '/var/usurper/usurper_online.db';

// MUD mode: connect directly to MUD TCP server instead of through SSH
// Default ON since the MUD server now listens directly on port 4000.
// Set MUD_MODE=0 to fall back to legacy SSH mode if needed.
const MUD_MODE = process.env.MUD_MODE !== '0';
const MUD_PORT = parseInt(process.env.MUD_PORT || '4000', 10);
const CACHE_TTL = 30000; // 30 seconds
const FEED_POLL_MS = 5000; // SSE feed polls DB every 5 seconds
const GITHUB_PAT = process.env.GITHUB_PAT || '';
const SPONSORS_CACHE_TTL = 3600000; // 1 hour

// Balance dashboard auth
const BALANCE_USER = process.env.BALANCE_USER || 'admin';
const BALANCE_DEFAULT_PASS = process.env.BALANCE_PASS || 'changeme';
const BALANCE_SECRET = process.env.BALANCE_SECRET || crypto.randomBytes(32).toString('hex');
const BALANCE_TOKEN_TTL = 24 * 60 * 60 * 1000; // 24 hours

function hashPassword(password) {
  return crypto.createHash('sha256').update(password).digest('hex');
}

// Get stored password hash from DB, or fall back to default
function getBalancePasswordHash() {
  if (!dbWrite) return hashPassword(BALANCE_DEFAULT_PASS);
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
  return hashPassword(BALANCE_DEFAULT_PASS);
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

function verifyBalancePassword(password) {
  return hashPassword(password) === getBalancePasswordHash();
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

// Dashboard cache constants
const DASH_NPC_CACHE_TTL = 10000; // 10 seconds
const DASH_EVENTS_CACHE_TTL = 5000; // 5 seconds
const DASH_HOURLY_CACHE_TTL = 60000; // 60 seconds
const DASH_SNAPSHOT_INTERVAL = 30000; // 30 seconds for NPC snapshots

const CLASS_NAMES = {
  0: 'Alchemist', 1: 'Assassin', 2: 'Barbarian', 3: 'Bard',
  4: 'Cleric', 5: 'Jester', 6: 'Magician', 7: 'Paladin',
  8: 'Ranger', 9: 'Sage', 10: 'Warrior'
};

const RACE_NAMES = {
  0: 'Human', 1: 'Elf', 2: 'Dwarf', 3: 'Orc', 4: 'Halfling',
  5: 'Gnoll', 6: 'Troll', 7: 'Mutant'
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

  // Class distribution
  const classDist = {};
  npcs.forEach(n => {
    const cls = CLASS_NAMES[n.class] || CLASS_NAMES[n.Class] || 'Unknown';
    classDist[cls] = (classDist[cls] || 0) + 1;
  });

  // Race distribution
  const raceDist = {};
  npcs.forEach(n => {
    const race = RACE_NAMES[n.race] || RACE_NAMES[n.Race] || 'Unknown';
    raceDist[race] = (raceDist[race] || 0) + 1;
  });

  // Faction distribution
  const factionDist = {};
  npcs.forEach(n => {
    const fid = String(n.npcFaction !== undefined ? n.npcFaction : (n.NPCFaction !== undefined ? n.NPCFaction : -1));
    const fname = FACTION_NAMES[fid] || 'None';
    factionDist[fname] = (factionDist[fname] || 0) + 1;
  });

  // Location distribution
  const locationDist = {};
  npcs.forEach(n => {
    const loc = n.location || n.Location || 'Unknown';
    locationDist[loc] = (locationDist[loc] || 0) + 1;
  });

  // Level distribution (buckets)
  const levelDist = {};
  npcs.forEach(n => {
    const lvl = n.level || n.Level || 1;
    const bucket = Math.floor(lvl / 10) * 10;
    const key = `${bucket}-${bucket + 9}`;
    levelDist[key] = (levelDist[key] || 0) + 1;
  });

  // Age distribution (buckets)
  const ageDist = {};
  npcs.forEach(n => {
    const age = n.age || n.Age || 0;
    if (age > 0) {
      const bucket = Math.floor(age / 10) * 10;
      const key = `${bucket}-${bucket + 9}`;
      ageDist[key] = (ageDist[key] || 0) + 1;
    }
  });

  // Average level
  const levels = npcs.map(n => n.level || n.Level || 1);
  const avgLevel = levels.length > 0 ? (levels.reduce((a, b) => a + b, 0) / levels.length).toFixed(1) : 0;

  // Total gold
  const totalGold = npcs.reduce((sum, n) => sum + (n.gold || n.Gold || 0), 0);

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
    try {
      const body = await readBody(req);
      if (body.username === BALANCE_USER && verifyBalancePassword(body.password)) {
        const isDefault = body.password === BALANCE_DEFAULT_PASS;
        sendJson(res, 200, { token: createBalanceToken(), mustChangePassword: isDefault });
      } else {
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

// --- HTTP Handler ---
function handleHttpRequest(req, res) {
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
    const tcp = net.connect({ host: '127.0.0.1', port: MUD_PORT }, () => {
      console.log(`[usurper-web] TCP connected to MUD server for ${clientIP}`);
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
