# Usurper Reborn - BBS Door Setup Guide

This guide explains how to configure Usurper Reborn to run as a door game on BBS software like Synchronet, Mystic, or other systems that support DOOR32.SYS or DOOR.SYS drop files.

## Overview

Usurper Reborn supports running as a traditional BBS door game, allowing users to play through telnet/SSH connections managed by BBS software. The game reads user information from standard drop files and communicates via inherited socket handles or console I/O.

## Supported Drop File Formats

### DOOR32.SYS (Recommended)
Modern 11-line format with socket handle support for telnet connections.

### DOOR.SYS
Legacy 52-line format. Works with console I/O (no direct socket support).

## Command Line Options

```
UsurperReborn --door <path>      # Auto-detect drop file type
UsurperReborn --door32 <path>    # Explicitly load DOOR32.SYS
UsurperReborn --doorsys <path>   # Explicitly load DOOR.SYS
UsurperReborn --node <directory> # Search directory for drop files
UsurperReborn --local            # Local testing mode (no BBS)
UsurperReborn --stdio            # Force Standard I/O mode (for Synchronet)
UsurperReborn --fossil <port>    # Force FOSSIL/serial mode on COM port
UsurperReborn --com <port>       # Same as --fossil
UsurperReborn --verbose, -v      # Enable detailed debug output
UsurperReborn --help             # Show help
```

### I/O Mode Flags

| Flag | Description |
|------|-------------|
| `--stdio` | Forces Standard I/O mode using ANSI escape codes. Recommended for Synchronet. |
| `--fossil <port>` | Forces FOSSIL/serial mode on the specified COM port (e.g., `COM1`). |
| `--verbose` or `-v` | Enables detailed debug output for troubleshooting connection issues. |

## Synchronet BBS Setup

### Step 1: Copy Game Files

Copy the Usurper Reborn files to your Synchronet doors directory:
```
/sbbs/xtrn/usurper/
├── UsurperReborn.exe (or UsurperReborn on Linux)
├── UsurperReborn.dll
├── Data/
└── Saves/
```

### Step 2: Configure External Program

In SCFG (Synchronet Configuration), navigate to:
`External Programs` → `Online Programs (Doors)` → `Add`

#### Option A: Standard I/O Mode (Recommended)

This is the recommended configuration for best compatibility:

| Setting | Value |
|---------|-------|
| Name | Usurper Reborn |
| Internal Code | USURPER |
| Start-up Directory | ../xtrn/usurper |
| Command Line | UsurperReborn --door32 %f --stdio |
| Clean-up Command | (leave blank) |
| Execution Cost | 0 |
| Access Requirements | (your preference) |
| Intercept Standard I/O | No |
| Native Executable | Yes |
| Use Shell to Execute | No |
| Modify User Data | No |
| Execute on Event | No |
| Pause After Execution | No |
| BBS Drop File Type | Door32.sys |
| Place Drop File In | Node Directory |
| **I/O Method** | **Standard** |

The `--stdio` flag tells the game to use ANSI escape codes for colors instead of Windows Console API calls, which ensures colors work correctly when stdin/stdout are redirected by Synchronet.

#### Option B: Socket I/O Mode

Alternative configuration using direct socket communication:

| Setting | Value |
|---------|-------|
| Name | Usurper Reborn |
| Internal Code | USURPER |
| Start-up Directory | ../xtrn/usurper |
| Command Line | UsurperReborn --door %f |
| Clean-up Command | (leave blank) |
| Execution Cost | 0 |
| Access Requirements | (your preference) |
| Intercept Standard I/O | No |
| Native Executable | Yes |
| Use Shell to Execute | No |
| Modify User Data | No |
| Execute on Event | No |
| Pause After Execution | No |
| BBS Drop File Type | Door32.sys |
| Place Drop File In | Node Directory |
| **I/O Method** | **Socket** |

**Note:** Socket mode may not work on all systems. If you experience connection issues or the game doesn't start, try Standard I/O mode instead.

## Mystic BBS Setup

### Step 1: Copy Game Files

Copy files to your Mystic doors directory:
```
/mystic/doors/usurper/
```

### Step 2: Configure Door

In Mystic's configuration, add a new door:

```
Door Name    : Usurper Reborn
Door Path    : /mystic/doors/usurper/
Door EXE     : UsurperReborn --door %3
```

The `%3` parameter passes the path to DOOR32.SYS.

## Example Drop Files

### DOOR32.SYS Example

```
2
1234
38400
Synchronet BBS v3.19
42
John Smith
CyberKnight
100
60
1
1
```

**Line-by-line breakdown:**
| Line | Value | Description |
|------|-------|-------------|
| 1 | 2 | Connection type (0=local, 1=serial, 2=telnet) |
| 2 | 1234 | Socket handle (for telnet I/O) |
| 3 | 38400 | Baud rate |
| 4 | Synchronet BBS v3.19 | BBS software name |
| 5 | 42 | User record number |
| 6 | John Smith | User's real name |
| 7 | CyberKnight | User's alias/handle |
| 8 | 100 | Security level |
| 9 | 60 | Time remaining (minutes) |
| 10 | 1 | Emulation (0=ASCII, 1=ANSI) |
| 11 | 1 | Node number |

### DOOR.SYS Example (First 31 lines)

