# Straftat Eights

Host-authoritative gameplay rules and shared movement, health, weapon, and game-mode settings for STRAFTAT.

## Compatibility

This plugin changes gameplay and is not vanilla-compatible. Every player in a lobby must use the same plugin and MyceliumNetworking package versions.

The plugin supports these modes and systems:

- Global movement and health settings
- Global weapon restrictions and weapon cycling
- Free For All
- Juggernaut
- Gun Game
- Sniper Battle
- Michael Meyers
- Kill the Rat
- One in the Chamber
- Hot Potato
- Infidel

## Installation

1. Install BepInEx 5 for STRAFTAT.
2. Install the MyceliumNetworking dependency listed in `manifest.json`.
3. Copy `StraftatEightsPlugin.dll` into the BepInEx plugins folder.
4. Restart the game after changing the plugin version.

The plugin configuration is created by BepInEx on first launch. Hosts control the shared settings for their lobby.

For development and future mode work, see [MULTIPLAYER_SYNC_NOTES.md](MULTIPLAYER_SYNC_NOTES.md) and
[FUTURE_MODE_GUIDE.md](FUTURE_MODE_GUIDE.md).

FFA, Juggernaut, Sniper Battle, and Gun Game use a shared 100-point limit. Normal kills award 10 points; taking the Juggernaut crown awards 20 points. The host selects the next enabled game mode at random when a match starts or a map changes. Clients receive the active mode and score state from the host.

Kill the Rat gives every player an unlimited Glock. The first player to get a kill becomes the Rat, gains a Taser, has half health, and moves at double speed. The Rat gains one point per second; killing the Rat awards 10 points and transfers the role. If the Rat dies without a killer, players must kill each other to choose a new Rat.

One in the Chamber gives each player one Pistol shot and a Couperet in the off hand. Each kill gives the player one more Pistol bullet. Players have one life; the last player alive wins.

Hot Potato gives one player a BaseballBat and every other player a Shotgun. All kills score points toward the global limit. When the bat holder gets a kill, the victim respawns with the bat and the former holder receives a Shotgun.

Infidel privately assigns one player as the Infidel and everyone else as Terrorists. Players have slow movement, no sliding or wall jumping, no weapons for 10 seconds, then receive an AK-K with two spare magazines. The Infidel has 200 health; Terrorists have 100 health and no regeneration. Role events change scores, and the first player to 100 points wins.

Gun Game grants the current weapon from the host to every player and gives each weapon unlimited magazine reloads.
