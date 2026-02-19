# v0.42.3 - BBS Hotfix

Fixes crashes on Windows BBS systems

---

## Bug Fixes

### Timezone Crash on Windows BBS (Critical)
Fixed a crash when creating new characters or logging in on Windows-based BBS systems. The daily reset system used the IANA timezone ID `America/New_York` which only works on Linux/macOS. Windows uses `Eastern Standard Time`. The game now tries both, so it works on all platforms.