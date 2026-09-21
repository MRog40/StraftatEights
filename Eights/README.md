Description: A large mod that adds health, movement, and weapon tweaks as well as many game modes.

This mod is a work in progress and will be getting fixes and more game modes.
Ping @MRog40 in the STRAFTAT Discord with any issues.

## Game Modes

**Default** is a round-based last-player or last-team-standing mode. Players earn points for winning
takes, and the first player or team to reach the shared point target wins the match.

**Free For All (FFA)** awards individual kill points. The first player to reach the shared point target
wins the round.

**Juggernaut** gives the crown to the first player to draw blood. The Juggernaut uses a Minigun, gains
health from kills, and moves more slowly; killing the Juggernaut claims the crown and transfers the role.

**Gun Game** moves each player through the configured weapon order as they earn kills. The first player
to finish the weapon progression wins.

**Sniper Battle** gives every player an M2000 and low health. Players earn points from sniper kills,
and the first player to reach the point target wins.

**Michael Meyers** selects one player as Michael after the round begins. Michael hunts the survivors with
a Couperet, while the survivors try to eliminate Michael; take scores continue until the target is
reached.

**Kill The Rat** selects the Rat after the first legitimate kill. The Rat uses a Taser
and moves faster, while the other players use Glocks; survival time and kills award points, and killing
the Rat transfers the role.

**One In The Chamber** gives each player a Revolver with one chambered shot and a Couperet. Players have
very low health, kills award reserve bullets, and the last player alive wins each take.

**Hot Potato** rotates non-Potato players through the configured weapon list and selects the first Potato
when a player dies. The Potato carries a HandGrenade, killing a player transfers it to the victim, and kill
points determine the winner.

**Infidel** selects one hidden Infidel against the remaining Terrorists. After the preparation delay,
the Infidel uses an AK-K while the roles fight through repeated takes for points; the Infidel wins
by surviving after all Terrorists are eliminated.

**Infected** selects one player as the initial Infected. The Infected uses only a Couperet and cannot
pick up guns. Survivors have 10 health and receive random weapons from the global allowed list; normal
weapon droppers remain active. Any survivor death turns that player Infected. Survivors who outlast the
round timer all receive a round point, while the initial Infected receives the point when every survivor
has been infected.

**HVT** selects a High-Value Target after the first legitimate kill. The HVT earns survival points,
killing the HVT transfers the role to the killer, and the first player to reach the point target wins.

**Assassin** assigns a hidden Assassin, a King, and Bodyguards each take. After the weapon delay,
the Assassin uses Silenzzio, the King uses a Taser, and Bodyguards use Glocks; killing the King rewards
the Assassin, while stopping the Assassin rewards the King and Bodyguards.

**Hardpoint** is a host-controlled team mode. It assigns balanced teams, rotates map-defined hardpoints
every 30 seconds, awards uncontested objective time, and shows team-colored outlines and a scoreboard.

**Capture The Flag** is a host-controlled two-team mode on `Barren_01_Alt`. It uses two authored bases,
awards 25 points per capture, and runs for 200 seconds at the default 100-point limit. Flags can be
carried, dropped on death, returned, and captured only when the carrier's own flag is home.

**Search And Destroy** is a host-controlled two-team bomb mode. Attackers carry and plant the bomb at
an authored site, defenders can eliminate attackers or defuse the bomb, and teams alternate sides over
rounds until one reaches the round-win target.

**Team Deathmatch** is a host-controlled team mode. It uses the same balanced two- or three-team
assignment and safe spawn logic as Hardpoint, awards 10 points per kill to the killer's team, and
respawns players after death.

**Ninja Hunters** is a host-controlled two-team mode. Ninjas use Katanas and Hunters use FG42s, with
one life per take. Team sides swap at each take; a team wipe wins immediately, while a timeout starts
a five-second uncontested hold on the map's first Hardpoint objective. Each take awards 40 points.

**Rabbit Hunters** uses the same single-life team rules. Hunters use Tromblonjs, while Rabbits
dual-wield Stylus weapons; team sides swap at each take, and a timeout starts the same five-second
uncontested hold on the map's first Hardpoint objective. Each take awards 40 points.

Hardpoint, Capture The Flag, Search And Destroy, and Team Deathmatch use the configured `Allowed Weapons`
and `Spare Magazines` settings for host-authoritative team loadouts. Each round creates one shuffled
weapon sequence and gives matching team slots the same weapons. Each team's respawns advance through
that sequence independently. The global weapon toggle and F8 cycle do not override these team loadouts;
normal weapon drops and pickups remain available.

Ninja Hunters and Rabbit Hunters use fixed team weapons and do not use the global weapon sequence.

The host can disable `Enable Map Overrides` under `Global Settings` to use the
normal STRAFTAT lobby map selection. Hardpoint then runs only on maps with authored
Hardpoint objectives; other modes can run on every selected map.