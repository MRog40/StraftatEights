using System;

namespace StraftatEightsPlugin;

internal static class LobbySnapshotCodec
{
    internal static string Build(ulong hostId, int roundId, int revision, params string[] fields)
    {
        string[] values = new string[3 + fields.Length];
        values[0] = hostId.ToString();
        values[1] = roundId.ToString();
        values[2] = revision.ToString();
        Array.Copy(fields, 0, values, 3, fields.Length);
        return string.Join("|", values);
    }

    internal static bool TryParse(string? payload, int fieldCount, out ulong hostId,
        out int roundId, out int revision, out string[] fields)
    {
        hostId = 0;
        roundId = -1;
        revision = -1;
        fields = Array.Empty<string>();
        if (string.IsNullOrEmpty(payload) || fieldCount < 0)
        {
            return false;
        }

        string[] parts = payload.Split('|');
        if (parts.Length != fieldCount + 3
            || !ulong.TryParse(parts[0], out hostId)
            || !int.TryParse(parts[1], out roundId)
            || !int.TryParse(parts[2], out revision)
            || hostId == 0 || roundId < 0 || revision < 0)
        {
            return false;
        }

        fields = new string[fieldCount];
        Array.Copy(parts, 3, fields, 0, fieldCount);
        return true;
    }

    internal static bool TryParseOrdered(string? payload, int partCount, int hostIndex,
        int roundIndex, int revisionIndex, out ulong hostId, out int roundId,
        out int revision, out string[] parts)
    {
        hostId = 0;
        roundId = -1;
        revision = -1;
        parts = Array.Empty<string>();
        if (string.IsNullOrEmpty(payload) || partCount <= 0
            || hostIndex < 0 || hostIndex >= partCount
            || roundIndex < 0 || roundIndex >= partCount
            || revisionIndex < 0 || revisionIndex >= partCount)
        {
            return false;
        }

        parts = payload.Split('|');
        if (parts.Length != partCount
            || !ulong.TryParse(parts[hostIndex], out hostId)
            || !int.TryParse(parts[roundIndex], out roundId)
            || !int.TryParse(parts[revisionIndex], out revision)
            || hostId == 0 || roundId < 0 || revision < 0)
        {
            parts = Array.Empty<string>();
            return false;
        }

        return true;
    }

    internal static bool TryParseBool(string value, out bool result)
    {
        if (value == "0")
        {
            result = false;
            return true;
        }

        if (value == "1")
        {
            result = true;
            return true;
        }

        result = false;
        return false;
    }
}
