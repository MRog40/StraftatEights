using StraftatEightsPlugin;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

int lastRound = -1;
int lastRevision = -1;
Assert(SnapshotValidation.TryAccept(2, 4, ref lastRound, ref lastRevision),
    "The first valid snapshot should be accepted.");
Assert(lastRound == 2 && lastRevision == 4, "The accepted snapshot cursor was not updated.");
Assert(!SnapshotValidation.TryAccept(1, 99, ref lastRound, ref lastRevision),
    "An older round must be rejected.");
Assert(!SnapshotValidation.TryAccept(2, 3, ref lastRound, ref lastRevision),
    "An older revision in the same round must be rejected.");
Assert(SnapshotValidation.TryAccept(2, 4, ref lastRound, ref lastRevision),
    "A duplicate snapshot should remain idempotently acceptable.");
Assert(SnapshotValidation.TryAccept(3, 0, ref lastRound, ref lastRevision),
    "A newer round with a reset revision should be accepted.");
Assert(!SnapshotValidation.TryAccept(-1, 0, ref lastRound, ref lastRevision),
    "A negative round must be rejected.");
Assert(!SnapshotValidation.TryAccept(3, -1, ref lastRound, ref lastRevision),
    "A negative revision must be rejected.");

string lobbyPayload = LobbySnapshotCodec.Build(76561198000000001UL, 7, 12, "1", "points:4", "");
Assert(LobbySnapshotCodec.TryParse(lobbyPayload, 3, out ulong lobbyHostId,
        out int lobbyRoundId, out int lobbyRevision, out string[] lobbyFields)
    && lobbyHostId == 76561198000000001UL && lobbyRoundId == 7 && lobbyRevision == 12
    && lobbyFields.SequenceEqual(new[] { "1", "points:4", "" }),
    "Lobby snapshots must round-trip the common envelope and empty fields.");
Assert(!LobbySnapshotCodec.TryParse("76561198000000001|7|12|1", 2,
        out _, out _, out _, out _),
    "Lobby snapshots with missing mode fields must be rejected.");
Assert(LobbySnapshotCodec.TryParseBool("0", out bool falseValue) && !falseValue
    && LobbySnapshotCodec.TryParseBool("1", out bool trueValue) && trueValue
    && !LobbySnapshotCodec.TryParseBool("true", out _),
    "Lobby boolean fields must use the explicit 0/1 wire format.");
Assert(LobbySnapshotCodec.TryParseOrdered("76561198000000001|11|7|2|19", 5, 0, 2, 4,
        out ulong orderedHostId, out int orderedRoundId, out int orderedRevision,
        out string[] orderedParts)
    && orderedHostId == 76561198000000001UL && orderedRoundId == 7
    && orderedRevision == 19 && orderedParts[1] == "11" && orderedParts[3] == "2",
    "Ordered lobby snapshots must validate cursors at explicit indexes.");

ModeSyncValidation sync = new();
Assert(sync.NextSettingsRevision() == 1 && sync.NextLiveRevision() == 1,
    "Settings and live revisions must start as independent streams.");
Assert(sync.TryAcceptSettings(2, 1), "The first settings snapshot should be accepted.");
Assert(sync.TryAcceptLive(1, 4), "The first live snapshot should use its own cursor.");
Assert(!sync.TryAcceptSettings(1, 99), "An older settings round must be rejected.");
Assert(!sync.TryAcceptLive(1, 3), "An older live revision must be rejected.");
Assert(sync.TryAcceptLive(2, 0), "A new live round must accept a reset revision.");
sync.ResetForLobby();
Assert(sync.TryAcceptSettings(0, 0) && sync.TryAcceptLive(0, 0),
    "Lobby reset must clear both accepted snapshot cursors.");
Assert(sync.SettingsRevision == 1 && sync.LiveRevision == 1,
    "Lobby reset must preserve outgoing revisions for late-join ordering.");

List<string> parsed = WeaponListParser.Parse(
    " Glock; SMG, Glock, Invalid, ;SMG ",
    new[] { "Glock", "SMG", "Shotgun" });
Assert(parsed.SequenceEqual(new[] { "Glock", "SMG" }),
    "Weapon parsing must trim, filter, preserve order, and remove duplicates.");
Assert(WeaponListParser.Parse(null!, new[] { "Glock" }).Count == 0,
    "A null weapon list must produce an empty result.");

