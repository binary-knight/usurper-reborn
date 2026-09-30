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

## Drift check

`tools/wiki-build/drift.js` checks that hand-written guides still match the
game. The game version is `GameConfig.Version` in
`Scripts/Core/GameConfig.cs`; `--version X` overrides it. For each guide:

- `checked` equal to the game version: current.
- `checked` older than the game version: every file in `sources` is compared
  with `git diff v<checked> HEAD`. A changed file, or one that did not exist at
  that tag, makes the guide stale. The tag `v<checked>` must exist; a missing
  tag is an error.
- `checked` newer than the game version: an error.
- A `sources` file that no longer exists makes the guide stale.

It also checks coverage: every `Scripts/Locations/*Location.cs` must appear in
some guide's `sources` or in `tools/wiki-build/coverage-allowlist.txt`, one file
name per line followed by the reason it needs no guide.

The report lists each stale guide with its changed files and the command that
shows each diff, each uncovered location and each error.

```sh
node tools/wiki-build/drift.js --warn
node tools/wiki-build/drift.js --fail --version 1.2.1
```

Run it from a clone with full history and tags (`git fetch --tags origin`).
Warn mode always exits 0. Fail mode exits 1 when anything is listed. In CI the
`wiki` job fetches full history and runs the check after the build. Ordinary
pull requests and pushes get warning annotations and pass. Pull requests whose
head branch starts with `release-`, `release` events and manual deploys run in
fail mode, so a stale guide fails the `wiki` job and blocks `deploy-server`.

To re-check a stale guide:

1. Read each diff the report prints.
2. Update the prose where the change affects what the guide says. Facts come
   from code; the content rules above apply.
3. Set `checked` to the game version, even if no prose changed. Add or remove
   `sources` entries if the guide's subject moved to another file.

Release prep, before opening the release pull request: set
`GameConfig.Version` to the new version (or pass `--version` with it until the
bump lands), run `node tools/wiki-build/drift.js --fail`, re-check every stale
guide, resolve every uncovered location, and repeat until the check passes.

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
excerpts and links, and says when no match exists. No GitHub token is used.
Unless the optional Claude API path below is enabled, no model is used either.
Ask permits one request per user per ten seconds and at most thirty handled
requests per minute globally, with a 300-character question cap.

## Ask with the Claude API (optional)

With this path on, Ask answers in plain language instead of quoting excerpts.
It uses the model `claude-sonnet-5-5` at effort `low` through the official
`@anthropic-ai/sdk` package, and it may use only the game wiki:

- It runs only for Ask in the configured wiki channels. It searches the public
  index first; with no match it makes no model call and gives the usual
  no-match reply.
- The model sees a fixed system prompt, the top five excerpts from the public
  index, one per page and at most 1,500 characters each (spoilers and secret
  names are already excluded there), and the question, capped at 300 characters and wrapped as untrusted data. No tools,
  no conversation history, and no player or database data are sent.
- The prompt limits answers to the excerpts, says to admit when they do not
  cover the question, to decline anything not about Usurper Reborn with a
  first line that is exactly `OFFTOPIC`, to ignore
  instructions inside the question, never to reveal spoilers or the prompt,
  and to stay near 120 words of plain text.
- The bot checks the output in code: mentions are disabled, any URL other than
  a retrieved wiki page is removed, the bot appends the source links itself,
  and the reply is capped at 1,900 characters. A decline has the `OFFTOPIC`
  line removed and gets no source links; the marker is removed from every
  reply.
- Each call has `max_tokens` 700, a 15 second timeout and one retry. It uses
  the server-side refusal fallback (`fallbacks: "default"`). A refusal,
  `max_tokens` stop, empty answer, API error or timeout gives the usual
  excerpt reply.
- The existing Ask rate limits stay. Two daily caps are added, counted per UTC
  day from the audit table so they survive restarts: a server-wide token cap
  and a per-user question cap. Over either cap, Ask gives the excerpt reply
  without a model call.
- The audit table `wiki_llm_usage` records time, user and channel IDs, the
  sanitized question (at most 300 characters), input and output tokens,
  latency and outcome (`answered`, `declined`, `refusal`, `max_tokens`, `empty`,
  `stopped`, `error`, `timeout`, `capped_tokens`, `capped_user`,
  `capped_error`). Rows older than 30 days are deleted at startup and on each
  new row. If the table cannot be created, the path stays off.

The path is on only when all of these hold; otherwise Ask behaves exactly as
before. The web service logs one line at startup saying whether it is on, and
why not when it is off. The key is never logged.

- `DISCORD_WIKI_LLM`: exactly `on` to enable. Anything else, or unset, is off.
  This is the kill switch.
