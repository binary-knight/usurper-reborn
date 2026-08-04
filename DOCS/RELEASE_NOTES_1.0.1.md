# Usurper Reborn v1.0.1

Emergency hotfix for a launch-day regression that blocked login for every
desktop and Steam player. One-line cause, one-function fix, eight regression
tests so it cannot recur.

## The bug

Desktop and Steam clients could not log in. Selecting an account returned
straight to the main menu with an error too brief to read. The browser client
was unaffected.

The client sends its login as a single line:

```
AUTH:<username>:<password>:<connectionType>:<clientVersion>
```

The relay it connects through rejected it:

```
[RELAY] Invalid AUTH format: 4 parts
```

## Root cause

v0.65.13 appended the trailing `clientVersion` field so capable clients could
be served full-color NPC portraits. `MudServer`'s AUTH parser was updated to
accept it. **`RelayClient`'s parser was not.** It accepted exactly two shapes
(3 payload fields for login, 4 with a `REGISTER` discriminator) and rejected
anything else outright.

The desktop and Steam clients connect SSH-encrypted **by default**, and that
path goes through the relay. So the regression took out the default connection
method for every non-browser player from v0.65.13 onward.

### Why it was hard to see

Three things disguised a transport-wide outage as an account-specific problem:

- **The server logged nothing.** The rejection happens in the relay, one hop
  before `MudServer`, which writes its `Connection from ...` line only after
  parsing. A 340 MB server log contained zero evidence of the failing logins.
- **Direct TCP kept working.** A raw-socket probe skips the relay and hits
  `MudServer`'s (correct) parser, so protocol-level testing of the exact wire
  format passed while real clients failed.
- **The browser kept working.** The web terminal logs in interactively (the
  user types at a menu) rather than sending an AUTH line, so it never touched
  the broken parser.

The combination made it look like one player's account was broken. It was the
transport.

## The fix

`RelayClient.ParseDirectAuth` now accepts all four legitimate wire shapes:

| Payload (after the `AUTH:` prefix) | Meaning |
|---|---|
| `user:pass:type` | login, pre-0.65.13 client |
| `user:pass:type:version` | login, 0.65.13+ client (**the broken shape**) |
| `user:pass:REGISTER:type` | registration |
| `user:pass:REGISTER:type:version` | registration, versioned |

The `REGISTER` discriminator is tested **before** the plain 4-field login
shape. Both are four fields, so without that ordering a registration would be
parsed as a login with `connectionType = "REGISTER"`. Malformed shapes are
still rejected as before.

Change is deliberately scoped to the parser. Nothing else was touched.

## Tests

`Tests/RelayAuthParseTests.cs` (new, 8 tests) pins every accepted shape,
including the exact line that was failing, the case-insensitive `REGISTER`
discriminator precedence, and continued rejection of malformed input. Adding a
field to the AUTH line again will now fail the build rather than silently
break the default connection path.

**916/916 tests pass.**

## Deploy requirements

This fix must ship to **both ends**:

1. **Server redeploy.** The relay runs from the same server binary
   (`UsurperReborn --mud-relay`), so the running server carries the broken
   parser until it is replaced.
2. **Steam depot upload.** Clients on 0.65.13 through 1.0.0 send the versioned
   line; they work against a fixed server without updating, but shipping the
   current build keeps the two ends in step.

Order matters: **deploy the server first.** A fixed server accepts both old and
new clients, so existing installs start working immediately without anyone
having to update.

## Known limits

- **Portrait fidelity on the SSH path is unchanged from pre-0.65.13.** The
  relay accepts the client version but does not forward it upstream, so
  `MudServer` cannot see it and Steam clients on the SSH path fall back to
  16-color portraits instead of truecolor. Degraded, not broken. Completing
  that is a feature change, not an outage fix, and was deliberately kept out
  of this hotfix.
- **The browser login menu takes about 3.5 seconds to appear** while the relay
  starts and connects. Keystrokes typed during that window are lost. Worth a
  progress indicator.
- **Two different login screens exist.** Direct connections get the 1.0.0
  screen with the `[G]` language picker; the browser path gets an older screen
  without it. The browser is what most new players use, so it is the one that
  should have the language option.

## Note on the version number

The `v1.0.1` tag was already used by a documentation-only commit that added
`DOCS/STEAM_RELEASE_NOTES_1.0.0.txt` (its commit message describes code changes
that were not in the diff). This release is the first 1.0.1 containing actual
code.

## Files Changed
- `Scripts/Core/GameConfig.cs` - Version 1.0.1
- `Scripts/Server/RelayClient.cs` - `ParseDirectAuth` accepts versioned login and register shapes; promoted to `internal` for test access
- `Tests/RelayAuthParseTests.cs` - New, 8 regression tests
