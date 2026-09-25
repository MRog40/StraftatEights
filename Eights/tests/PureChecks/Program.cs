using Eights;

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
Assert(GameModeToggleRules.ShouldEnableAll(new[] { false, false })
    && GameModeToggleRules.ShouldEnableAll(new[] { true, false })
    && !GameModeToggleRules.ShouldEnableAll(new[] { true, true }),
    "Toggle All must enable every mode unless all modes are already enabled.");

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

Assert(PlayerNameMarkup.Truncate("<g=Sunset>ABCDEFGHIJKLMNO</g>", 14)
    == "<g=Sunset>ABCDEFGHIJKLMN</g>",
    "Native shorthand gradient markup must survive visible-name truncation.");
Assert(PlayerNameMarkup.Truncate("<gradient=\"Sunset\"><b>PlayerName</b></gradient>", 6)
    == "<gradient=\"Sunset\"><b>Player</b></gradient>",
    "Nested TMP gradient markup must close cleanly after truncation.");
Assert(PlayerNameMarkup.Truncate("<color=#ffffff>Player</color>", 20)
    == "<color=#ffffff>Player</color>",
    "Untruncated TMP color markup must remain unchanged.");
Assert(PlayerNameMarkup.VisibleLength("<color=#ffffff>Player</color>") == 6
    && PlayerNameMarkup.VisibleLength("<g=Sunset>ABC</g>") == 3,
    "Visible name length must ignore TMP markup tags.");
Dictionary<int, string> displayNames = new()
{
    [9] = "<g=Sunset>Remote;Name=Two</g>",
    [2] = "Host"
};
string displayNamePayload = PlayerNameSnapshotCodec.Serialize(displayNames);
Assert(PlayerNameSnapshotCodec.TryDeserialize(displayNamePayload,
        out Dictionary<int, string> parsedDisplayNames)
    && parsedDisplayNames.Count == 2
    && parsedDisplayNames[9] == displayNames[9]
    && parsedDisplayNames[2] == displayNames[2],
    "Player display-name snapshots must preserve rich text and delimiters.");
Assert(!PlayerNameSnapshotCodec.TryDeserialize("9=not-base64", out _),
    "Malformed player display-name snapshots must be rejected.");

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

int firstRoundAboubiTeam = CountertatRules.GetAboubiTeamId(1);
int firstRoundShadowForceTeam = CountertatRules.GetShadowForceTeamId(1);
int secondRoundAboubiTeam = CountertatRules.GetAboubiTeamId(2);
int secondRoundShadowForceTeam = CountertatRules.GetShadowForceTeamId(2);
Assert(firstRoundAboubiTeam == 1 && secondRoundAboubiTeam == 0
    && CountertatRules.GetOffensiveTeamId(1) == firstRoundAboubiTeam
    && CountertatRules.GetOffensiveTeamId(2) == secondRoundAboubiTeam
    && TeamDisplayNames.Get(firstRoundAboubiTeam, firstRoundAboubiTeam, true) == "Aboubi"
    && TeamDisplayNames.Get(firstRoundShadowForceTeam, firstRoundAboubiTeam, true) == "Shadow Force"
    && TeamDisplayNames.Get(secondRoundAboubiTeam, secondRoundAboubiTeam, true) == "Aboubi"
    && TeamDisplayNames.Get(secondRoundShadowForceTeam, secondRoundAboubiTeam, true) == "Shadow Force",
    "Countertat must alternate team roles by official round, not map activation or take.");
Assert(CountertatRules.GetWeaponName(firstRoundAboubiTeam, 1, firstRoundAboubiTeam, 0) == "Glock"
    && CountertatRules.GetWeaponName(firstRoundAboubiTeam, 1, firstRoundShadowForceTeam, 5) == "Silenzzio"
    && CountertatRules.GetWeaponName(secondRoundAboubiTeam, 1, secondRoundAboubiTeam, 0) == "Glock"
    && CountertatRules.GetWeaponName(secondRoundAboubiTeam, 1, secondRoundShadowForceTeam, 5) == "Silenzzio",
    "Take one must give each team its single weapon to every player.");
Assert(CountertatRules.GetWeaponName(firstRoundAboubiTeam, 2, firstRoundAboubiTeam, 0) == "AK-K"
    && CountertatRules.GetWeaponName(firstRoundAboubiTeam, 2, firstRoundAboubiTeam, 2) == "Mac10"
    && CountertatRules.GetWeaponName(firstRoundAboubiTeam, 2, firstRoundAboubiTeam, 9) == "Mac10"
    && CountertatRules.GetWeaponName(firstRoundAboubiTeam, 2, firstRoundShadowForceTeam, 1) == "AR15"
    && CountertatRules.GetWeaponName(firstRoundAboubiTeam, 2, firstRoundShadowForceTeam, 2) == "SMG",
    "Take two must assign ordered team weapons and repeat the final weapon for extras.");