TeamWeaponSequence firstWeaponSequence = new(new[] { "Glock", "SMG", "Shotgun" }, 19);
TeamWeaponSequence secondWeaponSequence = new(new[] { "Glock", "SMG", "Shotgun" }, 19);
string[] firstTeamWeapons = Enumerable.Range(0, firstWeaponSequence.CycleLength)
    .Select(firstWeaponSequence.GetAt).ToArray();
string[] secondTeamWeapons = Enumerable.Range(0, secondWeaponSequence.CycleLength)
    .Select(secondWeaponSequence.GetAt).ToArray();
Assert(firstTeamWeapons.SequenceEqual(secondTeamWeapons)
    && firstTeamWeapons.Distinct().Count() == firstWeaponSequence.CycleLength,
    "Team weapon allocations must mirror a seeded permutation without duplicates.");
string[] secondCycle = Enumerable.Range(firstWeaponSequence.CycleLength,
        firstWeaponSequence.CycleLength)
    .Select(firstWeaponSequence.GetAt).ToArray();
Assert(secondCycle.Distinct().Count() == firstWeaponSequence.CycleLength
    && secondCycle.OrderBy(weapon => weapon).SequenceEqual(firstTeamWeapons.OrderBy(weapon => weapon)),
    "A completed weapon cycle must reshuffle into another valid permutation.");
Assert(firstWeaponSequence.GetAt(3) == secondWeaponSequence.GetAt(3)
    && firstWeaponSequence.GetAt(4) == secondWeaponSequence.GetAt(4),
    "Independent team cursors must resolve the same next weapons.");

Dictionary<string, IReadOnlyList<string>> playlistMaps = new()
{
    ["FFA"] = new[] { "Barren_01_Alt" },
    ["MichaelMeyers"] = new[] { "Map_A", "Map_B" },
    ["Unsupported"] = Array.Empty<string>()
};
List<MapPlaylistEntry<string>> playlist = MapPlaylist.Build(
    new[] { "FFA", "MichaelMeyers", "Unsupported", "FFA" },
    mode => playlistMaps.TryGetValue(mode, out IReadOnlyList<string>? maps)
        ? maps
        : Array.Empty<string>(),
    new Random(17));
Assert(playlist.Count == 2 && playlist.Any(entry => entry.Mode == "FFA")
    && playlist.Any(entry => entry.Mode == "MichaelMeyers"),
    "Map playlists must keep supported modes once and skip unsupported modes.");
Random mapCycleRandom = new(23);
string firstMap = MapPlaylist.SelectNextMap(new[] { "Map_A", "Map_B" }, string.Empty,
    mapCycleRandom);
string secondMap = MapPlaylist.SelectNextMap(new[] { "Map_A", "Map_B" }, firstMap,
    mapCycleRandom);
Assert(firstMap != secondMap, "A repeated mode must rotate to a different supported map.");
List<MapPlaylistEntry<string>> firstOrdering = MapPlaylist.Build(
    new[] { "FFA", "MichaelMeyers" }, mode => playlistMaps[mode], new Random(31));
List<MapPlaylistEntry<string>> secondOrdering = MapPlaylist.Build(
    new[] { "FFA", "MichaelMeyers" }, mode => playlistMaps[mode], new Random(31));
Assert(firstOrdering.Count == secondOrdering.Count
    && firstOrdering[0].Mode == secondOrdering[0].Mode
    && firstOrdering[0].MapName == secondOrdering[0].MapName,
    "A seeded map playlist must be reproducible.");

Dictionary<int, int> scores = new() { [4] = 2, [9] = 7 };
string scoreData = ScoreCodec.Serialize(scores);
Dictionary<int, int> parsedScores = ScoreCodec.Parse(scoreData, 7);
Assert(parsedScores.Count == 2 && parsedScores[4] == 2 && parsedScores[9] == 7,
    "Score payloads must round-trip valid entries.");
Dictionary<int, int> filteredScores = ScoreCodec.Parse("1:3;2:0;3:99;bad;1:4", 10);
Assert(filteredScores.Count == 2 && filteredScores[1] == 4 && filteredScores[2] == 0,
    "Score parsing must reject malformed/out-of-range entries and keep the last duplicate.");
