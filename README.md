# Usurper Reborn

## A Persistent Online Text RPG with a Living World

**BETA v0.60.8** | **FREE AND OPEN SOURCE** | **GPL v2**

130+ autonomous NPCs wake up, go to work, visit taverns, fall in love, get married, have children, age, and eventually die of old age, all while you're offline. Log back in, read the news feed, and discover that the blacksmith married the barmaid, the king was assassinated, or a new generation just came of age. The world doesn't wait for you.

**Play now:**
- Browser: https://usurper-reborn.net (no install required)
- SSH / MUD client: `ssh usurper@play.usurper-reborn.net -p 4000` (gateway password `play`, then register or log in inside the game)
- Direct MUD client (Mudlet, MUSHclient, TinTin++, etc.): `play.usurper-reborn.net 4000` (raw TCP, GMCP supported)
- Steam: https://store.steampowered.com/app/4336570/Usurper_Reborn/

**Download standalone:** [Latest Release](https://github.com/binary-knight/usurper-reborn/releases) | **Report bugs:** [Discord](https://discord.gg/EZhwgDT6Ta) or [GitHub Issues](https://github.com/binary-knight/usurper-reborn/issues) or press `!` in-game

---

## The Living World

The core of Usurper Reborn is a 24/7 agent-based simulation. NPCs aren't quest dispensers standing in place, they're goal-driven agents with personalities, memories, and opinions about each other and about you.

**Autonomous NPCs:** Each NPC has 13 personality traits, a memory system (30 memories per NPC, importance-weighted), and a goal-based AI brain. They choose careers, form gangs, visit shops, train at the guild, and develop relationships with each other independently of player action. NPCs react to neighbor density (Conway-inspired clustering), migrate when overcrowded, and form friend groups based on personality compatibility.

**Full Lifecycles:** Married NPCs can become pregnant, have children, and raise them. Children grow up over real time and eventually join the realm as new adult NPCs. Adults age according to their race's lifespan (Human ~30 days, Elf ~80 days, Orc ~22 days) and die permanently when their time comes. The population turns over. No one is permanent.

**Emergent Events:** Marriages, divorces, affairs, births, coming-of-age ceremonies, natural deaths, gang wars, and political upheavals all happen organically and appear in the live news feed on the website and in-game.

**Persistent Multiplayer:** Connect via browser, SSH, or any MUD client to a shared world backed by SQLite. Your actions affect other players. PvP arena, cross-player chat, leaderboards, guilds, group dungeons, world bosses, and a news feed that captures everything happening in the realm.

**Cross-platform Discord bridge:** In-game `/gos` lines mirror to a Discord channel and back, so the realm's gossip channel stays alive even when nobody is logged in.

---

## The Game

Beyond the simulation, there's a deep RPG with 100+ hours of content.

### Character Building
- **12 Base Classes + 5 Prestige Classes** (17 total): Warrior, Paladin, Assassin, Magician, Cleric, Ranger, Bard, Sage, Barbarian, Alchemist, Jester, Mystic Shaman, plus 5 NG+ prestige classes (Tidesworn, Wavecaller, Cyclebreaker, Abysswarden, Voidreaver) unlocked by completing different endings.
- **10 Races:** Human, Elf, Dwarf, Hobbit, Half-Elf, Orc, Gnome, Troll, Gnoll, Mutant, with race-specific lifespans, stats, and lore.
- **Specialization System:** Each class has 2 specializations (24 total) selectable at the Level Master, shaping NPC AI ability priorities and giving role-specific stat bonuses (Tank, DPS, Healer, Utility, Debuff).
- **75+ Spells** across caster classes, **44+ class abilities**, plus 5 universal abilities. Meaningful stat scaling with diminishing returns past natural caps.
- **Romantic orientation** (Straight / Gay / Bisexual / Asexual) selectable at character creation, affects NPC pool filtering for romance.

### 100-Floor Dungeon
- Deterministically generated floors with boss encounters, treasure rooms, traps, hidden secrets, settlements, meditation chambers, puzzles, and lever rooms.
- 7 corrupted Old Gods sealed in the depths (floors 25, 40, 55, 70, 85, 95, 100), each with multi-phase combat, channeled abilities, AoE mechanics, divine armor, and meaningful dialogue choices.
- 7 Ancient Seals to collect, unlocking the truth about who you are.
- 5 endings based on your choices: Conqueror, Savior, Defiant, True, and a secret Dissolution ending.
- Floor-aware monster loot with full per-slot armor coverage (head, arms, hands, legs, feet, waist, face, cloak, body, weapons, shields, rings, necklaces).
- Settlements at Floor 1 (NPC-built shops, services, vote-driven proposals).

### Story & Narrative
You wake with no memory. A letter in your own handwriting warns you: *"The gods are broken. Collect the Seven Seals. Break the cycle. You are not what you think you are."*

- **Ocean Philosophy:** A Buddhist-inspired awakening system with 7 levels: *"You are not a wave fighting the ocean. You ARE the ocean, dreaming of being a wave."*
- **5 Companions** who can die permanently: Lyris, Aldric, Mira, Vex, Melodia, each with personal quests, real grief consequences, and signature combat abilities.
- **NG+ Cycle System:** Each playthrough, you remember more. NPCs notice. Cycle 4+ players face stacking world modifiers (+monster HP/damage, +gold scaling).
- **6 Town NPCs with story arcs**, dream sequences, stranger encounters, and faction politics.

### Relationships & Politics
- Romance, marriage, children, divorce, affairs, polyamory.
- Challenge the throne, recruit guards, manage treasury, navigate court factions, set tax rates and city-control turf.
- 3 joinable factions: The Crown, The Shadows, The Faith.
- PvP arena with daily limits, gold theft, and leaderboards.
- Guild system with 8 chat commands, guild bank, member XP bonus, invite flow.
- Knighthood with combat buffs and Sir/Dame title prefix.

### Locations
30+ player-visitable locations: Main Street, Inn, Bank, Weapon Shop, Armor Shop, Magic Shop, Music Shop, Healer, Temple, Church, Dark Alley, Level Master, Marketplace, Castle, Castle Courtyard, Pantheon, Hall of the Ascended, Prison, Prison Walk, Anchor Road, Team Corner, Quest Hall, Dungeons, Home (with 5-tier upgrade system), Wilderness, Outskirts settlement, Arena, and more.

### Mod Support & Game Editor
Opt-in JSON mods drop into a `GameData/` folder next to the executable: NPCs, monster families, dreams, achievements, dialogue lines, balance constants, and custom equipment are all overridable without a recompile. A bundled menu-driven editor (`UsurperReborn --editor`, or `[G] Game Editor` from the main menu in single-player) is the sysop-tool analogue of the DOS-era `USEDIT.EXE`: arrow-key / number-key navigation, edits saves and mods from one UI, auto-backs up before writes. See `DOCS/MODDING.md` for the full guide.

### Accessibility
- **Screen reader mode:** auto-detected on Windows for the standard console launch (NVDA / JAWS / Narrator), or pass `--screen-reader` explicitly. Strips box-drawing, decorative Unicode, color-bracketed menus; all locations have plain-text paths.
- **Compact mode:** smaller terminals (mobile SSH, narrow windows). Toggle with `[Z]` from any menu or `/compact`.
- **Steam launcher:** `Play.bat` is the default; `Play-Accessible.bat` opts into screen-reader mode at launch.
- **5 languages:** English, Spanish, French, Hungarian, Italian. Swap via in-game preferences. Per-character language preference saved.

### Graphical Client (Beta)
An optional Electron-based graphical client is in active development with Darkest Dungeon-style combat sprites, a dungeon map overlay, paperdoll inventory, party panels, status overlays, and an audio infrastructure layer (sound files filling in over time). Text mode remains the primary supported way to play.

---

## Origins

Originally inspired by *Usurper* (1993) by Jakob Dangarden, a classic BBS door game. The original Pascal source was preserved by Rick Parrish and Daniel Zingaro. Usurper Reborn maintains compatibility with the original formulas while building an entirely new simulation layer on top.

## Building from Source

This is free and open source software, you can build it yourself.

### Prerequisites
- [.NET SDK 8.0+](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git

### Quick Build
```bash
git clone https://github.com/binary-knight/usurper-reborn.git
cd usurper-reborn

# Build and run (framework-dependent, requires .NET runtime installed)
dotnet build usurper-reloaded.csproj -c Release
dotnet run --project usurper-reloaded.csproj -c Release
```

### Self-Contained Builds (No .NET Runtime Required)

Build a standalone executable that includes the .NET runtime. **Always pass `--self-contained`** so the binary runs on machines without .NET installed.

#### Windows (64-bit)
```bash
dotnet publish usurper-reloaded.csproj -c Release -r win-x64 --self-contained -o publish/win-x64
# Run: publish/win-x64/UsurperReborn.exe
```

#### Windows (32-bit, BBS sysops)
```bash
dotnet publish usurper-reloaded.csproj -c Release -r win-x86 --self-contained -o publish/win-x86
```

#### Linux (x64)
```bash
dotnet publish usurper-reloaded.csproj -c Release -r linux-x64 --self-contained -o publish/linux-x64
chmod +x publish/linux-x64/UsurperReborn
# Run: ./publish/linux-x64/UsurperReborn
```

#### Linux (ARM64, Raspberry Pi, etc.)
```bash
dotnet publish usurper-reloaded.csproj -c Release -r linux-arm64 --self-contained -o publish/linux-arm64
chmod +x publish/linux-arm64/UsurperReborn
```

#### macOS (Intel)
```bash
dotnet publish usurper-reloaded.csproj -c Release -r osx-x64 --self-contained -o publish/osx-x64
chmod +x publish/osx-x64/UsurperReborn
```

#### macOS (Apple Silicon)
```bash
dotnet publish usurper-reloaded.csproj -c Release -r osx-arm64 --self-contained -o publish/osx-arm64
chmod +x publish/osx-arm64/UsurperReborn
```

### Self-hosting an online server

The full multi-player stack (game server + SSH gateway + web proxy + Nginx) is documented in [`DOCS/SERVER_DEPLOYMENT.md`](DOCS/SERVER_DEPLOYMENT.md). A Docker-based 3-container stack is available via `docker compose up -d` ([`DOCS/DOCKER.md`](DOCS/DOCKER.md)).

## Technical Details

- **Runtime:** .NET 8.0 (LTS) | **Language:** C# 12
- **Codebase:** 130,000+ lines across 200+ files, 70+ game systems
- **NPC Simulation:** Goal-based AI with 13 personality traits, importance-weighted memory, lifecycle aging, neighbor-pressure migration
- **Platforms:** Windows (x64/x86), Linux (x64/ARM64), macOS (Intel/Apple Silicon)
- **Multiplayer:** SQLite shared backend, SSH gateway, raw-TCP MUD interface, GMCP for Mudlet/MUSHclient/TinTin++, WebSocket browser terminal
- **Save System:** JSON (single-player file backend) / SQLite (online) with autosave, in-place repair for bloated saves, and 7-day archived restore for permadeath
- **Website:** Live stats API, SSE event feed, xterm.js terminal, real-time admin snoop, banned-IP / banned-account moderation tools, founder-statue hall, leaderboard
- **Discord bridge:** Bidirectional `/gos` mirror, login/logout announcements, live `#server-status` embed, `!who`/`!help` commands

### Project Structure
```
usurper-reborn/
├── Scripts/
│   ├── Core/           # Character, NPC, Item, Monster, GameEngine, GameConfig
│   ├── Systems/        # 70+ game systems
│   │   ├── OceanPhilosophySystem.cs
│   │   ├── AmnesiaSystem.cs
│   │   ├── CompanionSystem.cs
│   │   ├── GriefSystem.cs
│   │   ├── SevenSealsSystem.cs
│   │   ├── StoryProgressionSystem.cs
│   │   ├── PuzzleSystem.cs
│   │   ├── EndingsSystem.cs
│   │   ├── GuildSystem.cs
│   │   ├── WorldBossSystem.cs
│   │   ├── PermadeathHelper.cs
│   │   └── ... (many more)
│   ├── BBS/            # BBS door mode (DropFileParser, SocketTerminal, BBSTerminalAdapter)
│   ├── Server/         # MudServer, PlayerSession, GroupSystem, MudChatSystem, GmcpBridge
│   ├── Locations/      # 30+ game locations
│   ├── AI/             # NPC AI (Brain, Memory, Goals, Emotions, Personality)
│   ├── Data/           # NPCs, Equipment, Monsters, Old Gods, FounderStatueData
│   └── UI/             # Terminal emulator, ANSI art, accessibility detection
├── Console/            # Bootstrap (Program.cs)
├── Localization/       # en.json, es.json, fr.json, hu.json, it.json
├── electron-client/    # Optional Electron graphical client (beta)
├── web/                # Website (index.html, ssh-proxy.js, admin.html, language packs)
├── DOCS/               # Documentation, release notes, BBS setup, server deployment, modding
├── docker/             # Docker compose stack
└── .github/            # CI/CD workflows
```

### Quest & Bounty System
- **Quest Hall:** Central hub for viewing quests and bounties.
- **Starter Quests:** 11 pre-made quests spanning levels 1-100.
- **Open Contract Bounties:** Kill any NPC with a bounty and get paid immediately.
- **King's Bounties:** The reigning monarch posts bounties on criminals and NPCs.
- **Auto-Refresh:** Completed bounties are automatically replaced.
- **Difficulty Scaling:** Easy / Medium / Hard / Extreme tiers.

### Achievement System
50+ achievements across Combat, Progression, Economy, Exploration, Social, and Challenge categories. Tier-scaled Fame rewards. Server-wide broadcasts for Gold-tier and above unlocks. Steam achievements wired through the same path.

### Statistics Tracking
Total monsters killed, gold earned, time played, peak gold, deepest dungeon floor, quests completed, world boss kills, MVP count, achievements unlocked, and dozens of combat / economy / social counters.

### Difficulty Modes
- **Easy:** 150% XP, 50% monster damage, 150% gold
- **Normal:** Standard balanced experience
- **Hard:** 75% XP, 150% monster damage, 75% gold
- **Nightmare:** 50% XP, 200% monster damage, 50% gold

### Family System
- Marriage via the Church, multi-spouse polyamory supported.
- Children inherit traits from both parents, age over real time (1 week real = 1 year in-game).
- Per-child stat bonuses (HP, Strength, Charisma, daily gold).
- Coming-of-age at 18 turns children into adult NPCs that join the world.
- Custody, divorce, infidelity, and child rearing all carry mechanical weight.
- CK-style parenting (24 scenarios with moral choices that shape your child's alignment).

### Game Preferences
Quick settings via the Preferences menu (compact, screen-reader, language, font size, date format, character/monster art, hide intimate scenes, etc.). All preferences saved per character.

## Estimated Playtime

| Playstyle | Hours | Description |
|-----------|-------|-------------|
| **Casual** | 40-60 | Main story, reach level 50-60, see one ending |
| **Full Playthrough** | 100-150 | All seals, all gods defeated, multiple endings |
| **Completionist** | 200-400 | All achievements, all companions, all quests, level 100, multiple NG+ cycles |

*Playtime varies based on difficulty mode and exploration style.*

## How to Connect (Online)

The official server is `play.usurper-reborn.net`. Multiple connection paths:

- **Browser:** [usurper-reborn.net](https://usurper-reborn.net) with an embedded xterm.js terminal. No install. Easiest for new players.
- **SSH:** `ssh usurper@play.usurper-reborn.net -p 4000` (gateway password `play`). Once connected, you'll see the in-game register/login screen.
- **MUD client:** `play.usurper-reborn.net 4000` raw TCP. Mudlet, MUSHclient, TinTin++, etc. GMCP enabled (live HP/MP/SP gauges, room info, character status, chat capture).
- **Steam:** [Usurper Reborn on Steam](https://store.steampowered.com/app/4336570/Usurper_Reborn/), use the in-game `[O] Online Play` menu.
- **Standalone client:** Same as Steam, the `[O] Online Play` menu now opens a server picker (Official server pre-selected at `[1]`, or `[2]` to enter a custom hostname/port for a friend's server).

Each player has 3 free resurrections. Once those run out, the next death is permanent: the character is erased server-wide and the news feed records it. (Single-player saves are unaffected by online permadeath.)

## BBS Door Mode

Run Usurper Reborn as a door game on modern BBS software:

- **Auto-Detection:** Reads DOOR32.SYS and auto-configures. No special flags needed for most setups.
- **Fully Tested:** Synchronet (Standard I/O), EleBBS (Socket), Mystic BBS (Socket + SSH).
- **Should Work:** WWIV, GameSrv, ENiGMA, Renegade (NFU stdio).
- **Native Winsock I/O:** Bypasses .NET socket finalizers to fix the long-standing relaunch bug on EleBBS / Mystic.
- **DOOR32.SYS & DOOR.SYS:** Both drop-file formats supported.
- **Multi-Node Support:** Each node gets isolated session handling.
- **BBS-Isolated Saves:** Saves stored per-BBS so users on different BBSes don't conflict.
- **CP437 Auto-Detection:** Synchronet stdio mode automatically switches output encoding to CP437 for correct box-drawing.
- **SysOp Console:** In-game admin console for player management, difficulty settings, MOTD, online-play toggle, and auto-updates.
- **In-Game Bug Reports:** Players press `!` to submit bug reports directly from a BBS session, posted to Discord with player context.
- **Cross-Platform:** Windows x64/x86, Linux x64/ARM64, macOS.

**Quick Setup for Sysops:**
```bash
UsurperReborn --door32 <path>      # Just point it to your DOOR32.SYS
UsurperReborn --door32 <path> --online    # Local SQLite-backed shared world for THIS BBS's players
UsurperReborn --verbose            # Detailed debug output for troubleshooting
```

For detailed BBS setup, see [DOCS/BBS_DOOR_SETUP.md](DOCS/BBS_DOOR_SETUP.md).

**BBS Online Play:** A BBS player can pick `[O] Online Play` from the main menu to connect to the public game server (or any other Usurper Reborn server with a hostname they know). As of v0.60.8 the connection requires a normal username + password (the previous trusted-passthrough was removed for security; the BBS handle is pre-filled as the username default).

## Recent Highlights

The game ships small patches frequently. Each version has a dedicated release notes file in `DOCS/RELEASE_NOTES_*.md`. Highlights of the recent arc:

- **v0.60.x (Beta launch and post-launch hardening):** Online server wipe, founder statues for the 11 alpha-era pioneers, GMCP support for modern MUD clients, online-mode death system with 3 free resurrections, royal-guard arrest combat (replacing the phantom-arrest debuff), tank rebalance (75%-sticky AoE taunts), full ban-system rewrite (account + IP + CIDR + active-session kick + permadeath world-state purge), Discord bridge with login/logout announcements, server picker in `[O] Online Play`, AUTH security fix (loopback-only trusted auth), SR auto-detect false-positive fix on Steam and BBS.
- **v0.57.x (Alignment, Shields, and many hotfix rollups):** Paired chivalry/darkness movement, Temple Confession path, "Balanced" alignment with both-sides shop discounts, shield-required tank abilities, Warrior Shield Bash, Paladin Holy Shield Slam, save-file repair tooling, save-file resilience pass.
- **v0.56.x (Class Completeness + Difficulty Tuning):** Tank second-taunt abilities at level 40, healer onboarding (Curative Tincture, Mending Meditation, etc.), Tidesworn cohesion, champion / floor-boss / Old God rebalance, stamina-mana economy.
- **v0.55.x (The Specialist):** 24 NPC class specializations (2 per class), spec-driven AI ability priorities.
- **v0.54.x (The Soul Update):** Equipment system overhaul, NPC system overhaul, comprehensive gameplay audit (17 fixes), Vex timed death, Awakening Moments integrated, moments of silence after profound events, NPC dungeon idle comments, dynamic location flavor, moddable game data system phase 1.
- **v0.53.x (Ancestral Spirits):** Mystic Shaman class, comprehensive class/spell audit, relationship system audit, king/prison overhaul, Alethia lore woven through dungeon fragments, ELectron client DD-style combat UI.

For per-version detail, see the dedicated release notes in `DOCS/`. The complete in-CLAUDE.md changelog ships with every clone for archeological purposes.

## Versions Skipped on Purpose

- **0.58.x and 0.59.x:** originally reserved for the Electron graphical client roadmap. That work folded into beta, so the version number jumped straight from 0.57.x to 0.60.0.

## License & Your Rights

**Usurper Reborn is FREE SOFTWARE licensed under GPL v2.**

### Your Rights
- **Use:** Run the game for any purpose.
- **Study:** Examine the complete source code.
- **Share:** Distribute copies to anyone.
- **Modify:** Change the game and distribute improvements.
- **Commercial Use:** Even sell your versions, under GPL v2.

### Source Code
- Complete source included with every download.
- GitHub: https://github.com/binary-knight/usurper-reborn
- All build tools and scripts included.

## Community

Join Discord for discussions, feedback, and updates: **https://discord.gg/EZhwgDT6Ta**

## Acknowledgments

- **Jakob Dangarden:** Created the original *Usurper* (1993), the seed this grew from.
- **Rick Parrish:** Preserved the Pascal source code.
- **Daniel Zingaro:** Tremendous help with the Pascal source.
- **Coosh:** Community code contributor. Diagnosed an XP-formula desync from his own soft-locked Lv.73 character, traced it to 12 duplicated copies of the same function across the codebase, and submitted PR #98 centralizing them into one canonical implementation.
- **Xykier, DJLunacy, evanofficial, maxsond, LowLevelJavaCoder:** Community PRs covering combat loot party switching, smart sell filters, companion auto-equip, screen-reader preference persistence, shield loot generation, and the murder-mechanics rework.
- **The 11 alpha-era founders** commemorated in the Hall of the Ascended (in-game, `/founders`).
- **The BBS Community:** For keeping the spirit alive.
- **All players, testers, and bug reporters** who made beta possible.

---

*"You are not a wave fighting the ocean. You ARE the ocean, dreaming of being a wave."*

## Known Issues (Beta v0.60.8)

- Save files from earlier alpha versions may not be fully compatible.
- BBS FOSSIL mode not natively supported (use `--stdio` flag for FOSSIL-based BBSes via host pipe).
- Steam features only work when game is launched through the Steam client.
- Electron graphical client inventory equip flow is partially functional (interactive overlays in progress).
- Auto-updater for Linux x64 BBS deployments doesn't currently apply the update (under investigation).

**Report bugs:** Press `!` in-game, or [Discord](https://discord.gg/EZhwgDT6Ta), or [GitHub Issues](https://github.com/binary-knight/usurper-reborn/issues).

---

**Status:** BETA v0.60.8. The world is running. [Watch it live.](https://usurper-reborn.net)