Assert(CountertatRules.GetWeaponName(firstRoundAboubiTeam, 3, firstRoundAboubiTeam, 3) == "Dispenser"
    && CountertatRules.GetWeaponName(firstRoundAboubiTeam, 3, firstRoundShadowForceTeam, 1) == "QCW05"
    && CountertatRules.GetWeaponName(firstRoundAboubiTeam, 3, firstRoundShadowForceTeam, 2) == "AR15"
    && CountertatRules.GetWeaponName(firstRoundAboubiTeam, 4, firstRoundAboubiTeam, 1) == "M2000"
    && CountertatRules.GetWeaponName(firstRoundAboubiTeam, 4, firstRoundShadowForceTeam, 1) == "M2000"
    && CountertatRules.GetWeaponName(secondRoundAboubiTeam, 10, secondRoundAboubiTeam, 3) == "AK-K",
    "Countertat later take weapon lists must match their configured order.");

Dictionary<string, IReadOnlyList<string>> playlistMaps = new()
{
    ["Ffatat"] = new[] { "Barren_01_Alt" },
    ["Michaeltat"] = new[] { "Map_A", "Map_B" },
    ["Unsupported"] = Array.Empty<string>()
};
List<MapPlaylistEntry<string>> playlist = MapPlaylist.Build(
    new[] { "Ffatat", "Michaeltat", "Unsupported", "Ffatat" },
    mode => playlistMaps.TryGetValue(mode, out IReadOnlyList<string>? maps)
        ? maps
        : Array.Empty<string>(),
    new Random(17));
Assert(playlist.Count == 2 && playlist.Any(entry => entry.Mode == "Ffatat")
    && playlist.Any(entry => entry.Mode == "Michaeltat"),
    "Map playlists must keep supported modes once and skip unsupported modes.");
Random mapCycleRandom = new(23);
string firstMap = MapPlaylist.SelectNextMap(new[] { "Map_A", "Map_B" }, string.Empty,
    mapCycleRandom);
string secondMap = MapPlaylist.SelectNextMap(new[] { "Map_A", "Map_B" }, firstMap,
    mapCycleRandom);
Assert(firstMap != secondMap, "A repeated mode must rotate to a different supported map.");
string distributedMap = MapPlaylist.SelectNextMap(new[] { "Map_A", "Map_B", "Map_C", "Map_D" },
    "Map_C", new Random(17), new[] { "Map_A", "Map_B", "Map_C" });
Assert(distributedMap == "Map_D",
    "Recent map history must exclude the recent maps when an alternative exists.");
List<MapPlaylistEntry<string>> firstOrdering = MapPlaylist.Build(
    new[] { "Ffatat", "Michaeltat" }, mode => playlistMaps[mode], new Random(31));
List<MapPlaylistEntry<string>> secondOrdering = MapPlaylist.Build(
    new[] { "Ffatat", "Michaeltat" }, mode => playlistMaps[mode], new Random(31));
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
    && ScoreRules.PointsPerJuggertatCrown == 20 && ScoreRules.PointsPerRatSurvivalSecond == 3
    && ScoreRules.PointsPerHvtatSurvivalSecond == 3,
    "Shared score rules must use the 100-point target and mode award values.");
Assert(HealthUnits.DisplayedHealthPerInternalUnit == 25f
    && Math.Abs(HealthUnits.ToInternal(10f) - 0.4f) < 0.0001f
    && Math.Abs(HealthUnits.ToInternal(100f) - 4f) < 0.0001f,
    "Mode health values must convert displayed health to the native internal unit.");
Assert(ScoreRules.AddPoints(40, 10, 100) == 50
    && ScoreRules.AddPoints(80, 30, 100) == 100
    && ScoreRules.AddPoints(100, 10, 100) == 100,
    "Every score award must stop at the configured score limit.");
Dictionary<int, int> timeoutScores = new() { [4] = 60, [9] = 40 };
Assert(ModeTimeoutRules.DefaultRoundSeconds == 90f
    && ModeTimeoutRules.LongRoundSeconds == 250f
    && ModeTimeoutRules.TryGetUniqueLeader(timeoutScores, out int timeoutLeader)
    && timeoutLeader == 4
    && !ModeTimeoutRules.TryGetUniqueLeader(
        new Dictionary<int, int> { [4] = 60, [9] = 60 }, out _)
    && !ModeTimeoutRules.TryGetUniqueLeader(new Dictionary<int, int>(), out _),
    "A timed score mode must select a unique leader and treat ties or empty scores as sudden death.");
Assert(AssassintatRules.DefaultTakeTimeLimitSeconds == ModeTimeoutRules.DefaultRoundSeconds,
    "Assassintat takes must use the shared ninety-second time limit.");
Assert(InfectedtatRules.ShouldBecomeInfectedtat(false)
    && !InfectedtatRules.ShouldBecomeInfectedtat(true)
    && InfectedtatRules.ShouldEndRound(0)
    && InfectedtatRules.ShouldEndRound(-1)
    && !InfectedtatRules.ShouldEndRound(1)
    && InfectedtatRules.ShouldAwardInitialInfectedtat(7, 0)
    && !InfectedtatRules.ShouldAwardInitialInfectedtat(-1, 0)
    && !InfectedtatRules.ShouldAwardInitialInfectedtat(7, 1),
    "Infectedtat survivors must convert on death, and only the original infected wins a wipe.");
List<int> infectedSurvivors = InfectedtatRules.GetSurvivors(
    new[] { 7, 2, 4, 2 }, new HashSet<int> { 4 });