Assert(ScoreRules.PointsToWin == 100 && ScoreRules.PointsPerRoundWin == 50
    && ScoreRules.PointsPerKill == 10
    && ScoreRules.PointsPerJuggernautCrown == 20 && ScoreRules.PointsPerRatSurvivalSecond == 3
    && ScoreRules.PointsPerHVTSurvivalSecond == 3,
    "Shared score rules must use the 100-point target and mode award values.");
Assert(TeamRules.GetTeamCount(2) == 2 && TeamRules.GetTeamCount(3) == 3
    && TeamRules.GetTeamCount(4) == 2 && TeamRules.GetTeamCount(6) == 3,
    "Team count must use three teams only for player counts divisible by three.");
Dictionary<int, int> teamAssignments = TeamRules.AssignBalanced(new[] { 7, 2, 5, 1, 3, 4 });
Assert(teamAssignments.Count == 6 && teamAssignments[1] == 0 && teamAssignments[2] == 1
    && teamAssignments[3] == 2 && teamAssignments[4] == 0 && teamAssignments[5] == 1
    && teamAssignments[7] == 2,
    "Team assignment must be deterministic and balanced by sorted player ID.");
Dictionary<int, int> teamDeathmatchAssignments =
    TeamRules.AssignTwoTeams(new[] { 1, 2, 3, 4, 5, 6 });
Assert(teamDeathmatchAssignments.Values.Distinct().Count() == 2
    && teamDeathmatchAssignments.Values.Count(teamId => teamId == 0) == 3
    && teamDeathmatchAssignments.Values.Count(teamId => teamId == 1) == 3
    && !teamDeathmatchAssignments.Values.Contains(2),
    "Team Deathmatch must use a balanced two-team assignment for six players.");
Assert(TeamRules.GetHardpointTeamCount(4) == 2
    && TeamRules.GetHardpointTeamCount(5) == 3
    && TeamRules.GetHardpointTeamCount(7) == 3,
    "Hardpoint team count must use three teams for five and seven players.");
Dictionary<int, int> hardpointFiveAssignments =
    TeamRules.AssignHardpointBalanced(new[] { 1, 2, 3, 4, 5 });
Dictionary<int, int> hardpointSevenAssignments =
    TeamRules.AssignHardpointBalanced(new[] { 1, 2, 3, 4, 5, 6, 7 });
Assert(hardpointFiveAssignments.Values.Count(teamId => teamId == 0) == 2
    && hardpointFiveAssignments.Values.Count(teamId => teamId == 1) == 2
    && hardpointFiveAssignments.Values.Count(teamId => teamId == 2) == 1,
    "Hardpoint five-player assignment must be 2v2v1.");
Assert(hardpointSevenAssignments.Values.Count(teamId => teamId == 0) == 3
    && hardpointSevenAssignments.Values.Count(teamId => teamId == 1) == 2
    && hardpointSevenAssignments.Values.Count(teamId => teamId == 2) == 2,
    "Hardpoint seven-player assignment must be 3v2v2.");
Assert(Math.Abs(TeamRules.GetTeamHealthMultiplier(hardpointFiveAssignments, 3) - 2f) < 0.001f
    && Math.Abs(TeamRules.GetTeamHealthMultiplier(hardpointSevenAssignments, 2) - 1.5f) < 0.001f
    && Math.Abs(TeamRules.GetTeamHealthMultiplier(hardpointSevenAssignments, 1) - 1f) < 0.001f,
    "Uneven teams must receive health compensation based on player counts.");
Dictionary<int, int> unevenTwoTeams = new() { [1] = 0, [2] = 0, [3] = 1 };
Assert(Math.Abs(TeamRules.GetTeamHealthMultiplier(unevenTwoTeams, 3) - 2f) < 0.001f,
    "A one-player team must receive 100 percent extra health.");
Assert(TeamRules.GetColor(TeamRules.BlueTeamId).Equals(new TeamColorData(0, 114, 178))
    && TeamRules.GetColor(TeamRules.VermillionTeamId).Equals(new TeamColorData(213, 94, 0))
    && TeamRules.GetColor(TeamRules.GreenTeamId).Equals(new TeamColorData(0, 158, 115)),
    "Team colors must use the color-distinct palette.");
