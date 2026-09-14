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

        if (teamOrigins.Count < 2 || teamOrigins.Count > 3)
        {
            throw new ArgumentException("A map must define two or three team origins.", nameof(teamOrigins));
        }
        if (teamOrigins[0] == Vector3.zero || teamOrigins[1] == Vector3.zero)
        {
            throw new ArgumentException("The first two team origins must be authored positions.", nameof(teamOrigins));
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
                    new Vector3(9.022f, 8.02f, -28.046f),
                    new Vector3(-0.622f, 8.02f, -27.427f),
                    new Vector3(-6.764f, 7.363f, -19.613f),
                    new Vector3(-9.609f, 8.02f, -26.982f),
                    new Vector3(-22.701f, 8.02f, -22.323f),
                    new Vector3(-28.177f, 8.02f, -9.17f),
                    new Vector3(-20.181f, 7.02f, -7.681f),
                    new Vector3(-28.151f, 8.02f, -0.228f),
                    new Vector3(-21.899f, 8.02f, 12.256f),
                    new Vector3(-7.325f, 7.02f, 12.478f),
                    new Vector3(-6.488f, 8.02f, 23.101f),
                    new Vector3(2.452f, 7.188f, 21.862f),
                    new Vector3(5.177f, 7.02f, 28.579f),
                    new Vector3(3.406f, 8.02f, 39.26f),
                    new Vector3(16.88f, 8.02f, 45.077f),
                    new Vector3(22.932f, 7.039f, 35.24f),
                    new Vector3(26.141f, 8.02f, 44.574f),
                    new Vector3(34.874f, 8.02f, 44.765f),
                    new Vector3(48.337f, 8.02f, 39.131f),
                    new Vector3(53.251f, 8.02f, 26.318f),
                    new Vector3(45.434f, 7.02f, 24.379f),
                    new Vector3(53.332f, 8.02f, 17.321f),
                    new Vector3(48.189f, 8.02f, 4.163f),
                    new Vector3(35.597f, 7.02f, 5.348f),
                    new Vector3(32.227f, 8.02f, -6.674f),
                    new Vector3(23.113f, 7.02f, -4.858f),
                    new Vector3(20.015f, 7.019f, -12.276f),
                    new Vector3(21.906f, 8.02f, -22.501f),
                    new Vector3(8.668f, 8.02f, -27.588f),
                    new Vector3(-0.553f, 8.02f, -27.71f),
                    new Vector3(-5.118f, 7.359f, -19.861f)
                },
                Array.Empty<Vector3>(),
                new[]
                {
                    new HardpointObjective(new Vector3(12.447f, 7.007f, 8.975f), 5f),
                    new HardpointObjective(new Vector3(40.771f, 7.078f, 26.327f), 5f),
                    new HardpointObjective(new Vector3(15.493f, 6.988f, -9.839f), 5f)
                },
                new[]
                {
                    new Vector3(-14.234f, 7.599f, -18.271f),
                    new Vector3(40.828f, 7.451f, 32.341f)
                })
        };

    internal static bool TryGet(string mapName, out MapDefinition definition)
    {
        return Definitions.TryGetValue(mapName, out definition!);
    }

    internal static IReadOnlyCollection<string> GetNames()
    {
        return Definitions.Keys.ToArray();
    }
}