Assert(infectedSurvivors.SequenceEqual(new[] { 2, 7 }),
    "The Infectedtat timeout winner list must contain every non-infected round player in sorted order.");
Assert(MichaeltatRules.RoundTimeLimitSeconds == ModeTimeoutRules.DefaultRoundSeconds
    && MichaeltatRules.MovementMultiplier == 1.05f
    && MichaeltatRules.GetHealth(false) == 10f
    && MichaeltatRules.GetHealth(true) == 100f
    && !MichaeltatRules.ShouldEndTimeoutWithoutWinner(1)
    && MichaeltatRules.ShouldEndTimeoutWithoutWinner(2),
    "Michaeltat must use the full round timer and end multi-player timeouts without a winner.");
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
    "Tdmtat must use a balanced two-team assignment for six players.");
Assert(TeamRules.ResolveTeamId(teamDeathmatchAssignments, 1) == 0
    && TeamRules.ResolveTeamId(teamDeathmatchAssignments, 6) == 1
    && TeamRules.ResolveTeamId(teamDeathmatchAssignments, 99) == 99,
    "Custom team resolution must use plugin assignments and fall back to the player ID.");
Dictionary<int, int> previousTeamAssignments = new()
{
    [1] = 0,
    [2] = 0,
    [3] = 1,
    [4] = 1
};
Dictionary<int, int> distributedTeamAssignments = TeamRules.AssignTwoTeams(
    new[] { 1, 2, 3, 4 }, new Random(17), previousTeamAssignments);
Assert(distributedTeamAssignments.Values.Count(teamId => teamId == 0) == 2
    && distributedTeamAssignments.Values.Count(teamId => teamId == 1) == 2
    && distributedTeamAssignments.Any(entry =>
        previousTeamAssignments[entry.Key] != entry.Value),
    "Distributed team assignment must stay balanced while reducing repeated teams.");
Assert(TeamRules.GetHardtatTeamCount(2) == 2
    && TeamRules.GetHardtatTeamCount(3) == 3
    && TeamRules.GetHardtatTeamCount(4) == 2
    && TeamRules.GetHardtatTeamCount(5) == 3
    && TeamRules.GetHardtatTeamCount(6) == 2
    && TeamRules.GetHardtatTeamCount(7) == 3
    && TeamRules.GetHardtatTeamCount(8) == 2
    && TeamRules.GetHardtatTeamCount(9) == 3
    && TeamRules.GetHardtatTeamCount(10) == 3,
    "Hardtat must use two teams for even rosters through eight players and three teams for odd rosters or nine-plus players.");
Dictionary<int, int> hardpointTwoAssignments =
    TeamRules.AssignHardtatBalanced(new[] { 1, 2 });
Dictionary<int, int> hardpointThreeAssignments =
    TeamRules.AssignHardtatBalanced(new[] { 1, 2, 3 });
Dictionary<int, int> hardpointFourAssignments =
    TeamRules.AssignHardtatBalanced(new[] { 1, 2, 3, 4 });
Dictionary<int, int> hardpointFiveAssignments =
    TeamRules.AssignHardtatBalanced(new[] { 1, 2, 3, 4, 5 });
Dictionary<int, int> hardpointSixAssignments =
    TeamRules.AssignHardtatBalanced(new[] { 1, 2, 3, 4, 5, 6 });
Dictionary<int, int> hardpointSevenAssignments =
    TeamRules.AssignHardtatBalanced(new[] { 1, 2, 3, 4, 5, 6, 7 });
Dictionary<int, int> hardpointEightAssignments =
    TeamRules.AssignHardtatBalanced(new[] { 1, 2, 3, 4, 5, 6, 7, 8 });
