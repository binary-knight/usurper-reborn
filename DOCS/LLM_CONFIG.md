<!-- doc: checked 1.3.4; sources: Scripts/Server/MudServer.cs, DOCS/release-notes/RELEASE_NOTES_1.0.3.md -->
# LLM Moments: Sysop Configuration

LLM moments were removed in 1.0.3. The game no longer contacts any language model service in any
mode, reads no `USURPER_LLM_*` variable and has no API key for a server operator to configure.
See `DOCS/release-notes/RELEASE_NOTES_1.0.3.md`.

A server set up before 1.0.3 may still carry the systemd drop-in
`/etc/systemd/system/usurper-mud.service.d/llm.conf` (and `portraits.conf`); the game does not
read them, so they can be deleted and their keys rotated at the provider. The old `llm_usage`
table may remain in an existing database; nothing reads or writes it.
