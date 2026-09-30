# Contributing to Usurper Reborn

Thanks for wanting to contribute. This project is a modern C# recreation of the 1993 BBS door game Usurper. It's developed in the open on GitHub and licensed under GPL v2.

This document explains how to get a change merged. Read it end-to-end before you open a PR.

## The Expectation

This is a small project with one maintainer. You are expected to work independently:

- **Play the game first.** Ideally for several hours. Most good contributions come from players who hit something that felt off and decided to fix it. If you haven't played, you don't know the game well enough to change it.
- **Find your own work.** No issues are assigned. The issue tracker is a backlog of player-reported bugs and wishes, not a task board. Pick something you care about or something you stumble on in play.
- **Teach yourself the tools.** I won't walk you through git, C#, .NET, or the codebase. If you need a primer on pull requests, GitHub has good docs. If you need a C# tutorial, there are thousands. This project assumes you've already cleared those bars.
- **Debug your own work.** I won't sit on a call with you, step through your code, or guess why your build is red. A PR that doesn't compile or fails tests will sit until you fix it or I close it.

If any of that sounds unfair, contribute to a project with a larger maintainer team. This one has scale limits.

## What's Worth Contributing

Good contributions, roughly in order of how happy they make me:

1. **Bug fixes for things you personally hit in play.** Include enough detail in the PR description that I can see you understood the bug, not just the symptom.
2. **Balance tweaks backed by play experience.** "This class is weak/broken because X at level Y" lands better than "I think Z should be higher."
3. **Localization.** Adding or improving translations in one of the five supported languages (English, Spanish, French, Hungarian, Italian) or adding a new language. See [Localization/](Localization/) for the key files.
4. **New content that fits the setting.** Monsters, items, events, dialogue, dungeon features. Small and thematic beats large and out-of-place.
5. **Quality-of-life improvements** that you've personally wanted while playing. Usability, display, accessibility, compact-mode coverage.
6. **Performance fixes** with a measurement attached. "Faster" without numbers isn't compelling.
7. **Test coverage** for systems that don't have it. Look at [Tests/](Tests/) for the existing style.

## What's Not Worth Contributing

- **"I rewrote X to use Y framework."** No.
- **Style-only refactors** (renaming variables, reformatting code, reorganizing folders) without a behavior change. The codebase has its conventions; churn costs review time and I'm unlikely to merge it.
- **Dependency additions.** I'll probably reject anything that adds a NuGet package unless it's load-bearing for the feature.
- **Architecture debates in PR descriptions.** If you want to propose a rewrite, open a discussion first and expect me to say no.
- **AI-generated code you don't understand.** Fine if you used an assistant to help, but you're accountable for every line. If I ask you why a hunk is there and you can't answer, the PR gets closed.
- **Changes that touch save format without a migration plan.** Existing player saves must load cleanly.

## Before You Start a PR

1. **Build the project.** `dotnet build usurper-reloaded.csproj` must succeed with zero errors.
2. **Run the tests.** `dotnet test Tests/Tests.csproj` must pass.
3. **Play the change.** Boot the local build, reach the system you changed, verify the fix or feature works as intended. Include the steps you tested in your PR description.
4. **Check recent commits** around the file you're touching. If a system was changed recently, there's usually a reason — understand it before you alter it.

## Workflow

Standard GitHub fork-and-PR flow:

1. Fork the repo.
2. Create a branch off `main` with a short, descriptive name (`fix-assassin-backstab-crit`, `add-confession-temple`, `localize-fr-dungeon-keys`).
3. Make your change. Keep the diff focused on one thing — a PR that fixes a bug AND refactors three unrelated files will be asked to split.
4. Commit with a clear message. Follow the style of recent commits (`git log --oneline` to see). Imperative mood, short summary line, body if the change needs explaining.
5. Push to your fork and open a PR against `main`.
6. Fill out the PR description. See below.

## PR Description Requirements

A mergeable PR description answers these questions:

- **What** does this change?
- **Why** does it need to change? (Link the player-reported issue if one exists, or describe the in-game scenario that exposed the problem.)
- **How** does it change it? (One paragraph of technical summary is plenty.)
- **How did you test it?** (What you built, what you ran, what you played through.)
- **What could regress?** (Systems that share state with the one you touched.)

