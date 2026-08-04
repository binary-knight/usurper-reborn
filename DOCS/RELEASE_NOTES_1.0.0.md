# Usurper Reborn v1.0.0 "Coronation"

The Beta label comes off.

Nothing here is a new subsystem. Version 1.0 is what the "Countdown" arc
(v0.62 through v0.65) was building toward: a world that runs whether or not
you are watching, a progression curve that no longer punishes the players it
was meant to reward, five languages at parity, and a server that a stranger
can host without handing themselves to the first person who read the source.
This file records the flip itself and the last hardening pass. For what
actually changed under the hood across the arc, read the `RELEASE_NOTES_0.6*`
files; they are the substance, and this is the seal on them.

## The flip

- **Version 1.0.0, release name "Coronation."**
- **Title-screen banner replaced.** For the whole alpha and beta run the game
  opened with a red box warning you to expect bugs and promising daily
  patches. That box is now a cyan welcome carrying the two things worth
  putting in front of every player on every launch: how to report a bug, and
  where the community is. Rewritten in all five languages.
- **Banner geometry made translation-proof.** The three box lines used to be
  hand-padded to exact column counts per language, which meant any translator
  who ran one character long broke the border. The engine now pads and
  truncates to the box width itself, so the box cannot be blown apart by a
  future translation. Also fixes a pre-existing off-by-one that left the third
  line one column short of the frame in every language.
- **README, roadmap, and FILE_ID.DIZ** updated; the BBS file description and
  the server's `version.txt` derive from `GameConfig.Version` at build time,
  and CI refuses to publish a release whose git tag disagrees with it.
- **Production deploys now require human approval.** The GitHub `production`
  environment had no protection rules, so publishing a release auto-deployed
  to the live server unattended. It now requires an explicit reviewer.

## Final hardening (folded in from 0.65.8 through 0.65.14)

**The level-40 cliff, closed end to end.** Telemetry showed players dying out
of the game at the point the curve was supposed to open up. Six changes across
two releases: the early-game XP multiplier now tapers smoothly to 1.0 at level
40 instead of falling off a shelf at 21; bosses grant escalating flee odds as
you drop below half health, so a losing fight is survivable if you read it in
time; a failed flee grants one guarded round at halved incoming damage instead
of a free kill; and the Hall of the Fallen at the Temple keeps the names of
dead characters and passes a level-scaled inheritance to the next character on
that account. Involuntary deletions have effectively stopped in the live data.

**Localization completed to the edges.** The login gate, the permadeath
cinematic, and the death broadcast are localized, the last three surfaces that
still spoke English to everyone. The permadeath eulogy renders per recipient in
each viewer's own language. Hungarian long-umlaut characters now transliterate
correctly for CP437 BBS terminals instead of dropping. A new CI gate fails the
build on key-parity drift, format-argument mismatches that would crash a
translated session, empty values outside a documented whitelist, and any
increase in banned punctuation.

**NPC portraits at full fidelity.** The AI-painted portraits shipped in 0.65.7
were being encoded down to 16 colors and a 34x28 pixel canvas for the game's
own client, which turned a painted bust into noise. Clients now declare their
version during login, and capable ones get truecolor at a 48x48 canvas. BBS
terminals keep the authentic 16-color CP437 look. No art was regenerated.

**An external security audit, answered in full.** An independent source audit
of v0.65.10 reported eight findings. Two were serious and are fixed: a stock
Docker deploy shipped a working default admin password whose lockout disarmed
itself, exposing a world-wipe endpoint; and the superuser account name was
registrable on any fresh deploy, handing a stranger permanent administrative
control. Also fixed: a prefix-matched WebSocket origin allowlist, and a
hardcoded SSH gateway credential. Two findings were already correct in code
(player passwords have always been PBKDF2 with per-user salts), one was not
reproducible, and one is documented as accepted risk with a plan. The canonical
server was never exposed by any of it; the risk was entirely to self-hosters,
which is exactly the audience a 1.0 invites. Detail in
`RELEASE_NOTES_0.65.14.md`.

## Known limits, stated plainly

- World news feed entries are stored pre-rendered in English. Interface,
  dialogue, quests, and gameplay text are fully localized in all five
  languages; the news feed is not yet. Fixing it is a data-model change, not a
  translation pass.
- The Electron graphical client is optional and incomplete. The terminal
  client is the supported way to play, and the store page says so.
- The Linux auto-updater for BBS deployments does not currently apply updates.
- Save files from the earliest alpha versions may not be fully compatible.

## Tests
908 passing, including the localization integrity gate and the security
regression suite added in 0.65.14.

## Files Changed
- `Scripts/Core/GameConfig.cs` - Version 1.0.0, release name "Coronation"
- `Scripts/Core/GameEngine.cs` - ShowAlphaBanner renamed to ShowLaunchBanner; box padding/truncation made robust; border recolored; off-by-one fixed
- `Localization/{en,es,fr,it,hu}.json` - 7 banner strings rewritten per language
- `web/lang/{en,es,fr,it,hu}.json` - Roadmap NOW and 1.0 entries updated for launch
- `README.md` - 1.0 header, known issues, version history entry
- GitHub `production` environment - required reviewer added (repository setting, not a file)
