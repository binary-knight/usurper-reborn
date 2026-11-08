---
title: Ways to play
path: /wiki/en/getting-started/connections/
checked: 1.3.13
sources: README.md, DOCS/BBS_DOOR_SETUP.md, DOCS/SERVER_DEPLOYMENT.md, web/index.html
history: 1.3.13 | none
history: 1.3.12 | none
history: 1.3.11 | The play page shows the Continue key on Enter screens in every language, draws 256 colour and true colour art in colour, shows the whole Main Street text, adds a scroll bar when the right page runs past the window and gives key buttons to choices written (E)quip or (P)ray, (D)esecrate, or (L)eave, and the web service listens on loopback only unless WEB_BIND_HOST is set.
history: 1.3.10 | The Docker page links to the play page at /play/.
history: 1.3.9 | The website's Play buttons open the play page at /play/, a full page terminal with a tap pad on phones, a View choice of the Classic page or the Chronicle book view, also before login, and single key menus, on by default and changed with E in Settings.
history: 1.3.8 | none
history: 1.3.7 | none
history: 1.3.6 | none
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | none
history: 1.3.1 | none
history: 1.3.0 | none
history: 1.2.7 | none
history: 1.2.6 | none
history: 1.2.5 | The SSH login menu has G to change the language of its screens before you log in.
history: 1.2.4 | A reconnect waits for the old session to finish saving, and you stay on /who, /tell, groups and broadcasts after it.
history: 1.2.3 | none
---
## Browser and SSH

The [website's Play section](/#connect) has the connection information, and its Play buttons open the browser terminal, the play page at /play/, in a new tab. The browser displays the same text game; keep the tab open during a session. Connection addresses belong on the website rather than in this versioned rules reference.

An SSH client is another way to reach a hosted game. Use the server operator's published connection instructions. A dropped connection is not the same as choosing to quit, so reconnect and inspect your character before continuing. Online, a reconnect waits for your old session to finish saving, usually under a second, before your character loads, and you stay listed as online.

## Local single-player

Download a release from the [project releases](https://github.com/binary-knight/usurper-reborn/releases) and use the launcher supplied for your platform. Local saves and a hosted online character are separate.

## BBS door

A BBS operator can run the game as a door using dropfile integration. Players enter through that BBS's door menu; operators should use the repository's [BBS setup guide](https://github.com/binary-knight/usurper-reborn/blob/v1.2.0/DOCS/BBS_DOOR_SETUP.md).

## Readable menus

Use the game's screen-reader mode when a grid or decorative terminal layout makes navigation difficult. Read the current prompt, especially when changing menu presentation or moving between districts.
