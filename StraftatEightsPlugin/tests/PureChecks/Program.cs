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
