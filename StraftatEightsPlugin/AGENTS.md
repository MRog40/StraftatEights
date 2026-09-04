# AGENTS.md — StraftatEightsPlugin

Development notes for AI agents / contributors working on this BepInEx mod for STRAFTAT.
Keep this updated as the mod grows — treat it as persistent project memory.

## Core AI Instrunctions
- Always respond and use ASD-STE100 Simplified Technical English, a way of writing to simplify

## What this is
A BepInEx 5 plugin for STRAFTAT (Unity/Mono, FishNet networking, Steam lobbies). Host-authoritative
custom game rules/tweaks, synced to all players in a lobby via MyceliumNetworking.

## Key repo/tool locations
- Game source (open source!) is cloned locally at `D:\Repos\STRAFTAT-Public\STRAFTAT`
  (`Assets\Scripts\CONTROLLER\FirstPersonController.cs`, `PlayerHealth.cs`,
  `Assets\Scripts\GAMEPLAY\PlayerManager.cs`, etc). **Always check here first** for real field/method
  names and logic instead of decompiling — it's the ground truth for game logic and comments.
- Not part of this VS Code workspace, so `grep_search`/`file_search` don't reach it — use
  `Select-String -Path <file>` in a terminal instead.
- Other published Straftat mods (decompiled) live at `D:\Repos\StraftatMods\*.cs` — useful reference
  for established patterns (ModMenu API usage, MyceliumNetworking usage, Harmony patch conventions).
- Game install (Managed DLLs for compiling against): `F:\SteamLibrary\steamapps\common\STRAFTAT\STRAFTAT_Data\Managed`
  (path is a csproj property `GameManagedDir`, overridable).
- BepInEx plugins folder (Gale mod manager profile): `C:\Users\michael\AppData\Roaming\com.kesomannen.gale\straftat\profiles\Default\BepInEx\plugins`
  (csproj property `BepInExPluginsDir`, used only to locate the compile-time Mycelium reference).
- MyceliumNetworking dependency DLL: `...\BepInEx\plugins\straftatmodding-MyceliumNetworking\MyceliumNetworkingForStraftat.dll`.

## BepInEx project and distribution setup
- `StraftatEightsPlugin.csproj` is a normal class-library build: it produces only the plugin assembly
  (plus `.pdb` and `.deps.json` build artifacts) and has no post-build deployment target. Copy the DLL
  into a test profile manually or use the package layout for distribution.
- BepInEx/Unity package references are compile-time tooling and use `PrivateAssets="all"`; they must
  not be shipped with the mod. `BepInEx.Core` already supplies the compatible `HarmonyX`
  2.7.0 dependency; do not add a separate newer `HarmonyX` package reference, because newer versions
  pull in a `MonoMod.Backports` runtime chain that is not present in the game's BepInEx installation.
- Game assemblies (`Assembly-CSharp`, Unity, FishNet, Steamworks) and the external
  `MyceliumNetworkingForStraftat.dll` are direct compile-time references with `Private=false`; they are
  supplied by the game or their own BepInEx package and must not be copied into this mod's package.
- `[BepInDependency("RugbugRedfern.MyceliumNetworking")]` in `Plugin.cs` tells BepInEx that Mycelium
  is a required plugin and establishes load order. It does not download DLLs or replace Thunderstore
  package metadata.
- `manifest.json` is the public package metadata. Its Thunderstore dependency
  `straftatmodding-MyceliumNetworking-1.1.17` tells Gale/Thunderstore to install Mycelium separately;
  keep this in sync with the supported Mycelium version. Do not confuse Thunderstore package IDs with
  BepInEx plugin GUIDs.
- Public packages should contain the manifest, README/icon assets, and `StraftatEightsPlugin.dll`.
  Do not include `Assembly-CSharp.dll`, Unity/BepInEx DLLs, Mycelium's DLL, or build-only NuGet DLLs.

## Decompiling the shipped game DLL (when the open-source repo isn't enough)
Sometimes you need the actual **compiled/FishNet-weaved** runtime names (see gotcha below), which
differ from the open-source repo. Use `ilspycmd` (dotnet tool, already installed globally):
```
ilspycmd -l c "<dll>"                  # list class names
ilspycmd -t TypeName -o <outdir> "<dll>"  # decompile one type to a file
```
Clean up scratch decompile output when done (it's outside the project, e.g. a temp folder next to
the repo — never commit it).