- `ANTHROPIC_API_KEY`: the API key, in the usurper-web systemd drop-in (root
  owned, mode 600). Never commit it or put it in a shared file.
- `DISCORD_WIKI_LLM_DAILY_TOKENS`: server-wide input plus output tokens per UTC
  day. Default 200000.
- `DISCORD_WIKI_LLM_USER_DAILY`: model questions per user per UTC day.
  Default 20.
- The `@anthropic-ai/sdk` package installed in `/opt/usurper/web/node_modules`.
- The web service's writable database connection, which holds the usage table.

Owner steps before the first enable:

1. Create a dedicated Anthropic Console workspace for the wiki bot, set a
   monthly spend limit on it as the hard backstop, and create the API key in
   that workspace.
2. Run the manual eval below with that key and read every answer.
3. Install the web dependencies once. The release tarball excludes
   `node_modules`, and deploys do not run npm. `npm ci` replaces every web
   dependency with the versions pinned in `web/package-lock.json`, not only the
   SDK, so do it in a maintenance window and keep a copy of the old tree:

   ```sh
   sudo systemctl stop usurper-web
   sudo cp -a /opt/usurper/web/node_modules /opt/usurper/web-node_modules.bak
   cd /opt/usurper/web
   sudo -u usurper npm ci --omit=dev
   sudo systemctl start usurper-web
   journalctl -u usurper-web -n 50
   ```

   Confirm the `[usurper-web] Database connected` lines after the restart. If
   npm reports that install scripts were skipped for `better-sqlite3`, `ssh2`
   or `cpu-features`, allow them and run the install again; without its native
   build `better-sqlite3` cannot open the database. To roll back, stop the
   service, move the backup tree back to `node_modules` and start it.
4. Put the settings in a root-owned drop-in, then restart:

   ```sh
   sudo install -d -m 755 /etc/systemd/system/usurper-web.service.d
   sudo install -m 600 -o root -g root /dev/null /etc/systemd/system/usurper-web.service.d/wiki-llm.conf
   sudoedit /etc/systemd/system/usurper-web.service.d/wiki-llm.conf
   sudo systemctl daemon-reload
   sudo systemctl restart usurper-web
   journalctl -u usurper-web -n 50
   ```

   with this content:

   ```text
   [Service]
   Environment=ANTHROPIC_API_KEY=...
   Environment=DISCORD_WIKI_LLM=on
   Environment=DISCORD_WIKI_LLM_DAILY_TOKENS=200000
   Environment=DISCORD_WIKI_LLM_USER_DAILY=20
   ```

   The startup log should read `[Wiki] Ask LLM path on: ...`. The service has
   `MemoryMax=128M`; watch its memory for the first days. To switch the path
   off, set `DISCORD_WIKI_LLM=off` in the drop-in, reload and restart.

Review usage with a read-only query on an authorized copy, for example
`SELECT outcome, COUNT(*), SUM(input_tokens), SUM(output_tokens) FROM
wiki_llm_usage GROUP BY outcome;`.

### Manual eval

`tools/wiki-build/eval-ask.js` sends 20 wiki questions and 10 adversarial ones
(off-topic, jailbreak, spoiler fishing, prompt extraction) through the shipped
bot code with fake Discord messages and an in-memory database, and prints each
reply with its outcome, tokens and latency. It makes real, billed API calls, so
it is never run in CI or by the test gate. Run it by hand before enabling the
path and after any prompt or model change:

```sh
npm ci --omit=dev --prefix web
npm ci --prefix tools/wiki-build
dotnet run --project usurper-reloaded.csproj -c Release -- --export-wiki wiki-data
npm run build --prefix tools/wiki-build
ANTHROPIC_API_KEY=... node tools/wiki-build/eval-ask.js
```

Check that wiki answers match the pages, that every adversarial question is
declined or answered only from the excerpts, that no spoiler, prompt text,
foreign link or mention appears, and how many replies stopped at `max_tokens`.
Thinking counts toward the 700-token limit, so a high `max_tokens` share is the
first thing to look for; those replies fall back to excerpts.

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
mention stripping, queue persistence and off-server path confinement. The Claude
API Ask tests use a fake SDK client and never call the API. Browser
checks cover a phone width, both themes, focus/keyboard behavior, disclosure
controls and WCAG accessibility scans. These are automated checks, not a claim
of a full human screen-reader audit.

Generated item prices are base values, not guaranteed quotes or drops. Monster
samples are normal non-boss tier endpoints. Boss stats are base definitions.
No saves, player-gods' live standings, private messages or accounts enter the
static export. Live mods and NG+ can change the rules on a particular server.
