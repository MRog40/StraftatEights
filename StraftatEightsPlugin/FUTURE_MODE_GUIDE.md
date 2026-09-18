# Future Game Mode Guide

Use this guide before adding or changing a STRAFTAT game mode. The goal is to keep every mode host-authoritative, recoverable after missed network messages, and isolated from unrelated modes.

## 1. Define the mode contract

Write these decisions before writing code:

- What state is authoritative on the FishNet server?
- Which settings come from the host config?
- Which global systems does the mode override: health, weapons, movement, respawn, HUD, or outlines?
- What starts and ends a round or take?
- What happens on death, respawn, scene change, lobby join, lobby leave, and late join?
- Which state is public to all peers, and which state must be private to one player?
- What is the exact win condition and score limit?

Keep this contract in the mode's state class and in `MULTIPLAYER_SYNC_NOTES.md` when behavior is verified.

## 2. Use the standard file layout

Create one folder with these files where needed:

- `ModeConfig.cs` - BepInEx config entries and Mycelium RPC entry points.
- `ModeState.cs` - authoritative state, settings application, snapshots, round logic, and host-only actions.
- `ModePatches.cs` - Harmony hooks into the game.
- `ModeHud.cs` - mode-specific HUD, if needed.
- `ModeOutline.cs` or another helper - local presentation logic, if needed.

Add the mode to `GameModeManager` with one descriptor containing:

- display label and color;
- enabled-config predicate;
- reset callback;
- capability flags;
- periodic settings push callback;
- periodic live-state push callback;
- loadout reconciliation callback.

Do not grow one existing mode into a shared miscellaneous file. Put reusable behavior in `Shared/` only when at least two features need the same behavior.

### Assassin mode contract

Assassin uses a host-authoritative take state. One player is the private Assassin, one player is the public King, and all remaining players are Bodyguards. Each player receives only their own private role announcement. The King ID is public through the shared green outline state; the Assassin ID is never included in public live snapshots.

The King receives `Taser` immediately. After 15 seconds, the Assassin receives `Silenzzio` and Bodyguards receive `Glock`. The role weapons use the shared authoritative weapon service and unlimited-ammo path. Other weapons are blocked while the mode is active.

The Assassin receives 50 points when the King dies, including a friendly-fire King death. When the Assassin dies, the King receives 30 points, every Bodyguard receives 10 points, and the Bodyguard who made the kill receives an additional 20 points. Other deaths do not end the take. Scores persist between takes and use the shared `Points To Win` setting. The Assassin identity is announced publicly only after the take resolves.

Assassin does not override global health or movement settings. Its custom behavior is limited to roles, weapons, scoring, public King presentation, and take respawns.

### Hardpoint mode contract

Hardpoint assigns teams once at the official round start. It uses three balanced teams when the
player count is divisible by three; all other non-empty player counts use two teams. Team IDs and
scores are host-authoritative and are sent in revisioned live snapshots with a lobby-data fallback.

The first two team origins come from the map definition. If three teams are needed, the third origin
is selected from the map spawn candidates to maximize its distance from the authored origins. Respawn
selection then chooses the farthest valid team candidate from active enemies. Team assignments remain
stable through takes and are rebuilt for each official map or mode round.

Hardpoints use the map definition order, rotate every 30 seconds, and warn five seconds before the
next point. A team scores one point per uncontested second inside the two-unit vertical zone from
`y - 1` through `y + 1`. A contested or empty point drains the contest clock; a tied expiry enters
sudden death. Respawns use the shared configured delay. Team-colored outlines, the local scoreboard,
and ground markers are presentation-only and are reapplied on every peer.

### Team-mode weapon contract

Hardpoint, Capture The Flag, Search And Destroy, and Team Deathmatch share one team weapon path.
Team modes always keep map droppers active, but each dropper selects from the existing `Allowed
Weapons` list instead of its original map weapon. Picking up a dropped weapon uses the configured
`Spare Magazines` and shared ammo lifecycle, even when global weapon tweaks or F8 cycling are
disabled. Players also spawn with a weapon: the host creates one shuffled permutation of the allowed
weapon names, sorted player IDs establish the initial slot within each team, and respawns use an
independent cursor per team to receive the next weapon from the same sequence.

Use `Shared/TeamWeaponLoadouts.cs` for this behavior. Do not add a per-mode dispenser grant, a second
weapon allocator, or a client-side spawn path. The allocator detects a new player object after a
respawn and calls `WeaponService.GiveWeapon(playerId, weaponName, spareMagazines)` once for that
spawn. Do not continuously enforce the held weapon: normal Straftat drops and pickups must remain
usable after the grant. Team modes must keep the `IgnoreGlobalWeapons` capability so global cycling,
global item randomization, and cycle-only drop restrictions do not fight the team allocation.

## 3. Keep the plugin bootstrap complete

`Plugin.Awake()` must bind every config entry even if an optional subsystem fails. Startup modules use the safe initializer in `Plugin.cs`, which logs the failed feature and continues with later config bindings.

