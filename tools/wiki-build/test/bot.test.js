"use strict";
const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const Database = require("better-sqlite3");
const { createWikiBot, sanitize, loadIndex } = require("../../../web/wiki-bot");
const { validateSuggestion, assertDocsOnly } = require("../suggestion");
const index = {
  schemaVersion: 1,
  gameVersion: "1.2.0",
  pages: [
    {
      title: "Favor",
      path: "/wiki/en/gods/favor/",
      headings: ["Prayer"],
      text: "Favor improves your god boon. Prayer at the Temple restores Mental.",
    },
  ],
};
function fixture(options = {}) {
  const sent = [];
  let time = 100000;
  const logger = { error() {} };
  const bot = createWikiBot({
    channels: ["allowed"],
    roleId: "trusted",
    index,
    now: () => time,
    logger,
    ...options,
  });
  const message = (content = "<@123> how does Favor work?", changes = {}) => ({
    id: "1",
    guildId: "guild",
    channelId: "allowed",
    content,
    author: { id: "user", bot: false },
    member: { roles: { cache: new Set(["trusted"]) } },
    channel: { send: async (payload) => sent.push(payload) },
    ...changes,
  });
  return {
    bot,
    sent,
    message,
    advance: () => {
      time += 11000;
    },
  };
}
test("Ask uses cited excerpts, disables mentions, and consumes bot questions", async () => {
  const f = fixture();
  assert.equal(await f.bot.handle(f.message(), "123"), true);
  assert.match(
    f.sent[0].content,
    /https:\/\/usurper-reborn.net\/wiki\/en\/gods\/favor\//,
  );
  assert.match(f.sent[0].content, /Prayer at the Temple/);
  assert.deepEqual(f.sent[0].allowedMentions.parse, []);
  assert.equal(await f.bot.handle(f.message("ordinary gossip"), "123"), false);
  assert.equal(f.sent.length, 1);
});
test("Mentions outside the wiki channels are left for gossip routing", async () => {
  const f = fixture();
  assert.equal(
    await f.bot.handle(
      f.message("<@123> anyone online?", { channelId: "gossip" }),
      "123",
    ),
    false,
  );
  assert.equal(
    await f.bot.handle(f.message("<@123> question", { guildId: null }), "123"),
    false,
  );
  const disabled = fixture({ channels: [] });
  assert.equal(
    await disabled.bot.handle(
      disabled.message("<@123> anyone online?", { channelId: "gossip" }),
      "123",
    ),
    false,
  );
  assert.equal(
    await disabled.bot.handle(disabled.message("<@123> how does Favor work?"), "123"),
    false,
  );
  assert.equal(f.sent.length + disabled.sent.length, 0);
  assert.equal(await f.bot.handle(f.message("<@123> Favor"), "123"), true);
  assert.equal(f.sent.length, 1);
});
test("Ask says no match and enforces request length and cooldown", async () => {
  const f = fixture();
  await f.bot.handle(f.message("<@123> zzzunknown"), "123");
  assert.match(f.sent[0].content, /could not find/);
  await f.bot.handle(f.message(), "123");
  assert.equal(f.sent.length, 1);
  f.advance();
  await f.bot.handle(f.message("<@!123> " + "x".repeat(301)), "123");
  assert.match(f.sent[1].content, /300 characters/);
});
test("Suggest is role-gated, sanitized, persistent and deduplicated", async () => {
  const db = new Database(":memory:");
  try {
    const f = fixture({ db });
    await f.bot.handle(
      f.message("<@123> suggest: correction", {
        member: { roles: { cache: new Set() } },
      }),
      "123",
    );
    assert.match(f.sent[0].content, /trusted helper role/);
    assert.equal(
      db.prepare("SELECT COUNT(*) AS n FROM wiki_suggestions").get().n,
      0,
    );
    f.advance();
    await f.bot.handle(
      f.message("<@123> suggest: @everyone <@456> Terran HP missing"),
      "123",
    );
    const row = db.prepare("SELECT * FROM wiki_suggestions").get();
    assert.equal(row.status, "pending");
    assert.ok(!row.text.includes("@everyone") && !row.text.includes("<@"));
    assert.match(f.sent[1].content, /suggestion #1/);
    f.advance();
    await f.bot.handle(f.message("<@123> suggest: same message"), "123");
    assert.equal(
      db.prepare("SELECT COUNT(*) AS n FROM wiki_suggestions").get().n,
      1,
    );
  } finally {
    db.close();
  }
});
test("Suggestion limits persist across bot restart and global cap is enforced", async () => {
  const db = new Database(":memory:");
  try {
    const f = fixture({ db });
    for (let n = 1; n <= 4; n++) {
      f.advance();
      await f.bot.handle(
        f.message("<@123> suggest: correction", { id: String(n) }),
        "123",
      );
    }
    assert.equal(
      db.prepare("SELECT COUNT(*) AS n FROM wiki_suggestions").get().n,
      3,
    );
    assert.match(f.sent[3].content, /3 per rolling/);
    const restarted = fixture({ db, now: () => 200000 });
    await restarted.bot.handle(
      restarted.message("<@123> suggest: correction", { id: "5" }),
      "123",
    );
    assert.match(restarted.sent[0].content, /3 per rolling/);
    for (let n = 4; n <= 30; n++)
      db.prepare(
        "INSERT INTO wiki_suggestions (message_id, author_id, guild_id, channel_id, text, created_at) VALUES (?, ?, ?, ?, ?, ?)",
      ).run(
        "global-" + n,
        "other-" + n,
        "guild",
        "allowed",
        "correction",
        150000,
      );
    const global = fixture({ db, now: () => 250000 });
    await global.bot.handle(
      global.message("<@123> suggest: correction", {
        id: "31",
        author: { id: "new", bot: false },
      }),
      "123",
    );
    assert.match(global.sent[0].content, /daily limit/);
  } finally {
    db.close();
  }
});
test("Missing index, disabled suggestions and failures never relay", async () => {
  const f = fixture({ index: undefined, indexFile: "/no-such-index" });
  assert.equal(await f.bot.handle(f.message(), "123"), true);
  assert.match(f.sent[0].content, /unavailable/);
  f.advance();
  await f.bot.handle(f.message("<@123> suggest: fix"), "123");
  assert.match(f.sent[1].content, /not enabled/);
  const broken = fixture();
  const message = broken.message();
  message.channel.send = async () => {
    throw Error("Discord unavailable");
  };
  assert.equal(await broken.bot.handle(message, "123"), true);
  assert.throws(() => createWikiBot({ origin: "http://unsafe.invalid" }));
});
test("Untrusted index paths and oversized submissions are rejected", async () => {
  const temp = fs.mkdtempSync(
    path.join(os.tmpdir(), "usurper-wiki-index-test-"),
  );
  try {
    const file = path.join(temp, "index.json");
    fs.writeFileSync(
      file,
      JSON.stringify({
        ...index,
        pages: [{ ...index.pages[0], path: "//evil.invalid/" }],
      }),
    );
    assert.throws(() => loadIndex(file), /Invalid wiki index/);
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
  assert.equal(sanitize("@here\n<@123>\u0000"), "[mention] [mention]");
  const db = new Database(":memory:");
  try {
    const f = fixture({ db });
    await f.bot.handle(f.message("<@123> suggest: " + "x".repeat(801)), "123");
    assert.match(f.sent[0].content, /800 characters/);
    assert.equal(
      db.prepare("SELECT COUNT(*) AS n FROM wiki_suggestions").get().n,
      0,
    );
  } finally {
    db.close();
  }
});
test("global Ask limit covers many users without leaking requests", async () => {
  const f = fixture();
  for (let n = 0; n < 31; n++)
    assert.equal(
      await f.bot.handle(
        f.message("<@123> Favor", { author: { id: "user-" + n, bot: false } }),
        "123",
      ),
      true,
    );
  assert.equal(f.sent.length, 30);
});
test("Off-server drafts are confined to existing prose with valid evidence", () => {
  const root = path.resolve(__dirname, "../../..");
  const input = {
    id: "12",
    page: "gods/favor.md",
    markdown: fs.readFileSync(
      path.join(root, "DOCS/wiki/gods/favor.md"),
      "utf8",
    ),
    suggestion: "Terran HP is missing",
    evidence: "Scripts/Core/GameConfig.cs:1",
  };
  assert.ok(validateSuggestion(input, root).file.endsWith("favor.md"));
  for (const page of ["../../web/ssh-proxy.js", "new.md", "/tmp/page.md"])
    assert.throws(() => validateSuggestion({ ...input, page }, root));
  assert.throws(() =>
    validateSuggestion(
      { ...input, evidence: "Scripts/Core/GameConfig.cs:999999" },
      root,
    ),
  );
  assertDocsOnly(["DOCS/wiki/gods/favor.md"]);
  assert.throws(() =>
    assertDocsOnly(["DOCS/wiki/gods/favor.md", "web/index.html"]),
  );
  assert.throws(() => assertDocsOnly([]));
});
