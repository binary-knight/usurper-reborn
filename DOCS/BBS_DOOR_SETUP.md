# Usurper Reborn - BBS Door Setup Guide

This guide explains how to configure Usurper Reborn to run as a door game on BBS software like Synchronet, Mystic, EleBBS, WWIV, GameSrv, ENiGMA, or other systems that support DOOR32.SYS drop files.

## Overview

Usurper Reborn supports running as a traditional BBS door game, allowing users to play through telnet/SSH connections managed by BBS software. The game reads user information from standard drop files and communicates via inherited socket handles or standard I/O (stdin/stdout).

> **Note:** This is a native .NET 8.0 application. It does **not** support FOSSIL drivers or DOS-based serial communication. Use socket mode or standard I/O mode instead.

## Quick Start - It Just Works!

The game automatically detects your BBS software and configures itself. Just point it to your DOOR32.SYS:

```
UsurperReborn --door32 <path-to-door32.sys>
```

**Drop file path parameters vary by BBS software:**

| BBS Software | Path Parameter | Example Command |
|--------------|----------------|-----------------|
| Synchronet | `%f` | `UsurperReborn --door32 %f` |
| EleBBS | `*N\door32.sys` | `UsurperReborn --door32 *N\door32.sys` |
| Mystic | temp dir + node | `UsurperReborn --door32 c:\mystic\temp%3\door32.sys` |
| WWIV | `%T` or temp path | `UsurperReborn --door32 %T\door32.sys` |
| GameSrv | configured path | `UsurperReborn --door32 door32.sys` |
| ENiGMA | `{dropFilePath}` | configured via menu.hjson |

## BBS Compatibility

| BBS Software | I/O Mode | Auto-Detected? | Status |
|--------------|----------|----------------|--------|
| **Synchronet** | Standard I/O | ✅ Yes (by name) | ✅ Fully tested |
| **EleBBS** | Socket | (default) | ✅ Fully tested |
| **Mystic BBS (telnet)** | Socket | (default) | ✅ Fully tested |
| **Mystic BBS (SSH)** | Standard I/O | ✅ Yes (redirected I/O) | ✅ Fully tested |
| **WWIV** | Standard I/O | ✅ Yes (by name) | Should work |
| **GameSrv** | Standard I/O | ✅ Yes (by name) | Should work |
| **ENiGMA½** | Standard I/O | ✅ Yes (by name) | Should work |

> **Auto-Detection:** The game uses two methods to auto-detect the correct I/O mode:
> 1. **BBS name** (from DOOR32.SYS line 4): Synchronet, GameSrv, ENiGMA, and WWIV are detected by name
> 2. **Redirected I/O**: If the BBS redirects stdin/stdout (e.g., for SSH or TLS connections), the game automatically uses Standard I/O mode
>
> No special flags needed! The same door configuration works for both telnet and SSH users.

> **Windows Note:** The console window is automatically hidden when running in socket mode. Use `--verbose` to keep it visible for debugging.

## Command Line Options

```
UsurperReborn --door <path>      # Auto-detect drop file type
UsurperReborn --door32 <path>    # Explicitly load DOOR32.SYS
UsurperReborn --doorsys <path>   # Explicitly load DOOR.SYS
UsurperReborn --node <directory> # Search directory for drop files
UsurperReborn --local            # Local testing mode (no BBS)
UsurperReborn --stdio            # Force Standard I/O mode
UsurperReborn --verbose, -v      # Enable detailed debug output
UsurperReborn --help             # Show help
```

### I/O Mode Flags

| Flag | Description |
|------|-------------|
| `--stdio` | Forces Standard I/O mode. Usually not needed - game auto-detects. |
| `--verbose` or `-v` | Enables detailed debug output. Keeps console window visible on Windows. |

---

## Synchronet BBS

Synchronet uses `%f` command line specifier for the full drop file path.

### Step 1: Copy Game Files

