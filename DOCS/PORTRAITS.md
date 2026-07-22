# AI-Generated NPC Portraits (v0.65.7)

Every named NPC can have a painted, Darkest-Dungeon-style portrait shown on the
`[T] Talk` screen, replacing the classic procedural block portrait. Portraits are
generated ONCE per NPC by the PixelLab image API, cached to disk forever, and
rendered into the terminal at whatever fidelity the player's connection supports.
The system never blocks gameplay: while a portrait does not exist yet (or the
feature is unconfigured), the classic procedural portrait renders exactly as before.

## How it works

1. A player talks to an NPC. If a cached portrait PNG exists for that NPC, it is
   decoded and encoded to ANSI for the session's terminal tier, framed in the same
   box-and-name-bar layout as the classic portrait.
2. If no cached portrait exists AND generation is configured, a background task
   calls PixelLab with a prompt built from the NPC's actual identity (race, sex,
   class, age band, alignment bearing, crown for the monarch) and caches the
   result. The current visit still shows the classic portrait; the painted one
   appears from the next visit on.
3. Budget and safety: max `USURPER_PORTRAIT_DAILY_CAP` generation attempts per UTC
   day (default 60), one in-flight generation per NPC, 6-hour cooldown per NPC
   after a failure, corrupt downloads are validated and rejected before caching.

## Terminal tiers (automatic)

| Connection                  | Rendering                                        |
|-----------------------------|--------------------------------------------------|
| Web terminal, Steam, local  | 24-bit truecolor half-blocks                     |
| SSH relay, raw-TCP MUD      | xterm-256 half-blocks (safe for older clients)   |
| BBS door (CP437)            | classic 16-color ANSI with half/shade blocks     |
| Screen reader / plain text  | no art (unchanged behavior)                      |
| Electron client             | classic procedural portrait (own UI pipeline)    |

All tiers use the same 34x14 character footprint as the classic portrait. The
half-block technique (foreground paints the top pixel of a cell, background the
bottom) doubles vertical resolution to a 34x28 pixel canvas.

## Server configuration

All via environment variables. Generation is OFF by default; display of
already-cached portraits always works.

```
USURPER_PORTRAIT_ENABLED=true
USURPER_PIXELLAB_API_KEY=<key from pixellab.ai>
USURPER_PORTRAIT_DAILY_CAP=60          # optional, default 60 attempts/day
USURPER_PORTRAIT_TIMEOUT_MS=45000      # optional, default 45s
USURPER_PORTRAIT_DIR=/var/usurper/portraits   # optional; default is
                                       # {ApplicationData}/UsurperReloaded/portraits
```

Systemd drop-in for the live server (`/etc/systemd/system/usurper-mud.service.d/portraits.conf`),
one `Environment=` directive per line (the v0.64.0 llm.conf space-separation
misparse applies here too):

```
[Service]
Environment=USURPER_PORTRAIT_ENABLED=true
Environment=USURPER_PIXELLAB_API_KEY=REPLACE_ME
Environment=USURPER_PORTRAIT_DIR=/var/usurper/portraits
```

Then: `sudo mkdir -p /var/usurper/portraits && sudo chown usurper:usurper /var/usurper/portraits`,
`sudo systemctl daemon-reload`, and restart `usurper-mud` (kicks online players;
follow the normal deploy approval flow).

Watch it work: `grep PORTRAIT /opt/usurper/logs/debug.log` shows each generation
(name, bytes, latency) and any failures.

## Cache semantics

- Files: `{sanitized-name}_{10-hex-hash}.png` in the cache dir. The hash covers
  name, race, sex, and class: a class change or rename regenerates; aging and
  leveling do NOT (no cache churn as the world simulates).
- The cache is plain PNG files. Sysops can pre-generate portraits offline, copy
  the folder between machines, drop in hand-made portraits (any 8-bit PNG works,
  ~128x128 recommended), or delete a file to force regeneration.
- Cost: roughly USD 0.01 per portrait; a full 200-NPC world is about two dollars,
  spread over days by the daily cap. New NPCs (immigrants, children coming of
  age) trickle in far below the cap.

## Code map

- `Scripts/UI/PortraitEncoder.cs` -- pure 3-tier ANSI encoder (34x28 half-block
  canvas, weighted palette matching, shadow gamma lift for xterm-256, BBS-safe
  low-8 backgrounds for 16-color). Ported from the validated prototype in
  `tools/portrait_halfblock_poc.py`; previews in `tools/portrait_poc_out/`.
- `Scripts/UI/MiniPng.cs` -- dependency-free PNG decoder (8-bit depth, color
  types 0/2/3/4/6, no interlace). Out-of-profile files throw and the caller
  falls back to the procedural portrait.
- `Scripts/Systems/NPCPortraitSystem.cs` -- settings, disk cache, tier
  resolution, framed rendering, budget, background generation, prompt builder.
- Call site: `BaseLocation.InteractWithNPC` (the `[T] Talk` screen).
- QA tooling: `tools/ans_to_png_check.py` renders any encoded `.ans` back to a
  PNG simulation of what the terminal shows.
