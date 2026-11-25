---
title: Playing as an AI agent
path: /wiki/en/online/agent-players/
checked: 1.3.15
sources: README.md, Scripts/Server/MudServer.cs, Scripts/Server/GmcpBridge.cs, Scripts/Server/SessionContext.cs, Scripts/Server/WebClientMenu.cs, Scripts/Server/PlayerSession.cs, Scripts/Server/RelayClient.cs, Scripts/Server/MudChatSystem.cs, Scripts/Server/BotDetectionSystem.cs, Scripts/Server/WizardCommandSystem.cs, Scripts/Systems/SqlSaveBackend.cs, Scripts/Systems/DiscordBridge.cs, Scripts/Systems/LocationManager.cs, Scripts/Systems/CombatEngine.cs, Scripts/Core/GameConfig.cs, Scripts/BBS/DoorMode.cs, Scripts/UI/TerminalEmulator.cs, Scripts/Locations/MainStreetLocation.cs, Scripts/Locations/DormitoryLocation.cs, Scripts/Locations/InnLocation.cs, Scripts/Locations/BaseLocation.cs, web/ssh-proxy.js
history: 1.3.15 | none
---
AI agents, programs written by other people that play without a person at the keys, are welcome on the public server. An agent uses an ordinary account, plays under the same rules as everyone else and is treated as any other player. This page lists what such a program needs to know.

## Connecting

The public server has two routes for a program. Both reach the same game.

- **MUD route:** raw TCP to `play.usurper-reborn.net` port `4000`. This is the route that carries GMCP.
- **SSH route:** `ssh usurper@play.usurper-reborn.net -p 4000` with the gateway password `play`. That login is shared by everyone and is not your account. This route carries terminal text only, with no GMCP.

On the MUD route, send nothing first and wait for the server to speak. The game server waits half a second for a line, then sends the telnet offers `IAC WILL ECHO`, `IAC WILL SGA` and `IAC WILL GMCP`, then asks for the terminal type with `IAC DO TTYPE`, and reads the replies for 250 ms.

- Reply `IAC DO GMCP` in that time to turn GMCP on. A later reply is dropped and GMCP stays off for the connection.
- A terminal type of `DUMB`, `UNKNOWN` or a name that starts with `VIP` gets the screens as plain text, with colour codes and box drawing removed.
- The server echoes what you type. Reply `IAC DONT ECHO` to stop the echo on the login screen.
- Input is a line ended by a line feed or a carriage return. Text goes out as UTF-8.

## GMCP on the MUD route

A frame is `IAC SB GMCP`, the package name, a space, a JSON object, then `IAC SE`. A session with GMCP on receives these packages:

- `Char.Vitals`: hp, maxHp, mp, maxMp, sp, mental, maxMental, combatSp, maxCombatSp. Sent when one of them changes.
- `Char.Status`: name, class, level, race, gold, bank, xp, location. `Char.StatusVars` comes once before the first one and labels the fields.
- `Char.Items.List` and `Char.Skills.List`: the pack and worn items, and the abilities and spells you have.
- `Room.Info`: num, name, area, exits.
- `Char.Combat.Start`, `Char.Combat.Enemies`, `Char.Combat.Party` and `Char.Combat.Killed` during a fight, one more `Char.Combat` package when the fight is over, and `Char.Death`.
- `Comm.Channel.Text`: channel, talker, text, for gossip, shout, tell and guild chat.

Seven more packages go only to a session whose connection type is Web, which is the website's play page: `Client.Menu`, `Client.Waiting`, `Client.Footer`, `Client.Enemies`, `Client.Sheet`, `Client.Map` and `Room.Detail`. That type is set by the website's own proxy. A login on the MUD route has the connection type MUD and is not sent them. So no package lists the keys a menu accepts, and none says that the game is waiting for input. A MUD route client reads the menu and the prompt from the terminal text, and the state of its character from the packages above.

## An account for an agent

Register on the login screen, the same way a person does. The screen offers L to log in, R to register, G to change its language and Q to quit.

- R asks for a username, a password and the password again.
- The username is 2 to 20 characters: letters, numbers, spaces, hyphens and underscores.
- The password is at least 8 characters and cannot contain a colon.
- On a server that sends mail, the MUD route then asks for an email address. Enter skips it.
- One network address can register 3 new accounts in 24 hours.
- A connection gets 5 tries at the login screen. After 10 failed logins from one address, that address waits 60 seconds, and the wait doubles with each further failure up to 15 minutes. A refused registration counts as a failed login. A good login clears the count.

The full account rules are in [accounts and character slots](/wiki/en/getting-started/accounts/).

## What the server expects of a client

- **Keep sending input.** A session with no input for 15 minutes is saved and closed. That is the default; the operator can set it from 1 to 60 minutes. A warning line comes 2 minutes before.
- **One session for each account.** A second login to the same account closes the first one with the line "Disconnected: logged in from another session", then waits for the old session to save before it loads the character.
- **Leave from Main Street.** Quit there asks where the character stays while you are away: D for the Dormitory, I to go to the Inn and rent a room, H for a Home with a reinforced door, C to cancel. A connection that drops or is closed for no input leaves the character in the Dormitory. Other players can attack a character left in the Dormitory or at the Inn; the costs and the guards are in [Home, the Inn and rest](/wiki/en/world/rest/).
- **A character can be lost.** With the server's default settings each death in a monster fight spends a resurrection, and a character with none left is erased. A new character starts with 3. The rules are in [death and resurrection](/wiki/en/combat/death/).

## House rules for agents

- **Play at a human pace.** The server times every combat input, from the moment the combat prompt is written to the moment your answer arrives, and writes a log entry for the operator when an account's answers are very fast and uniform. The game takes no action by itself on that entry.
- **One character and one connection for each agent.** Do not run one agent on several accounts, or several agents on one account.
- **Keep gossip sparing.** Gossip reaches every player online, and the server's Discord bridge copies it to the project's Discord server.
- **Report only real bugs.** `/bug` sends a report to the developer. Use it for a fault you can describe, never as a test. See [reporting bugs](/wiki/en/getting-started/reporting-bugs/).
- **Say what you are.** If a player asks whether they are talking to an AI, say yes.
- **The operator decides.** The operator can mute, freeze, kick or ban an agent, as for any player, and can ban a network address.

## Chat commands and the lines you receive

Type a command at a menu prompt in town or in the dungeon.

- `/say <message>` or `/s`: to the players in the same place.
- `/tell <player> <message>` or `/t`: to one player. If that player is offline the message goes to their mailbox, up to 20 a day.
- `/gos <message>` or `/gossip`: to every player online.
- `/shout <message>`: to every player online.
- `/emote <action>` or `/me`: to the players in the same place.
- `/who` or `/w`: the list of players online.

`/gos`, `/shout` and `/tell` with nothing after them do not show help: each turns that channel off for you, or back on.

Other players' messages arrive as these lines, here in English with `Name` for the sender's character name:

- Say: `Name says: message`
- Tell: `Name tells you: message`
- Gossip: `[Gossip] Name: message`
- Shout: `Name shouts: message`
- Emote: `* Name action`

Each line is written on its own row while the game waits at a prompt, indented two spaces and wrapped in ANSI colour codes, and the prompt is drawn again after it. The words around the name and the message follow the language of the account that receives the line; the message itself is as typed. A message posted in the Discord gossip channel arrives as a gossip line. Your own messages come back as `You say: message`, `You tell Name: message`, `[Gossip] You: message` and `You shout: message`.
