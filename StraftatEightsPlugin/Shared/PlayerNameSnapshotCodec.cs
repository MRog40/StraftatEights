using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StraftatEightsPlugin;

internal static class PlayerNameSnapshotCodec
{
    private const int MaxEntries = 64;
    private const int MaxNameLength = 256;
    private const int MaxPayloadLength = 4000;

    internal static string Serialize(IReadOnlyDictionary<int, string> names)
    {
        List<int> playerIds = new();
        foreach (KeyValuePair<int, string> entry in names)
        {
            if (entry.Key >= 0 && playerIds.Count < MaxEntries)
            {
                playerIds.Add(entry.Key);
            }
        }

        playerIds.Sort();
        StringBuilder payload = new();
        foreach (int playerId in playerIds)
        {
            string name = names[playerId] ?? string.Empty;
            if (name.Length > MaxNameLength)
            {
                name = PlayerNameMarkup.Truncate(name, MaxNameLength);
            }

            string encodedName = Convert.ToBase64String(Encoding.UTF8.GetBytes(name));
            if (payload.Length > 0)
            {
                payload.Append(';');
            }

            payload.Append(playerId.ToString(CultureInfo.InvariantCulture))
                .Append('=').Append(encodedName);
        }

        return payload.ToString();
    }

    internal static bool TryDeserialize(string? payload, out Dictionary<int, string> names)
    {
        names = new Dictionary<int, string>();
        if (payload == null || payload.Length > MaxPayloadLength)
        {
            return false;
        }

        if (payload.Length == 0)
        {
            return true;
        }

        foreach (string entry in payload.Split(';'))
        {
            int separator = entry.IndexOf('=');
            if (separator <= 0 || !int.TryParse(entry[..separator], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int playerId) || playerId < 0
                || names.ContainsKey(playerId) || names.Count >= MaxEntries)
            {
                return false;
            }

            try
            {
                string name = Encoding.UTF8.GetString(Convert.FromBase64String(
                    entry[(separator + 1)..]));
                if (name.Length > MaxNameLength)
                {
                    return false;
                }

                names[playerId] = name;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return true;
    }
}