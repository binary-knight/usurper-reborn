<!-- doc: checked 1.3.15; sources: Scripts/Systems/TelemetryStore.cs, Scripts/Systems/TelemetryUploader.cs, Scripts/Systems/TelemetryRow.cs, Scripts/Systems/TelemetryPrompt.cs, Scripts/Systems/VersionChecker.cs, Scripts/Systems/BugReportSystem.cs, Scripts/Systems/UserDataPaths.cs, Scripts/Systems/AccountMail.cs, Scripts/Server/MudChatSystem.cs, Scripts/BBS, web/ssh-proxy.js -->
# Privacy Policy

**Usurper Reborn** - Last updated: November 2026

## Overview

Usurper Reborn is free and open source software (GPL v2). We respect your privacy and collect minimal data only when you explicitly opt in.

## Telemetry (Opt-In Only)

The game offers **optional** combat telemetry to help tune game balance. It is pseudonymous, not anonymous: a random id and exact stat values can link one character's fights.

### How to Enable/Disable

- **Opt-in**: Single player and Steam ask once per copy of the game, before the main menu. A BBS door or a server asks each player once, after the MOTD, and only while the operator allows sharing (off by default)
- **Opt-out**: You can turn sharing off at any time under Preferences; turning it off deletes anything not yet sent
- **Default**: Telemetry is **disabled by default**

### What We Collect (When Enabled)

If you opt in, each monster fight adds one row of whole numbers, and nothing else:

| Data Type | Examples | Purpose |
|-----------|----------|---------|
| Outcome | Victory, fled or death | Balance combat difficulty |
| Your character | Class, level, maximum HP, HP at the end, strength, dexterity, weapon and armor power | Balance combat difficulty |
| The monsters | Family, level, maximum HP, strength, defence, boss or not, how many | Balance combat difficulty |
| The fight | Dungeon floor, difficulty, party size, teammates lost, rounds, damage dealt and taken, healing, potions, abilities and spells used, XP and gold gained | Balance combat difficulty |
| Upload | Game version, kind of install (single player, Steam, BBS door or server), a random id | Tell rows and versions apart |

### What We DO NOT Collect

- Your real name or identity
- IP addresses (none is stored with the rows)
- Location data
- Email addresses
- Any personally identifiable information
- Player or monster names
- Chat or text input
- Save contents or hardware ids
- Files on your computer

### Random Identifier

A random id is stored in `telemetry/state.json` in the save folder. This id:
- Is randomly generated and not linked to your identity
- On single player and Steam is made when you say yes, covers every character on that copy of the game, and can be replaced under Preferences
- On a BBS or a self hosted server belongs to that server and is shared by its players; no player can replace it
- Is only used if you opt into telemetry

### Where Data Is Sent

Telemetry rows are sent to the project's own server, `https://usurper-reborn.net/api/telemetry/v1/combat`, and kept there in their own database, with no address and no time finer than the day.

### Data Retention

Rows are kept for up to 30 days, including rows sent before you turn sharing off.

## Auto-Update Feature

The game checks for updates by contacting GitHub's public API:
- `https://api.github.com/repos/binary-knight/usurper-reborn/releases/latest`
- If that fails (a system without TLS 1.2), `http://usurper-reborn.net/api/releases/latest`, which the project's server answers from the same GitHub data

This check:
- Sends your game version and a standard User-Agent header
- Does not send any personal information
- Results are cached locally for 4 hours to minimize requests
- Is skipped on Steam builds, and on a server or BBS door unless the SysOp starts it from the SysOp console

## Local Data Storage

The game stores the following data locally on your computer:

| File | Purpose | Location |
|------|---------|----------|
| Save files | Your game progress and preferences | `saves/` in the user data folder (`%APPDATA%\UsurperReloaded`, `~/.local/share/UsurperReloaded` or `~/Library/Application Support/UsurperReloaded`) |
| Debug logs | Troubleshooting (local only) | `logs/` folder next to the game |
| Version cache | Reduce update checks | `version_cache.json` next to the game |
| Telemetry state | Your answer, the random id and rows not yet sent | `telemetry/` in the save folder |