If the change is user-visible, add a line for the release notes. Don't bump the version in `GameConfig.cs` yourself — I bump versions at release time and consolidate changelog entries.

## Code Expectations

- **Match the surrounding style.** No strict .editorconfig enforcement; just don't reformat neighbors.
- **No emojis in code, comments, commit messages, or release notes** unless a specific user-facing feature needs them. (Project convention.)
- **Keep comments tight.** Comment the *why* when it's non-obvious. Don't narrate the *what*.
- **Don't pre-emptively abstract.** Three similar lines beats a premature helper.
- **Trust internal code.** Don't add null-guards or error-handling for scenarios that can't happen — only validate at system boundaries (user input, external APIs, save loads).
- **Localize user-facing text.** Any new string the player can see goes through `Loc.Get("key")` with keys added to all five language files. See existing keys in [Localization/en.json](Localization/en.json) for naming conventions.

## Save-Compatibility Rules

The game has months/years of player saves on public BBSes and the online server. Breaking save compatibility is the single fastest way to get a PR rejected.

- **New fields on existing save structures**: fine, JSON deserialization defaults them.
- **New `Character`/`NPC`/`Item` properties**: add them to `SaveDataStructures.cs`, `SaveSystem.cs` (serialize), `GameEngine.cs` (restore), and `DailySystemManager.cs` if they're daily counters. Skipping any of these is a guaranteed item-loss bug.
- **Removing or renaming fields**: don't, unless you also write a migration path.
- **New enum values**: append to the end; inserting changes ordinal serialization and corrupts saves.

## Localization Rules

- Every new user-facing string needs keys in all five files: `en.json`, `es.json`, `fr.json`, `hu.json`, `it.json`.
- Keys stay identical across languages — only values differ.
- `{0}`, `{1}` format args must be present in all translations.
- If you don't speak a language, English-to-machine-translation with a note in the PR (`"[MT, needs review]"`) is acceptable for non-English files, as long as the English version is correct.
- Don't translate localization keys themselves, only values.

### Hardcoded text ratchet

`Tests/Localization/HardcodedTextRatchetTests.cs` counts player-visible English written directly in `Scripts/` (terminal output, UIHelper boxes and menus, news, mail and broadcasts) per file and compares it with `Tests/Localization/hardcoded-baseline.json`. A file may not gain hardcoded text, and when you move strings to `Loc.Get` the baseline must come down with them. After localizing, update it with:

```
Tests/Localization/loc-scan.sh --write-baseline
```

The same script without the flag prints the full inventory and a per system rollup. Admin and SysOp only files are listed with a reason in `Tests/Localization/hardcoded-exclusions.txt`.

A second ratchet, `Tests/Localization/DataTextRatchetTests.cs`, counts player-visible English outside output calls, per file and category, against `Tests/Localization/hardcoded-data-baseline.json`: `data` (data tables listed in `Tests/Localization/hardcoded-data-sources.txt`, such as dialogue lines, achievement text and item names), `electron` (text sent to the Electron client) and `throw` (exception messages a catch prints to the player). Tables that already resolve a Loc key (`keyed`) and proper noun name lists (`names`) are listed there but not counted. If you add a table of player text, prefer Loc keys; if it must hold English, add its slot to the sources file. After localizing, update the baseline with:

```
Tests/Localization/loc-scan.sh --write-data-baseline
```

## Review Timing

I review PRs when I have time. It might be the same day, it might be three weeks. If your PR has been open a month with no response, a gentle ping on the PR thread is fair; a ping after a week is not.

PRs that can't merge (failing build, merge conflict, unresponsive to review comments) get closed after a long silence. Reopen when you've addressed the blockers.

## Where to Ask Questions

Questions specific to a PR: on that PR's thread.

Questions about the game itself: [Discord](https://discord.gg/EZhwgDT6Ta).

Questions like "how does this system work?" / "how do I set up C#?" / "can you explain this file?": the answer is "read the code and figure it out." That is not a brush-off, it is the honest boundary of what this project can support.

## License

By contributing you agree that your changes ship under the project's existing license (GPL v2). You keep copyright to your own work; the license grants it to the project and its downstream users.

---

Thanks for reading this far. If you ship a good PR, your name lands in the release notes that shipped the change, and the game gets a little better for everyone.