string assignmentData = TeamRules.SerializeAssignments(teamAssignments);
Dictionary<int, int> parsedAssignments = TeamRules.ParseAssignments(assignmentData, 3);
Assert(parsedAssignments.Count == teamAssignments.Count
    && parsedAssignments[7] == 2,
    "Team assignments must round-trip through the network format.");
Dictionary<int, int> filteredAssignments = TeamRules.ParseAssignments("1:0;2:9;bad;1:2", 3);
Assert(filteredAssignments.Count == 1 && filteredAssignments[1] == 2,
    "Team assignment parsing must reject invalid teams and keep the last duplicate.");
Dictionary<int, int> captureTheFlagAssignments =
    CaptureTheFlagRules.AssignStrictTwoTeams(new[] { 7, 2, 5, 2, -1 });
Assert(captureTheFlagAssignments.Count == 3 && captureTheFlagAssignments[2] == 0
    && captureTheFlagAssignments[5] == 1 && captureTheFlagAssignments[7] == 0,
    "Capture The Flag assignment must always use two deterministic teams.");
Assert(CaptureTheFlagRules.GetSpawnCandidateCount(31) == 10
    && CaptureTheFlagRules.GetSpawnCandidateCount(2) == 1
    && CaptureTheFlagRules.GetMatchDuration(100) == 200f,
    "Capture The Flag must use the closest floor-third spawn pool and a two-second-per-point timer.");
int nearestFlagTeam = CaptureTheFlagRules.FindNearestTeam(new TeamPoint(9f, 0f, 1f),
    new[] { new TeamPoint(0f, 0f, 0f), new TeamPoint(20f, 0f, 0f) });
Assert(nearestFlagTeam == 0, "A flag must belong to its nearest authored team origin.");
Assert(CaptureTheFlagRules.TryPickup(CaptureTheFlagFlagStatus.Home, true,
        out CaptureTheFlagFlagStatus carried)
    && carried == CaptureTheFlagFlagStatus.Carried
    && !CaptureTheFlagRules.TryPickup(carried, true, out _),
    "Only an enemy flag can be picked up, and a carried flag cannot be picked up twice.");
Assert(CaptureTheFlagRules.TryDrop(carried, out CaptureTheFlagFlagStatus dropped)
    && dropped == CaptureTheFlagFlagStatus.Dropped
    && CaptureTheFlagRules.TryReturn(dropped, true, out CaptureTheFlagFlagStatus returned)
    && returned == CaptureTheFlagFlagStatus.Home
    && !CaptureTheFlagRules.TryReturn(returned, true, out _),
    "A carried flag must drop on death and a dropped home flag must return on touch.");
Dictionary<int, int> captureScores = new() { [0] = 75, [1] = 20 };
Assert(CaptureTheFlagRules.TryAwardCapture(captureScores, 0, 100, true, true,
        out int captureWinner)
    && captureWinner == 0 && captureScores[0] == 100
    && !CaptureTheFlagRules.TryAwardCapture(captureScores, 1, 100, true, false, out _),
    "A capture must require the enemy flag and home flag, award twenty-five points, and win at target.");
Assert(!CaptureTheFlagRules.TryResolveTimeoutWinner(
        new Dictionary<int, int> { [0] = 50, [1] = 50 }, out _)
    && CaptureTheFlagRules.TryResolveTimeoutWinner(
        new Dictionary<int, int> { [0] = 60, [1] = 50 }, out int timeoutWinner)
    && timeoutWinner == 0,
    "A tied CTF timer must enter sudden death, while a unique leader wins.");
Assert(TeamDeathmatchRules.PointsPerKill == 10
    && TeamDeathmatchRules.AddKillPoints(0, 100) == 10
    && TeamDeathmatchRules.AddKillPoints(90, 100) == 100
    && TeamDeathmatchRules.IsMatchWon(100, 100)
    && !TeamDeathmatchRules.IsMatchWon(90, 100),
    "Team Deathmatch must award ten points per kill and stop at the team score limit.");
Dictionary<int, int> searchAndDestroyAssignments =
    SearchAndDestroyRules.AssignStrictTwoTeams(new[] { 7, 2, 5, 2, -1 });
Assert(searchAndDestroyAssignments.Count == 3 && searchAndDestroyAssignments[2] == 0
    && searchAndDestroyAssignments[5] == 1 && searchAndDestroyAssignments[7] == 0,
    "Search and Destroy assignment must always use two deterministic teams.");
