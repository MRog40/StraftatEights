# Future Game Mode Guide

Use this guide before adding or changing a STRAFTAT game mode. The goal is to keep every mode host-authoritative, recoverable after missed network messages, and isolated from unrelated modes.

## 1. Define the mode contract

Write these decisions before writing code:

- What state is authoritative on the FishNet server?
- Which settings come from the host config?
- Which global systems does the mode override: health, weapons, movement, respawn, HUD, or outlines?
- What starts and ends a round or sub-round?
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

Every feature gets a unique `uint ModId`. Register the persistent `Plugin.Instance` once for that feature and put the feature's `[CustomRPC]` methods on the `Plugin` partial class.

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
- a new round or sub-round;
- a mode change;
- a scene transition;
- a late-join state replacement when the snapshot advances the round.

A mode that remains active across maps still needs a new-round reset. Do not assume that changing the enum value is required to detect a new round.

Private state uses targeted RPCs. For example, a private role assignment must send only the receiving player's role. Do not place private identity data in a broadcast live snapshot.

## 7. Respect mode precedence

`GameModeManager` is the source of active-mode capability flags. A mode-specific rule must explicitly override a global rule while that mode is active.

Examples:

- Gun Game, Sniper Battle, One in the Chamber, Hot Potato, Michael Meyers, Exterminators, and Infidel can ignore global weapons when their loadouts require it.
- Sniper Battle, One in the Chamber, and Infidel can ignore global health when their health rules require it.
- A Rat, Infidel, or Juggernaut movement multiplier layers on top of or replaces global movement only when the mode's precedence rule says so.
- A mode that owns respawn timing must use the shared respawn helper and prevent the normal path from fighting it.

When adding a capability, add the guard at the shared patch boundary. Do not rely on patch ordering to produce precedence.

Only one game mode is expected to be active at a time. If future modes can be selected simultaneously, add a shared active-mode guard before allowing two modes to patch the same system.

## 8. Reuse shared gameplay services

Use `WeaponService.GiveWeapon(playerId, weaponName, spareMagazines)` for server-authoritative weapon grants. Do not instantiate prefabs or duplicate hand attachment logic in a mode.

Use `WeaponService.GiveWeaponToLeftHand(...)` for off-hand grants and `AttachUnparentedWeapon(...)` for repair of an existing hand SyncVar whose transform is not attached.

Use `WeaponAmmoTuning` for magazine, reserve, reload, and HUD behavior. Native reload weapons keep their native reload path. Do not add a second ammo counter or temporarily alter the native reload flag.

Persistent choices belong in dictionaries keyed by `ClientInstance.PlayerId`. Player and weapon GameObjects are recreated on every spawn and respawn. Loadout reconciliation must retry for a bounded time because settings, player objects, parent objects, and SyncVars can arrive in different orders.

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
