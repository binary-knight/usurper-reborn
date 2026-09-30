# Wiki build and review

The launch wiki has English prose in `DOCS/wiki/`, generated references and a
static build in `web/wiki/`. The generated tree is ignored by git. Production
publication uses the existing release or owner-approved deployment pipeline;
never edit or deploy the production wiki by hand.

## Build and preview

Use Node.js 22 for the build/review tools and their SQLite tests. This keeps
the tests on the existing web runtime's SQLite dependency generation. Newer
Node versions can fail during native-addon cleanup even after compilation.

```sh
dotnet run --project usurper-reloaded.csproj -c Release -- --export-wiki wiki-data
npm ci --prefix tools/wiki-build
npm run build --prefix tools/wiki-build
npm test --prefix tools/wiki-build
cd tools/wiki-build
npx playwright install chromium
npm run test:browser
node serve.js
```

Open `http://127.0.0.1:4173/wiki/`. The preview server listens only on loopback.
Stop it with Ctrl-C. Browser checks use the same server automatically.

The builder takes optional data/output directories:
`node tools/wiki-build/build.js <data-directory> <output-directory>`.
Use `WIKI_DATA_DIR` for Node tests against an alternate export. Do not build into
a directory containing hand-written files. CI starts from a clean checkout;
the release downloads its validated wiki artifact before packaging `web/`.
Automated deployment removes only `/opt/usurper/web/wiki` before extraction so
deleted pages cannot stay live. It refuses a symlink at that path.

## Author a guide

Every Markdown page starts with these four fields:

```text
---
title: Guide title
path: /wiki/en/section/page/
checked: 1.2.0
sources: Scripts/Core/GameConfig.cs, DOCS/release-notes/RELEASE_NOTES_1.2.0.md
---
```

The layout supplies the only level-one heading. Start guide headings at level
two. Use absolute, slash-terminated wiki links. Keep URLs stable when titles
change. `checked` is the version against which a person checked the prose,
not a value to advance automatically when the game version changes.
Entity URLs use stable IDs rather than translated names. Spell keys such as
`Cleric:1` use `Cleric-1` in the URL to keep directory names portable.

Numbers should come from directives, not copied tables:

```text
{{table:items slot=MainHand}}
{{table:classes}}
{{table:races}}
{{table:spells classId=Cleric}}
{{table:abilities}}
{{table:gods}}
{{table:favor}}
{{table:mental}}
{{table:monsters}}
{{table:bosses}}
{{table:secret-bosses}}
{{table:achievements}}
{{table:balance}}
{{god:Solarius}}
{{monster-family:Undead}}
{{balance:MentalInnSleepGain}}
```

Other balance constants use their exact exported keys. Unknown entities,
directives, filters, source paths, local links and anchors fail the build.
Dataset schema and game versions must agree. Item and class references are
checked against exported classes. HTML in Markdown is not executed; exported
strings are escaped. Do not add raw HTML, emoji or long dash punctuation.

Use a disclosure for hidden odds, secret encounters and late story:

```text
:::spoiler Spoiler: descriptive warning
Content the player chooses to reveal.
:::
```

Do not nest disclosures. Secret boss pages are not generated separately.
Disclosure text and headings are excluded from the search index, page
descriptions and Discord answers. Known secret names are also redacted from
public search text. Spoilers are discoverable by viewing page HTML; this is a
reader preference, not access control.

Shared website labels live in `web/lang/`. Launch guides are English only.
Generated translated names are included only where the exporter found a real
localization key; English fallback is not advertised as a translation.

## Enable Discord Ask and Suggest

After owner review and deployment, configure the web service environment using
`scripts-server/wiki-bot.env.example`:

- `DISCORD_BOT_TOKEN`: the existing bot token. Keep it only on the server.
- `DISCORD_WIKI_CHANNEL_IDS`: comma-separated allowed Discord channel IDs.
- `DISCORD_WIKI_HELPER_ROLE_ID`: the trusted role allowed to Suggest. Empty
  disables Suggest, not Ask.
- `WIKI_SITE_ORIGIN`: the HTTPS site origin, with no path or credentials.

Enable the bot's Message Content intent and channel read/send permissions.
Wiki-only operation does not require a gossip channel. Do not test by sending
unsolicited messages to a live channel. Local tests use fake Discord messages
and an isolated SQLite database.