Dictionary<int, int> hardpointNineAssignments =
    TeamRules.AssignHardtatBalanced(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
Dictionary<int, int> hardpointTenAssignments =
    TeamRules.AssignHardtatBalanced(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
Dictionary<int, int> randomizedHardtatAssignments =
    TeamRules.AssignHardtatBalanced(new[] { 1, 2, 3, 4, 5, 6 }, new Random(17));
Assert(hardpointSixAssignments.Values.Count(teamId => teamId == 0) == 3
    && hardpointSixAssignments.Values.Count(teamId => teamId == 1) == 3
    && !hardpointSixAssignments.Values.Contains(2)
    && hardpointEightAssignments.Values.Count(teamId => teamId == 0) == 4
    && hardpointEightAssignments.Values.Count(teamId => teamId == 1) == 4
    && !hardpointEightAssignments.Values.Contains(2)
    && hardpointTwoAssignments.Values.Count(teamId => teamId == 0) == 1
    && hardpointTwoAssignments.Values.Count(teamId => teamId == 1) == 1
    && hardpointThreeAssignments.Values.Count(teamId => teamId == 0) == 1
    && hardpointThreeAssignments.Values.Count(teamId => teamId == 1) == 1
    && hardpointThreeAssignments.Values.Count(teamId => teamId == 2) == 1
    && hardpointFourAssignments.Values.Count(teamId => teamId == 0) == 2
    && hardpointFourAssignments.Values.Count(teamId => teamId == 1) == 2
    && !hardpointFourAssignments.Values.Contains(2)
    && hardpointFiveAssignments.Values.Count(teamId => teamId == 0) == 2
    && hardpointFiveAssignments.Values.Count(teamId => teamId == 1) == 2
    && hardpointFiveAssignments.Values.Count(teamId => teamId == 2) == 1
    && hardpointSevenAssignments.Values.Count(teamId => teamId == 0) == 3
    && hardpointSevenAssignments.Values.Count(teamId => teamId == 1) == 2
    && hardpointSevenAssignments.Values.Count(teamId => teamId == 2) == 2,
    "Hardtat must use 1v1, 1v1v1, 2v2, 2v2v1, 3v3, and 2v2v3 for two through seven players.");
Assert(randomizedHardtatAssignments.Values.Count(teamId => teamId == 0) == 3
    && randomizedHardtatAssignments.Values.Count(teamId => teamId == 1) == 3
    && !randomizedHardtatAssignments.Values.Contains(2)
    && randomizedHardtatAssignments.Any(entry =>
        entry.Value != hardpointSixAssignments[entry.Key]),
    "Hardtat round assignments must remain balanced while allowing randomized player order.");
Assert(hardpointNineAssignments.Values.Count(teamId => teamId == 0) == 3
    && hardpointNineAssignments.Values.Count(teamId => teamId == 1) == 3
    && hardpointNineAssignments.Values.Count(teamId => teamId == 2) == 3
    && hardpointTenAssignments.Values.Count(teamId => teamId == 0) == 4
    && hardpointTenAssignments.Values.Count(teamId => teamId == 1) == 3
    && hardpointTenAssignments.Values.Count(teamId => teamId == 2) == 3,
    "Hardtat nine- and ten-player assignments must use balanced three-team matches.");
Assert(Math.Abs(TeamRules.GetTeamHealthMultiplier(hardpointEightAssignments, 2) - 1f) < 0.001f
    && Math.Abs(TeamRules.GetTeamHealthMultiplier(hardpointEightAssignments, 1) - 1f) < 0.001f
    && Math.Abs(TeamRules.GetTeamHealthMultiplier(hardpointTenAssignments, 2) - 1.3333333f) < 0.001f
    && Math.Abs(TeamRules.GetTeamHealthMultiplier(hardpointTenAssignments, 1) - 1f) < 0.001f,
    "Uneven Hardtat teams must receive health compensation based on player counts.");
Dictionary<int, int> unevenTwoTeams = new() { [1] = 0, [2] = 0, [3] = 1 };
Assert(Math.Abs(TeamRules.GetTeamHealthMultiplier(unevenTwoTeams, 3) - 2f) < 0.001f,
    "A one-player team must receive 100 percent extra health.");
Assert(TeamRules.GetColor(TeamRules.BlueTeamId).Equals(new TeamColorData(0, 114, 178))
    && TeamRules.GetColor(TeamRules.VermillionTeamId).Equals(new TeamColorData(220, 38, 38))
    && TeamRules.GetColor(TeamRules.GreenTeamId).Equals(new TeamColorData(0, 158, 115)),
    "Team colors must use the color-distinct palette.");
Assert(TeamDisplayNames.Get(0) == "Shadow Force"
    && TeamDisplayNames.Get(1) == "Aboubi"
    && TeamDisplayNames.Get(2) == "Mercs",
    "Team display names must use the representative team names.");
string assignmentData = TeamRules.SerializeAssignments(teamAssignments);
Dictionary<int, int> parsedAssignments = TeamRules.ParseAssignments(assignmentData, 3);
Assert(parsedAssignments.Count == teamAssignments.Count
    && parsedAssignments[7] == 2,
    "Team assignments must round-trip through the network format.");
Dictionary<int, int> filteredAssignments = TeamRules.ParseAssignments("1:0;2:9;bad;1:2", 3);
Assert(filteredAssignments.Count == 1 && filteredAssignments[1] == 2,
    "Team assignment parsing must reject invalid teams and keep the last duplicate.");
Dictionary<int, int> captureTheFlagAssignments =
    CapturetatRules.AssignStrictTwoTeams(new[] { 7, 2, 5, 2, -1 });
Assert(captureTheFlagAssignments.Count == 3 && captureTheFlagAssignments[2] == 0
    && captureTheFlagAssignments[5] == 1 && captureTheFlagAssignments[7] == 0,
    "Capturetat assignment must always use two deterministic teams.");
Assert(CapturetatRules.GetSpawnCandidateCount(31) == 10
    && CapturetatRules.GetSpawnCandidateCount(2) == 1
    && CapturetatRules.GetMatchDuration(100) == 200f,
    "Capturetat must use the closest floor-third spawn pool and a two-second-per-point timer.");
int nearestFlagTeam = CapturetatRules.FindNearestTeam(new TeamPoint(9f, 0f, 1f),
    new[] { new TeamPoint(0f, 0f, 0f), new TeamPoint(20f, 0f, 0f) });
Assert(nearestFlagTeam == 0, "A flag must belong to its nearest authored team origin.");
Assert(CapturetatRules.TryPickup(CapturetatFlagStatus.Home, true,
        out CapturetatFlagStatus carried)
    && carried == CapturetatFlagStatus.Carried
    && !CapturetatRules.TryPickup(carried, true, out _),
    "Only an enemy flag can be picked up, and a carried flag cannot be picked up twice.");
Assert(CapturetatRules.TryDrop(carried, out CapturetatFlagStatus dropped)
    && dropped == CapturetatFlagStatus.Dropped
    && CapturetatRules.TryReturn(dropped, true, out CapturetatFlagStatus returned)
    && returned == CapturetatFlagStatus.Home
    && !CapturetatRules.TryReturn(returned, true, out _),
    "A carried flag must drop on death and a dropped home flag must return on touch.");
Dictionary<int, int> captureScores = new() { [0] = 75, [1] = 20 };
Assert(CapturetatRules.TryAwardCapture(captureScores, 0, 100, true, true,
        out int captureWinner)
    && captureWinner == 0 && captureScores[0] == 100
    && !CapturetatRules.TryAwardCapture(captureScores, 1, 100, true, false, out _),
    "A capture must require the enemy flag and home flag, award twenty-five points, and win at target.");
Assert(!CapturetatRules.TryResolveTimeoutWinner(
        new Dictionary<int, int> { [0] = 50, [1] = 50 }, out _)
    && CapturetatRules.TryResolveTimeoutWinner(
        new Dictionary<int, int> { [0] = 60, [1] = 50 }, out int timeoutWinner)
    && timeoutWinner == 0,
    "A tied CTF timer must enter sudden death, while a unique leader wins.");
Assert(TdmtatRules.PointsPerKill == 10
    && TdmtatRules.AddKillPoints(0, 100) == 10
    && TdmtatRules.AddKillPoints(90, 100) == 100
    && TdmtatRules.IsMatchWon(100, 100)
    && !TdmtatRules.IsMatchWon(90, 100),
    "Tdmtat must award ten points per kill and stop at the team score limit.");
Dictionary<int, int> searchAndDestroyAssignments =
    SndtatRules.AssignStrictTwoTeams(new[] { 7, 2, 5, 2, -1 });
Assert(searchAndDestroyAssignments.Count == 3 && searchAndDestroyAssignments[2] == 0
    && searchAndDestroyAssignments[5] == 1 && searchAndDestroyAssignments[7] == 0,
    "Sndtat assignment must always use two deterministic teams.");
Assert(SndtatRules.GetOffensiveTeamId(1) == 0
    && SndtatRules.GetOffensiveTeamId(2) == 1
    && SndtatRules.GetOtherTeamId(0) == 1
    && SndtatRules.GetOtherTeamId(1) == 0
    && SndtatRules.GetOtherTeamId(2) == -1,
    "Sndtat offense must alternate between takes.");
Assert(SndtatRules.PointsPerRoundWin == 40
    && SndtatRules.TakeTimeLimitSeconds == ModeTimeoutRules.DefaultRoundSeconds
    && SndtatRules.PlantDurationSeconds == 5f
    && SndtatRules.DefuseDurationSeconds == 7.5f
    && SndtatRules.FuseDurationSeconds == 30f
    && SndtatRules.PlantSiteRadius == 3f
    && SndtatRules.IsMatchWon(4, 4)
    && !SndtatRules.IsMatchWon(3, 4),
    "Sndtat must use the configured round and interaction timings.");
HashSet<int> searchAndDestroyAlive = new() { 2, 7 };
Assert(SndtatRules.IsTeamWiped(searchAndDestroyAlive,
        searchAndDestroyAssignments, 1)
    && !SndtatRules.IsTeamWiped(searchAndDestroyAlive,
        searchAndDestroyAssignments, 0),
    "Sndtat must detect a team wipe from the alive-player set.");
Assert(SndtatRules.CanResolveTeamWipe(SndtatBombStatus.Planted,
        1, 1)
    && !SndtatRules.CanResolveTeamWipe(SndtatBombStatus.Planted,
        0, 1)
    && SndtatRules.CanResolveTeamWipe(SndtatBombStatus.Carried,
        0, 1),
    "A planted bomb must resolve a defensive wipe but keep an offensive wipe active.");
Assert(SndtatRules.TryRecoverBomb(SndtatBombStatus.Dropped,
        true, true, out SndtatBombStatus recoveredBomb)
    && recoveredBomb == SndtatBombStatus.Carried
    && !SndtatRules.TryRecoverBomb(SndtatBombStatus.Dropped,
        false, true, out _),
    "Only an in-range offense player can recover a dropped bomb.");
Assert(SndtatRules.TryStartPlant(SndtatBombStatus.Carried,
        true, true, out SndtatBombStatus plantingBomb)
    && plantingBomb == SndtatBombStatus.Carried
    && !SndtatRules.TryStartPlant(SndtatBombStatus.Carried,
        false, true, out _)
    && !SndtatRules.TryCompletePlant(SndtatBombStatus.Carried,
        SndtatRules.PlantDurationSeconds - 0.1f, out _)
    && SndtatRules.TryCompletePlant(SndtatBombStatus.Carried,
        SndtatRules.PlantDurationSeconds, out SndtatBombStatus plantedBomb)
    && plantedBomb == SndtatBombStatus.Planted,
    "Planting must require the carrier and the full plant duration.");
Assert(SndtatRules.TryStartDefuse(SndtatBombStatus.Planted,
        true, true, out SndtatBombStatus defusingBomb)
    && defusingBomb == SndtatBombStatus.Planted
    && !SndtatRules.TryStartDefuse(SndtatBombStatus.Planted,
        false, true, out _)
    && !SndtatRules.TryCompleteDefuse(SndtatBombStatus.Planted,
        SndtatRules.DefuseDurationSeconds - 0.1f, out _)
    && SndtatRules.TryCompleteDefuse(SndtatBombStatus.Planted,
        SndtatRules.DefuseDurationSeconds, out SndtatBombStatus defusedBomb)
    && defusedBomb == SndtatBombStatus.Home,
    "Defusing must require a defender and the full defuse duration.");
Assert(0f - SndtatRules.PlantSiteMinVerticalOffset >= 1f
    && SndtatRules.PlantSiteMaxVerticalOffset == 2f,
    "Sndtat planting must use a bounded vertical site window.");
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
TeamPoint deterministicRandomizedRespawn = TeamRules.SelectRandomizedFarthestFromEnemies(
    new[] { new TeamPoint(0f, 0f, 0f), new TeamPoint(10f, 0f, 0f), new TeamPoint(20f, 0f, 0f) },
    new[] { new TeamPoint(2f, 0f, 0f), new TeamPoint(4f, 0f, 0f) }, 0f, 0);
Assert(deterministicRandomizedRespawn.X == 20f,
    "Zero spawn randomness must keep the safest Tdmtat spawn.");
TeamPoint partiallyRandomizedRespawn = TeamRules.SelectRandomizedFarthestFromEnemies(
    new[] { new TeamPoint(0f, 0f, 0f), new TeamPoint(10f, 0f, 0f), new TeamPoint(20f, 0f, 0f) },
    new[] { new TeamPoint(2f, 0f, 0f), new TeamPoint(4f, 0f, 0f) }, 0.5f, 1);
Assert(partiallyRandomizedRespawn.X == 10f,
    "Partial spawn randomness must select within the safest ranked pool.");
TeamPoint fullyRandomizedRespawn = TeamRules.SelectRandomizedFarthestFromEnemies(
    new[] { new TeamPoint(0f, 0f, 0f), new TeamPoint(10f, 0f, 0f), new TeamPoint(20f, 0f, 0f) },
    new[] { new TeamPoint(2f, 0f, 0f), new TeamPoint(4f, 0f, 0f) }, 1f, 2);
Assert(fullyRandomizedRespawn.X == 0f,
    "Full spawn randomness must allow the least-safe candidate.");
TeamPoint hardpointRespawn = TeamRules.SelectRandomizedFarthestFromEnemiesAndObjective(
    new[] { new TeamPoint(0f, 0f, 0f), new TeamPoint(10f, 0f, 0f), new TeamPoint(20f, 0f, 0f) },
    new[] { new TeamPoint(2f, 0f, 0f) }, new TeamPoint(0f, 0f, 0f), 0f, 0);
Assert(hardpointRespawn.X == 20f,
    "Hardtat respawns must prefer positions far from enemies and the objective.");
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
TeamPoint randomizedSpawn = SafeSpawnRules.SelectRandomized(teammateSpawnCandidates,
    Array.Empty<TeamPoint>(), null, Array.Empty<TeamPoint>(), 1, out _);
Assert(randomizedSpawn.X == 25f,
    "Randomized spawn selection must use the host-selected candidate index.");
TeamPoint freshRandomizedSpawn = SafeSpawnRules.SelectRandomized(teammateSpawnCandidates,
    Array.Empty<TeamPoint>(), null, new[] { new TeamPoint(5f, 0f, 0f) }, 0, out _);
Assert(freshRandomizedSpawn.X == 25f,
    "Randomized spawn selection must avoid a recent point when a safe alternative exists.");
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
Assert(HardtatRules.GetContestTimeLimit(250) == 300
    && HardtatRules.GetContestTimeLimit(100) == 120
    && HardtatRules.GetContestTimeLimit(1) == 1,
    "The Hardtat contest clock must use 1.2 seconds per point with a one-second minimum.");
Assert(HardtatRules.GetNextObjectiveIndex(0, 3) == 1
    && HardtatRules.GetNextObjectiveIndex(2, 3) == 0
    && HardtatRules.NextObjectiveWarningSeconds == 10f
    && HardtatRules.IsWarningActive(20f, 30f,
        HardtatRules.NextObjectiveWarningSeconds),
    "Hardtat objectives must rotate in order and warn ten seconds before rotation.");
Dictionary<int, int> hardpointScores = new() { [0] = 99, [1] = 20 };
Assert(HardtatRules.TryAwardPoint(hardpointScores, 0, 100, false, out int scoreWinner)
    && scoreWinner == 0 && hardpointScores[0] == 100,
    "An uncontested point must win when it reaches the score limit.");
Assert(HardtatRules.TryAwardPoint(hardpointScores, 1, 100, true, out int suddenDeathWinner)
    && suddenDeathWinner == 1,
    "The first uncontested point must win sudden death immediately.");
Dictionary<int, int> tiedScores = new() { [0] = 20, [1] = 20 };
Assert(!HardtatRules.TryResolveTimerWinner(tiedScores, out _),
    "A tied contest-clock expiry must enter sudden death.");
Dictionary<int, int> leadingScores = new() { [0] = 21, [1] = 20 };
Assert(HardtatRules.TryResolveTimerWinner(leadingScores, out int timerWinner)
    && timerWinner == 0,
    "The leading team must win when the contest clock expires.");
List<string> defaultGuntatWeapons = WeaponListParser.Parse(
    "Glock, Webley, SMG, Bukanee, Shotgun, AR15, QCW05, HK_G11, M2000, Couperet",
    new[] { "Glock", "Webley", "SMG", "Bukanee", "Shotgun", "AR15", "QCW05", "HK_G11", "M2000", "Couperet" });
Assert(defaultGuntatWeapons.Count == 10,
    "Default Guntat weapon list must contain exactly ten validated weapons.");
Assert(defaultGuntatWeapons.Count * 10 == 100,
    "Guntat score limit must equal ten times the validated weapon count.");
Assert(GuntatRules.GetWeaponIndex(0, 10) == 0
    && GuntatRules.GetWeaponIndex(10, 10) == 1
    && GuntatRules.GetWeaponIndex(90, 10) == 9
    && GuntatRules.GetWeaponIndex(100, 10) == 9,
    "Guntat must advance one weapon per kill and require a final-gun kill to win.");
Dictionary<int, int> maximumScores = ScoreCodec.Parse("1:100;2:101", ScoreRules.PointsToWin);
Assert(maximumScores.Count == 1 && maximumScores[1] == ScoreRules.PointsToWin,
    "Score parsing must accept the shared maximum and reject values above it.");

Assert(ChambertatRules.IsAllowedWeapon("Revolver(Clone)"),
    "Chambertat must allow the exact Revolver prefab.");
Assert(ChambertatRules.IsAllowedWeapon("Couperet(Clone)"),
    "Chambertat must allow the Couperet prefab.");
Assert(!ChambertatRules.IsAllowedWeapon("Webley(Clone)")
    && !ChambertatRules.IsAllowedWeapon("SMG(Clone)"),
    "Chambertat must reject unrelated weapon substitutions.");
var afterMiss = (Magazine: 0, Reserve: 0);
var afterKnifeKill = ChambertatRules.AwardBullet(afterMiss.Magazine,
    afterMiss.Reserve);
var afterShot = (Magazine: 0, Reserve: 0);
var afterGunKill = ChambertatRules.AwardBullet(afterShot.Magazine,
    afterShot.Reserve);
var afterNextKnifeKill = ChambertatRules.AwardBullet(afterGunKill.Magazine,
    afterGunKill.Reserve);
Assert(afterKnifeKill.Magazine == 1 && afterKnifeKill.Reserve == 0,
    "A knife kill after a miss must put one bullet in the empty chamber.");
Assert(afterGunKill.Magazine == 1 && afterGunKill.Reserve == 0,
    "A gun kill after firing must refill the empty chamber with one bullet.");
Assert(afterNextKnifeKill.Magazine == 2 && afterNextKnifeKill.Reserve == 0,
    "A kill with one bullet loaded must put the next bullet in the magazine without reserve ammo.");
HashSet<int> alivePlayers = new() { 1, 2, 3 };
Assert(ChambertatRules.ApplyDeath(alivePlayers, 3, 1)
    && !alivePlayers.Contains(3),
    "A valid kill must eliminate the victim.");
Assert(ChambertatRules.ApplyDeath(alivePlayers, 2, -1)
    && !alivePlayers.Contains(2),
    "A non-kill death must eliminate the victim.");
Assert(alivePlayers.Count == 1 && alivePlayers.Contains(1),
    "The last remaining player must be the round winner.");
Assert(Math.Abs(ChambertatRules.PlayerHealth - 0.4f) < 0.001f,
    "Chambertat must use ten displayed health for every player.");
List<string> hotPotatoWeapons = new() { "Shotgun", "Tromblonj", "Gust", "Crisis" };
Assert(PotatotatRules.IsAllowedWeapon("HandGrenade(Clone)", true, hotPotatoWeapons)
    && PotatotatRules.IsAllowedWeapon("Shotgun(Clone)", false, hotPotatoWeapons)
    && PotatotatRules.IsAllowedWeapon("Crisis(Clone)", false, hotPotatoWeapons),
    "Potatotat must use the HandGrenade and configured weapon prefabs.");
Assert(!PotatotatRules.IsAllowedWeapon("Glock(Clone)", false, hotPotatoWeapons),
    "Potatotat must reject unrelated weapons.");
Assert(PotatotatRules.ResolvePotato(-1, 5, 2) == 2,
    "The first player to die must become the Hot Potato.");
Assert(PotatotatRules.ResolvePotato(1, 1, 2) == 2
    && PotatotatRules.ResolvePotato(1, 3, 2) == 1,
    "Only a kill by the current grenade holder may transfer the potato.");
Assert(InfideltatRules.GetKillerAward(true, false) == 30
    && InfideltatRules.GetKillerAward(true, false) == InfideltatRules.PointsForKillingInfideltat,
    "A terrorist must receive thirty points for killing the Infideltat.");
Assert(InfideltatRules.GetKillerAward(false, true) == 0
    && InfideltatRules.GetKillerAward(false, false) == 0
    && InfideltatRules.GetKillerAward(true, true) == 0
    && InfideltatRules.GetWinnerAward(false) == 0,
    "Ordinary kills and non-winning events must award no Infideltat points.");
Assert(InfideltatRules.GetWinnerAward(true) == 50
    && InfideltatRules.GetWinnerAward(true) == InfideltatRules.PointsForInfideltatWin,
    "The Infideltat must receive fifty points for winning the take.");
Assert(AssassintatRules.IsTerminalDeath(true, false)
    && AssassintatRules.IsTerminalDeath(false, true)
    && !AssassintatRules.IsTerminalDeath(false, false),
    "Only King and Assassintat deaths must end an Assassintat take.");
Assert(AssassintatRules.GetAssassintatAward(true) == 50
    && AssassintatRules.GetAssassintatAward(false) == 0
    && AssassintatRules.GetAssassintatAward(true) == AssassintatRules.PointsForAssassintatWin,
    "The Assassintat must receive fifty points for a King death.");
Assert(AssassintatRules.GetKingAward(true) == 30
    && AssassintatRules.GetKingAward(false) == 0
    && AssassintatRules.GetKingAward(true) == AssassintatRules.PointsForKingSurvival,
    "The King must receive thirty points when the Assassintat dies.");
Assert(AssassintatRules.GetBodyguardAward(true) == 10
    && AssassintatRules.GetBodyguardAward(false) == 0
    && AssassintatRules.GetBodyguardAward(true) == AssassintatRules.PointsForBodyguardSurvival,
    "Each Bodyguard must receive ten points when the Assassintat dies.");
Assert(AssassintatRules.GetBodyguardKillerAward(true, true, false) == 20
    && AssassintatRules.GetBodyguardKillerAward(true, false, false) == 0
    && AssassintatRules.GetBodyguardKillerAward(true, true, true) == 0
    && AssassintatRules.GetBodyguardKillerAward(false, true, false) == 0,
    "Only a non-self Bodyguard kill of the Assassintat must receive the twenty-point bonus.");

List<string> modes = new() { "Straftat", "Ffatat", "Guntat" };
Assert(ModeCycle.TrySelectNext(modes, "Straftat", out string nextMode) && nextMode == "Ffatat",
    "Mode selection must choose the next configured mode.");
Assert(ModeCycle.TrySelectNext(modes, "Guntat", out nextMode) && nextMode == "Straftat",
    "Mode selection must wrap after the last configured mode.");
Assert(ModeCycle.TrySelectNext(modes, "Missing", out nextMode) && nextMode == "Straftat",
    "An unconfigured current mode must select the first configured mode.");
Assert(!ModeCycle.TrySelectNext(Array.Empty<string>(), "Straftat", out _),
    "Mode selection must report no result when no modes are configured.");
Assert(ModeCycle.TrySelectRandom(modes, "Straftat", 0, out nextMode) && nextMode == "Ffatat",
    "Random mode selection must choose an enabled mode other than the current mode.");
Assert(ModeCycle.TrySelectRandom(modes, "Straftat", 1, out nextMode) && nextMode == "Guntat",
    "Random mode selection must reach every non-current enabled mode.");
Assert(ModeCycle.TrySelectRandom(modes, "Missing", 0, out nextMode) && nextMode == "Straftat",
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

Assert(HuntModesRules.GetOriginIndex(0, 1) == 0
    && HuntModesRules.GetOriginIndex(1, 1) == 1
    && HuntModesRules.GetOriginIndex(0, 2) == 1
    && HuntModesRules.GetOriginIndex(1, 2) == 0
    && HuntModesRules.GetOriginIndex(0, 3) == 0,
    "Hunters must swap team spawn sides on every other take.");
Dictionary<int, int> huntersAssignments = new() { [1] = 0, [2] = 0, [3] = 1, [4] = 1 };
HashSet<int> huntersAlive = new() { 1, 2, 3, 4 };
Assert(!HuntModesRules.TryGetTeamWipeWinner(huntersAlive, huntersAssignments, out _),
    "A Hunters take must continue while both teams have living players.");
huntersAlive.Remove(1);
huntersAlive.Remove(2);
Assert(HuntModesRules.TryGetTeamWipeWinner(huntersAlive, huntersAssignments,
        out int huntersWipeWinner) && huntersWipeWinner == 1,
    "A team wipe must award the take to the surviving team.");
float huntersHoldProgress = 0f;
huntersHoldProgress = HuntModesRules.AdvanceTieBreakHold(2f, -1, 0,
    ref huntersHoldProgress);
huntersHoldProgress = HuntModesRules.AdvanceTieBreakHold(2f, 0, 0,
    ref huntersHoldProgress);
huntersHoldProgress = HuntModesRules.AdvanceTieBreakHold(1f, 0, 1,
    ref huntersHoldProgress);
Assert(Math.Abs(huntersHoldProgress - 1f) < 0.001f,
    "A contested or changed hardpoint controller must reset continuous hold time.");
Assert(HuntModesRules.AddTakePoints(0, 100) == HuntModesRules.PointsPerTakeWin
    && HuntModesRules.AddTakePoints(40, 100) == 80
    && HuntModesRules.AddTakePoints(80, 100) == 100,
    "A Hunters take win must award forty points without exceeding the match limit.");

Console.WriteLine("Pure checks passed.");
