# Straftat Eights

Straftat Eights is a host-authoritative BepInEx plugin for STRAFTAT. It adds configurable movement,
health, weapons, and custom game modes for multiplayer lobbies.

## Compatibility

This plugin changes gameplay and is not vanilla-compatible. Every player in a lobby must use the same
Straftat Eights version and MyceliumNetworking version.

The host controls the shared settings. Clients receive the active settings, game mode, scores, roles,
and loadout state over MyceliumNetworking.

It has been compatible with all of the other mods I'm testing side by side with, but other mods that
effect game mode, spawn logic, etc will likely conflict with this mod. 

## Installation

This will be published to Thunderstore. I use Gale. If it's not published, you can also place the
`StraftatEightsPlugin.dll` into `%APPDATA%\Roaming\com.kesomannen.gale\straftat\profiles\Default\BepInEx\plugins\StraftatEightsPlugin`.

## Shared Features

### Movement Settings

- Enable or disable the movement tuning.
- Change ground movement speed, ADS speed, gravity, momentum, and air movement speed.
- Enable or disable wall jumping, wall-jump speed boosts, sliding, and slide speed boosts.
- Set momentum independently from movement speed. Higher momentum makes acceleration, stopping, and
	turning heavier; lower momentum makes movement more immediate.

### Health Settings

- Change maximum health from 10% to 400% of normal.
- Enable or disable health regeneration.
- Configure regeneration delay and regeneration rate.
- Game modes with their own health rules take priority over these global settings.

### Respawn

- Configure the respawn delay from 0 to 10 seconds.

### Weapon Settings

- Restrict normal weapon spawners to a list of allowed weapon IDs.
- Configure spare magazines for newly granted weapons.
- Enable F8 weapon cycling. F8 replaces the current weapon with the next allowed weapon.
- Weapon cycling is disabled while a game mode owns its loadouts, such as Gun Game or Sniper Battle.
- Weapon spawning, hand attachment, and ammo setup remain server-authoritative.

## Game Modes

Enable one or more modes in the `Game Mode Settings` section. The host selects the next enabled mode
at random when a match starts or a map changes. Only one mode is active at a time.

### Default

Uses normal STRAFTAT rules, map weapon spawners, and default health. Global movement settings remain
available. This mode does not use custom respawning or global weapon overrides.

### Free For All

Players respawn after death and earn 10 points per kill. The first player to reach 100 points wins.

### Juggernaut

The first player to draw blood becomes the Juggernaut. Killing the Juggernaut claims the crown. The
Juggernaut uses a Minigun, has increased health, gains health and points for kills, and moves at a
different speed. Crown changes and scores are synchronized to the lobby.

### Gun Game

Players progress through a host-defined weapon order. A kill advances the killer to the next weapon.
Weapons receive unlimited magazine reloads. This mode owns its weapon loadouts and ignores global
weapon cycling.

### Sniper Battle

Players respawn with an M2000 and unlimited ammo. Each kill awards 10 points, and the first player to
reach 100 points wins.

### Michael Meyers

One player becomes Michael and hunts the survivors with a Couperet. Survivors must avoid Michael until
the round ends. This is a one-life mode.

### Kill the Rat

Players start with unlimited Glocks. The first killer becomes the Rat and receives a Taser, half health,
and increased movement speed. The Rat earns one point per second; killing the Rat awards 10 points and
transfers the role.

### One in the Chamber

Players receive one Pistol shot and a Couperet in the off hand. A kill gives the killer another Pistol
bullet. Players have one life, and the last player alive wins.

### Hot Potato

One player receives a BaseballBat and the other players receive Shotguns. Kills score points. When the
bat holder gets a kill, the victim respawns with the bat and the former holder receives a Shotgun.

### Infidel

One player is privately assigned as the Infidel. The other players are Terrorists. Players start with
slow movement and no weapons, then receive delayed loadouts. The Infidel has increased health while
Terrorists have normal health and no regeneration. The Infidel earns 10 points per terrorist kill.
Terrorists earn no points for killing another Terrorist, but the Infidel earns 10 points. Killing the
Infidel awards the killer 10 points for each Terrorist still alive. The first player to reach 100 points
wins.

## Configuration

ModMenu automatically lists the plugin's BepInEx configuration entries. Most settings are host-controlled
and are safest to change before starting a lobby. Clients should use the same plugin version as the host.