All local data stays on your computer and is never uploaded unless you enable telemetry or send a bug report with `/bug` (it goes to the project's server at usurper-reborn.net, which forwards it to the developers).

## BBS Door Mode

When running as a BBS door game:
- The game reads your BBS username from the drop file (DOOR32.SYS or DOOR.SYS)
- This name is used only for your in-game character
- Saves are kept in the game's SQLite database on that BBS, one account per BBS user
- Auto-update is disabled (sysops manage updates)

## Online Account Email (Optional)

An online account may keep one email address, stored only with that account on the game server and used only to send password reset links and account notices (sent through the Resend mail service, with no tracking); remove it at any time with E on the character screen, which deletes it from the account. The address is never sold or shared, except with the mail service that delivers these messages. The address is optional; without one, a forgotten password can only be reset by the server's operator. A server that does not send mail asks for no address.

## Chat on the Official Server

On the project's own server (usurper-reborn.net):
- Lines on the gossip channel are also posted to the project's Discord server, with the character name of the player who wrote them.
- Some characters are played by AI agents that the project runs. They log in and play like any other account. When one of them answers chat, this is sent to Anthropic's API, which writes the reply: the channel, the sender's character name and the text of the message (a tell sent to that character, or a gossip, shout or say line it hears), the last few messages exchanged with that sender, and the AI-run character's own name, class, race, level and location.
- When an AI-run character meets a screen its own rules do not cover, the last lines of that screen are sent to Anthropic's API to choose the next key, with the character's own hit points, gold, level and location. Chat lines are removed first, but the screen can name other characters as the game prints them, for example in the news or a team name; the lists of sleeping guests at the inn and the dormitory show account names.
- At the start of a session an AI-run character sends its own details to Anthropic's API to choose how it will play: its name, class, race, level, gold, number of potions, a short description of its temperament, and the notes it wrote after earlier sessions. As it goes to sleep at the end of a session it sends its name, class, race, level and temperament, the way of playing it chose with its reason, and what the session came to (minutes played, levels gained, deaths, deepest dungeon floor, fights, enemies it fled, gold gained or lost, gear bought and where it slept), and gets back a note of one or two sentences, which is kept on the operator's own machine for up to 30 days. Nothing about other players is in these two requests. The temperament and the chosen way of playing with its reason are also sent with the screen described above.
- Passwords, email addresses and IP addresses are never sent. The sender's character name is sent, and for a character named after its account that is also the login name.
- AI-run characters are usually online, so assume that a gossip, shout or say line on the official server is sent.
- The agents' play logs, on the operator's own machine, keep the chat lines an AI-run character hears and the screens it sent, for up to 30 days.
- A tell to a character that is not AI-run, and team or guild chat without an AI-run member, is not sent to Anthropic.

A self hosted server or a BBS door has no AI-run characters and no Discord link unless its operator adds them.

## Public World Pages

The project's website shows the online world on public pages, read from `/api/stats` and `/api/feed`, and for the Observatory page at `/dashboard` from `/api/dash/`. They show:
- the news, the game's own lines about what happened in the world, which name characters;
- the players online, the leaderboard, the ruler, the immortals and the arena results, by character name;
- on the Observatory, the NPCs and their ties, the realm, the dungeon depths, the market and the fallen, each as a fixed set of fields. A player's character name appears there only as one of the ten top damage dealers of the current or latest world boss, as the ruler, as the god of the week, or as one of the 20 latest fallen heroes with that character's level, class, the killer when it is a monster or an NPC, and time of death;
- an hourly world history of counts only, such as NPCs alive and players online.

No field on these pages is read from an account's login name.

## Third-Party Services

| Service | Purpose | Privacy Policy |
|---------|---------|----------------|
| GitHub | Update checks, releases | https://docs.github.com/en/site-policy/privacy-policies |
| Resend | Password reset and account notice mail (online accounts with an address) | https://resend.com/legal/privacy-policy |
| Anthropic | Replies and choices for AI-run characters on the official server (chat they receive, screens their rules do not cover) | https://www.anthropic.com/legal/privacy |
| Discord | Gossip channel lines from the official server | https://discord.com/privacy |

## Open Source

This game is open source. You can review exactly what data is collected by examining:
- `Scripts/Systems/TelemetryStore.cs` and `Scripts/Systems/TelemetryUploader.cs` - Telemetry implementation
- `Scripts/Systems/VersionChecker.cs` - Update checker

Source code: https://github.com/binary-knight/usurper-reborn

## Contact

For privacy questions or concerns:
- Open an issue: https://github.com/binary-knight/usurper-reborn/issues
- Email: [Add your contact email if desired]

## Changes to This Policy

We may update this privacy policy as the game develops. Check the "Last updated" date at the top of this document.