## Critical gotcha: FishNet method renaming
FishNet's build-time ILPP weaver renames/wraps some `NetworkBehaviour` lifecycle methods in the
**shipped, compiled** assembly (not in the open-source repo, which still shows the original names):
- `Awake`, `OnEnable`, `OnDisable`, `Start` → renamed to `X___UserLogic` with a generated wrapper that
  calls network init + the original body. **Harmony patches targeting these must use the `___UserLogic`
  suffix** (e.g. `"Awake___UserLogic"`), not the plain name.
- `Update`, `OnControllerColliderHit`, and other custom/non-overridden methods keep their original
  names — patch those directly.
- If a patch target doesn't seem to exist, decompile the real DLL and check for renaming before
  assuming the method doesn't exist.

## Networking: MyceliumNetworking (RugbugRedfern.MyceliumNetworking)
This is how host-authoritative settings get synced to all lobby members. Namespace `MyceliumNetworking`.
- `MyceliumNetwork.IsHost` / `InLobby` / `Players` (CSteamID[]) / `LobbyHost`
- Events: `LobbyEntered`, `LobbyLeft`, `PlayerEntered(CSteamID)`, `PlayerLeft(CSteamID)`
- `MyceliumNetwork.RegisterNetworkObject(objInstance, modIdUint, mask=0)` then mark instance methods
  `[CustomRPC]` (found via reflection by method name string, any params allowed)
- `MyceliumNetwork.RPC(modId, "MethodName", ReliableType.Reliable, ...args)` — broadcast to all
- `MyceliumNetwork.RPCTarget(modId, "MethodName", CSteamID target, ReliableType.Reliable, ...args)` —
  send to one player (used to catch up late joiners via `PlayerEntered`)
- **Serializer only supports primitives**: byte, bool, int, uint, short, ushort, long, ulong, float,
  string, Vector3, Quaternion, CSteamID (+ byte[]/bool[] arrays). No custom structs/classes — flatten
  everything to primitive RPC params.
- Give each feature module its own unique `uint ModId` constant for `RegisterNetworkObject`/RPC calls.

## Project architecture: one folder per feature module
- `Plugin.cs` — minimal bootstrap only: `Awake()` sets `Plugin.Instance` (a persistent MonoBehaviour
  handle other modules can use to host coroutines or attach child components), calls each module's
  `InitializeXxx()`, then `Harmony.PatchAll()`. `Plugin` is a `partial class` so each module
  contributes to it from its own file.
- `Shared/` — cross-module helpers with no config/state of their own, reused by multiple feature
  modules (currently just `PlayerLookup.cs`: resolving `PlayerHealth`/`ClientInstance`/killer from a
  `PlayerId`, building the `{PLAYER_NAME}:{id}` chat tag). Add future cross-cutting helpers here
  (e.g. a team-assignment or weapon-giving helper) instead of duplicating them per game mode - see
  "Planning for future game modes" below.
