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

## Installation

1. Install BepInEx 5 for STRAFTAT.
2. Install the MyceliumNetworking dependency listed in `manifest.json`.
3. Copy `StraftatEightsPlugin.dll` into the BepInEx plugins folder.
4. Restart the game after changing the plugin version.

The plugin configuration is created by BepInEx on first launch. Hosts control the shared settings for their lobby.

The `Global Settings` section contains the shared `Points To Win` limit for FFA, Juggernaut, Sniper Battle, and Gun Game. The host also selects the next enabled game mode at random when a match starts or a map changes. Clients receive both values from the host.