Assert(SearchAndDestroyRules.GetOffensiveTeamId(1) == 0
    && SearchAndDestroyRules.GetOffensiveTeamId(2) == 1
    && SearchAndDestroyRules.GetOtherTeamId(0) == 1
    && SearchAndDestroyRules.GetOtherTeamId(1) == 0
    && SearchAndDestroyRules.GetOtherTeamId(2) == -1,
    "Search and Destroy offense must alternate between sub-rounds.");
Assert(SearchAndDestroyRules.PointsPerRoundWin == 40
    && SearchAndDestroyRules.GetSubRoundTimeLimit(100) == 100f
    && SearchAndDestroyRules.GetSubRoundTimeLimit(0) == 1f
    && SearchAndDestroyRules.PlantDurationSeconds == 5f
    && SearchAndDestroyRules.DefuseDurationSeconds == 7.5f
    && SearchAndDestroyRules.FuseDurationSeconds == 30f
    && SearchAndDestroyRules.IsMatchWon(4, 4)
    && !SearchAndDestroyRules.IsMatchWon(3, 4),
    "Search and Destroy must use the configured round and interaction timings.");
HashSet<int> searchAndDestroyAlive = new() { 2, 7 };
Assert(SearchAndDestroyRules.IsTeamWiped(searchAndDestroyAlive,
        searchAndDestroyAssignments, 1)
    && !SearchAndDestroyRules.IsTeamWiped(searchAndDestroyAlive,
        searchAndDestroyAssignments, 0),
    "Search and Destroy must detect a team wipe from the alive-player set.");
Assert(SearchAndDestroyRules.TryRecoverBomb(SearchAndDestroyBombStatus.Dropped,
        true, true, out SearchAndDestroyBombStatus recoveredBomb)
    && recoveredBomb == SearchAndDestroyBombStatus.Carried
    && !SearchAndDestroyRules.TryRecoverBomb(SearchAndDestroyBombStatus.Dropped,
        false, true, out _),
    "Only an in-range offense player can recover a dropped bomb.");
Assert(SearchAndDestroyRules.TryStartPlant(SearchAndDestroyBombStatus.Carried,
        true, true, out SearchAndDestroyBombStatus plantingBomb)
    && plantingBomb == SearchAndDestroyBombStatus.Carried
    && !SearchAndDestroyRules.TryStartPlant(SearchAndDestroyBombStatus.Carried,
        false, true, out _)
    && !SearchAndDestroyRules.TryCompletePlant(SearchAndDestroyBombStatus.Carried,
        SearchAndDestroyRules.PlantDurationSeconds - 0.1f, out _)
    && SearchAndDestroyRules.TryCompletePlant(SearchAndDestroyBombStatus.Carried,
        SearchAndDestroyRules.PlantDurationSeconds, out SearchAndDestroyBombStatus plantedBomb)
    && plantedBomb == SearchAndDestroyBombStatus.Planted,
    "Planting must require the carrier and the full plant duration.");
Assert(SearchAndDestroyRules.TryStartDefuse(SearchAndDestroyBombStatus.Planted,
        true, true, out SearchAndDestroyBombStatus defusingBomb)
    && defusingBomb == SearchAndDestroyBombStatus.Planted
    && !SearchAndDestroyRules.TryStartDefuse(SearchAndDestroyBombStatus.Planted,
        false, true, out _)
    && !SearchAndDestroyRules.TryCompleteDefuse(SearchAndDestroyBombStatus.Planted,
        SearchAndDestroyRules.DefuseDurationSeconds - 0.1f, out _)
    && SearchAndDestroyRules.TryCompleteDefuse(SearchAndDestroyBombStatus.Planted,
        SearchAndDestroyRules.DefuseDurationSeconds, out SearchAndDestroyBombStatus defusedBomb)
    && defusedBomb == SearchAndDestroyBombStatus.Home,
    "Defusing must require a defender and the full defuse duration.");
Assert(TeamRules.ResolveController(Array.Empty<int>()) == -1
    && TeamRules.ResolveController(new[] { 1, 1 }) == 1
    && TeamRules.ResolveController(new[] { 1, 2 }) == -2,
    "Point control must distinguish empty, sole-team, and contested states.");
