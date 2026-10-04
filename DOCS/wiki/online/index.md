---
title: The online world
path: /wiki/en/online/
checked: 1.2.5
sources: DOCS/MULTIPLAYER_ARCHITECTURE.md, Scripts/Systems/DailySystemManager.cs, Scripts/Systems/WorldEventSystem.cs
history: 1.2.5 | Daily messages and the blood moon broadcast reach each player in their own language, and grief stages are shown translated.
history: 1.2.4 | New: /history shows your last 50 chat lines, kept in server memory only.
---
Online play shares a persistent world. NPCs and other players can change the town while you are away. A wiki page describes rules, not a snapshot of today's state.

- [NPC life and relationships](/wiki/en/online/npcs/)
- [Teams and guilds](/wiki/en/online/teams/)
- [The Arena](/wiki/en/online/arena/)
- [Rankings, news and events](/wiki/en/online/events/)
- [Discord wiki help](/wiki/en/online/discord/)

## Shared time

Online daily resets and sleep are not the same as single-player starting a new day. Read current server messages and your remaining allowances.

## Chat history

/history (or /hist) shows your last 50 chat lines: tells you sent and received, gossip, say, shout, emote and guild chat. Add a channel to filter, for example /history tell or /history gossip. History is kept in server memory only, never saved, and lasts through a reconnect for 15 minutes. After a screen redraw, a line tells you how many new messages arrived.

## Shared combat

Arena and player fights differ from monster combat. World bosses use their own shared encounter system and are not eligible for ordinary daily Miracles.

The wiki exports no player accounts, private messages or server database state.
