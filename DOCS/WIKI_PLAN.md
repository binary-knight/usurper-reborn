# Usurper Reborn player wiki plan

Status: design choices approved by the owner, 2026-09-29. Baseline: the released
`v1.2.0` tag. Implementation and publication remain separate review steps.

This plan adapts the original design in `~/usurper/evidence/wiki-design.md` to the
released code. The decisions at the end govern content, navigation, and later
Discord features.

## Purpose

Build a player wiki at `/wiki/` on the existing website. It should explain how
to play, then give reliable references for character building, combat, gods,
Mental, the world, items, monsters, bosses, quests, and online play. Game numbers
come from the released executable's code and data. Guides live as reviewed
Markdown in the repository. A later Discord bot may answer from published wiki
pages and collect suggestions for owner-reviewed changes.

The wiki is versioned with the game release. It must distinguish fixed built-in
rules from per-server mods and the changing online world. It must never imply
that an unknown drop source, shop, or translation is known.

## Reader path and navigation

The landing page offers three entry points:

1. **New player:** create a character, take the first hour step by step, choose
   single-player or online play, and find help for browser, SSH, and BBS play.
2. **Build a character:** classes, races, stats, leveling, specializations,
   abilities, spells, equipment, gods and Favor, and Mental.
3. **Look something up:** searchable item, monster, boss, achievement, and
   balance references.

The main navigation groups pages as Getting Started, Characters, Combat, Gods
and Faith, World, Items and Economy, Monsters and Bosses, Online World, and
Reference. Each section starts with a short overview and links to deeper pages.
Breadcrumbs and a section sidebar show where a page belongs. Entity pages use
stable IDs in their URLs, such as `/wiki/en/items/1000/`, while display names
may change in translation. The English root `/wiki/` links to `/wiki/en/`.

Initial prose pages: Getting Started, Classes, Combat Basics, Gods and Favor,
and Mental. The 1.2.0 release notes are a factual starting source for the last
two, but wiki prose needs a separate review. Each prose page records the game
version against which it was checked. Secret bosses, hidden odds, and late story
details use spoiler controls, as agreed in D1 below.

## Data and build pipeline

```
released C# code + Localization/*.json
    -> UsurperReborn --export-wiki <dir>
    -> wiki-data/*.json (generated)
DOCS/wiki/**/*.md + wiki-data/*.json
    -> tools/wiki-build/ (Node build dependency, separate from web runtime)
    -> web/wiki/**/index.html + search-index.json (generated)
    -> existing web assets tarball and release deployment
```

`--export-wiki` is a read-only build command, separate from `--export-data`.
The latter writes example custom equipment rather than the built-in catalog.
The new exporter must use built-in definitions even if a local `GameData/`
directory exists; otherwise a developer's mods can silently change the public
wiki. It reads no saves or online database. Export errors fail with a nonzero
exit code. Every dataset includes stable keys, display names, source version,
and an explicit schema version. `meta.json` records the game version and, when
available, the commit used for the build.

Run Phase 1 locally with:

```sh
dotnet run --project usurper-reloaded.csproj -c Release -- --export-wiki wiki-data
```

The exporter writes the 11 datasets in
the table below plus `meta.json`. Each dataset has `schemaVersion`,
`gameVersion`, and `data` at the top level. `meta.json` also records the commit
(`GITHUB_SHA` in CI, otherwise `git rev-parse HEAD`, or null when neither is
available) and the UTC export time. Values are the shipped defaults: the export
ignores the current difficulty, the server monster HP multiplier and any loaded
spell or ability overrides. Running `--export-wiki` without a directory prints
a usage line and exits with code 2. Item and monster names without a verified
localization key are English only. Monster tier stat samples are normal,
non-boss values at the tier's minimum and maximum levels from the game's
generator; server modifiers and NG+ can change live encounters.
Boss numbers in the export are base definitions, since encounters can scale them.

| Dataset | Primary source | Export rule |
| --- | --- | --- |
| Items | `EquipmentDatabase` and equipment models | Export built-in templates by ID, not generated copies or mod examples. Include known stats and restrictions. Add sources of acquisition only when code establishes them. |
| Monster families | `MonsterFamilies` plus monster generation rules | Export tiers and abilities. Derived HP or attack values require a defined level and formula; do not present a single value as universal. |
| Classes and races | `GameConfig`, character creation, growth rules, `SpecializationData` | Separate starting values, race bonuses, and level growth. Mark prestige unlock conditions. |
| Abilities and spells | `ClassAbilitySystem`, `SpellSystem` | Reuse numeric override templates, then add player-facing names and descriptions only where the code supplies them. |
| Gods, Favor, Mental | `GodSystem`, `FavorSystem`, `GodBoonSystem`, deed and Mental systems, `GameConfig` | Export fixed pantheon definitions and formulas, not randomized runtime god state or player-gods. |
| Achievements, bosses, balance | Built-in achievement and boss data, `BalanceConfig`, `GameConfig` | Export data used by the game; identify constants not represented by the moddable balance file. Apply spoiler policy to boss details. |

The exporter should expose a small explicit wiki model for each dataset, not
serialize whole runtime objects. Validate coverage and a few known values for
every dataset in C# tests. Validate IDs and cross-references before rendering.

