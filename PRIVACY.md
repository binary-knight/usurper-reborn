# Privacy Policy

**Usurper Reborn** - Last updated: January 2026

## Overview

Usurper Reborn is free and open source software (GPL v2). We respect your privacy and collect minimal data only when you explicitly opt in.

## Telemetry (Opt-In Only)

During alpha testing, we offer **optional** anonymous telemetry to help improve game balance and fix bugs.

### How to Enable/Disable

- **Opt-in**: You are asked during character creation if you want to enable telemetry
- **Opt-out**: You can disable telemetry at any time through the game settings
- **Default**: Telemetry is **disabled by default**

### What We Collect (When Enabled)

If you opt in, we collect anonymous gameplay statistics:

| Data Type | Examples | Purpose |
|-----------|----------|---------|
| Session info | Session start/end, play duration | Understand engagement |
| Combat stats | Win/loss, damage dealt, dungeon floor | Balance combat difficulty |
| Progression | Level ups, achievements unlocked | Track game progression |
| Shop activity | Items purchased, gold spent | Balance economy |
| Technical info | Platform (Windows/Linux/macOS), game version | Bug fixing |

### What We DO NOT Collect

- Your real name or identity
- IP addresses (GeoIP processing is explicitly disabled)
- Location data
- Email addresses
- Any personally identifiable information
- Your character's name (only used locally)
- Chat or text input
- Files on your computer

### Anonymous Identifier

A random anonymous ID (UUID) is stored locally in `.telemetry_id` to track sessions from the same installation. This ID:
- Is randomly generated and not linked to your identity
- Can be deleted by removing the `.telemetry_id` file
- Is only used if you opt into telemetry

### Where Data Is Sent

Telemetry data is sent to [PostHog](https://posthog.com), an open source analytics platform, hosted in the United States. PostHog's privacy policy: https://posthog.com/privacy

### Data Retention

Analytics data is retained for the duration of the alpha/beta testing period to help with game development. After the game reaches stable release, historical telemetry data may be deleted.

## Auto-Update Feature

The game checks for updates by contacting GitHub's public API:
- `https://api.github.com/repos/binary-knight/usurper-reborn/releases/latest`

This check:
- Sends your game version and a standard User-Agent header
- Does not send any personal information
- Results are cached locally for 4 hours to minimize requests

## Local Data Storage

The game stores the following data locally on your computer:

| File | Purpose | Location |
|------|---------|----------|
| Save files | Your game progress | `saves/` folder |
| Settings | Game preferences | `settings.json` |
| Debug logs | Troubleshooting (local only) | `logs/` folder |
| Version cache | Reduce update checks | `version_cache.json` |
| Telemetry ID | Anonymous session tracking | `.telemetry_id` |

All local data stays on your computer and is never uploaded unless you explicitly enable telemetry.

## BBS Door Mode

When running as a BBS door game:
- The game reads your BBS username from the drop file (DOOR32.SYS or DOOR.SYS)
- This name is used only for your in-game character
- Save files are stored in a BBS-specific folder to separate users
- Auto-update is disabled (sysops manage updates)

## Online Account Email (Optional)

An online account may keep one email address, stored only with that account on the game server and used only to send password reset links and account notices (sent through the Resend mail service, with no tracking); remove it at any time with E on the character screen, which deletes it from the account. The address is never sold or shared, except with the mail service that delivers these messages. The address is optional; without one, a forgotten password can only be reset by the server's operator. A server that does not send mail asks for no address.

## Third-Party Services

| Service | Purpose | Privacy Policy |
|---------|---------|----------------|
| GitHub | Update checks, releases | https://docs.github.com/en/site-policy/privacy-policies |
| PostHog | Analytics (opt-in only) | https://posthog.com/privacy |

## Open Source

This game is open source. You can review exactly what data is collected by examining:
- `Scripts/Systems/TelemetrySystem.cs` - Telemetry implementation
- `Scripts/Systems/VersionChecker.cs` - Update checker

Source code: https://github.com/binary-knight/usurper-reborn

## Contact

For privacy questions or concerns:
- Open an issue: https://github.com/binary-knight/usurper-reborn/issues
- Email: [Add your contact email if desired]

## Changes to This Policy

We may update this privacy policy as the game develops. Check the "Last updated" date at the top of this document.