TeamPoint thirdTeamSpawn = TeamRules.SelectFarthestFromOrigins(
    new[] { new TeamPoint(0f, 0f, 10f), new TeamPoint(10f, 0f, 0f), new TeamPoint(-10f, 0f, -10f) },
    new[] { new TeamPoint(0f, 0f, 0f), new TeamPoint(10f, 0f, 10f) });
Assert(thirdTeamSpawn.X == -10f && thirdTeamSpawn.Z == -10f,
    "The third team origin must be farthest from both authored origins.");
TeamPoint respawn = TeamRules.SelectFarthestFromEnemies(
    new[] { new TeamPoint(0f, 0f, 0f), new TeamPoint(10f, 0f, 0f), new TeamPoint(20f, 0f, 0f) },
    new[] { new TeamPoint(2f, 0f, 0f), new TeamPoint(4f, 0f, 0f) });
Assert(respawn.X == 20f, "Respawn selection must maximize distance from the nearest enemy.");
List<SpawnCandidate> coveredSpawnCandidates = new()
{
    new SpawnCandidate(new TeamPoint(10f, 0f, 0f),
        new[] { new SpawnThreat(new TeamPoint(0f, 0f, 0f), false) }),
    new SpawnCandidate(new TeamPoint(20f, 0f, 0f),
        new[] { new SpawnThreat(new TeamPoint(0f, 0f, 0f), true) })
};
TeamPoint coveredSpawn = SafeSpawnRules.SelectBest(coveredSpawnCandidates,
    Array.Empty<TeamPoint>(), null, out _);
Assert(coveredSpawn.X == 10f,
    "A covered spawn should beat a slightly farther spawn visible to an enemy.");
List<SpawnCandidate> teammateSpawnCandidates = new()
{
    new SpawnCandidate(new TeamPoint(5f, 0f, 0f), Array.Empty<SpawnThreat>()),
    new SpawnCandidate(new TeamPoint(25f, 0f, 0f), Array.Empty<SpawnThreat>())
};
TeamPoint teammateSpawn = SafeSpawnRules.SelectBest(teammateSpawnCandidates,
    new[] { new TeamPoint(6f, 0f, 0f) }, null, out _);
Assert(teammateSpawn.X == 5f,
    "A spawn close to a teammate should beat a distant spawn when threat safety is equal.");
TeamPoint objectiveSpawn = SafeSpawnRules.SelectBest(teammateSpawnCandidates,
    Array.Empty<TeamPoint>(), new TeamPoint(24f, 0f, 0f), out _);
Assert(objectiveSpawn.X == 25f,
    "A spawn close to the objective should beat a distant spawn without threats.");
TeamPoint noContextSpawn = SafeSpawnRules.SelectBest(teammateSpawnCandidates,
    Array.Empty<TeamPoint>(), null, out _);
Assert(noContextSpawn.X == 5f,
    "Spawn selection without threats, teammates, or an objective must be deterministic.");
List<SpawnCandidate> visibleSpawnCandidates = new()
{
    new SpawnCandidate(new TeamPoint(5f, 0f, 0f),
        new[] { new SpawnThreat(new TeamPoint(0f, 0f, 0f), true) }),
    new SpawnCandidate(new TeamPoint(30f, 0f, 0f),
        new[] { new SpawnThreat(new TeamPoint(0f, 0f, 0f), true) })
};
TeamPoint farVisibleSpawn = SafeSpawnRules.SelectBest(visibleSpawnCandidates,
    Array.Empty<TeamPoint>(), null, out _);
Assert(farVisibleSpawn.X == 30f,
    "When all candidates are visible, enemy distance must remain the fallback priority.");
Assert(HardpointRules.GetContestTimeLimit(100) == 50
    && HardpointRules.GetContestTimeLimit(1) == 1,
    "The Hardpoint contest clock must be half the score limit with a one-second minimum.");
Assert(HardpointRules.GetNextObjectiveIndex(0, 3) == 1
    && HardpointRules.GetNextObjectiveIndex(2, 3) == 0
    && HardpointRules.IsWarningActive(25f, 30f, 5f),
    "Hardpoint objectives must rotate in order and warn five seconds before rotation.");
Dictionary<int, int> hardpointScores = new() { [0] = 99, [1] = 20 };
Assert(HardpointRules.TryAwardPoint(hardpointScores, 0, 100, false, out int scoreWinner)
    && scoreWinner == 0 && hardpointScores[0] == 100,
    "An uncontested point must win when it reaches the score limit.");
