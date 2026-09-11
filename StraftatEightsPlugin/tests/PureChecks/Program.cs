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

Assert(OneInTheChamberRules.IsAllowedWeapon("Silenzzio(Clone)"),
    "One in the Chamber must allow the exact Silenzzio prefab.");
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
Assert(OneInTheChamberRules.PlayerHealth == 10,
    "One in the Chamber must use ten health for every player.");
Assert(HotPotatoRules.IsAllowedWeapon("BaseballBat(Clone)", true)
    && HotPotatoRules.IsAllowedWeapon("Shotgun(Clone)", false),
    "Hot Potato must use the BaseballBat and Shotgun prefabs.");
Assert(!HotPotatoRules.IsAllowedWeapon("Glock(Clone)", false),
    "Hot Potato must reject unrelated weapons.");
Assert(HotPotatoRules.ResolvePotato(1, 1, 2) == 2
    && HotPotatoRules.ResolvePotato(1, 3, 2) == 1,
    "Only a kill by the current bat holder may transfer the potato.");
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
