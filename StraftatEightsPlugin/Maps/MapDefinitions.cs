using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StraftatEightsPlugin;

internal sealed class HardpointObjective
{
    internal Vector3 Position { get; }
    internal float Radius { get; }

    internal HardpointObjective(Vector3 position, float radius)
    {
        if (radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "A hardpoint radius must be positive.");
        }

        Position = position;
        Radius = radius;
    }
}

internal sealed class MapDefinition
{
    internal string Name { get; }
    internal IReadOnlyList<Vector3> SpawnPoints { get; }
    internal IReadOnlyList<Vector3> SndObjectives { get; }
    internal IReadOnlyList<HardpointObjective> HardpointObjectives { get; }
    internal IReadOnlyList<Vector3> TeamOrigins { get; }

    internal MapDefinition(string name, IReadOnlyList<Vector3> spawnPoints,
        IReadOnlyList<Vector3> sndObjectives, IReadOnlyList<HardpointObjective> hardpointObjectives,
        IReadOnlyList<Vector3> teamOrigins)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A map must have a name.", nameof(name));
        }

        if (teamOrigins.Count != 3)
        {
            throw new ArgumentException("A map must define exactly three team origins.", nameof(teamOrigins));
        }

        Name = name;
        SpawnPoints = Copy(spawnPoints);
        SndObjectives = Copy(sndObjectives);
        HardpointObjectives = Copy(hardpointObjectives);
        TeamOrigins = Copy(teamOrigins);
    }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> positions)
    {
        return Array.AsReadOnly(positions.ToArray());
    }
}

internal static class MapDefinitions
{
    internal const string Barren01AltName = "Barren_01_Alt";

    private static readonly IReadOnlyDictionary<string, MapDefinition> Definitions =
        new Dictionary<string, MapDefinition>(StringComparer.Ordinal)
        {
            [Barren01AltName] = new MapDefinition(
                Barren01AltName,
                new[]
                {
                    new Vector3(31.686f, 13.02f, 21.609f),
                    new Vector3(-5.385f, 13.517f, -4.458f)
                },
                Array.Empty<Vector3>(),
                new[]
                {
                    new HardpointObjective(new Vector3(12.447f, 7.007f, 8.975f), 5f)
                },
                new[] { Vector3.zero, Vector3.zero, Vector3.zero })
        };

    internal static bool TryGet(string mapName, out MapDefinition definition)
    {
        return Definitions.TryGetValue(mapName, out definition!);
    }
}