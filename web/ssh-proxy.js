// Usurper Reborn - WebSocket to SSH Bridge + Stats API
// Proxies browser terminal connections to the game's SSH server
// Serves /api/stats endpoint with live game statistics

const http = require('http');
const { Server } = require('ws');
const { Client } = require('ssh2');

let Database;
try {
  Database = require('better-sqlite3');
} catch (err) {
  console.error(`[usurper-web] better-sqlite3 not available: ${err.message}`);
}

const WS_PORT = 3000;
const SSH_HOST = '127.0.0.1';
const SSH_PORT = 4000;
const SSH_USER = 'usurper';
const SSH_PASS = 'play';
const DB_PATH = '/var/usurper/usurper_online.db';
const CACHE_TTL = 30000; // 30 seconds

const CLASS_NAMES = {
  0: 'Alchemist', 1: 'Assassin', 2: 'Barbarian', 3: 'Bard',
  4: 'Cleric', 5: 'Jester', 6: 'Magician', 7: 'Paladin',
  8: 'Ranger', 9: 'Sage', 10: 'Warrior'
};

const ALLOWED_ORIGINS = [
  'https://usurper-reborn.net',
  'https://www.usurper-reborn.net',
  'http://usurper-reborn.net',
  'http://www.usurper-reborn.net',
  'http://localhost',
];

// --- Database Setup ---
let db = null;
if (Database) {
  try {
    db = new Database(DB_PATH, { readonly: true, fileMustExist: true });
    db.pragma('journal_mode = WAL');
    console.log(`[usurper-web] Database connected: ${DB_PATH}`);
  } catch (err) {
    console.error(`[usurper-web] Database not available: ${err.message}`);
  }
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
    return { error: 'Database not available', online: [], stats: {}, highlights: {}, news: [] };
  }

  try {
    // Online players with class/level
    const online = db.prepare(`
      SELECT op.username, op.display_name, op.location, op.connected_at,
             json_extract(p.player_data, '$.player.level') as level,
             json_extract(p.player_data, '$.player.class') as class_id,
             COALESCE(op.connection_type, 'Unknown') as connection_type
      FROM online_players op
      LEFT JOIN players p ON LOWER(op.username) = LOWER(p.username)
      WHERE op.last_heartbeat >= datetime('now', '-120 seconds')
      ORDER BY op.display_name
    `).all().map(row => ({
      name: row.display_name || row.username,
      level: row.level || 1,
      className: CLASS_NAMES[row.class_id] || 'Unknown',
      location: row.location || 'Unknown',
      connectedAt: row.connected_at,
      connectionType: row.connection_type || 'Unknown'
    }));

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
      FROM players WHERE is_banned = 0
    `).get();

    // Top player
    const topPlayer = db.prepare(`
      SELECT display_name,
             json_extract(player_data, '$.player.level') as level,
             json_extract(player_data, '$.player.class') as class_id
      FROM players WHERE is_banned = 0
      ORDER BY json_extract(player_data, '$.player.level') DESC LIMIT 1
    `).get();

    // Most popular class
    const popClass = db.prepare(`
      SELECT json_extract(player_data, '$.player.class') as class_id, COUNT(*) as cnt
      FROM players WHERE is_banned = 0
      GROUP BY class_id ORDER BY cnt DESC LIMIT 1
    `).get();

    // Children count
    let childrenCount = 0;
    try {
      const childResult = db.prepare(`
        SELECT COALESCE(SUM(json_array_length(json_extract(player_data, '$.storySystems.children'))), 0) as cnt
        FROM players WHERE is_banned = 0
          AND json_extract(player_data, '$.storySystems.children') IS NOT NULL
      `).get();
      childrenCount = childResult ? childResult.cnt : 0;
    } catch (e) { /* children array may not exist */ }

    // Marriage count
    let marriageCount = 0;
    try {
      const marriageResult = db.prepare(`
        SELECT COUNT(*) as cnt
        FROM players, json_each(json_extract(player_data, '$.storySystems.relationships'))
        WHERE is_banned = 0
          AND json_extract(player_data, '$.storySystems.relationships') IS NOT NULL
          AND json_extract(json_each.value, '$.marriedDays') > 0
      `).get();
      marriageCount = marriageResult ? Math.floor(marriageResult.cnt / 2) : 0;
    } catch (e) { /* relationships may not exist */ }

    // Current king
    let king = null;
    try {
      const kingRow = db.prepare(`SELECT value FROM world_state WHERE key = 'king'`).get();
      if (kingRow && kingRow.value) {
        const kingData = JSON.parse(kingRow.value);
        king = kingData.name || kingData.Name || null;
      }
    } catch (e) { /* king may not exist */ }

    // Player leaderboard (all players, ranked by level then XP)
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
        ORDER BY json_extract(p.player_data, '$.player.level') DESC,
                 json_extract(p.player_data, '$.player.experience') DESC
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
        FROM news ORDER BY created_at DESC LIMIT 10
      `).all().map(row => ({
        message: row.message,
        category: row.category,
        playerName: row.player_name,
        time: row.created_at
      }));
    } catch (e) { /* news table may not exist */ }

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
        children: childrenCount
      },
      highlights: {
        topPlayer: topPlayer ? {
          name: topPlayer.display_name,
          level: topPlayer.level,
          className: CLASS_NAMES[topPlayer.class_id] || 'Unknown'
        } : null,
        king: king,
        popularClass: popClass ? CLASS_NAMES[popClass.class_id] || 'Unknown' : null
      },
      leaderboard: leaderboard,
      news: news,
      cachedAt: new Date().toISOString()
    };
    statsCacheTime = now;
    return statsCache;
  } catch (err) {
    console.error(`[usurper-web] Stats query error: ${err.message}`);
    return { error: 'Stats query failed', online: [], stats: {}, highlights: {}, news: [] };
  }
}

// --- HTTP Handler ---
function handleHttpRequest(req, res) {
  if (req.method === 'GET' && req.url === '/api/stats') {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Content-Type', 'application/json');
    res.setHeader('Cache-Control', 'public, max-age=15');
    res.writeHead(200);
    res.end(JSON.stringify(getStats()));
  } else if (req.method === 'OPTIONS') {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
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

  console.log(`[usurper-web] New connection from ${clientIP}`);

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
});

// Graceful shutdown
process.on('SIGTERM', () => {
  console.log('[usurper-web] Shutting down...');
  if (db) db.close();
  wss.close(() => httpServer.close(() => process.exit(0)));
});

process.on('SIGINT', () => {
  console.log('[usurper-web] Shutting down...');
  if (db) db.close();
  wss.close(() => httpServer.close(() => process.exit(0)));
});
