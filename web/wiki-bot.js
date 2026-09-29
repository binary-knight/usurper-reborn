"use strict";
const fs = require("node:fs");
const path = require("node:path");
const { search, excerpt } = require("./wiki-search");

function sanitize(text) {
  return String(text)
    .replace(/<@!?\d+>|<@&\d+>|<#\d+>/g, "[mention]")
    .replace(/@everyone|@here/gi, "[mention]")
    .replace(/[\x00-\x1f\x7f]/g, " ")
    .trim();
}
function loadIndex(file) {
  if (fs.statSync(file).size > 16 * 1024 * 1024)
    throw Error("Wiki index too large");
  const index = JSON.parse(fs.readFileSync(file, "utf8"));
  if (
    index.schemaVersion !== 1 ||
    !index.gameVersion ||
    !Array.isArray(index.pages)
  )
    throw Error("Unsupported wiki index");
  for (const page of index.pages) {
    if (
      !/^\/wiki\/en\/(?:[\w%:-]+\/)*$/.test(page.path) ||
      typeof page.title !== "string" ||
      typeof page.text !== "string"
    )
      throw Error("Invalid wiki index page");
  }
  return index;
}
function ensureSuggestionSchema(db) {
  db.exec(`CREATE TABLE IF NOT EXISTS wiki_suggestions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    message_id TEXT NOT NULL UNIQUE,
    author_id TEXT NOT NULL,
    guild_id TEXT NOT NULL,
    channel_id TEXT NOT NULL,
    text TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending' CHECK(status IN ('pending','drafted','approved','rejected')),
    outcome TEXT,
    reviewed_at INTEGER
  );
  CREATE INDEX IF NOT EXISTS idx_wiki_suggestions_created ON wiki_suggestions(created_at);
  CREATE INDEX IF NOT EXISTS idx_wiki_suggestions_author ON wiki_suggestions(author_id, created_at);`);
}
function recordSuggestion(db, message, text, now) {
  return db
    .transaction(() => {
      const existing = db
        .prepare("SELECT id FROM wiki_suggestions WHERE message_id = ?")
        .get(message.id);
      if (existing) return { id: existing.id, duplicate: true };
      const since = now - 24 * 60 * 60 * 1000;
      const count = db
        .prepare(
          "SELECT COUNT(*) AS n FROM wiki_suggestions WHERE author_id = ? AND created_at > ?",
        )
        .get(message.author.id, since).n;
      const global = db
        .prepare(
          "SELECT COUNT(*) AS n FROM wiki_suggestions WHERE created_at > ?",
        )
        .get(since).n;
      if (count >= 3)
        return { error: "Your suggestion limit is 3 per rolling 24 hours." };
      if (global >= 30)
        return {
          error:
            "The wiki suggestion queue has reached its daily limit. Try again later.",
        };
      const inserted = db
        .prepare(
          "INSERT INTO wiki_suggestions (message_id, author_id, guild_id, channel_id, text, created_at) VALUES (?, ?, ?, ?, ?, ?)",
        )
        .run(
          message.id,
          message.author.id,
          message.guildId,
          message.channelId,
          text,
          now,
        );
      return { id: Number(inserted.lastInsertRowid) };
    })
    .immediate();
}
function createWikiBot({
  channels = [],
  roleId = "",
  origin = "https://usurper-reborn.net",
  indexFile = path.join(__dirname, "wiki/search-index.json"),
  db = null,
  now = Date.now,
  logger = console,
  index: suppliedIndex,
} = {}) {
  const allowed = new Set(channels.filter(Boolean));
  const base = new URL(origin);
  if (
    base.protocol !== "https:" ||
    base.username ||
    base.password ||
    base.pathname !== "/" ||
    base.search ||
    base.hash
  )
    throw Error("WIKI_SITE_ORIGIN must be an HTTPS origin");
  let index = suppliedIndex;
  if (allowed.size && !index) {
    try {
      index = loadIndex(indexFile);
    } catch (err) {
      logger.error(`[Wiki] Ask unavailable: ${err.message}`);
    }
  }
  let suggestionsReady = false;
  if (allowed.size && roleId && db) {
    try {
      ensureSuggestionSchema(db);
      suggestionsReady = true;
    } catch (err) {
      logger.error(`[Wiki] Suggest unavailable: ${err.message}`);
    }
  }
  const userTimes = new Map();
  let windowStart = now();
  let globalRequests = 0;
  const safeDiscord = (text) =>
    sanitize(text).replace(/[\\`*_~|>\[\]()]/g, "\\$&");
  async function handle(message, botId) {
    if (message.author.bot || !botId) return false;
    const mention = new RegExp(`<@!?${botId}>`, "g");
    if (!mention.test(message.content || "")) return false;
    // Every bot mention is consumed, including disallowed channels. Never relay it.
    if (!message.guildId || !allowed.has(message.channelId)) return true;
    const send = (content) =>
      message.channel.send({
        content: content.slice(0, 1900),
        allowedMentions: { parse: [], repliedUser: false },
      });
    try {
      const time = now();
      for (const [id, last] of userTimes)
        if (time - last >= 60000) userTimes.delete(id);
      if (time - windowStart >= 60000) {
        windowStart = time;
        globalRequests = 0;
      }
      if (
        time - (userTimes.get(message.author.id) ?? -Infinity) < 10000 ||
        globalRequests >= 30
      )
        return true;
      userTimes.set(message.author.id, time);
      globalRequests++;
      const raw = (message.content || "").replace(mention, "").trim();
      if (/^suggest\s*:/i.test(raw)) {
        if (!roleId || !suggestionsReady) {
          await send(
            "Wiki suggestions are not enabled. Ask the owner about the review queue.",
          );
          return true;
        }
        if (!message.member?.roles?.cache?.has(roleId)) {
          await send(
            "Wiki suggestions require the trusted helper role. Anyone can Ask.",
          );
          return true;
        }
        const proposal = raw.replace(/^suggest\s*:/i, "").trim();
        if (!proposal || proposal.length > 800) {
          await send(
            "Describe the proposed wiki correction in 1 to 800 characters.",
          );
          return true;
        }
        const result = recordSuggestion(db, message, sanitize(proposal), time);
        await send(
          result.error ||
            `Wiki suggestion #${result.id} ${result.duplicate ? "is already" : "was"} recorded for owner review. It will not publish automatically.`,
        );
        return true;
      }
      if (!raw || raw.length > 300) {
        await send(
          "Mention me with a wiki question up to 300 characters, or use `suggest: ...` if you have the trusted role.",
        );
        return true;
      }
      if (!index) {
        await send(
          "The published wiki index is unavailable. Please use the wiki section links or ask the owner.",
        );
        return true;
      }
      const matches = search(index.pages, sanitize(raw), 2);
      if (!matches.length) {
        await send(
          `I could not find that in the public wiki. Try a class, god, item or mechanic name: ${base.origin}/wiki/`,
        );
        return true;
      }
      const answer = matches
        .map(
          (p) =>
            `**${safeDiscord(p.title)}**\n${safeDiscord(excerpt(p, raw, 420))}\n${base.origin}${p.path}`,
        )
        .join("\n\n");
      await send(
        `Wiki excerpts (game ${safeDiscord(index.gameVersion)}):\n\n${answer}`,
      );
    } catch (err) {
      logger.error(`[Wiki] Request failed: ${err.message}`);
      // Fail closed; a failed wiki request must not enter the gossip relay.
    }
    return true;
  }
  return { handle };
}
module.exports = {
  createWikiBot,
  loadIndex,
  sanitize,
  ensureSuggestionSchema,
  recordSuggestion,
};
