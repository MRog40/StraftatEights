# Multiplayer Sync Notes

These notes record verified behavior from the STRAFTAT FishNet and MyceliumNetworking paths in this
repository. Read them before adding a networked game mode or patch.

## Confirmed Mycelium failure and permanent recovery

The long two-machine test on 2026-09-10 completed without a scoreboard desync. The client log had no
error-level entries, exceptions, dropped RPCs, Mycelium send errors, or failed session messages. It had
one expected stale-snapshot warning: a Sniper Battle lobby-data revision arrived after the client had
already accepted the newer RPC revision. The client rejected the old revision and continued accepting
new score snapshots.

The original failure was a transport-session problem, not a scoreboard-only problem. Mycelium 1.1.17
sets Steam's reliable and auto-restart flags, but its `SendBytes` method has no delivery acknowledgement
or retry queue. It logs `ProblemDetectedLocally` when the Steam Networking Messages session fails, while
the caller can still return as if the RPC was sent. A one-shot state RPC can therefore disappear, and a
later client can remain on an old or empty state until another mode reset.

The plugin now has a shared transport recovery layer in `Shared/MyceliumTransportRecovery.cs`:

- A Harmony patch covers Mycelium's private `SendBytes` funnel, so all current `RPC()` and `RPCTarget()`
  calls use the same recovery path.
- An explicit Steam send failure or send exception is queued for bounded retry at 0.35, 0.75, 1.5, 3,
  and 6 seconds. The queue has a 128-message limit and identical queued packets are not duplicated.
- A successful send is never sent again by this layer. This prevents duplicate weapon grants, respawns,
  input requests, and announcements.
- A Harmony patch observes the Mycelium session-failure callback, closes only the affected peer session,
  and sends a targeted probe/ack exchange to make the next send establish a fresh session.
- The probe and retry queues are cleared when the lobby ends. Recovery is bounded; it cannot repair a
  Steam outage, a peer that left the lobby, or an incompatible Mycelium version.

Transport recovery is only one part of the contract. Latest-value state must still be safe to resend:
include the host Steam ID, round ID, and revision; reject stale or foreign snapshots; periodically resend
host state; and use a Steam lobby-data fallback for important presentation state. The fallback is required
because it uses a separate Steam lobby metadata path and does not depend on the Mycelium peer session.
Sniper Battle now follows the same fallback pattern as Gun Game and active-mode state.

When testing a future networking change, verify the following before changing gameplay code:

1. Both peers load the same plugin DLL and the same Mycelium version.
2. The host and client logs show the expected `IsHost` value and the same lobby host Steam ID.
3. State snapshots carry the same round and increasing revision on both peers.
4. A stale snapshot rejection is followed by acceptance of a newer snapshot, not by a state reset.
5. The log has no `Session request failed`, `Error sending message`, `Dropped RPC`, or `Error executing RPC`
   entries during the failure window.
6. Test the same mode across several kills, a respawn, a new round, a mode change, and a late join.

Do not solve a new presentation mismatch by adding an unconditional retry to a gameplay command. Add a
revisioned latest-value snapshot for state, or add an explicit command ID and receiver deduplication for
an action that must be retried.

## Authority and State

- Keep game rules, scores, kills, crowns, health writes, weapon spawning, and weapon ownership on the
  FishNet server. Clients should request a host action through a registered Mycelium RPC.
- Mycelium `RPC()` and `RPCTarget()` returning without an exception does not prove delivery. Settings
  and live state need periodic host broadcasts, not only a one-shot send during lobby join or a config
  change. Keep a revision and round ID in every snapshot.
- `ReliableType.Reliable` does not remove the Steam/Mycelium session failure mode. For small latest-value
  state that controls client presentation, use a registered Steam lobby-data key as a second channel.
  Publish the host ID, round ID, revision, and payload; read it on lobby entry and on
  `LobbyDataUpdated`; apply the same host and cursor validation as the RPC path. Gun Game settings,
  Gun Game scores, Sniper Battle settings/scores, and the active game mode use this fallback.
- Accept snapshots by host identity, round ID, and revision. A newer round must accept a reset
  revision, and client mode state must reset when the round ID advances even if the mode is unchanged.
- Reset all per-match dictionaries and IDs when the mode, round, lobby, or session changes. Do not
  leave the previous mode's scores in a shared HUD state.
- Mycelium serializers support primitives and selected arrays only. Flatten dictionaries into a
  string such as `id:score;id:score`, then validate IDs and score limits when parsing.

## Kill the Rat

- The host owns the current Rat, points, and role transfers. A valid player kill awards 10 points
  to the killer; the Rat earns 1 point per second while active, up to the shared 100-point target.
- A death with no valid killer clears the Rat role instead of selecting the dead player as a new Rat.
  The host announces that humans must kill each other to choose the next Rat.
