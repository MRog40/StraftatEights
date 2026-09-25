Sndtat, Hardtat, Capturetat, Tdmtat, Ffatat, Michaeltat, Chambertat, Infectedtat, Assassintat, and MORE.

Minimap, health regen, custom movement, reloading for all weapons, custom spawns, respawn logic, and MORE.

This mod is a work in progress and will be getting fixes, more maps, and more game modes over time.
Ping @MRog40 in the STRAFTAT Discord with any issues or suggestions.

## Game Modes

**Straftat** is a round-based last-player or last-team-standing mode. Players earn points for winning
takes, and the first player or team to reach the shared point target wins the match.

**Ffatat** awards individual kill points. The first player to reach the shared point target
wins the round.

**Nifetat** is a melee-only Ffatat. Every player has 10 health and uses one randomly selected melee weapon;
the weapon changes at the start of each round.

**Juggertat** gives the crown to the first player to draw blood. The Juggernaut uses a Minigun, gains
health from kills, and moves more slowly; killing the Juggernaut claims the crown and transfers the role.

**Guntat** moves each player through the configured weapon order as they earn kills. The first player
to finish the weapon progression wins.

**Snipertat** gives every player an M2000 and low health. Players earn points from sniper kills,
and the first player to reach the point target wins.

**Michaeltat** selects one player as Michael after the round begins. Michael hunts the survivors with
a Couperet and moves 5% faster than them. Everyone has 10 health during the hunt; when only Michael and
the last survivor remain, the survivor gets a Couperet and both final players return to 100 health.
Take scores continue until the target is reached.

**Ratatat** selects the Rat after the first legitimate kill. The Rat uses a Taser
and moves faster, while the other players use Glocks; survival time and kills award points, and killing
the Rat transfers the role.

**Chambertat** gives each player a Revolver with one chambered shot and a Couperet. Players have
very low health, kills award reserve bullets, and the last player alive wins each take.

**Potatotat** rotates non-Potato players through the configured weapon list and selects the first Potato
when a player dies. The Potato carries a HandGrenade, killing a player transfers it to the victim, and kill
points determine the winner.

**Infideltat** selects one hidden Infidel against the remaining Terrorists. After the preparation delay,
the Infidel uses an AK-K while the roles fight through repeated takes for points; the Infidel wins
by surviving after all Terrorists are eliminated.

**Infectedtat** selects one player as the initial Infected. The Infected uses only a Couperet and cannot
pick up guns. Survivors have 10 health and receive random weapons from the global allowed list; normal
weapon droppers remain active. Any survivor death turns that player Infected. Survivors who outlast the
round timer all receive a round point, while the initial Infected receives the point when every survivor
has been infected.

**PotatoInftat** uses the same role conversion, scoring, health, timer, and respawn rules as
Infectedtat, but infected players use renewable HandGrenades instead of Couperets. Survivors receive
random weapons from the global allowed list, and every survivor death turns that player Infected.

**Hvtat** selects a High-Value Target after the first legitimate kill. The HVT earns survival points,
killing the HVT transfers the role to the killer, and the first player to reach the point target wins.

**Assassintat** assigns a hidden Assassin, a King, and Bodyguards each take. After the weapon delay,
the Assassin uses Silenzzio, the King uses a Taser, and Bodyguards use Glocks; killing the King rewards
the Assassin, while stopping the Assassin rewards the King and Bodyguards.

**Hardtat** is a host-controlled team mode. It assigns balanced teams, rotates map-defined hardpoints
every 30 seconds, awards uncontested objective time, and shows team-colored outlines and a scoreboard.

**Capturetat** is a host-controlled two-team mode on `Barren_01_Alt`. It uses two authored bases,
awards 25 points per capture, and runs for 200 seconds at the default 100-point limit. Flags can be
carried, dropped on death, returned, and captured only when the carrier's own flag is home.

**Sndtat** is a host-controlled two-team bomb mode. Attackers carry and plant the bomb at
an authored site, or press `L` to drop it for a teammate to pick up. Defenders can eliminate attackers
or defuse the bomb, and teams alternate sides over rounds until one reaches the round-win target.

**Countertat** uses the same maps, bomb rules, and scoring, but its sides never switch: Aboubi attacks
and Shadow Force defends on every take. Each team's players receive weapons by ascending player ID;
players beyond a listed sequence receive its last weapon. Aboubi / Shadow Force weapons are: take 1,
`Glock` / `Silenzzio`; take 2, `AK-K`, `Dispenser`, `Mac10` / `QCW05`, `AR15`, `SMG`; take 3,
`AK-K`, `AK-K`, `AK-K`, `Dispenser` / `QCW05`, `QCW05`, `AR15`; take 4 and later, `AK-K`, `M2000`,
`AK-K` / `QCW05`, `M2000`, `QCW05`.

**Tdmtat** is a host-controlled team mode. It uses the same balanced two- or three-team
assignment and safe spawn logic as Hardtat, awards 10 points per kill to the killer's team, and
respawns players after death.

**Ninjatat** is a host-controlled two-team mode. Ninjas use Katanas and Hunters use FG42s, with
one life per take. Team sides swap at each take; a team wipe wins immediately, while a timeout starts
a five-second uncontested hold on the map's first Hardpoint objective. Each take awards 40 points.

**Hunttat** uses the same single-life team rules. Hunters use the Tromblonj, while Rabbits
use the Smith Carbine; team sides swap at each take, and a timeout starts the same five-second
uncontested hold on the map's first Hardpoint objective. Each take awards 40 points.

**Tanktat** is a two-team Hunters variant where every player uses the HK_Caws, has 400 health, and
cannot regenerate, jump, or slide. Players stay crouched and move slowly, so teams must coordinate
their targets; take wins, side swaps, and the Hardpoint timeout tie-break use the shared Hunters rules.

Hardtat, Capturetat, Sndtat, and Tdmtat use the configured `Allowed Weapons`
and `Spare Magazines` settings for host-authoritative team loadouts. Each round creates one shuffled
weapon sequence and gives matching team slots the same weapons. Each team's respawns advance through
that sequence independently. The global weapon toggle does not override these team loadouts;
normal weapon drops and pickups remain available.

Ninjatat and Hunttat use fixed team weapons and do not use the global weapon sequence.

The host can disable `Enable Map Overrides` under `Global Settings` to use the
normal STRAFTAT lobby map selection. Hardtat then runs only on maps with authored
Hardpoint objectives; other modes can run on every selected map.

The host can enable `Keep Teams` under `Global Settings` to reuse the same team layout
between rounds when the same players return. The layout is saved separately for two-team
and three-team matches. The `Mixup teams` button restarts the current map and mode with a
new balanced layout, resets the round timer and score, and updates the saved layout.