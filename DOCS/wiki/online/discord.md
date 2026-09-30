---
title: Discord wiki help
path: /wiki/en/online/discord/
checked: 1.2.2
sources: web/wiki-bot.js
---
The Discord bot can answer from the published English wiki in channels enabled by the server owner. Mention the bot and ask a focused question, for example: `@UsurperBot how does Favor work?`

## What Ask does

Ask searches the published wiki for pages that match your question. How it answers depends on the server:

- **With the Claude API enabled.** The bot sends the best matching wiki excerpts and your question to Claude, a language model, and replies with a short plain-language answer of about 120 words, followed by links to the pages it used. The model may use only those excerpts. If they do not cover your question, the answer says so.
- **Without it.** The bot replies with short excerpts and links from the matching pages.

If no page matches, the bot says so and makes no model call. Ask never inspects player saves or private game data, and spoiler disclosure text is left out of what it can see.

Questions that are not about Usurper Reborn, such as other games, programming or general chat, are declined without source links.

## Limits

- A question can be up to 300 characters.
- Each user can ask once every ten seconds, and the server handles a limited number of questions per minute.
- With the Claude API enabled, there are also daily limits per user and for the whole server. Past a limit, you get the excerpt reply instead.
- If the model call fails or its answer runs too long, you also get the excerpt reply.

Questions are not relayed into in-game gossip. Availability depends on the owner enabling allowed channels and deploying a built search index.

## Report a bug

Ask and Suggest are for the wiki, not for game bugs. To report a bug, press `!` or type `/bug` in the game, or open a GitHub issue; see [reporting bugs](/wiki/en/getting-started/reporting-bugs/).

## Suggest an improvement

Trusted helpers with the owner's configured role can use `@UsurperBot suggest: ...` or `@UsurperBot suggestion: ...`. Either word works in any case, with or without a space before the colon. Describe a missing explanation or incorrect claim, not instructions to execute code.

Suggestions enter an audit queue. They do not edit the production site or publish a page. The owner reviews the claim against game code and exported data, then prepares a docs-only pull request. The next reviewed release or approved deployment publishes it.

If Suggest is not enabled, ask the owner to configure the trusted role. Rate limits apply to both features.