This matters because ModMenu reads the plugin's BepInEx `ConfigFile`. If startup stops halfway through, ModMenu only shows the sections bound before the exception. A stale config file can hide this problem because old sections remain on disk.

When debugging missing settings:

1. Check the current `BepInEx/LogOutput.log`.
2. Confirm the log contains `Plugin <guid> is loaded!`.
3. Confirm the current DLL hash matches the deployed DLL.
4. Check the generated config file, but do not treat old entries as proof that the current startup completed.
5. Check for `[Startup] Failed to initialize ...` messages.
6. Restart the game after replacing the DLL. There is no hot reload.

ModMenu automatically lists supported `Config.Bind` entries. Use bools, strings, integral or floating values, enums, keyboard types, and acceptable ranges or lists. Do not add a ModMenu reference for ordinary config pages.

BepInEx requires an `AcceptableValueRange` minimum to be lower than its maximum. For a fixed value,
use a normal config description and enforce the constant in code; do not use a range such as `100, 100`.
An invalid range throws during startup and can leave later config bindings and lobby lifecycle hooks
uninitialized.

## 4. Keep authority host-side

The host owns:

- rules and phase transitions;
- scores, kills, crowns, roles, and alive sets;
- health writes and regeneration;
- weapon spawning, despawning, ownership, and loadouts;
- round completion and scene transitions.

Clients may display state and request an action through a registered Mycelium RPC. A client must not locally grant a weapon, award points, select a winner, or write authoritative health.

Every feature gets a unique `uint ModId` in the plugin's one global ModId namespace. This includes
game modes, shared systems, and transport helpers. Before adding a mode, search all source files
for existing `ModId` constants and choose a value that is not already used. Do not reuse an old
value, even when the old feature is currently disabled: Mycelium uses the ModId to find RPC
handlers, and a collision can route packets to the wrong registration and break deserialization.
Register the persistent `Plugin.Instance` once for that feature and put the feature's `[CustomRPC]`
methods on the `Plugin` partial class.

Mycelium RPC parameters must be primitives supported by the installed serializer. Flatten dictionaries into strings such as `id:value;id:value`, then validate every parsed ID and value.

## 5. Use the shared sync protocol

Every networked mode uses one private `ModeSyncState`:

- `NextSettingsRevision()` for settings snapshots;
- `NextLiveRevision()` for live state snapshots;
- `IsSettingsPushDue()` for periodic settings resend;
- `IsLivePushDue()` for periodic live-state resend;
- `TryAcceptSettingsSnapshot(...)` for settings cursors;
- `TryAcceptLiveSnapshot(...)` for live cursors;
- `ResetForLobby()` when a lobby session starts or ends;
- `ResetLiveState()` when a match or round state is cleared.

A snapshot normally contains:

- the host Steam ID;
- the game round ID;
- a stream-specific revision;
- the mode payload.

Settings and live state have separate revision streams and accepted cursors. Do not reuse one revision for both. A new round may restart its live revision at any value accepted by the round-aware validation, but outgoing revision counters must not be reset merely because gameplay state was reset.

A successful call to `RPC()` or `RPCTarget()` only proves that the local send call did not throw. It does not prove delivery. Resend settings and live state periodically while the host is in a lobby. Late-join sends are useful, but they are not a replacement for periodic retry.

Keep thin adapter methods such as `TryAcceptSettingsSnapshot(...)` when existing RPC handlers use them. The adapter preserves the wire API while delegating cursor logic to `ModeSyncState`.

## 6. Separate settings from live gameplay state

Settings state includes values such as enabled flags, weapon lists, score limits, and movement multipliers. Apply it on every peer after an accepted host snapshot.

Live state includes values such as scores, roles, alive players, current holder, winner, and phase. Reset all live collections on:

- a new lobby;
- a new match;
- a new round or take;
- a mode change;
- a scene transition;
- a late-join state replacement when the snapshot advances the round.

A mode that remains active across maps still needs a new-round reset. Do not assume that changing the enum value is required to detect a new round.

Private state uses targeted RPCs. For example, a private role assignment must send only the receiving player's role. Do not place private identity data in a broadcast live snapshot.

## 7. Respect mode precedence

`GameModeManager` is the source of active-mode capability flags. A mode-specific rule must explicitly override a global rule while that mode is active.

Examples:

- Gun Game, Sniper Battle, One in the Chamber, Hot Potato, Michael Meyers, Kill The Rat, Infidel,
  Hardpoint, Capture The Flag, Search And Destroy, and Team Deathmatch can ignore global weapons when
  their loadouts require it.
- Sniper Battle, One in the Chamber, and Infidel can ignore global health when their health rules require it.
- A Rat, Infidel, or Juggernaut movement multiplier layers on top of or replaces global movement only when the mode's precedence rule says so.
- A mode that owns respawn timing must use the shared respawn helper and prevent the normal path from fighting it.

When adding a capability, add the guard at the shared patch boundary. Do not rely on patch ordering to produce precedence.