```
COM1:
38400
8
1
38400
Y
Y
Y
Y
John Smith
Somewhere, USA
555-1234
555-5678
PASSWORD
100
50
01-15-2025
3600
60
GR
24
Y
1,2,3,4,5
1
01-15-2026
42
X
100
50
1000
32767
```

**Key fields:**
| Line | Description |
|------|-------------|
| 1 | COM port (COM0: for local) |
| 4 | Node number |
| 10 | User's full name |
| 16 | Security level |
| 20 | Time remaining (minutes) |
| 21 | Graphics mode (GR=ANSI, NG=ASCII) |
| 22 | Screen height |
| 26 | User record number |

## Testing Locally

To test the door mode without a BBS:

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

### Run with:
```bash
UsurperReborn --door door32.sys
```

Or use local mode (no drop file needed):
```bash
UsurperReborn --local
```

## Troubleshooting

### Using Verbose Mode for Debugging

When experiencing connection issues, add the `--verbose` or `-v` flag to get detailed debug output:

```bash
UsurperReborn --door32 door32.sys --verbose
```

Verbose mode provides:
- **Raw drop file dump**: Shows the exact contents of your drop file line-by-line
- **Parsed session info**: Displays how the drop file was interpreted (CommType, SocketHandle, ComPort, etc.)
- **Connection debugging**: Shows socket/serial initialization attempts
- **Error details**: Includes full exception information and stack traces
- **Pause points**: Stops at key points so you can read the output before it scrolls away

**Example verbose output:**
```
[VERBOSE] Verbose mode enabled - detailed debug output will be shown
[VERBOSE] === RAW DROP FILE CONTENTS: door32.sys ===
[VERBOSE] Line 1: 2
[VERBOSE] Line 2: 1234
[VERBOSE] Line 3: 38400
...
[VERBOSE] Session info from drop file:
[VERBOSE]   CommType: Telnet
[VERBOSE]   SocketHandle: 1234 (0x000004D2)
[VERBOSE]   ComPort:
[VERBOSE]   UserName: John Smith
[VERBOSE]   UserAlias: CyberKnight
[VERBOSE] Press Enter to continue...
```

### "Could not parse drop file"
- Use `--verbose` to see the raw drop file contents
- Verify the drop file exists at the specified path
- Check file permissions
- Ensure the file format matches DOOR32.SYS or DOOR.SYS specifications

### "Failed to initialize socket"
- Use `--verbose` to see the socket handle value from the drop file
- The socket handle from DOOR32.SYS may be invalid
- Try using `--stdio` flag for Standard I/O mode instead
- Try using DOOR.SYS instead (falls back to console I/O)
- Verify your BBS is configured to pass socket handles

### No output / garbled text
- Ensure terminal emulation is set to ANSI
- Check that the BBS is not intercepting I/O
- Verify "Native Executable" is set to Yes
- Try adding `--stdio` flag to use ANSI escape codes

### Connection drops immediately
- Use `--verbose` to identify where initialization fails
- Check the command line path is correct
- Verify all DLL dependencies are present
- Look for error messages in the BBS log

### Output shows locally but not remotely
1. Try `--stdio` flag for Standard I/O mode
2. Use `--verbose` to see detailed connection info
3. Check your DOOR32.SYS has correct CommType (2=telnet) and socket handle
4. Verify Synchronet's I/O Method matches your command line flags

## Log Output

Usurper Reborn writes diagnostic messages to stderr, which most BBS software captures in logs:

```
[19:45:32] USURPER: Initializing BBS door mode...
[19:45:32] USURPER: Session: CyberKnight from Synchronet BBS
[19:45:32] USURPER: Connection: Telnet, Node: 1
```

## Multi-Node Support

Each node should have its own:
- Drop file (automatically handled by BBS)
- Working directory (recommended)

The game uses the node number from the drop file to prevent conflicts.

## Save File Location

By default, saves are stored in:
```
<game directory>/Saves/
```

In BBS mode, the player name from the drop file is used to locate/create save files.

## Platform Notes

### Windows
- Use `UsurperReborn.exe`
- Ensure .NET 8.0 runtime is installed (or use self-contained build)
- Socket handle inheritance works natively

### Linux
- Use `./UsurperReborn`
- May need to set executable permission: `chmod +x UsurperReborn`
- Build for Linux: `dotnet publish -c Release -r linux-x64 --self-contained`
- Socket handle (file descriptor) inheritance is supported
- If socket mode fails, the game automatically falls back to console I/O
- For DOSEMU-based setups, consider using the Windows build with Wine

#### Synchronet on Linux
Configure the door with these settings:
```
Native Executable: Yes
Use Shell to Execute: No
I/O Method: Socket (for direct socket) or Standard I/O (for console)
```

If using Standard I/O mode, the game will use stdin/stdout instead of the socket handle.

### macOS
- Use `./UsurperReborn`
- Build: `dotnet publish -c Release -r osx-x64 --self-contained`
- Self-contained builds recommended

## Support

- **Discord**: https://discord.gg/EZhwgDT6Ta
- **GitHub Issues**: https://github.com/binary-knight/usurper-reborn/issues

---

*Usurper Reborn is a faithful recreation of the classic 1993 BBS door game by Jakob Dangarden.*