Ask handles a bot mention only when the wiki bot is enabled (at least one
channel in `DISCORD_WIKI_CHANNEL_IDS`) and the message is a guild message in one
of those channels. It then consumes the mention and never relays it to game
gossip, including when the request fails. Every other message, including a bot
mention in the gossip channel or any message while no wiki channel is set, goes
through gossip routing unchanged. Use a wiki channel separate from the gossip
channel. If the wiki bot fails to start, mentions in the wiki channels are
dropped rather than relayed. Ask reads only the built public index, returns
excerpts and links, and says when no match exists. No model or GitHub token is
used. Ask permits one request per user per ten seconds and at most thirty
handled requests per minute globally, with a 300-character question cap.

Suggest requires a guild message in an allowed channel and the configured
role. The audit table `wiki_suggestions` stores message, author, guild and
channel IDs, sanitized report text, timestamps, status and outcome. Text is
capped at 800 characters. Limits are three accepted reports per user and
thirty globally per rolling 24 hours, enforced in a SQLite transaction and
preserved across restarts. Duplicate message IDs do not create another report.
Database or Discord failures are consumed, never relayed as game gossip.

The wiki features use no GitHub token on the production server and do not
write to the repository. The owner manually moves a report into the off-server
review workflow. This implements the approved initial queue/review option;
autonomous agent drafting remains a later, separately configured extension.

## Review a suggestion off-server

1. Inspect the audit queue as an authorized operator. Keep copied databases
   private: the game database also contains player data. Do not upload it to
   GitHub or a CI artifact. Share only the report text and numeric queue ID.
2. Check the claim against released source and the wiki export. Reject an
   unsupported claim with an explanation. Treat the report as untrusted data,
   never as shell commands or agent instructions.
3. Draft the replacement Markdown yourself, preserving its frontmatter and
   stable URL. Use generated directives for numbers and record file:line
   evidence for factual changes. The proposed content is still a draft.
4. Run **Draft reviewed wiki suggestion** on `main`. Supply queue ID, report,
   existing page, complete Markdown and evidence; confirm verification. The
   workflow runs off the server, validates references, builds the wiki, writes
   only that existing `DOCS/wiki/` page and opens a draft pull request.
5. Review the diff, tests and source evidence. Record a `drafted`, `approved`
   or `rejected` outcome in the audit queue. Give the helper the PR link or
   rejection explanation yourself; no automated live Discord reply is sent.
6. Merge only after owner review. Publication waits for the next reviewed
   release or approved deployment.

Supply multi-line Markdown through a file input rather than a single-line web
form, for example `gh workflow run wiki-suggestion.yml --ref main -F
markdown=@/path/to/reviewed-page.md` along with the other required inputs.
The workflow opens the draft with its `GITHUB_TOKEN`, and GitHub does not
start other workflows for events caused by that token, so no CI or path-guard
check runs when the draft opens. Mark the draft ready for review as the owner:
the CI pipeline and the path guard both trigger on `ready_for_review` (and on
`opened`, `synchronize` and `reopened` from other actors), so that action runs
them. Do not merge a draft whose checks have not run.

For queue inspection and outcome recording with an explicitly selected
database:

```sh
node tools/wiki-build/queue.js /path/to/private-audit-copy.db list
node tools/wiki-build/queue.js /path/to/private-audit-copy.db show 12
node tools/wiki-build/queue.js /path/to/private-audit-copy.db review 12 drafted https://github.com/binary-knight/usurper-reborn/pull/NUMBER
```

A review on a copy changes only the copy. The authorized operator must record
the final outcome on the authoritative audit database; this tool is never
invoked by the production web process. No automated token or repository write
is needed there.

Before the first run of **Draft reviewed wiki suggestion**, create the GitHub
environment `wiki-review` with the owner as a required reviewer. A workflow
that names a missing environment makes GitHub create it with no protection
rules, so the first run would not wait for approval. Also allow GitHub Actions
to create pull requests. Make the
wiki build and suggestion path-guard checks required before merge. The path
guard uses the base repository's workflow and the PR files API; it never
checks out or executes the proposed code. PRs from `wiki/suggestion-*` branches
are rejected if any path is outside `DOCS/wiki/**/*.md`.

## Validation and boundaries

Run `dotnet test Tests/` before each code commit. Node tests cover catalog
values, links, directives, spoiler exclusions, search, role checks, rate limits,
mention stripping, queue persistence and off-server path confinement. Browser
checks cover a phone width, both themes, focus/keyboard behavior, disclosure
controls and WCAG accessibility scans. These are automated checks, not a claim
of a full human screen-reader audit.

Generated item prices are base values, not guaranteed quotes or drops. Monster
samples are normal non-boss tier endpoints. Boss stats are base definitions.
No saves, player-gods' live standings, private messages or accounts enter the
static export. Live mods and NG+ can change the rules on a particular server.