Assert(HardpointRules.TryAwardPoint(hardpointScores, 1, 100, true, out int suddenDeathWinner)
    && suddenDeathWinner == 1,
    "The first uncontested point must win sudden death immediately.");
Dictionary<int, int> tiedScores = new() { [0] = 20, [1] = 20 };
Assert(!HardpointRules.TryResolveTimerWinner(tiedScores, out _),
    "A tied contest-clock expiry must enter sudden death.");
Dictionary<int, int> leadingScores = new() { [0] = 21, [1] = 20 };
Assert(HardpointRules.TryResolveTimerWinner(leadingScores, out int timerWinner)
    && timerWinner == 0,
    "The leading team must win when the contest clock expires.");
List<string> defaultGunGameWeapons = WeaponListParser.Parse(
    "Glock, Webley, SMG, Bukanee, Shotgun, AR15, QCW05, HK_G11, M2000, Couperet",
    new[] { "Glock", "Webley", "SMG", "Bukanee", "Shotgun", "AR15", "QCW05", "HK_G11", "M2000", "Couperet" });
Assert(defaultGunGameWeapons.Count == 10,
    "Default Gun Game weapon list must contain exactly ten validated weapons.");
Assert(defaultGunGameWeapons.Count * 10 == 100,
    "Gun Game score limit must equal ten times the validated weapon count.");
Assert(GunGameRules.GetWeaponIndex(0, 10) == 0
    && GunGameRules.GetWeaponIndex(10, 10) == 1
    && GunGameRules.GetWeaponIndex(90, 10) == 9
    && GunGameRules.GetWeaponIndex(100, 10) == 9,
    "Gun Game must advance one weapon per kill and require a final-gun kill to win.");
Dictionary<int, int> maximumScores = ScoreCodec.Parse("1:100;2:101", ScoreRules.PointsToWin);
Assert(maximumScores.Count == 1 && maximumScores[1] == ScoreRules.PointsToWin,
    "Score parsing must accept the shared maximum and reject values above it.");

Assert(OneInTheChamberRules.IsAllowedWeapon("Revolver(Clone)"),
    "One in the Chamber must allow the exact Revolver prefab.");
Assert(OneInTheChamberRules.IsAllowedWeapon("Couperet(Clone)"),
    "One in the Chamber must allow the Couperet prefab.");
Assert(!OneInTheChamberRules.IsAllowedWeapon("Webley(Clone)")
    && !OneInTheChamberRules.IsAllowedWeapon("SMG(Clone)"),
    "One in the Chamber must reject unrelated weapon substitutions.");
HashSet<int> alivePlayers = new() { 1, 2, 3 };
Dictionary<int, int> reserveBullets = new() { [1] = 0, [2] = 2 };
Assert(OneInTheChamberRules.ApplyDeath(alivePlayers, reserveBullets, 3, 1)
    && !alivePlayers.Contains(3) && reserveBullets[1] == 1,
    "A valid kill must eliminate the victim and award one bullet.");
Assert(OneInTheChamberRules.ApplyDeath(alivePlayers, reserveBullets, 2, -1)
    && !alivePlayers.Contains(2) && reserveBullets[1] == 1,
    "A non-kill death must eliminate the victim without awarding a bullet.");
Assert(alivePlayers.Count == 1 && alivePlayers.Contains(1),
    "The last remaining player must be the round winner.");
Assert(Math.Abs(OneInTheChamberRules.PlayerHealth - 0.4f) < 0.001f,
    "One in the Chamber must use ten displayed health for every player.");
Assert(HotPotatoRules.IsAllowedWeapon("HandGrenade(Clone)", true)
    && HotPotatoRules.IsAllowedWeapon("Shotgun(Clone)", false),
    "Hot Potato must use the HandGrenade and Shotgun prefabs.");
Assert(!HotPotatoRules.IsAllowedWeapon("Glock(Clone)", false),
    "Hot Potato must reject unrelated weapons.");
Assert(HotPotatoRules.ResolvePotato(-1, 5, 2) == 2,
    "The first player to die must become the Hot Potato.");
Assert(HotPotatoRules.ResolvePotato(1, 1, 2) == 2
    && HotPotatoRules.ResolvePotato(1, 3, 2) == 1,
    "Only a kill by the current grenade holder may transfer the potato.");
