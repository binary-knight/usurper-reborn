# Usurper Reborn v0.65.13 - NPC Portrait Fidelity

## Summary

Fixes the "AI portraits look horrible" report. The v0.65.7 painted NPC portraits
were being generated correctly (the cached 128px busts are clean, recognizable
character art) but were destroyed on the way to the screen by two compounding
encode-side choices: a defensive 16-color tier for the game's own [O] Online
client, and a 34x28-pixel canvas too small to carry a painted bust. No art is
regenerated; every cached portrait immediately renders at the new fidelity.

## Root Cause

A player screenshot from the WezTerm client showed an NPC portrait as
unrecognizable color noise. Pulling the NPC's cached PNG from the server proved
the SOURCE was fine; a pixel-exact simulation of the encoder reproduced the
screenshot from the 16-color tier and showed the same file at truecolor was
already clearly the same character. Two fixes follow from that:

1. **The game's own client was locked to 16-color.** `ResolveTier` sent every
   `Local` / `Steam` connection to `Ansi16` because clients older than v0.65.7
   pipe server output through a legacy SGR parser that shreds truecolor
   `38;2` sequences. That defensive choice punished every CURRENT client too.

2. **34x28 pixels cannot carry a painted 128px bust**, even in truecolor.

## Changes

### Client version handshake (AUTH 5th field)
- The [O] Online client now appends its game version to the login AUTH header:
  `AUTH:user:pass:type:0.65.13`. Pre-0.65.13 servers ignore the extra field
  (their `Split(':', 5)` already tolerated it); pre-0.65.13 clients simply
  don't send it and keep the safe tier. The REGISTER form is deliberately NOT
  versioned (a 6th field would fold into the type token on old servers); the
  first session after registering renders at the safe tier, every later login
  is versioned.
- `MudServer` parses the field, carries it through `PlayerSession` into
  `SessionContext.ClientVersion`.
- `NPCPortraitSystem.ClientSupportsTrueColor(version)`: 0.65.7+ clients (the
  first whose SGR parser passes truecolor through) are eligible for the
  truecolor portrait tier. `Local` / `Steam` connections with an eligible
  version now get truecolor; older ones stay on 16-color exactly as before.

### Larger truecolor canvas (48x48 px)
- `PortraitEncoder` gains a size-parameterized `Encode(rgba, w, h, tier, cols,
  rows)` overload plus `TrueColorCols = 48` / `TrueColorRows = 24` constants.
  The classic `Encode(...)` overload still returns the 34x14 footprint.
- The truecolor tier (web terminal, WezTerm single-player, and now
  version-eligible [O] clients) renders portraits at 48x24 cells = 48x48
  half-block pixels, validated against real cached portraits as clearly
  recognizable. The BBS `Ansi16` tier keeps the classic 34x14 CP437-scene
  footprint, and `Xterm256` (SSH / raw-TCP MUD) also stays at 34x14 so 80x24
  terminals don't scroll the talk menu off-screen.
- The portrait frame (`RenderFramed`) and the encoded-row memory cache are
  size-aware.

## Deploy Notes
- Server binary + client binaries. The server change is backward compatible
  with old clients; the client change is backward compatible with old servers.
  Players see the improvement once BOTH their client and the server are on
  0.65.13 (single-player sees it from the client binary alone).
- No cache invalidation: existing `/var/usurper/portraits/*.png` files render
  at the new fidelity immediately.

## Tests
- 11 new tests (895/895 total): sized-overload footprint per tier (glyph-count
  proof), classic-overload 34x14 contract, `ClientSupportsTrueColor` version
  gating (null / garbage / 0.65.6 / 0.65.7 / 0.65.13 / 1.0.0).

## Files Changed
- `Scripts/Core/GameConfig.cs` - Version 0.65.13
- `Scripts/UI/PortraitEncoder.cs` - Size-parameterized encode; TrueColorCols/Rows; encoders derive dims from the pixel canvas
- `Scripts/Systems/NPCPortraitSystem.cs` - ResolveLayout (tier + footprint); version-gated truecolor for Local/Steam; size-aware cache key and frame
- `Scripts/Server/MudServer.cs` - Parse optional AUTH 5th field (client version)
- `Scripts/Server/PlayerSession.cs` - ClientVersion property, flows into SessionContext
- `Scripts/Server/SessionContext.cs` - ClientVersion field
- `Scripts/Systems/OnlinePlaySystem.cs` - Login AUTH forms append the client version
- `Tests/PortraitTests.cs` - 11 new tests