- Each feature lives in its own folder, e.g. `GlobalModifiers/`:
  - `GlobalModifiersConfig.cs` — `partial class Plugin`: `ConfigEntry<T>` fields, `InitializeXxx()`
    (binds config, wires `SettingChanged` → push-if-host, registers the Mycelium network object), and
    the `[CustomRPC]` sync method (must live on the `Plugin` partial since that's the registered object).
  - `GlobalModifiersState.cs` — `internal static class` holding the *effective* synced values every
    peer enforces locally, plus `Apply`/`PushIfHost`/`OnLobbyEntered`/`OnPlayerEntered` sync logic.
    Only the host's config is authoritative; `PushIfHost()` no-ops for non-hosts.
  - `GlobalModifiersPatches.cs` — all `[HarmonyPatch]` classes that read the State class and actually
    change game behavior.
  - Extra helper classes (e.g. `MovementTuning.cs`, `PlayerHealthTuning.cs`) for reflection-heavy or
    otherwise-reusable logic, kept out of the patches file.
- When adding a new feature, copy this file layout into a new folder rather than growing an existing
  one, and give it its own `ModId` constant.

## Planning for future game modes (Juggernaut is the first; more are coming)
- `Juggernaut/` is the reference layout for a full **game mode** (as opposed to GlobalModifiers,
  which is just always-on tweaks): `JuggernautConfig.cs` (config + RPCs), `JuggernautState.cs`
  (synced runtime state + host-only game logic), `JuggernautPatches.cs` (Harmony hooks into the
  actual game), `JuggernautOutline.cs` / `JuggernautHud.cs` (mode-specific visuals). Copy this shape
  for the next mode rather than growing Juggernaut's files.
- Kill/death hook: `[HarmonyPatch(typeof(GameManager), "RpcLogic___PlayerDied_3316948804")]` Postfix
  runs host-only (its RpcReader already gates on `IsServer`) - this is the one true "a kill happened"
  event, reuse it for any mode that needs kill-based scoring instead of re-deriving it. Killer
  identity comes from `PlayerLookup.FindKillerId(deadPlayerHealth)` (reads the dead player's
  `PlayerHealth.killer` Transform, set by every weapon script on a killing hit).
- Auto-respawn pattern: Postfix on `PlayerHealth.DespawnObject()` (public, unrenamed - safe to patch
  by plain name), gated on `__instance.IsOwner`, starts a delayed coroutine on `Plugin.Instance` (not
  on the dying player's own GameObject/component - that GameObject gets `SetActive(false)`
  synchronously inside `DespawnObject()`'s own call chain, so `StartCoroutine` on it would silently
  no-op) that calls `PlayerManager.TryRespawn()` after a delay. `TryRespawn()` is public, unrenamed,
  and internally checks `IsOwner`, so it's safe to call directly with no reflection.
- Per-mode movement/speed control: layer a Postfix onto `FirstPersonController.Update()` *after*
  GlobalModifiers' own Update Prefix (which sets the baseline `movementFactor`), and multiply further
  only for players matching the mode's target (e.g. `health.playerValues.playerClient.PlayerId`).
  Postfix ordering across unrelated Harmony patch classes isn't guaranteed, so this only takes full
  effect one frame after a state change - fine given everything is already re-applied every frame.
- Team awareness: `ScoreManager.Instance.GetTeamId(playerId)` returns the team id live (only
  meaningful when `SteamLobby.Instance.playingTeams` is on) - there's no team state to cache/sync,
  just read it directly wherever needed. Not implemented in any current mode.
- Weapon-giving to a specific player: see `GunGameSource.cs` in `StraftatMods/` for the full
  precedent (`GunGameWeaponManager.GiveWeaponToPlayer`) - it's a real coroutine dance (find the
  player's `PlayerPickup`, drop/despawn whatever they're holding via `NetworkObject`/
  `ServerManager.Despawn`, then spawn+attach the new weapon prefab). Non-trivial; hasn't been ported
  here yet since no current mode needs it, but that source is the reference if/when one does.
- Mode isolation: nothing currently stops two game modes from being enabled at once and fighting over
  the same hooks (e.g. two modes both wanting to control `movementFactor` or auto-respawn). Only one
  mode is expected to be "the active game mode" at a time for now; if more modes are added, consider
  a shared `ActiveGameMode` guard so `InitializeXxx()`/patches for a mode no-op unless it's the
  selected one, rather than every mode independently checking its own `Enabled` config.

## Hard-won implementation lessons
- **Player objects are fully re-`Instantiate()`'d from a prefab on every spawn/respawn**
  (`PlayerManager.SpawnPlayer`), so `Awake`-based patches re-run every respawn, not just once at game
  start — but that also means a config change made *while already alive* won't visibly apply until
  the player's fields are touched again.
- **Prefer applying config-driven values every frame (in `Update`), not just at `Awake`.** Several bugs
  came from Awake-only application: (1) it requires a respawn to take effect, and (2) for
  network-authority-sensitive fields (like a SyncVar-backed health), `IsServer` may not even be
  reliably true that early in the FishNet lifecycle. Pattern used throughout: a `ConditionalWeakTable`
  keyed by the game object caches the last-applied "version" (a counter bumped whenever settings
  change); the per-frame patch does a cheap version-compare and only does the expensive
  work (e.g. reflection `FieldInfo.SetValue`) when something actually changed. See
  `MovementTuning.ApplyMomentumIfChanged` / `PlayerHealthTuning.ApplyIfChanged`.
- **Harmony postfixes always run, even if the original method returns early internally.** E.g.
  `PlayerHealth.Update()` has `if (!IsOwner) return;` at the top, but a Postfix patch still fires for
  every instance on every peer — useful for enforcing host-authoritative changes on objects you don't
  own (e.g. scaling *other* players' health from the host's point of view).
- **Prefer Postfixes that mutate public fields over Prefixes that rewrite `ref` arguments.** Both are
  valid Harmony techniques, but a Postfix directly setting the resulting public state (e.g.
  `__instance.forceAdded = ...`) is easier to reason about and verify than relying on Harmony's
  by-ref argument rewriting for a `Prefix`.