Assert(InfidelRules.GetKillerAward(true, false) == 30
    && InfidelRules.GetKillerAward(true, false) == InfidelRules.PointsForKillingInfidel,
    "A terrorist must receive thirty points for killing the Infidel.");
Assert(InfidelRules.GetKillerAward(false, true) == 0
    && InfidelRules.GetKillerAward(false, false) == 0
    && InfidelRules.GetKillerAward(true, true) == 0
    && InfidelRules.GetWinnerAward(false) == 0,
    "Ordinary kills and non-winning events must award no Infidel points.");
Assert(InfidelRules.GetWinnerAward(true) == 50
    && InfidelRules.GetWinnerAward(true) == InfidelRules.PointsForInfidelWin,
    "The Infidel must receive fifty points for winning the sub-round.");
Assert(AssassinRules.IsTerminalDeath(true, false)
    && AssassinRules.IsTerminalDeath(false, true)
    && !AssassinRules.IsTerminalDeath(false, false),
    "Only King and Assassin deaths must end an Assassin sub-round.");
Assert(AssassinRules.GetAssassinAward(true) == 50
    && AssassinRules.GetAssassinAward(false) == 0
    && AssassinRules.GetAssassinAward(true) == AssassinRules.PointsForAssassinWin,
    "The Assassin must receive fifty points for a King death.");
Assert(AssassinRules.GetKingAward(true) == 30
    && AssassinRules.GetKingAward(false) == 0
    && AssassinRules.GetKingAward(true) == AssassinRules.PointsForKingSurvival,
    "The King must receive thirty points when the Assassin dies.");
Assert(AssassinRules.GetBodyguardAward(true) == 10
    && AssassinRules.GetBodyguardAward(false) == 0
    && AssassinRules.GetBodyguardAward(true) == AssassinRules.PointsForBodyguardSurvival,
    "Each Bodyguard must receive ten points when the Assassin dies.");
Assert(AssassinRules.GetBodyguardKillerAward(true, true, false) == 20
    && AssassinRules.GetBodyguardKillerAward(true, false, false) == 0
    && AssassinRules.GetBodyguardKillerAward(true, true, true) == 0
    && AssassinRules.GetBodyguardKillerAward(false, true, false) == 0,
    "Only a non-self Bodyguard kill of the Assassin must receive the twenty-point bonus.");

List<string> modes = new() { "Default", "FFA", "GunGame" };
Assert(ModeCycle.TrySelectNext(modes, "Default", out string nextMode) && nextMode == "FFA",
    "Mode selection must choose the next configured mode.");
Assert(ModeCycle.TrySelectNext(modes, "GunGame", out nextMode) && nextMode == "Default",
    "Mode selection must wrap after the last configured mode.");
Assert(ModeCycle.TrySelectNext(modes, "Missing", out nextMode) && nextMode == "Default",
    "An unconfigured current mode must select the first configured mode.");
Assert(!ModeCycle.TrySelectNext(Array.Empty<string>(), "Default", out _),
    "Mode selection must report no result when no modes are configured.");
Assert(ModeCycle.TrySelectRandom(modes, "Default", 0, out nextMode) && nextMode == "FFA",
    "Random mode selection must choose an enabled mode other than the current mode.");
Assert(ModeCycle.TrySelectRandom(modes, "Default", 1, out nextMode) && nextMode == "GunGame",
    "Random mode selection must reach every non-current enabled mode.");
Assert(ModeCycle.TrySelectRandom(modes, "Missing", 0, out nextMode) && nextMode == "Default",
    "Random mode selection must choose any enabled mode when the current mode is missing.");
Assert(ModeCycle.TrySelectRandom(new[] { "Only" }, "Only", 42, out string onlyMode) && onlyMode == "Only",
    "Random mode selection must keep the only enabled mode.");

RequestVersionTracker requests = new();
int firstRequest = requests.Next(7);
int secondRequest = requests.Next(7);
Assert(!requests.IsCurrent(7, firstRequest),
    "A superseded weapon request must no longer be current.");
Assert(requests.IsCurrent(7, secondRequest),
    "The newest weapon request must remain current.");
requests.Clear();
Assert(!requests.IsCurrent(7, secondRequest),
    "Clearing pending requests must invalidate the previous request.");

Console.WriteLine("Pure checks passed.");