The Node builder reads Markdown from `DOCS/wiki/`, renders a shared accessible
layout, expands explicit directives for live tables and entities, and creates
one page per selected entity. It builds a client-side search index from page
titles, headings, summaries, and entity names. Unknown directives, missing
entities, duplicate URLs, and broken internal links fail the build. Markdown
and exported strings are escaped or sanitized before insertion into HTML.
Spoiler sections use accessible disclosure controls with a clear summary.
Secret entities appear inside those sections on a parent page rather than in
standalone indexed pages. Their names and hidden text are excluded from search
snippets and page descriptions.

Use directory `index.html` pages for clean URLs, so the checked-in nginx
`try_files $uri $uri/ =404` can serve them without a configuration change.
The website has its own language files in `web/lang/`; shared navigation text
must follow that convention. Add an exception for `tools/wiki-build/` to
`.gitignore`, which currently ignores most of `tools/`. Ignore generated
`web/wiki/` and `wiki-data/`.

The wiki build runs in pull-request CI from a clean checkout and again in the
release deployment job before the web tarball is created. The deployment
currently extracts the tarball over `/opt/usurper/web`, so it must remove only
the old generated wiki tree as part of the automated release step before
extracting the new one. That prevents deleted wiki pages from remaining live.
No one edits or deploys wiki files by hand on the production server.

## Language, presentation, and accessibility

English prose launches first. The five game localization files can supply
names and labels where a verified key exists. They do not currently cover
every built-in item or monster name: many are English literals in C#. The
exporter must use `Loc.GetIn(language, key)` for known keys and carry explicit
fallback status for missing translations. The site must not present English
fallback text as a completed translation. English pages and tables launch first,
with translated labels only where source keys are verified, as agreed in D3.

Match the site's dark palette and typography while prioritizing readable body
text, adequate contrast, and small-screen tables that do not cause horizontal
page scroll. Provide a light theme, semantic headings, table headers, focus
states, keyboard navigation, and meaningful alternative text. Generated pages
and prose use no emoji, em dashes, or en dashes, following project style.

## Later Discord work

Phase 3 adds Ask after the published wiki has a usable search index. It should
answer with short excerpts and page links, and say when the wiki has no answer.
An LLM is optional; search plus cited excerpts is the first design. The
existing `messageCreate` handler returns early outside the gossip channel, so
allowed Ask channels need an explicit routing check before that gate. Ask must
never relay a question into in-game gossip.

Phase 4 adds Suggest with a role gate, per-user and global limits, and an audit
record. The production Node process only records or dispatches a suggestion.
A drafting agent off the server treats the suggestion as untrusted data, checks
claims against code and exported facts, edits only `DOCS/wiki/` on a branch,
and opens a pull request for owner review. The wiki features on the production
process use no GitHub token. No suggestion publishes automatically.

## Reviewable implementation phases

1. **Export:** `--export-wiki`, schema, deterministic built-in datasets, and
   C# coverage checks. Start with classes, races, gods, Mental, items, and
   monsters; extend to spells, abilities, achievements, bosses, and balance
   before calling Phase 1 complete.
2. **Site:** builder, first prose pages, generated references, search, link
   checks, accessibility checks, CI build, and release packaging. Validate on
   a phone width and with keyboard and screen reader navigation.
3. **Ask:** Discord read-only question routing over published wiki content.
4. **Suggest:** role gate, audit log, off-server drafting workflow, and
   owner-reviewed pull requests.
5. **Content:** remaining world, economy, online, and advanced guides; add
   translated prose if approved and maintained.

Each phase is a separate reviewable pull request or small group of pull
requests. Branch from released `main`, never push directly to `main`, and run
`dotnet test Tests/` for code commits. Pull request descriptions state facts
and tests without an attribution footer.

## Agreed decisions (2026-09-29)

- **D1, spoilers:** Show ordinary player-discoverable mechanics. Put secret
  bosses, exact hidden odds, and late story details behind spoiler controls.
- **D2, Suggest access:** Anyone can Ask. Suggest requires a trusted Discord
  role granted by the owner.
- **D3, launch languages:** Launch English prose and tables. Use translated
  labels only where source keys are verified, then expand language coverage.
- **D4, bot model:** Ask uses search, short excerpts, and links without an LLM.
  An agent may later draft Suggest changes for owner review.
- **D5, URL:** Host the wiki at `/wiki/` on the existing website.

No production publication is authorized by this design plan.

## Implementation handoff

The launch implementation includes the static builder, all nine content
sections, generated entity references, search, release packaging and CI checks.
Spell and ability detail pages expose costs, numeric templates and equipment
requirements. Race compatibility and specialization gates are also exported
from the character rules rather than inferred by the prose.
Discord Ask and the role-gated Suggest audit queue are implemented but require
owner-provided environment configuration after review. The off-server
owner-triggered drafting workflow opens docs-only draft pull requests. No
autonomous drafting agent is enabled; D4 reserves that as a later extension.

See `DOCS/WIKI_OPERATIONS.md` for build commands, content rules, validation,
configuration and the complete suggestion review procedure. Nothing has been
merged to `main` or deployed by hand.