Only one game mode is expected to be active at a time. If future modes can be selected simultaneously, add a shared active-mode guard before allowing two modes to patch the same system.

## 8. Reuse shared gameplay services

Use `WeaponService.GiveWeapon(playerId, weaponName, spareMagazines)` for server-authoritative weapon grants. Do not instantiate prefabs or duplicate hand attachment logic in a mode.

For the four team modes, use the shared team allocator instead of assigning a fixed weapon. Its host
sequence must be mirrored by team slot, use an independent cursor per team on respawn, and pass the
configured spare-magazine count. Keep the grant one-shot for each player object so normal drops and
pickups are not overwritten.

Use `WeaponService.GiveWeaponToLeftHand(...)` for off-hand grants and `AttachUnparentedWeapon(...)` for repair of an existing hand SyncVar whose transform is not attached.

Use `WeaponAmmoTuning` for magazine, reserve, reload, and HUD behavior. Native reload weapons keep their native reload path. Do not add a second ammo counter or temporarily alter the native reload flag.

Use the shared safe-spawn path for modes that respawn players. The mode descriptor must opt into the
`SafeRespawn` capability. It scores scene or mode-provided
candidates by nearest-enemy distance, host-side line of sight, teammate proximity, and optional
objective proximity. FFA, Gun Game, and other non-team modes treat every other active player as an
enemy. Team modes provide team candidates and classify same-team players as teammates. Line of sight
is a strong penalty rather than a hard rejection so open maps and incomplete collider timing retain a
deterministic distance-based fallback.

Persistent choices belong in dictionaries keyed by `ClientInstance.PlayerId`. Player and weapon GameObjects are recreated on every spawn and respawn. Loadout reconciliation must retry for a bounded time because settings, player objects, parent objects, and SyncVars can arrive in different orders.

Team-mode loadouts use `Shared/TeamWeaponLoadouts.cs`, not a per-mode fixed grant. The host creates one
shuffled sequence from the configured allowed list, assigns matching team slots the same weapon, and
advances an independent cursor for each team on respawn. Pass `SpareMagazines` to
`WeaponService.GiveWeapon`, assign only once per new player object, and preserve normal drops and
pickups after the grant.

Coroutines that survive despawn must run on `Plugin.Instance`. A dying player's GameObject is disabled during despawn, so a coroutine started on that object can stop immediately.

## 9. Patch the real game path

Use the open-source STRAFTAT project as the first source for fields and normal logic. Use the shipped compiled DLL and `ilspycmd` when FishNet weaving or runtime names matter.

FishNet changes some lifecycle method names in the compiled assembly:

- `Awake`, `OnEnable`, `OnDisable`, and `Start` commonly require the `___UserLogic` target.
- `Update`, custom methods, and `OnControllerColliderHit` normally keep their plain names.
- Generated RPC logic names contain a hash and can change after a game update.

For a SyncVar-backed field, do not write the field from the external plugin and do not call an owner-only public ServerRpc as a shortcut. Use the generated server logic method already used by the game, gated by server authority, and re-check its name after game updates.

For kill-based modes, use the host-only `GameManager` death hook and `PlayerLookup.FindKillerId(...)` instead of reconstructing kills from weapon or health patches.

## 10. Treat visuals as local state

Every peer must apply its own outlines, materials, HUD text, and local transforms after it receives authoritative state. Network state alone does not create a visual.

Player cosmetics can replace materials after spawn. Reapply outlines after cosmetics initialization or enforce them while the target remains active. Test both directions: host target observed by client, and client target observed by host.

Resolve current player objects by player ID. Do not trust a cached `ClientInstance.PlayerSpawner.player` during respawn. Check both `value == null` and `!value` before calling Unity methods on a possibly destroyed object.

## 11. Validate incrementally

Before play testing:

1. Build Debug.
2. Run the pure checks.
3. Run diagnostics on touched files.
4. Run `git diff --check`.
5. Search for old per-mode revision, cursor, and retry fields.
6. Build Release.

For team weapon changes, also verify that initial team slots match, each team advances independently
on respawn, the allowed list reshuffles without duplicates inside one cycle, and a dropped weapon is
not immediately replaced.

For play testing, use the matrix in `MULTIPLAYER_SYNC_NOTES.md`:

- solo host settings and HUD;
- host target and client observation;
- client target and host observation;
- kill and target change;
- same mode in a new round;
- mode change;
- respawn;
- late join;
- scene transition;
- host and client log comparison.

When a test fails, first identify whether the problem is transport, snapshot acceptance, stale object lookup, authoritative mutation, or local presentation. Do not add another retry or RPC until that layer is known.

## 12. Version-sensitive items

Recheck these after a STRAFTAT, FishNet, or Mycelium update:

- generated `RpcLogic___..._<hash>` names;
- generated weapon hand-attachment methods;
- lifecycle method names used by Harmony patches;
- Mycelium serializer-supported parameter types;
- ModMenu supported config types;
- game player and weapon spawn order;
- material and cosmetic initialization order.
