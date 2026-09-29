---
title: Discord wiki help
path: /wiki/en/online/discord/
checked: 1.2.0
sources: web/wiki-bot.js
---
The Discord bot can answer from the published English wiki in channels enabled by the server owner. Mention the bot and ask a focused question, for example: `@UsurperBot how does Favor work?`

## What Ask does

Ask searches the wiki and replies with short excerpts and links. It does not run a language model, inspect player saves or read private game data. If no page matches, it says so. Spoiler disclosure text is excluded.

Questions are not relayed into in-game gossip. Availability depends on the owner enabling allowed channels and deploying a built search index.

## Suggest an improvement

Trusted helpers with the owner's configured role can use `@UsurperBot suggest: ...`. Describe a missing explanation or incorrect claim, not instructions to execute code.

Suggestions enter an audit queue. They do not edit the production site or publish a page. The owner reviews the claim against game code and exported data, then prepares a docs-only pull request. The next reviewed release or approved deployment publishes it.

If Suggest is not enabled, ask the owner to configure the trusted role. Rate limits apply to both features.