- **Watch call ordering within `Update()` when patches interact with decaying "force" systems.**
  `FirstPersonController` recomputes `moveDirection` fresh every frame from WASD state
  (`HandleMovementInput`) *after* slide/wall-jump impulses are set up (`HandleSlide`/`Jump`), so a
  boost's decaying contribution (`forceAdded`/`bforcefinal`) is **additive on top of** `moveDirection`,
  not a replacement for it. To cap a "speed boost" reliably, clamp the actual combined vector at the
  point it's finalized each frame (e.g. in `HandleAddingForce`/`HandleBForce`), not by rescaling the
  impulse at the moment it's created — the latter undershoots/overshoots depending on that frame's
  `moveDirection` contribution.
- **`BForce`/`AddHorizontalForce` are shared by multiple unrelated effects** (wall jump, ceiling
  bounce, glass break, taser knockback, etc). To target just one effect, find a state flag that's
  only true during that specific call (e.g. `CanWallJump` is still `true` exactly when `Jump()` calls
  `BForce` for a wall jump, and gets cleared right after) and record it in a `Prefix` on the shared
  method for a later patch to consult.
- **Straftat's "acceleration" fields only smooth which WASD keys are held, not world-space velocity.**
  `moveDirection` is recomputed fresh every frame from `dirForward`/`dirRight` (current camera facing)
  — there's no persisted velocity vector. So spinning the camera 180° while holding the same key was
  always instant regardless of acceleration settings. Fixed by adding a *new* velocity-blend layer
  (`MovementTuning.BlendHorizontalVelocity`, patched onto `HandleMovementInput`) that smooths the
  resulting world-space direction/speed across frames — this didn't exist in stock Straftat at all.
- **To change a `[SyncVar]`-backed value (e.g. `PlayerHealth.health`) from an external, un-weaved
  assembly**, don't write the field directly and don't call the public RPC wrapper either (extra
  round-trip, meant for owner-initiated calls). Call the **generated `RpcLogic___MethodName_<hash>`
  method directly**, gated on `IsServer`, exactly like the published Juggurnaught mod does for its
  bonus-health-on-kill feature (negative "damage" = heal). The hash suffix is FishNet codegen and may
  change if the game updates — re-verify via decompile if patches stop working. Plain (non-SyncVar)
  fields like `fullHealth` are safe to write directly.
- **`[ObserversRpc]`/`[ServerRpc]` methods keep their plain name, but their body gets rewritten** to
  call generated `RpcWriter___.../RpcLogic___..._<hash>` helpers. The **real shared logic lives in
  `RpcLogic___MethodName_<hash>`**, called both by the plain-named wrapper (when `RunLocally = true`)
  *and* by `RpcReader___...` on remote observers/server. Patch the `RpcLogic___...` method (not the
  plain name) if you need the behavior to apply uniformly for every peer, not just the caller — e.g.
  `FirstPersonController.RpcLogic___PlaySoundObservers_3316948804` is the actual method containing all
  the `audio.PlayOneShot(...)` calls for footsteps/jump/ladder/fall sounds (used to scale footstep
  volume without touching unrelated sound effects funneled through the same method by clip-id).

## Testing
- No bots/dummies use `PlayerHealth`, so testing damage/health scaling generally needs a second real
  player in the lobby (or self-damage sources like fall damage / hazards for a quick solo sanity check
  — health scaling applies at spawn, so the HUD number changes immediately even without taking damage).
- A solo hosted lobby still counts as `IsHost`/`InLobby`, so most toggles/sliders can be sanity-checked
  alone; only *cross-player sync* (host changes → other client receives) needs a second real account.
- No BepInEx hot-reload is set up (declined — restart the game to pick up DLL changes). Enable
  `[Logging.Console] Enabled = true` in the Gale profile's `BepInEx.cfg` to see load/error output.

## ModMenu integration
ModMenu (`kestrel.straftat.modmenu`) auto-lists a plugin's BepInEx `Config.Bind` entries — no
`ModMenu.Api` reference needed for simple checkboxes/sliders/dropdowns/enum pickers. Use
`ConfigDescription` + `AcceptableValueRange<T>`/`AcceptableValueList<T>` for sliders/dropdowns; an
unrestricted bind renders as a free-text numeric box. Group related settings with the same section
string in `Config.Bind` (first arg) — ModMenu shows one header per section.