```
/sbbs/xtrn/usurper/
├── UsurperReborn.exe (or UsurperReborn on Linux)
├── *.dll files
├── Data/
└── Saves/
```

### Step 2: Configure in SCFG

Navigate to: `External Programs` → `Online Programs (Doors)` → `Add`

| Setting | Value |
|---------|-------|
| Name | Usurper Reborn |
| Internal Code | USURPER |
| Start-up Directory | ../xtrn/usurper |
| Command Line | `UsurperReborn --door32 %f` |
| Native Executable | **Yes** |
| Use Shell or New Context | Yes |
| BBS Drop File Type | DOOR32.SYS |
| **I/O Method** | **Standard** or **Standard, WWIV Color** |

> **Important:** I/O Method must be set to Standard, not Socket. The game auto-detects Synchronet.

**Reference:** [Synchronet Door Setup Guide](https://wiki.synchro.net/howto:door:index)

---

## EleBBS

EleBBS co-created the DOOR32.SYS standard and works with socket mode. Use `*N` to represent the node directory.

### Step 1: Copy Game Files

```
C:\ELE\DOORS\USURPER\
├── UsurperReborn.exe
├── *.dll files
├── Data/
└── Saves/
```

### Step 2: Configure Door

```
Door Path    : C:\ELE\DOORS\USURPER\
Door Command : UsurperReborn --door32 *N\door32.sys
Drop File    : Door32.sys
```

The console window hides automatically in socket mode.

### Troubleshooting EleBBS

If the door doesn't connect:
1. Use `--verbose` to see detailed connection info
2. Check line 2 of DOOR32.SYS has a non-zero socket handle
3. Try `--stdio` flag if socket mode fails

**Reference:** [DOOR32.SYS Specification](https://github.com/NuSkooler/ansi-bbs/blob/master/docs/dropfile_formats/door32_sys.txt)

---

## Mystic BBS

Mystic creates drop files in each node's temp directory (e.g., `c:\mystic\temp1\` for node 1). The same door configuration works for both telnet and SSH users - the game auto-detects SSH connections and switches to Standard I/O mode automatically.

**Important Mystic variables:**
- `%3` = Node number (NOT drop file path)
- `%0` = Socket handle
- Drop files are in: `<mystic>\temp<node>\door32.sys`

### Step 1: Copy Game Files

```
/mystic/doors/usurper/
├── UsurperReborn.exe (or UsurperReborn on Linux)
├── *.dll files
├── Data/
└── Saves/
```

### Step 2: Configure Door (D3 command)

In your menu configuration, use the D3 command which creates DOOR32.SYS:

```
Menu Command : D3
Data         : CD<c:\mystic\doors\usurper>UsurperReborn --door32 c:\mystic\temp%3\door32.sys
```

The `CD<path>` helper changes to the door directory before execution.

### SSH Support

No additional configuration needed. When a user connects via SSH, Mystic redirects stdin/stdout through the SSH encryption layer. The game detects this and automatically switches to Standard I/O mode, so all game output is properly encrypted.

**Reference:** [Mystic BBS Wiki - Menu Commands](https://wiki.mysticbbs.com/doku.php?id=menu_commands)

---

## WWIV BBS

WWIV uses `%N` for node number. Drop files are located in temp directories.

**WWIV 5.5+ drop file path:** `\wwiv\e\%N\temp\`
**WWIV 5.3 and earlier:** `\wwiv\temp%N\`

### Step 1: Copy Game Files

```
c:\wwiv\doors\usurper\
├── UsurperReborn.exe
├── *.dll files
├── Data/
└── Saves/
```

### Step 2: Configure Chain

Add a new chain in WWIV:

```
A) Description  : Usurper Reborn
B) Filename     : c:\wwiv\doors\usurper\UsurperReborn --door32 c:\wwiv\e\%N\temp\door32.sys
...
K) Exec Mode    : STDIO
```

> **Important:** Set Exec Mode to STDIO. The game auto-detects WWIV.

**Reference:** [WWIV BBS Doors Documentation](https://docs.wwivbbs.org/en/latest/chains/doors/)

---

## GameSrv

GameSrv is Rick Parrish's C# door game server. It generates DOOR32.SYS automatically.

### Configuration

Edit your door configuration in GameSrv:

```ini
[Usurper]
Name=Usurper Reborn
Command=UsurperReborn
Parameters=--door32 door32.sys
Native=True
```

The game auto-detects GameSrv and uses Standard I/O mode.

**Reference:** [GameSrv on GitHub](https://github.com/rickparrish/GameSrv)

---

## ENiGMA½ BBS

ENiGMA uses the `abracadabra` module for doors with variables like `{dropFilePath}` and `{node}`.

> **Note:** ENiGMA does not support direct DOOR32.SYS socket descriptor sharing due to Node.js limitations, but Standard I/O works.

### Configuration (menu.hjson)

```hjson
usurperReborn: {
    desc: Usurper Reborn
    module: abracadabra
    config: {
        name: Usurper Reborn
        dropFileType: DOOR32
        cmd: /path/to/usurper/UsurperReborn
        args: [
            "--door32", "{dropFilePath}"
        ]
        io: stdio
    }
}
```

The game auto-detects ENiGMA and uses Standard I/O mode.

**Reference:** [ENiGMA½ Local Doors Documentation](https://nuskooler.github.io/enigma-bbs/modding/local-doors.html)

---

## DOOR32.SYS Format Reference

```
2                           Line 1:  Comm type (0=local, 1=serial, 2=telnet)
1234                        Line 2:  Socket handle
38400                       Line 3:  Baud rate
Synchronet BBS v3.19        Line 4:  BBS software name (used for auto-detection)
42                          Line 5:  User record number
John Smith                  Line 6:  User's real name
CyberKnight                 Line 7:  User's alias/handle
100                         Line 8:  Security level
60                          Line 9:  Time remaining (minutes)
1                           Line 10: Emulation (0=ASCII, 1=ANSI, 2=Avatar, 3=RIP)
1                           Line 11: Node number
```

---

## Testing Locally

### Create a test DOOR32.SYS:
```
0
0
0
Test BBS
1
Test User
TestPlayer
100
999
1
1
```

### Run:
```bash
UsurperReborn --door32 door32.sys
```

Or use local mode (no drop file):
```bash
UsurperReborn --local
```

---

## Troubleshooting

### Using Verbose Mode

Add `--verbose` to see detailed debug output:

```bash
UsurperReborn --door32 door32.sys --verbose
```

This shows:
- Raw drop file contents
- Parsed session info (CommType, SocketHandle, BBS name)
- Socket/stdio initialization details
- Any errors with full stack traces

### Common Issues

#### "Could not parse drop file"
- Verify the drop file exists at the specified path
- Check file permissions
- On Linux, check case sensitivity (`door32.sys` vs `DOOR32.SYS`)

#### "Failed to initialize socket"
- Try `--stdio` flag for Standard I/O mode
- Verify your BBS is passing socket handles correctly
- Check the socket handle (line 2) is non-zero for telnet connections

#### No output / garbled text
- Ensure terminal emulation is ANSI
- Set "Native Executable" to Yes
- Try `--stdio` flag

#### Output shows on server console but not remotely
- **Synchronet/WWIV/GameSrv/ENiGMA:** Should auto-detect. If not, add `--stdio`
- **EleBBS:** Socket mode should work. Use `--verbose` to debug.
- **Mystic (telnet):** Socket mode should work automatically.
- **Mystic (SSH):** Auto-detects redirected I/O and switches to Standard I/O mode.
- Verify BBS I/O Method matches expected mode

#### "Bad packet length" error on SSH
- This means raw data is being written to the SSH socket, bypassing encryption
- The game should auto-detect SSH via redirected I/O. If not, add `--stdio`
- Do NOT use socket mode with SSH connections

---

## Platform Notes

### Windows
- Use `UsurperReborn.exe`
- Self-contained builds don't require .NET runtime
- Console auto-hides in socket mode
- Both x64 and x86 builds available

### Linux
- Use `./UsurperReborn`
- Set executable permission: `chmod +x UsurperReborn`
- Drop file names may be lowercase - game checks both cases

### macOS
- Use `./UsurperReborn`
- Apple Silicon (ARM64) supported: `-r osx-arm64`

---

## SysOp Administration Console

SysOps with security level 100 or higher can access an in-game administration console to manage the game on their BBS.

### Accessing the Console

When you connect to the door as a SysOp (security level 100+), you'll see a prompt at the **BBS Door welcome screen** (before character selection):

```
╔═══════════════════════════════════════════════════════════════════╗
║  SysOp detected! Press [%] for Administration Console            ║
╚═══════════════════════════════════════════════════════════════════╝
(Or press Enter to continue to the game)
```

Press `%` to open the SysOp Console, or press Enter to continue to the normal game.

> **Note:** The SysOp Console is accessible BEFORE any player saves are loaded. This allows SysOps to manage the game, reset data, delete players, and apply updates without any game state interference.

### Available Functions

| Category | Function | Description |
|----------|----------|-------------|
| **Game Management** | View All Players | List all player saves with file info |
| | Delete Player | Permanently remove a player's save file |
| | Reset Game | Complete game wipe (deletes ALL data) |
| **Game Settings** | Difficulty | Adjust XP, gold, monster HP/damage multipliers |
| | Set MOTD | Set Message of the Day for players |
| **Monitoring** | View Statistics | Player count, NPC status, story progress |
| | View Debug Log | Paginated debug log viewer |
| | View Active NPCs | Paginated list of all NPCs with status |
| **System Maintenance** | Check for Updates | Download and install game updates |

### Security Level Threshold

The default SysOp security level is 100, which is standard for most BBS software. The game reads the security level from:
- **DOOR32.SYS**: Line 8
- **DOOR.SYS**: Line 16

**Configuring the threshold:**

Different BBS software uses different security levels for SysOps. You can configure the threshold via command line:

```bash
UsurperReborn --door %f --sysop-level 90
```

This setting is also saved in `sysop_config.json`, so you only need to specify it once. Future runs will use the saved value.

### Configuration Persistence

All game configuration changes made through the SysOp Console are automatically saved to `sysop_config.json` in your BBS's save directory (e.g., `Saves/MyBBS/sysop_config.json`). Settings persist across door restarts and include:

- Message of the Day (MOTD)
- Difficulty multipliers (XP, Gold, Monster HP, Monster Damage)
- SysOp security level threshold

---

## Multi-Node Support

Each node automatically gets its own:
- Drop file (handled by BBS)
- Save isolation (by BBS name + username)

---

## Save File Location

Saves are stored in `<game directory>/Saves/<BBS Name>/` and are keyed by the username from the drop file.

---

## Support

- **Discord**: https://discord.gg/EZhwgDT6Ta
- **GitHub Issues**: https://github.com/binary-knight/usurper-reborn/issues

## References

- [DOOR32.SYS Specification](https://github.com/NuSkooler/ansi-bbs/blob/master/docs/dropfile_formats/door32_sys.txt)
- [Synchronet Door Setup Guide](https://wiki.synchro.net/howto:door:index)
- [Mystic BBS Wiki](https://wiki.mysticbbs.com/doku.php?id=menu_commands)
- [WWIV BBS Documentation](https://docs.wwivbbs.org/en/latest/chains/doors/)
- [GameSrv on GitHub](https://github.com/rickparrish/GameSrv)
- [ENiGMA½ Documentation](https://nuskooler.github.io/enigma-bbs/modding/local-doors.html)

---

*Usurper Reborn is a faithful recreation of the classic 1993 BBS door game by Jakob Dangarden.*
