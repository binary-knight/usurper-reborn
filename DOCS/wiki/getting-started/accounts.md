---
title: Accounts and character slots
path: /wiki/en/getting-started/accounts/
checked: 1.2.5
sources: Scripts/Core/GameEngine.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/SqlSaveBackend.cs, Scripts/Server/MudServer.cs, Scripts/Server/RelayClient.cs, Scripts/Systems/OnlinePlaySystem.cs, Scripts/Systems/CharacterCreationSystem.cs, Scripts/Systems/EndingsSystem.cs, Scripts/Locations/PantheonLocation.cs, Scripts/Locations/ArenaLocation.cs, Scripts/BBS/DoorMode.cs, Scripts/Systems/SysOpConfigSystem.cs, Scripts/Systems/ServerSettingsRegistry.cs, README.md
history: 1.2.5 | The SSH login screen gains G to change its language, and the default message of the day and the idle warning show in your language.
---
How you sign in, how many characters you can keep, and what you can set before you enter the game. Single-player, online and BBS play each handle this differently. For where to connect, see [ways to play](/wiki/en/getting-started/connections/).

## Online accounts

An online account is a username and a password kept by the game server. The browser, MUD client and SSH paths end at the same login screen, with four choices: L to log in, R to register, G to change the language of the login screen, and Q to quit.

