---
title: Reporting bugs
path: /wiki/en/getting-started/reporting-bugs/
checked: 1.2.7
sources: Scripts/Systems/BugReportSystem.cs, Scripts/Locations/BaseLocation.cs
history: 1.2.7 | none
history: 1.2.5 | The bug report screen title is shown in your language.
history: 1.2.4 | none
history: 1.2.3 | none
---
Found a bug? There are two ways to report it: the in-game bug report, or an issue on the project's GitHub page. Either one reaches the developer.

## Report a bug in the game

Press `!` or type `/bug` on any screen where commands work. `/report` and `/bugreport` do the same.

1. The game asks you to describe the bug briefly. Write what happened and what you expected to happen.
2. Press Enter to submit. To cancel, press Enter on a blank line instead.
3. The game collects diagnostic information, saves a copy of the report and sends it to the developer through the Usurper Reborn website.

The screen then tells you whether the report was sent. If it could not be sent, for example with no internet connection or on a BBS without internet access, the saved copy stays on that machine so the SysOp can forward it.

## What the report includes

Along with your description, the report attaches this information automatically:

- The game version.
- Your platform: the operating system and processor type.
- How you are playing: Steam, BBS door, online or local.
- The BBS name, when you play through a BBS door.
- Your character's name, level, race and class.
- Where you are in the game, and the dungeon floor when you are in the dungeon.
- The last lines of the game's debug log.

The saved copy also lists the release name, the date and time of the report, and your character's current and maximum hit points and total play time. It is written to the `bug_reports` folder next to the game. No save file or account data is attached.

## Open an issue on GitHub

You can also open an issue at [the Usurper Reborn issue tracker](https://github.com/binary-knight/usurper-reborn/issues) on GitHub. This needs a free GitHub account. Include:

- The game version, shown on the title screen.
- The steps that lead to the bug, in order.
- What you expected to happen, and what happened instead.
- Screenshots or copied screen text, if they help.

Issues on GitHub are public. Do not post passwords, email addresses or other personal details.

## Tips for a good report

- Report one bug per report.
- Give exact steps: where you were, which keys you pressed and what you chose.
- Always include the game version.
- Say whether the bug happens every time or only sometimes.
