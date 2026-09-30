# Usurper Reborn v1.2.1

## What is new in 1.2.1

- **The player wiki is open at `/wiki/` on the game website.** Hand-written
  guides sit beside reference pages generated from the game's own data, with
  search on every page.
- **The Discord bot answers questions from the wiki (Ask)** and takes
  corrections from trusted helpers (Suggest). When the server enables it,
  Ask answers in plain language from the wiki through the Claude API.
- **NPC stat gains now last,** and street fight rage no longer makes the
  town NPC stronger for good.
- **Iron Rations keeps its max HP bonus** through a stat recalculation in
  the middle of a fight.

## The wiki

- The wiki has 921 pages: guides written for players, and reference pages
  for items, classes, races, spells, abilities, monsters, gods and balance
  numbers generated from a versioned export of the game data. Every page
  shows the game version and the version its guide was checked against.
- Search ranks guides above generated item and spell pages, so a question
  about a place or a system finds the guide first.
- New guides: the castle and royalty, the Arena, New Game Plus, the story
  and the Old Gods (behind spoiler disclosures), equipment slots, home and
  family, and Anchor Road. The law, town, Dark Alley, gods (with the Temple
  steps) and Discord pages are extended.
- Spoiler disclosures keep secret bosses, late story and hidden odds out of
  search and out of Discord answers until you choose to open them.

## Discord Ask and Suggest

- **Ask:** mention the bot in a wiki channel with a question. It searches
  the wiki and replies with excerpts and links, or says when no page
  matches. It never reads player saves or private game data.
- **Ask in plain language:** when the server enables it, Ask answers
  through the Claude API instead of quoting excerpts:
  - it uses only the public wiki excerpts it finds, five excerpts from five
    different pages;
  - it declines questions that are not about Usurper Reborn, and a decline
    carries no source links;
  - links in an answer are kept only when they point to a wiki page;
  - answers run up to 700 tokens;
  - there are per-user and daily caps; over a cap, Ask gives the excerpt
    reply.
- **Suggest:** a helper with the server's trusted role can report a missing
  explanation or a wrong claim. Suggestions go to a review queue; they do
  not edit the wiki.

## Fixes

- **NPC stat gains now last.** World pre-history training, Temple blessings
  and prisoner activities were lost when the NPC's stats were next
  rebuilt. They now stay.
- **Street fight rage raises only the fight's opponent.** A challenge
  insult, a murder grudge, a refused apology and a spouse taunt make the
  NPC you fight stronger for that fight; the town NPC keeps its own stats.
- **Admin stat edits are kept.** Live edits from the admin console, and
  wizard `/set` on a player who is offline, now keep their stat changes.
- **Iron Rations:** the max HP bonus is kept through a stat recalculation
  during a fight, so max HP after the fight equals its value before the
  fight. Before, a recalculation mid-fight could leave max HP too low until
  the next recalculation.

## Known and unchanged

- A murder grudge still raises the town NPC's HP, up to 1.5 times its max
  HP. The fight uses its own opponent, so this has no effect in the fight.
- Wiki guides are in English only. Some reference pages list translated
  names where the game has them.

## For server operators

Ask in plain language is off unless `DISCORD_WIKI_LLM=on` and
`ANTHROPIC_API_KEY` are set for the web service. `DISCORD_WIKI_LLM_DAILY_TOKENS`
and `DISCORD_WIKI_LLM_USER_DAILY` set the server-wide daily token cap and the
per-user daily question cap. Suggest needs `DISCORD_WIKI_HELPER_ROLE_ID`.
See `scripts-server/wiki-bot.env.example` and `DOCS/WIKI_OPERATIONS.md`.

## Internal

- Game pacing pauses go through one helper, `Pacing.Wait`, which the test
  assembly switches off; player builds pause as before. 1,971 pauses and
  255 combat pauses were converted, and a guard test fails on a new bare
  pause. The full test suite runs in about 2 to 3.5 minutes, down from
  12 minutes 23 seconds.
- The wiki data export (`--export-wiki`) writes a versioned dataset that
  the wiki build reads; the build fails on a version mismatch, an unknown
  entity or a broken link.
- A wiki drift check runs in CI. A guide whose source files changed since
  the version it was checked against is stale: pull requests get a warning,
  and release branches, releases and deploys fail. Every game location
  must be covered by a guide or listed in an allowlist.