- **Browser and MUD clients.** The [website's Play section](/#connect) opens a browser terminal. A MUD client such as Mudlet, MUSHclient or TinTin++ connects to the game port directly and gets the same login screen, with GMCP data when the client asks for it. A client that reports a screen reader terminal type gets the plain text version of the screen.
- **SSH.** The SSH gateway uses one shared gateway login, published with the connection instructions. It is not your account. After it, a login screen with the same L, R, G and Q choices asks for your own username and password. Its G changes the language of these login screens only; it is not saved to an account registered there.
- **The standalone game.** O (Online Multiplayer) on the main menu offers the official server or a server address and port you type in. Its own login menu has L, R and Q, and uses the language the game is already set to. After you log in it offers to remember your login on this computer: Y, N, or D to stop asking.

Registration rules:

- The username is 2 to 20 characters: letters, numbers, spaces, hyphens and underscores. It must not already exist, in any capitalization.
- The password is at least 4 characters and cannot contain a colon. You type it twice.
- The server limits how many accounts one network address can create and slows repeated failed logins.

The language you chose on the login screen is saved to a new account. Online, your screen reader setting and language belong to the account and come back at your next login. Only one connection per account plays at a time: logging in again replaces the older session.

P on the character screen changes your password. It asks for the current one first.

## Character slots online

Each online account has one main character. Your account name is the main character's save key; during creation you may choose a different display name, or press Enter to use the account name.

An account can also hold one alt character, a second mortal character with its own name, class and progress. The character screen offers M (Create Alt Character) when all of these are true:

- your main character has reached level 25, is an immortal, or has earned the alt slot by ascending earlier;
- the account has no alt yet.

The slot stays open after your god renounces immortality and after New Game Plus. There is only ever one alt; ascending does not add more slots. A main character that is still below level 25 sees the message "You need a character at level 25 or higher, or an immortal, to create an alt character." when pressing M.

What an alt cannot do:

- An alt cannot ascend to godhood. It is refused right after answering Y, before it gives anything up. See [immortals and player-gods](/wiki/en/gods/player-gods/).
- Your main and your alt never meet in the Arena.
- Arena gold won by an alt is capped at 1,000 plus 100 per level of the alt.

An immortal main character keeps slot 1 and is marked [IMMORTAL] on the character screen. Slot 2 is the alt.

## The character screen online

After you log in, the character screen lists your characters with level, class and the time last played, then these keys:

- **1**: Play your main character
- **2**: Play your alt character
- **M**: Create Alt Character, when the slot is open
- **D**: Delete Alt Character
- **N**: Create your main character, or replace it
- **I, H, B, C**: The story so far, Usurper history, the BBS and server list, credits
- **A**: Screen reader mode on or off
- **Z**: Compact mode on or off
- **S**: Spectate a player
- **G**: Language
- **P**: Change password
- **Q**: Quit

Keys appear only when they apply: 2 and D need an alt, M needs an open slot, S and P need a connection to a game server. Pressing Enter alone plays your main character, or your alt if you only have an alt, or starts creation if you have neither.

N on an account that has a main character warns you, then asks you to type DELETE in capitals. Anything else cancels. The old main character is removed along with its world ties (claimed quests, the King's bounty on it, guild membership, children and queued deliveries), and creation starts at once. An alt is not affected. D asks for DELETE the same way and removes only the alt.

You cannot undo a delete yourself. The server keeps a deleted character's save for 7 days, and within that time only a server admin can restore it. A restore brings back the save, not the world ties removed at deletion.

Screen reader, compact mode and language chosen here apply to the session. A new character takes them on. An existing character loads its own saved screen reader and compact settings, while a language picked on this screen in the same session replaces the character's saved language.

## Single-player

A local game has no account and no login. The main menu offers:

- **S**: Single-player, to choose or create a character
- **O**: Online Multiplayer
- **G**: Game editor
- **I, H, B, C**: The story so far, Usurper history, the BBS and server list, credits
- **A**: Screen reader mode on or off
- **Z**: Compact mode on or off
- **L**: Language
- **Q**: Quit

There is no fixed number of characters. S lists every character saved on this computer, and N creates another under any name not already used (capitalization does not matter). With no saves at all, S goes straight to creation. Choosing a character lists its saves, autosaves and manual saves, up to 10, and loads the one you pick. A save the game cannot read is marked and opens a recovery menu instead.

D on a character's save list deletes all of that character's save files after you type DELETE. The game keeps no copy to restore.

Local saves and online characters are separate. Alt characters exist only online. Screen reader mode chosen on the local menu is remembered for the next launch. On Windows, when the game finds a running screen reader at startup it turns screen reader mode on by itself, except inside the WezTerm terminal; the `--screen-reader` launch option always turns it on.

## BBS doors

Through a BBS, the BBS has already signed you in: your BBS username is the account, and there is no game password. A BBS door uses the same character screen as the online game, in a shorter layout that fits 24 line terminals, with the same main and alt rules. The short layout does not list the language key; G still changes the language. O, if the sysop allows it, connects to another server, where you log in with that server's own account.

## Server and sysop settings

This section is for people who run their own game: a BBS sysop, a private server operator or a single-player owner. No setting, file or flag changes the number of characters per account or the alt slot rules. Those are fixed in the game.

Command line flags:

- `--local`: Local session with no BBS
- `--door`, `--door32`, `--doorsys`, `--node`: Run as a BBS door from a drop file; turns on online mode
- `--online`: Online mode with the shared database (default off)
- `--user <name>`: Account name for the session, for an SSH ForceCommand setup (default in-game login)
- `--db <path>`: Database file (default `usurper_online.db` next to the program)
- `--mud-server`: Run the multiplayer game server
- `--mud-port <port>`: Game server port (default 4000)
- `--mud-relay`: Relay used as the SSH gateway command
- `--admin <name>`: Gives a username full admin rights on the game server; repeatable (default none)
- `--auto-provision`: Creates accounts for password-less logins from the local machine (default off)
- `--sysop-level <n>`: BBS security level that opens the SysOp console (default 100)
- `--idle-timeout <min>`: Idle disconnect, 1 to 60 minutes (default 15)
- `--screen-reader`: Start in screen reader mode (default off)
- `--worldsim`, `--no-worldsim`, `--sim-interval`, `--npc-xp`, `--save-interval`: Background world simulation
- `--editor`: Open the game editor

The built-in help, `--help`, lists the door and online flags. Password-less logins are accepted only from the same machine.

A multiplayer server keeps its settings in the game database and the admin dashboard edits them. The settings and defaults:

- Starting resurrections for new characters: default 3, 0 to 99
- Online permadeath: default on
- XP, gold, monster HP and monster damage multipliers: default 1.0, 0.1 to 10.0
- Disable online play: default off
- Idle timeout in minutes: default 15, 1 to 60
- Message of the day: the default greeting, up to 500 characters

A local game that is not in online mode reads `sysop_config.json` from its save folder. Its fields and defaults: message of the day, daily turns 325 (1 to 9999), the four multipliers at 1.0 (0.1 to 10.0), maximum dungeon level 100 (1 to 100), SysOp security level 100 (1 to 255), idle timeout 15, default color theme, disable online play off, screen reader off, and the online server address and port that O offers first. Online and BBS door games skip this file and use the database settings.