- The mode ignores global weapon cycling so the host can enforce an unlimited Glock for humans and a
  Taser for the Rat. It keeps global movement and human health settings; the Rat layers 2x movement
  and a fixed 50% baseline health on top of the shared rules.
- Rat loadouts are retried after every respawn because player and weapon objects are recreated. Role
  state is keyed by `ClientInstance.PlayerId`, not by a cached player object.

## One in the Chamber

- The host owns the alive-player set and each player's reserve bullet count. A valid Pistol or
  Couperet kill awards one reserve bullet; environmental and other non-kill deaths only remove the
  player from the alive set.
- The mode grants the exact `Pistol` prefab in the right hand and `Couperet` in the left hand. It
  blocks other weapon pickups and retries both grants because player and weapon objects can be
  recreated during a round.
- The Pistol starts with one chambered round. Reserve bullets persist by `ClientInstance.PlayerId`
  and use the shared manual reload path; the mode does not use Webley or Glock.
- Every player has exactly 10 health while the mode is active. The host clamps excess health through
  the normal FishNet health path, so the Pistol's first hit is lethal.
- One in the Chamber never schedules a respawn. The last remaining alive player completes the custom
  round through the host's normal round transition.

## Hot Potato

- The host owns the score and current bat holder. Every valid kill awards the shared 10-point kill
  score; the first player to the global 100-point target wins.
- The exact `BaseballBat` and `Shotgun` prefabs are enforced. The bat holder keeps the bat until that
  player gets a kill, then the victim becomes the bat holder after respawning and the former holder
  receives a Shotgun.
- Hot Potato uses the normal custom-mode respawn path. Loadouts are retried after each respawn because
  the game creates new player and weapon objects.

## Infidel

- The host keeps the Infidel identity private by sending each peer only its own role. The identity is
  not included in the broadcast live-state payload.
- Each sub-round chooses one Infidel, keeps all other players as Terrorists, and gives no weapons for
  10 seconds. After the delay, active players receive the exact `AK-K` prefab with two spare magazines.
- The Infidel has 200 health; Terrorists have 100 health. Global health and regeneration settings are
  ignored while the mode is active. Movement is 70 percent with sliding and wall jumping disabled.
- The Infidel's death ends the sub-round immediately. The Infidel earns 10 points per Terrorist kill.
  A Terrorist-on-Terrorist kill awards 0 points to the killer and 10 points to the Infidel. Killing
  the Infidel awards the killer 10 points for each Terrorist still alive after the kill. All players
  then respawn for a new sub-round, and scores persist until a player reaches 100 points.

## FishNet RPCs and Ownership

- Always inspect the shipped, FishNet-weaved DLL when patching a network method. The open-source
  method body shows the intended logic, but generated `RpcLogic___..._<hash>` names and ownership
  behavior come from the compiled assembly.
- A `[ServerRpc]` wrapper may only be called by its owner. For server-side changes to a SyncVar,
  call the generated `RpcLogic___..._<hash>` method directly on the server when that is the game's
  established path. Do not call the public wrapper from external code and create an extra round trip.
- An `[ObserversRpc(..., ExcludeOwner = true)]` does not run on the owning client. If its logic sets
  local hand state, transforms, HUD references, or other owner-visible fields, the owner needs a
  separate local path or owner-targeted notification.
- The normal server hand-attachment method can already call observer logic and then transfer
  ownership. Do not call the observer wrapper or logic a second time without checking the generated
  server method first. Duplicate calls can run in the wrong order around ownership transfer.
- Network-spawned objects can arrive before their SyncVars, parent, or dependent components are
  ready. Use a bounded retry on a persistent plugin object for client repair. Never start a delayed
  coroutine on a player object that the game disables during despawn.

## Player and Weapon Lifetimes

- The game instantiates a new player object and a new weapon object on every spawn and respawn.
  Store persistent selections and scores by `ClientInstance.PlayerId`, not on a player or weapon
  component.
- `ClientInstance.PlayerSpawner.player` can point to an old player during respawn. Before using it,
  verify `PlayerHealth.playerValues.playerClient.PlayerId`. If it fails, search current live
  `PlayerHealth` objects by that identity.
- A client can receive a valid hand SyncVar while the weapon is still unparented or on a world layer.
  The game's `RightHandFix()` can then interpret it as dropped. Complete the owner-side attachment
  before allowing that repair/drop check to run, and clear the temporary guard after success or a
  bounded timeout.
- Stock `PlayerSetup.OnStartClient()` clears the owner ammo text after every respawn, and
  `PlayerSetup.OnDisable()` clears and moves both ammo displays for every teardown. Reapply the owner
  HUD after start/disable and suppress remote teardown cleanup when it would modify the local HUD.
