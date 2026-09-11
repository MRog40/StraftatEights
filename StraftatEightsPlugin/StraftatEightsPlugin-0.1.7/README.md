Description: A large mod that adds health, movement, and weapon tweaks as well as many game modes.

This mod is a work in progress and will be getting fixes and more game modes.
Ping @MRog40 in the STRAFTAT Discord with any issues.

For multiplayer troubleshooting, set `Diagnostics` -> `Debug Logging` to `true` in the BepInEx config. Reproduce the issue on every peer and collect each `LogOutput.log`. Set it back to `false` after testing because the diagnostic mode writes detailed sync, scene, HUD, and player lifecycle events.

Mycelium transport failures are recovered with bounded retries and targeted peer-session reset probes. State snapshots remain revision-validated, so retries do not replay old rounds or scores.