- A destroyed Unity object can pass a C# null-conditional check. Before calling Unity methods on a
  cached object, check both `value == null` and `!value`.

## Client-Side Visuals

- Server state does not automatically make a client visual appear. Materials, renderer state, HUD
  text, and outline properties are local to each peer. Apply presentation on every peer after that
  peer receives the authoritative state.
- `PlayerSetup.ChangeDress` replaces player renderer materials after spawn. Any outline or material
  property applied before that call can disappear. Reapply persistent visual state after material
  changes, or enforce it while the target remains active.
- Host-owned and client-owned players use different local/remote renderer branches. Test both
  directions: host is the target and client is the target. Resolve the visible `PlayerHealth` by its
  player ID, then apply to that peer's local renderers.
- Do not mark a visual target as complete when no renderer was ready. Leave it eligible for a later
  retry after spawn, cosmetics, or scene transition.

## Test Matrix

For every host-authoritative mode, test at least:

- Host target, client observes the target.
- Client target, host observes the target.
- Target changes after a kill.
- Same mode starts in a new round.
- Mode changes between rounds.
- Player respawns while the mode is active.
- A player joins after the state already exists.

When a result is asymmetric, compare the host and client logs and verify these facts in order:

1. The client received a snapshot with the current host ID, round ID, and revision.
2. The client accepted it instead of rejecting it as old state.
3. The client resolved the current replicated player object, not a stale respawn object.
4. The client applied the local presentation after the player's materials and renderers were ready.

## Shared sync implementation

- `Shared/ModeSyncState.cs` owns transport bookkeeping only: settings/live revisions, resend timers,
  accepted snapshot cursors, lobby reset, and live-state reset. It must not know a mode's payload,
  score rules, roles, loadouts, or coroutines.
- Every mode and global sync system has one private `ModeSyncState`. Keep settings and live revisions
  separate. Use `LastLiveRoundId` only for mode-specific round-aware live-state decisions.
- `ResetForLobby()` clears accepted cursors and retry timers. `ResetLiveState()` clears live cursors
  without resetting outgoing revision order. This prevents stale state while preserving ordering for
  late joiners.
- A mode's state class keeps the RPC method signature, payload construction, payload validation,
  gameplay state, and thin acceptance adapters. This keeps the wire contract stable while removing
  repeated transport code.
- Custom resend intervals belong in the mode's `ModeSyncState` constructor. Juggernaut uses a one
  second live-state interval; ordinary settings and live streams use the shared default interval.

## Global systems

- Global movement, health, and weapon settings use the same host ID, round ID, revision, acceptance,
  late-join, and periodic resend rules as game modes.
- Global weapons keeps selected cycling weapons and pending loadout retries outside the sync helper.
  Selection is keyed by player ID because weapon objects are replaced after respawn.
- `GameModeManager` has separate settings and active-mode streams. Its active-mode snapshot carries
  the mode, round, phase, and live revision; its global settings snapshot carries respawn and score
  values. Do not merge these payloads just to reduce RPC count. Active mode also has a Steam lobby-data
  latest-value fallback because the client HUD visibility depends on this stream.
- Mode capabilities provide precedence guards at shared patch boundaries. A mode-specific weapon,
  health, movement, or respawn rule must explicitly block or layer the global rule while active.
- Custom respawns remain host-authoritative and use the shared respawn timing path. They do not add
  temporary invincibility or a special player outline.

## Startup and ModMenu

- BepInEx and ModMenu read the plugin's `ConfigFile`, not the source files. A config file can contain
  entries left by an older build, so old sections do not prove that the current startup completed.
- Bind every config entry before optional HUD setup or patch work can stop startup. `Plugin.cs` uses
  per-feature safe initialization and logs `[Startup] Failed to initialize ...` while continuing to
  later config modules.
- ModMenu automatically displays supported `Config.Bind` entries. Normal settings do not require a
  ModMenu API reference. Use shared section names to group related settings.
- When settings disappear after a refactor, check the current BepInEx log for the final plugin-loaded
  message, compare the deployed DLL hash with the build output, and restart the game after replacing
  the DLL. There is no hot reload.

## Future mode checklist

Before merging a new mode, confirm:

- the mode has its own folder, config, state, patch, and presentation files;
- it has one unique Mycelium `ModId` and primitive RPC payloads;
- all host actions and authoritative mutations are server-side;
- settings and live state use `ModeSyncState` and periodic resend;
- lobby, match, round, scene, respawn, and late-join resets are explicit;
- global precedence flags are applied at shared patch boundaries;
- player and weapon lookups use current player IDs, not stale component references;
- all local visuals are applied on every peer after spawn and cosmetics;
- Debug, pure checks, diagnostics, whitespace checks, Release, and the multiplayer test matrix pass.

For the complete mode recipe and version-sensitive checklist, see `FUTURE_MODE_GUIDE.md`.