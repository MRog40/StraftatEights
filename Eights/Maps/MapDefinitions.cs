using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Eights;

internal sealed class HardtatObjective
{
    internal Vector3 Position { get; }
    internal float Radius { get; }

    internal HardtatObjective(Vector3 position, float radius)
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
    internal IReadOnlyList<HardtatObjective> HardtatObjectives { get; }
    internal IReadOnlyList<Vector3> CapturetatObjectives { get; }
    internal IReadOnlyList<Vector3> TeamOrigins { get; }

    internal MapDefinition(string name, IReadOnlyList<Vector3> spawnPoints)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A map must have a name.", nameof(name));
        }

        Name = name;
        SpawnPoints = Copy(spawnPoints);
        SndObjectives = Array.Empty<Vector3>();
        HardtatObjectives = Array.Empty<HardtatObjective>();
        CapturetatObjectives = Array.Empty<Vector3>();
        TeamOrigins = Array.Empty<Vector3>();
    }

    internal MapDefinition(string name, IReadOnlyList<Vector3> spawnPoints,
        IReadOnlyList<Vector3> sndObjectives, IReadOnlyList<HardtatObjective> hardpointObjectives,
        IReadOnlyList<Vector3> captureTheFlagObjectives, IReadOnlyList<Vector3> teamOrigins)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A map must have a name.", nameof(name));
        }

        if (teamOrigins.Count != 2)
        {
            throw new ArgumentException("A map must define exactly two team origins.", nameof(teamOrigins));
        }
        if (teamOrigins[0] == Vector3.zero || teamOrigins[1] == Vector3.zero)
        {
            throw new ArgumentException("The first two team origins must be authored positions.", nameof(teamOrigins));
        }

        Name = name;
        SpawnPoints = Copy(spawnPoints);
        SndObjectives = Copy(sndObjectives);
        HardtatObjectives = Copy(hardpointObjectives);
        CapturetatObjectives = Copy(captureTheFlagObjectives);
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
    internal const string DragonflyNeighbourhoodName = "Dragonfly_Neighbourhood";
    internal const string Bazaar03Name = "Bazaar_03";
    internal const string Drain01Name = "Drain_01";
    internal const string TheSamePlace11Name = "TheSamePlace_11";
    internal const string Bazaar02Name = "Bazaar_02";
    internal const string TheSamePlace14Name = "TheSamePlace_14";
    internal const string DragonflyVestigeName = "Dragonfly_Vestige";
    internal const string TheSamePlace04AltName = "TheSamePlace_04_Alt";
    internal const string Corridor11Name = "Corridor_11";
    internal const string Garden02Name = "Garden_02";
    internal const string Chateaux06NoGrassName = "Chateaux_06_NoGrass";
    internal const string BasketSwirlyName = "Basket_Swirly";
    internal const string BasketSwirlyAltName = "Basket_Swirly_Alt";
    internal const string Republic08Name = "Republic_08";
    internal const string Adobe02Name = "Adobe_02";
    internal const string Adobe02AltName = "Adobe_02_Alt";
    internal const string ArenaShadow08Name = "Arena_Shadow_08";
    internal const string Garden01Name = "Garden_01";
    internal const string Garden01AltName = "Garden_01_Alt";
    internal const string Chateaux01Name = "Chateaux_01";
    internal const string Arena13Name = "Arena_13";
    internal const string Arena14Name = "Arena_14";

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
                new[]
                {
                    new Vector3(-1.642f, 7.021f, 14.202f),
                    new Vector3(27.083f, 7.474f, 2.737f)
                },
                new[]
                {
                    new HardtatObjective(new Vector3(12.447f, 7.007f, 8.975f), 6f),
                    new HardtatObjective(new Vector3(40.771f, 7.078f, 26.327f), 6f),
                    new HardtatObjective(new Vector3(15.493f, 6.988f, -9.839f), 6f)
                },
                new[]
                {
                    new Vector3(26.794f, 7.363f, 22.218f),
                    new Vector3(-1.008f, 7.032f, -6.947f)
                },
                new[]
                {
                    new Vector3(-14.234f, 7.599f, -18.271f),
                    new Vector3(40.828f, 7.451f, 32.341f)
                }),
            [DragonflyNeighbourhoodName] = new MapDefinition(
                DragonflyNeighbourhoodName,
                new[]
                {
                    new Vector3(-57.443f, 9.689f, 17.904f),
                    new Vector3(-59.23f, 9.689f, 22.393f),
                    new Vector3(-57.969f, 9.689f, 27.199f),
                    new Vector3(-58.103f, 9.689f, 34.003f),
                    new Vector3(-58.075f, 9.689f, 50.362f),
                    new Vector3(-58.04f, 9.689f, 58.866f),
                    new Vector3(-55.285f, 9.689f, 60.425f),
                    new Vector3(-43.216f, 12.138f, 59.792f),
                    new Vector3(-34.908f, 14.61f, 63.548f),
                    new Vector3(-33.874f, 14.684f, 67.818f),
                    new Vector3(-30.395f, 14.684f, 67.416f),
                    new Vector3(-26.751f, 9.789f, 72.209f),
                    new Vector3(-30.296f, 6.968f, 86.362f),
                    new Vector3(-30.823f, 7.156f, 89.455f),
                    new Vector3(-26.175f, 6.963f, 90.664f),
                    new Vector3(-19.267f, 6.692f, 90.446f),
                    new Vector3(-8.607f, 6.818f, 90.155f),
                    new Vector3(-1.82f, 7.088f, 89.942f),
                    new Vector3(2.951f, 6.692f, 85.343f),
                    new Vector3(3.013f, 9.689f, 69.559f),
                    new Vector3(3.432f, 9.689f, 62.634f),
                    new Vector3(1.891f, 9.689f, 53.254f),
                    new Vector3(2.071f, 9.942f, 37.546f),
                    new Vector3(1.629f, 9.689f, 28.189f),
                    new Vector3(3.823f, 9.689f, 22.372f),
                    new Vector3(2.667f, 9.689f, 18.324f),
                    new Vector3(-5.988f, 9.689f, 18.742f),
                    new Vector3(-12.648f, 9.689f, 19.182f),
                    new Vector3(-22.828f, 12.187f, 19.365f),
                    new Vector3(-30.751f, 12.187f, 18.339f),
                    new Vector3(-34.882f, 12.187f, 19.143f),
                    new Vector3(-38.153f, 9.689f, 20.967f),
                    new Vector3(-43.071f, 9.689f, 19.466f),
                    new Vector3(-50.174f, 9.689f, 19.054f),
                    new Vector3(-32.124f, 9.689f, 35.043f),
                    new Vector3(-22.315f, 9.689f, 34.46f),
                    new Vector3(-17.189f, 9.689f, 51.049f),
                    new Vector3(-5.61f, 9.689f, 56.823f),
                    new Vector3(-14.043f, 6.692f, 73.584f),
                    new Vector3(-14.136f, 6.692f, 84.89f)
                },
                new[]
                {
                    new Vector3(-4.628f, 9.689f, 28.858f),
                    new Vector3(-33.844f, 14.648f, 58.236f)
                },
                new[]
                {
                    new HardtatObjective(new Vector3(-20.059f, 9.725f, 42.464f), 6f),
                    new HardtatObjective(new Vector3(-14.757f, 6.692f, 78.28f), 6f),
                    new HardtatObjective(new Vector3(-28.439f, 12.187f, 20.911f), 6f),
                    new HardtatObjective(new Vector3(-33.587f, 14.722f, 65.717f), 6f),
                    new HardtatObjective(new Vector3(-0.782f, 9.689f, 21.067f), 6f)
                },
                new[]
                {
                    new Vector3(-55.771f, 9.689f, 21.343f),
                    new Vector3(-2.804f, 6.692f, 87.907f)
                },
                new[]
                {
                    new Vector3(-55.771f, 9.689f, 21.343f),
                    new Vector3(-2.804f, 6.692f, 87.907f)
                }),
            [Bazaar03Name] = new MapDefinition(
                Bazaar03Name,
                new[]
                {
                    new Vector3(-17.572f, 0.2f, -15.442f),
                    new Vector3(-14.81f, 0.199f, -20.901f),
                    new Vector3(-7.624f, 0.2f, -20.883f),
                    new Vector3(-1.638f, 0f, -20.803f),
                    new Vector3(7.357f, 0.2f, -21.257f),
                    new Vector3(16.378f, 0.2f, -20.912f),
                    new Vector3(20.089f, 0.2f, -20.852f),
                    new Vector3(20.416f, 0.2f, -15.253f),
                    new Vector3(20.455f, 0.2f, -5.759f),
                    new Vector3(21.077f, 0.2f, 6.077f),
                    new Vector3(14.333f, 0.2f, 8.334f),
                    new Vector3(18.611f, 0.2f, 15.265f),
                    new Vector3(18.07f, 0.2f, 22.543f),
                    new Vector3(18.173f, 0.2f, 29.301f),
                    new Vector3(19.999f, 0.2f, 27.594f),
                    new Vector3(18.1f, 0.2f, 26.042f),
                    new Vector3(15.142f, 0.2f, 27.916f),
                    new Vector3(11.69f, 0.2f, 29.163f),
                    new Vector3(5.574f, 0.2f, 29.318f),
                    new Vector3(0.08f, 0f, 29.149f),
                    new Vector3(-3.056f, 0.2f, 27.452f),
                    new Vector3(-4.598f, 0.2f, 22.049f),
                    new Vector3(-4.689f, 0.2f, 19.146f),
                    new Vector3(-6.644f, 0.2f, 16.679f),
                    new Vector3(-6.779f, 0.2f, 12.92f),
                    new Vector3(-10.223f, 0.2f, 16.012f),
                    new Vector3(-11.402f, 0.2f, 14f),
                    new Vector3(-7.09f, 8.398f, 24.5f),
                    new Vector3(-20.77f, 8.506f, 24.533f),
                    new Vector3(-15.414f, 8.398f, 24.523f),
                    new Vector3(-20.427f, 5.2f, 15.597f),
                    new Vector3(-21.904f, 5.2f, 9.532f),
                    new Vector3(-21.806f, 5.2f, 3.644f),
                    new Vector3(-19.812f, 5.2f, 0.812f),
                    new Vector3(-16.331f, 0.2f, -2.127f),
                    new Vector3(-17.574f, 0.2f, -6.293f),
                    new Vector3(-17.589f, 0.2f, -15.292f),
                    new Vector3(-4.819f, 0.2f, 4.657f),
                    new Vector3(3.266f, 0.2f, 7.177f),
                    new Vector3(6.261f, 0.2f, 18.369f),
                    new Vector3(15.473f, 0.2f, 18.394f),
                    new Vector3(8.748f, 0.2f, -4.655f),
                    new Vector3(10.104f, 5.2f, -14.414f),
                    new Vector3(6.318f, 5.2f, -14.19f),
                    new Vector3(-2.896f, 5.2f, -13.737f),
                    new Vector3(-7.491f, 5.2f, -13.768f)
                },
                new[]
                {
                    new Vector3(8.753f, 0f, -0.545f),
                    new Vector3(-6.875f, 0.2f, 14.473f)
                },
                new[]
                {
                    new HardtatObjective(new Vector3(0.149f, 0f, 6.361f), 6f),
                    new HardtatObjective(new Vector3(17.175f, 0.463f, 10.164f), 6f),
                    new HardtatObjective(new Vector3(-19.433f, 5.2f, 8.965f), 6f),
                    new HardtatObjective(new Vector3(2.349f, 0f, -12.355f), 6f)
                },
                new[]
                {
                    new Vector3(-14.08f, 0.2f, -19.943f),
                    new Vector3(17.423f, 0.2f, 27.424f)
                },
                new[]
                {
                    new Vector3(-14.08f, 0.2f, -19.943f),
                    new Vector3(17.423f, 0.2f, 27.424f)
                }),
            [Drain01Name] = new MapDefinition(
                Drain01Name,
                new[]
                {
                    new Vector3(19.122f, 0.2f, -38.363f),
                    new Vector3(22.046f, 0.2f, -43.61f),
                    new Vector3(28.991f, 0.2f, -45.487f),
                    new Vector3(36.388f, 0.2f, -44.232f),
                    new Vector3(40.819f, 0.2f, -39.227f),
                    new Vector3(41.095f, 0.2f, -29.65f),
                    new Vector3(41.075f, 0.2f, -20.465f),
                    new Vector3(40.623f, 0.2f, -11.48f),
                    new Vector3(34.601f, 0.2f, -1.12f),
                    new Vector3(23.531f, 0.391f, -5.642f),
                    new Vector3(26.389f, 0.236f, 2.668f),
                    new Vector3(27.272f, 0.623f, 17.766f),
                    new Vector3(15.092f, 0.448f, 28.803f),
                    new Vector3(1.827f, 0.229f, 30.567f),
                    new Vector3(-2.849f, 0.2f, 37.073f),
                    new Vector3(-3.386f, 0.2f, 43.349f),
                    new Vector3(-11.829f, 0.2f, 44.751f),
                    new Vector3(-23.688f, 0.2f, 44.316f),
                    new Vector3(-31.885f, 0.2f, 41.674f),
                    new Vector3(-38.623f, 0.2f, 34.012f),
                    new Vector3(-40.691f, 0.2f, 24.3f),
                    new Vector3(-40.251f, 0.2f, 13.1f),
                    new Vector3(-40.025f, 0.2f, 3.135f),
                    new Vector3(-36.908f, 0.2f, -2.4f),
                    new Vector3(-32.169f, 0.2f, -6.632f),
                    new Vector3(-28.675f, 0.2f, -7.8f),
                    new Vector3(-26.88f, 0.2f, -3.495f),
                    new Vector3(-20.917f, 0.2f, -4.631f),
                    new Vector3(-21.57f, 0.2f, -14.994f),
                    new Vector3(-21.553f, 0.2f, -21.52f),
                    new Vector3(-27.546f, 0.2f, -29.954f),
                    new Vector3(-32.029f, 0.2f, -30.266f),
                    new Vector3(-37.628f, 0.2f, -30.019f),
                    new Vector3(-36.784f, 0.2f, -38.927f),
                    new Vector3(-31.199f, 0.2f, -41.339f),
                    new Vector3(-23.501f, 0.2f, -41.289f),
                    new Vector3(-16.624f, 0.2f, -41.673f),
                    new Vector3(-7.692f, 0.2f, -41.19f),
                    new Vector3(-2.153f, 0.2f, -34.081f),
                    new Vector3(8.072f, 0.2f, -32.52f),
                    new Vector3(20.096f, 0.2f, -32.447f),
                    new Vector3(9.959f, 0.2f, -20.093f),
                    new Vector3(4.231f, 0.2f, -19.404f),
                    new Vector3(-10.976f, 0.2f, -14.251f),
                    new Vector3(-10.76f, 0.2f, -7.868f),
                    new Vector3(12.608f, 0.2f, 11.261f)
                },
                new[]
                {
                    new Vector3(-27.61f, 0.2f, -36.005f),
                    new Vector3(13.465f, 0.2f, -7.363f)
                },
                new[]
                {
                    new HardtatObjective(new Vector3(2.524f, 0.2f, -15.63f), 6f),
                    new HardtatObjective(new Vector3(14.416f, 0.442f, 26.654f), 6f),
                    new HardtatObjective(new Vector3(-33.882f, 0.2f, -35.439f), 6f),
                    new HardtatObjective(new Vector3(-31.074f, 0.2f, -2.388f), 6f)
                },
                new[]
                {
                    new Vector3(23.84f, 0.2f, -28.819f),
                    new Vector3(-5.298f, 0.2f, 25.943f)
                },
                new[]
                {
                    new Vector3(23.84f, 0.2f, -28.819f),
                    new Vector3(-5.298f, 0.2f, 25.943f)
                }),
            [TheSamePlace11Name] = new MapDefinition(
                TheSamePlace11Name,
                new[]
                {
                    new Vector3(-18.845f, 0.219f, -7.918f),
                    new Vector3(-12.586f, 0.219f, -5.245f),
                    new Vector3(-12.363f, 2.352f, 1.034f),
                    new Vector3(-18.073f, 0.219f, 1.066f),
                    new Vector3(-14.927f, 0.219f, 3.893f),
                    new Vector3(-18.864f, 0.219f, 8.125f),
                    new Vector3(-8.558f, 0.219f, 17.51f),
                    new Vector3(-5.159f, 0.359f, 22.63f),
                    new Vector3(-0.153f, 0.219f, 22.143f),
                    new Vector3(5.241f, 0.219f, 22.633f),
                    new Vector3(8.684f, 0.219f, 18.606f),
                    new Vector3(18.016f, 0.219f, 8.364f),
                    new Vector3(19.164f, 0.219f, 0.172f),
                    new Vector3(12.47f, 0.219f, 4.722f),
                    new Vector3(12.112f, 2.352f, -0.834f),
                    new Vector3(15.02f, 0.219f, -3.722f),
                    new Vector3(18.58f, 0.219f, -8.122f),
                    new Vector3(11.983f, 0.219f, -13.971f),
                    new Vector3(9.033f, 0.219f, -17.79f),
                    new Vector3(5.725f, 0.219f, -18.817f),
                    new Vector3(5.923f, 0.219f, -22.955f),
                    new Vector3(5.705f, 0.219f, -21.32f),
                    new Vector3(-0.58f, 0.219f, -23.074f),
                    new Vector3(-5.848f, 0.219f, -23.203f),
                    new Vector3(-9.122f, 0.219f, -18.228f),
                    new Vector3(-13.867f, 0.219f, -12.48f)
                },
                new[]
                {
                    new Vector3(-4.024f, 0.219f, 22.053f),
                    new Vector3(-0.234f, 0.219f, -8.494f)
                },
                new[]
                {
                    new HardtatObjective(new Vector3(-0.497f, 0.348f, 0.039f), 6f),
                    new HardtatObjective(new Vector3(-2.144f, 0.219f, 21.935f), 6f),
                    new HardtatObjective(new Vector3(2.016f, 0.219f, -22.054f), 6f)
                },
                new[]
                {
                    new Vector3(-16.286f, 0.219f, 1.097f),
                    new Vector3(15.762f, 0.219f, 0.352f)
                },
                new[]
                {
                    new Vector3(-16.286f, 0.219f, 1.097f),
                    new Vector3(15.762f, 0.219f, 0.352f)
                }),
            [Bazaar02Name] = new MapDefinition(
                Bazaar02Name,
                new[]
                {
                    new Vector3(16.329f, 0.971f, -31.728f),
                    new Vector3(20.313f, 0.971f, -32.414f),
                    new Vector3(22.643f, 0.971f, -31.269f),
                    new Vector3(23.197f, 0.971f, -26.659f),
                    new Vector3(25.047f, 0.971f, -22.854f),
                    new Vector3(28.548f, 0.971f, -22.348f),
                    new Vector3(29.063f, 0.971f, -17.368f),
                    new Vector3(28.897f, 0.971f, -12.915f),
                    new Vector3(22.827f, 0.971f, -12.714f),
                    new Vector3(15.963f, 0.971f, -12.813f),
                    new Vector3(28.797f, 3.8f, -8.032f),
                    new Vector3(28.685f, 3.8f, -0.236f),
                    new Vector3(21.926f, 3.8f, 0.86f),
                    new Vector3(19.604f, 0.971f, 12.219f),
                    new Vector3(22.106f, 0.971f, 12.831f),
                    new Vector3(25.332f, 0.971f, 16.274f),
                    new Vector3(25.571f, 0.971f, 21.529f),
                    new Vector3(22.811f, 0.971f, 24.632f),
                    new Vector3(19.514f, 0.971f, 25.386f),
                    new Vector3(18.599f, 0.971f, 22.771f),
                    new Vector3(15.504f, 0.971f, 22.247f),
                    new Vector3(6.611f, 3.8f, 19.257f),
                    new Vector3(-0.157f, 3.8f, 21.039f),
                    new Vector3(-2.748f, 7.4f, 25.701f),
                    new Vector3(-2.303f, 7.4f, 31.441f),
                    new Vector3(-5.074f, 7.4f, 35.003f),
                    new Vector3(-7.234f, 7.4f, 30.026f),
                    new Vector3(-8.016f, 7.4f, 24.683f),
                    new Vector3(-12.611f, 7.4f, 23.214f),
                    new Vector3(-16.697f, 7.4f, 22.25f),
                    new Vector3(-17.149f, 7.4f, 17.376f),
                    new Vector3(-16.869f, 7.4f, 12.463f),
                    new Vector3(-19.821f, 3.8f, 3.897f),
                    new Vector3(-22.508f, 3.8f, -0.269f),
                    new Vector3(-18.313f, 3.8f, -3.304f),
                    new Vector3(-3.475f, 0.971f, -3.519f),
                    new Vector3(-2.93f, 0.971f, 3.567f),
                    new Vector3(-12.685f, 7.2f, -13.85f),
                    new Vector3(-4.67f, 7.4f, -10.117f),
                    new Vector3(1.037f, 3.8f, -23.263f),
                    new Vector3(0.702f, 3.8f, -27.812f),
                    new Vector3(3.945f, 3.8f, -28.423f),
                    new Vector3(5.508f, 3.8f, -23.668f)
                },
                new[]
                {
                    new Vector3(21.962f, 0.971f, 19.05f),
                    new Vector3(-12.811f, 7.2f, -14.873f)
                },
                new[]
                {
                    new HardtatObjective(new Vector3(8.784f, 3.8f, 6.381f), 6f),
                    new HardtatObjective(new Vector3(-19.991f, 3.8f, -0.779f), 6f),
                    new HardtatObjective(new Vector3(26.281f, 3.6f, -4.258f), 6f)
                },
                new[]
                {
                    new Vector3(-12.611f, 7.4f, 16.361f),
                    new Vector3(13.1f, 3.8f, -10.262f)
                },
                new[]
                {
                    new Vector3(-12.611f, 7.4f, 16.361f),
                    new Vector3(13.1f, 3.8f, -10.262f)
                }),
            [TheSamePlace14Name] = new MapDefinition(
                TheSamePlace14Name,
                new[]
                {
                    new Vector3(-19.494f, 0.011f, -18.616f),
                    new Vector3(-17.159f, 0.556f, -25.288f),
                    new Vector3(-2.815f, 0.123f, -22.623f),
                    new Vector3(2.378f, 0.127f, -22.682f),
                    new Vector3(16.32f, 0.618f, -24.504f),
                    new Vector3(18.61f, 0.011f, -18.083f),
                    new Vector3(13.322f, 0.011f, -13.439f),
                    new Vector3(13.811f, 0.011f, -9.558f),
                    new Vector3(19.484f, 0.374f, -1.903f),
                    new Vector3(19.3f, 0.351f, 2.24f),
                    new Vector3(14.344f, 0.011f, 9.841f),
                    new Vector3(13.213f, 0.011f, 13.982f),
                    new Vector3(18.502f, 0.011f, 18.57f),
                    new Vector3(17.488f, 0.33f, 22.661f),
                    new Vector3(16.583f, 0.75f, 25.982f),
                    new Vector3(10.057f, 0.414f, 26.091f),
                    new Vector3(2.794f, 0.112f, 23.038f),
                    new Vector3(-2.531f, 0.082f, 22.225f),
                    new Vector3(-10.024f, 0.401f, 25.195f),
                    new Vector3(-16.908f, 0.388f, 22.953f),
                    new Vector3(-18.811f, 0.011f, 17.963f),
                    new Vector3(-13.836f, 0.011f, 13.43f),
                    new Vector3(-14.137f, 0.011f, 9.941f),
                    new Vector3(-13.89f, 0.1f, 5.588f),
                    new Vector3(-19.023f, 0.489f, 1.649f),
                    new Vector3(-19.282f, 0.542f, -0.816f),
                    new Vector3(-14.477f, 0.124f, -5.756f),
                    new Vector3(-14.673f, 0.011f, -10.112f),
                    new Vector3(-19.136f, 0.071f, -12.495f),
                    new Vector3(-13.246f, 0.011f, -13.704f)
                },
                new[]
                {
                    new Vector3(18.696f, 0.04f, -12.402f),
                    new Vector3(-15.475f, 0.544f, 24.059f)
                },
                new[]
                {
                    new HardtatObjective(new Vector3(0.019f, 0.223f, -0.346f), 6f),
                    new HardtatObjective(new Vector3(-0.632f, 0.182f, -23.584f), 6f),
                    new HardtatObjective(new Vector3(0.152f, 0.111f, 23.003f), 6f),
                    new HardtatObjective(new Vector3(15.265f, 0.102f, 7.146f), 6f),
                    new HardtatObjective(new Vector3(-15.823f, 0.132f, -8.445f), 6f)
                },
                new[]
                {
                    new Vector3(13.951f, 0.011f, 15.02f),
                    new Vector3(-14.035f, 0.011f, -15.673f)
                },
                new[]
                {
                    new Vector3(13.951f, 0.011f, 15.02f),
                    new Vector3(-14.035f, 0.011f, -15.673f)
                }),
            [DragonflyVestigeName] = new MapDefinition(
                DragonflyVestigeName,
                new[]
                {
                    new Vector3(13.71f, 1.484f, -29.734f),
                    new Vector3(7.086f, 1.479f, -29.066f),
                    new Vector3(-0.618f, 0.122f, -28.59f),
                    new Vector3(2.767f, 1.942f, -23.852f),
                    new Vector3(-4.008f, 3.237f, -25.108f),
                    new Vector3(-11.196f, 3.237f, -25.187f),
                    new Vector3(-12.897f, 3.238f, -19.319f),
                    new Vector3(-16.97f, 3.237f, -22.051f),
                    new Vector3(-19.194f, 3.237f, -26.825f),
                    new Vector3(-22.457f, 3.237f, -29.619f),
                    new Vector3(-26.083f, 3.237f, -28.428f),
                    new Vector3(-28.231f, 3.237f, -25.301f),
                    new Vector3(-27.975f, 3.237f, -21.623f),
                    new Vector3(-24.936f, 3.237f, -18.431f),
                    new Vector3(-22.238f, 3.237f, -13.751f),
                    new Vector3(-22.042f, 3.237f, -9.226f),
                    new Vector3(-22.699f, 3.292f, -4.604f),
                    new Vector3(-29.547f, 4.991f, 0.029f),
                    new Vector3(-22.068f, 3.358f, 4.696f),
                    new Vector3(-22.693f, 3.237f, 12.747f),
                    new Vector3(-25.355f, 0.906f, 18.727f),
                    new Vector3(-27.772f, 0.527f, 26.03f),
                    new Vector3(-22.397f, 0.927f, 30.522f),
                    new Vector3(-17.805f, 0.877f, 25.815f),
                    new Vector3(-12.48f, 0.364f, 27.191f),
                    new Vector3(-4.945f, 0.679f, 26.658f),
                    new Vector3(6.472f, 0.911f, 28.811f),
                    new Vector3(4.836f, 3.237f, 25.279f),
                    new Vector3(11.606f, 3.238f, 25.647f),
                    new Vector3(12.986f, 3.237f, 19.317f),
                    new Vector3(17.109f, 3.237f, 27.679f),
                    new Vector3(20.321f, 3.238f, 29.718f),
                    new Vector3(24.247f, 3.237f, 29.566f),
                    new Vector3(27.72f, 3.238f, 27.133f),
                    new Vector3(29.023f, 3.238f, 23.824f),
                    new Vector3(27.027f, 3.237f, 20.03f),
                    new Vector3(25.025f, 3.237f, 17.834f),
                    new Vector3(22.795f, 3.237f, 13.839f),
                    new Vector3(22.261f, 3.238f, 7.179f),
                    new Vector3(29.326f, 5.005f, 0.364f),
                    new Vector3(22.295f, 3.238f, -0.019f),
                    new Vector3(22.237f, 3.237f, -5.977f),
                    new Vector3(23.11f, 3.237f, -12.974f),
                    new Vector3(24.5f, 0.974f, -18.533f),
                    new Vector3(27.317f, 0.621f, -20.709f),
                    new Vector3(26.93f, 0.478f, -25.712f),
                    new Vector3(21.922f, 1.257f, -29.408f),
                    new Vector3(17.019f, 0.932f, -25.727f),
                    new Vector3(3.423f, 1.548f, -4.478f),
                    new Vector3(-1.192f, 1.377f, 1.707f),
                    new Vector3(-0.997f, 1.38f, -0.974f),
                    new Vector3(-2.969f, 1.485f, 5.911f),
                    new Vector3(-3.103f, 1.366f, -5.001f)
                },
                new[]
                {
                    new Vector3(-20.363f, 0.252f, 23.706f),
                    new Vector3(11.854f, 2.261f, -14.346f)
                },
                new[]
                {
                    new HardtatObjective(new Vector3(-0.092f, 1.552f, 0.162f), 6f),
                    new HardtatObjective(new Vector3(-22.57f, 0.755f, 24.284f), 6f),
                    new HardtatObjective(new Vector3(14.167f, 0.496f, 10.26f), 6f),
                    new HardtatObjective(new Vector3(6.866f, 1.681f, -28.378f), 6f)
                },
                new[]
                {
                    new Vector3(-18.739f, 3.456f, -20.085f),
                    new Vector3(18.145f, 3.237f, 19.88f)
                },
                new[]
                {
                    new Vector3(-18.739f, 3.456f, -20.085f),
                    new Vector3(18.145f, 3.237f, 19.88f)
                }),
            [TheSamePlace04AltName] = new MapDefinition(
                TheSamePlace04AltName,
                new[]
                {
                    new Vector3(-37.19f, 0.2f, 3.592f),
                    new Vector3(-32.94f, 0.2f, 3.038f),
                    new Vector3(-36.882f, 0.2f, -0.278f),
                    new Vector3(-36.622f, 0.2f, -3.685f),
                    new Vector3(-32.556f, 0.2f, -3.547f),
                    new Vector3(-27.321f, 0.2f, -3.836f),
                    new Vector3(-24.675f, 0.2f, 3.732f),
                    new Vector3(-21.37f, 0.2f, -3.7f),
                    new Vector3(-16.099f, 0.2f, -8.427f),
                    new Vector3(-12.929f, 0.2f, -12.757f),
                    new Vector3(-7.236f, 0.2f, -16.416f),
                    new Vector3(-0.456f, 0.2f, -18.071f),
                    new Vector3(10.593f, 0.2f, -14.25f),
                    new Vector3(16.517f, 0.2f, -7.951f),
                    new Vector3(20.905f, 0.2f, -4.252f),
                    new Vector3(24.027f, 0.2f, -3.17f),
                    new Vector3(31.289f, 0.2f, -3.226f),
                    new Vector3(36.575f, 0.2f, -3.699f),
                    new Vector3(37.382f, 0.2f, 3.336f),
                    new Vector3(29.83f, 0.2f, 3.981f),
                    new Vector3(23.028f, 0.2f, 4.066f),
                    new Vector3(16.212f, 0.2f, 7.903f),
                    new Vector3(13.606f, 0.2f, 12.263f),
                    new Vector3(5.994f, 0.2f, 17.035f),
                    new Vector3(0.936f, 0.2f, 17.923f),
                    new Vector3(-2.907f, 0.2f, 17.584f),
                    new Vector3(-10.572f, 0.2f, 14.762f),
                    new Vector3(-15.759f, 0.2f, 8.864f),
                    new Vector3(-23.154f, 0.2f, 3.835f),
                    new Vector3(-0.006f, 6.011f, -17.004f),
                    new Vector3(-0.108f, 6.011f, 16.209f)
                },
                new[]
                {
                    new Vector3(0.059f, 6.011f, -16.268f),
                    new Vector3(-0.161f, 0.2f, 16.468f)
                },
                new[]
                {
                    new HardtatObjective(new Vector3(-0.64f, 0.747f, -0.62f), 6f),
                    new HardtatObjective(new Vector3(13.429f, 0.2f, -9.913f), 6f),
                    new HardtatObjective(new Vector3(-13.028f, 0.2f, 9.895f), 6f),
                    new HardtatObjective(new Vector3(33.866f, 0.2f, 0.699f), 6f),
                    new HardtatObjective(new Vector3(-33.363f, 0.2f, 2.51f), 6f)
                },
                new[]
                {
                    new Vector3(-31.821f, 0.2f, -0.338f),
                    new Vector3(31.018f, 0.2f, -0.113f)
                },
                new[]
                {
                    new Vector3(-31.821f, 0.2f, -0.338f),
                    new Vector3(31.018f, 0.2f, -0.113f)
                }),
            [Corridor11Name] = new MapDefinition(
                Corridor11Name,
                new[]
                {
                    new Vector3(-51.574f, 0.696f, 1.72f),
                    new Vector3(-51.191f, 0.694f, -0.965f),
                    new Vector3(-47.907f, 0.404f, -1.293f),
                    new Vector3(-47.718f, 0.377f, 0.992f),
                    new Vector3(-45.078f, 0.2f, 1.415f),
                    new Vector3(-44.811f, 0.537f, -1.142f),
                    new Vector3(-37.695f, 0.2f, -3.709f),
                    new Vector3(-37.031f, 0.2f, 4.245f),
                    new Vector3(-32.109f, -1.37f, 13.237f),
                    new Vector3(-25.811f, -2.376f, 12.315f),
                    new Vector3(-18.961f, -2.918f, 12.362f),
                    new Vector3(-11.042f, -3.176f, 14.949f),
                    new Vector3(-8.423f, -3.176f, 14.77f),
                    new Vector3(2.911f, -3.176f, 13.052f),
                    new Vector3(11.992f, -3.176f, 19.117f),
                    new Vector3(19.616f, -1.683f, 12.243f),
                    new Vector3(26.355f, -1.145f, 11.997f),
                    new Vector3(32.31f, -0.751f, 11.236f),
                    new Vector3(37.202f, 0.2f, 4.036f),
                    new Vector3(42.678f, 0.327f, 1.622f),
                    new Vector3(44.982f, 0.2f, -1.289f),
                    new Vector3(47.623f, 0.431f, 1.974f),
                    new Vector3(51.073f, 0.38f, -1.039f),
                    new Vector3(52.114f, 0.676f, 1.478f),
                    new Vector3(37.135f, 0.2f, -3.914f),
                    new Vector3(31.69f, -1.69f, -11.051f),
                    new Vector3(25.546f, -2.599f, -11.066f),
                    new Vector3(19.081f, -2.92f, -12.011f),
                    new Vector3(11.917f, -3.176f, -14.214f),
                    new Vector3(8.392f, -3.176f, -14.206f),
                    new Vector3(-19.294f, 6.814f, -13.235f),
                    new Vector3(6.565f, 6.577f, 13.629f)
                },
                new[]
                {
                    new Vector3(-3.545f, 6.588f, -12.772f),
                    new Vector3(-8.915f, -3.176f, 13.318f)
                },
                new[]
                {
                    new HardtatObjective(new Vector3(-0.567f, -3.176f, -0.469f), 6f),
                    new HardtatObjective(new Vector3(-19.303f, -2.953f, 11.364f), 6f),
                    new HardtatObjective(new Vector3(26.109f, -2.39f, -11.389f), 6f),
                    new HardtatObjective(new Vector3(-9.434f, -3.176f, -18.868f), 6f),
                    new HardtatObjective(new Vector3(-14.363f, 7.042f, 17.662f), 6f)
                },
                new[]
                {
                    new Vector3(-28.808f, -3.076f, -0.237f),
                    new Vector3(27.527f, -3.176f, 0.057f)
                },
                new[]
                {
                    new Vector3(-28.808f, -3.076f, -0.237f),
                    new Vector3(27.527f, -3.176f, 0.057f)
                }),
            [Garden02Name] = new MapDefinition(
                Garden02Name,
                new[]
                {
                    new Vector3(-15.537f, -0.1f, -40.783f),
                    new Vector3(-22.974f, -0.1f, -38.827f),
                    new Vector3(-25.456f, -0.1f, -32.959f),
                    new Vector3(-27.158f, -0.1f, -26.963f),
                    new Vector3(-29.148f, -0.1f, -23.153f),
                    new Vector3(-30.129f, -0.1f, -17.996f),
                    new Vector3(-31.103f, -0.1f, -12.314f),
                    new Vector3(-32.8f, -0.1f, -7.309f),
                    new Vector3(-33.935f, -0.1f, -0.677f),
                    new Vector3(-33.615f, -0.1f, 4.873f),
                    new Vector3(-32.985f, -0.1f, 10.274f),
                    new Vector3(-33.103f, -0.1f, 16.617f),
                    new Vector3(-30.961f, -0.1f, 23.515f),
                    new Vector3(-27.776f, -0.1f, 31.4f),
                    new Vector3(-24.275f, -0.1f, 37.568f),
                    new Vector3(-19.976f, -0.1f, 40.789f),
                    new Vector3(-13.594f, -0.1f, 40.586f),
                    new Vector3(-8.313f, -0.1f, 40.462f),
                    new Vector3(-1.99f, -0.1f, 40.61f),
                    new Vector3(4.466f, -0.1f, 40.469f),
                    new Vector3(10.804f, -0.1f, 40.389f),
                    new Vector3(17.817f, -0.1f, 39.409f),
                    new Vector3(24.686f, -0.1f, 38.9f),
                    new Vector3(26.842f, -0.1f, 30.901f),
                    new Vector3(26.65f, -0.1f, 24.898f),
                    new Vector3(27.789f, -0.1f, 19.332f),
                    new Vector3(33.859f, -0.1f, 17.653f),
                    new Vector3(33.251f, -0.1f, 11.188f),
                    new Vector3(34.539f, -0.1f, 0.696f),
                    new Vector3(34.601f, -0.1f, -7.764f),
                    new Vector3(34.68f, -0.1f, -15.409f),
                    new Vector3(34.358f, -0.1f, -22.838f),
                    new Vector3(27.064f, -0.1f, -29.356f),
                    new Vector3(26.335f, -0.1f, -35.201f),
                    new Vector3(25.268f, -0.1f, -40.828f),
                    new Vector3(13.253f, -0.1f, -41.353f),
                    new Vector3(3.359f, -0.1f, -41.024f),
                    new Vector3(-3.119f, 0.2f, -19.159f),
                    new Vector3(8.584f, 0.2f, 20.325f),
                    new Vector3(-11.999f, 0.2f, 20.793f),
                    new Vector3(-14.737f, 0.2f, -17.049f)
                },
                new[]
                {
                    new Vector3(8.343f, 0.2f, -9.098f),
                    new Vector3(-19.813f, -0.391f, 11.435f)
                },
                new[]
                {
                    new HardtatObjective(new Vector3(0.534f, 0.203f, -0.171f), 6f),
                    new HardtatObjective(new Vector3(32.868f, -0.1f, -26.333f), 6f),
                    new HardtatObjective(new Vector3(-17.283f, 0.2f, 30.123f), 6f),
                    new HardtatObjective(new Vector3(-8.027f, 0.423f, -9.463f), 6f),
                    new HardtatObjective(new Vector3(-6.264f, -0.1f, -40.675f), 6f)
                },
                new[]
                {
                    new Vector3(-17.516f, 0.2f, -17.071f),
                    new Vector3(18.526f, 0.2f, 24.083f)
                },
                new[]
                {
                    new Vector3(-17.516f, 0.2f, -17.071f),
                    new Vector3(18.526f, 0.2f, 24.083f)
                }),
            [Chateaux06NoGrassName] = new MapDefinition(
                Chateaux06NoGrassName,
                new[]
                {
                    new Vector3(22.487f, 1.347f, -2.583f),
                    new Vector3(21.949f, 1.343f, 1.989f),
                    new Vector3(19.458f, 1.325f, 7.719f),
                    new Vector3(21.566f, 7.269f, 5.895f),
                    new Vector3(20.369f, 8.114f, 21.106f),
                    new Vector3(20.656f, 8.387f, 25.532f),
                    new Vector3(15.65f, 8.426f, 25.76f),
                    new Vector3(7.853f, 8.429f, 25.78f),
                    new Vector3(-5.023f, 8.417f, 25.62f),
                    new Vector3(-9.934f, 8.408f, 25.5f),
                    new Vector3(-11.263f, 9.211f, 20.204f),
                    new Vector3(-13.358f, 8.387f, 24.971f),
                    new Vector3(-16.65f, 8.214f, 20.604f),
                    new Vector3(-16.197f, 5.948f, 12.476f),
                    new Vector3(-10.922f, 2.202f, 16.031f),
                    new Vector3(-15.824f, 2.51f, 16.4f),
                    new Vector3(-11.856f, 1.766f, 6.971f),
                    new Vector3(-16.334f, 1.791f, 3.22f),
                    new Vector3(-12.988f, 1.788f, -4.567f),
                    new Vector3(-16.723f, 1.77f, -10.75f),
                    new Vector3(-15.366f, 1.762f, -13.521f),
                    new Vector3(-13.453f, 1.761f, -14.052f),
                    new Vector3(-11.491f, 1.972f, -11.712f),
                    new Vector3(-9.372f, 1.773f, -9.874f),
                    new Vector3(-6.42f, 1.773f, -9.629f),
                    new Vector3(-0.886f, 1.757f, -15.381f),
                    new Vector3(0.269f, 3.124f, -20.078f),
                    new Vector3(-3.055f, 7.269f, -26.469f),
                    new Vector3(-10.016f, 9.482f, -18.949f),
                    new Vector3(25.392f, 7.269f, -18.267f),
                    new Vector3(23.698f, 7.269f, -23.639f),
                    new Vector3(20.181f, 7.269f, -26.536f),
                    new Vector3(17.109f, 1.314f, -19.91f),
                    new Vector3(18.834f, 1.32f, -13.188f),
                    new Vector3(18.779f, 1.32f, -6.712f),
                    new Vector3(22.313f, 1.346f, -1.667f),
                    new Vector3(8.518f, 1.785f, -5.601f),
                    new Vector3(-5.142f, 0.274f, 0.26f),
                    new Vector3(-8.141f, 0.294f, 6.598f),
                    new Vector3(-3.753f, 2.075f, 16.477f),
                    new Vector3(2.963f, 1.77f, 16.577f),
                    new Vector3(10.069f, 1.887f, 16.435f)
                },
                new[]
                {
                    new Vector3(-14.561f, 1.767f, -11.962f),
                    new Vector3(14.735f, 1.461f, 10.28f)
                },
                new[]
                {
                    new HardtatObjective(new Vector3(0.075f, 0.281f, 2.322f), 6f),
                    new HardtatObjective(new Vector3(-13.904f, 1.953f, 12.02f), 6f),
                    new HardtatObjective(new Vector3(14.059f, 0.233f, -13.464f), 6f),
                    new HardtatObjective(new Vector3(-15.628f, 8.446f, 24.558f), 6f),
                    new HardtatObjective(new Vector3(21.071f, 1.337f, -0.481f), 6f)
                },
                new[]
                {
                    new Vector3(-11.63f, 2.079f, 14.346f),
                    new Vector3(14.134f, 0.232f, -13.735f)
                },
                new[]
                {
                    new Vector3(-11.63f, 2.079f, 14.346f),
                    new Vector3(14.134f, 0.232f, -13.735f)
                }),
            [BasketSwirlyName] = CreateBasketSwirlyDefinition(BasketSwirlyName),
            [BasketSwirlyAltName] = CreateBasketSwirlyDefinition(BasketSwirlyAltName),
            [Republic08Name] = new MapDefinition(
                Republic08Name,
                new[]
                {
                    new Vector3(17.774f, -3.813f, 37.688f),
                    new Vector3(9.873f, -2.967f, 37.68f),
                    new Vector3(-5.116f, -2.967f, 37.955f),
                    new Vector3(-4.588f, -2.967f, 27.726f),
                    new Vector3(0.24f, -2.967f, 19.557f),
                    new Vector3(-9.516f, -2.967f, 19.212f),
                    new Vector3(-15.479f, -2.967f, 20.338f),
                    new Vector3(-19.04f, -2.967f, 36.878f),
                    new Vector3(-22.751f, -2.967f, 28.925f),
                    new Vector3(-18.484f, -2.967f, 15.933f),
                    new Vector3(-29.611f, -4.922f, 12.875f),
                    new Vector3(-35.518f, -6.766f, 7.73f),
                    new Vector3(-19.24f, -2.967f, -0.234f),
                    new Vector3(-33.159f, -6.03f, -5.612f),
                    new Vector3(-26.19f, -3.854f, -8.645f),
                    new Vector3(-18.529f, -2.967f, -12.696f),
                    new Vector3(-26.311f, -3.892f, -19.315f),
                    new Vector3(-13.057f, -2.967f, -30.365f),
                    new Vector3(-13.424f, -2.967f, -39.014f),
                    new Vector3(-9f, -2.967f, -21.341f),
                    new Vector3(0.748f, -2.967f, -38.215f),
                    new Vector3(0.705f, -2.967f, -31.663f),
                    new Vector3(4.645f, -2.967f, -20.539f),
                    new Vector3(14.732f, -2.967f, -37.183f),
                    new Vector3(19.496f, -4.388f, -40.548f),
                    new Vector3(14.512f, -2.967f, -25.741f),
                    new Vector3(19.504f, -4.391f, -16.192f),
                    new Vector3(27.98f, -7.221f, -11.26f),
                    new Vector3(18.811f, -4.159f, 1.153f),
                    new Vector3(29.509f, -7.732f, 2.622f),
                    new Vector3(29.719f, -7.802f, 6.966f),
                    new Vector3(23.878f, -5.851f, 13.037f),
                    new Vector3(29.798f, -7.829f, 24.133f),
                    new Vector3(24.358f, -6.012f, 28.744f),
                    new Vector3(24.617f, -6.098f, 32.043f),
                    new Vector3(6.537f, -2.967f, 16.303f),
                    new Vector3(10.609f, -2.967f, 6.41f),
                    new Vector3(-6.953f, -2.967f, 2.2f),
                    new Vector3(-10.98f, -2.967f, -6.555f),
                    new Vector3(-6.023f, -2.967f, -10.492f),
                    new Vector3(5.74f, -2.967f, -11.775f),
                    new Vector3(6.897f, -2.967f, 0.33f),
                    new Vector3(12.522f, -2.967f, 7.013f)
                },
                new[]
                {
                    new Vector3(-27.69f, -4.322f, 11.897f),
                    new Vector3(11.317f, -2.967f, -7.18f)
                },
                new[]
                {
                    new HardtatObjective(new Vector3(-3.914f, -2.967f, 7.339f), 6f),
                    new HardtatObjective(new Vector3(16.344f, -3.336f, -30.243f), 6f),
                    new HardtatObjective(new Vector3(-27.172f, -4.161f, -6.629f), 6f),
                    new HardtatObjective(new Vector3(-20.736f, -2.967f, 24.654f), 6f),
                    new HardtatObjective(new Vector3(11.274f, -2.967f, 35.522f), 6f)
                },
                new[]
                {
                    new Vector3(20.916f, -4.862f, 26.612f),
                    new Vector3(-11.567f, -2.967f, -21.437f)
                },
                new[]
                {
                    new Vector3(20.916f, -4.862f, 26.612f),
                    new Vector3(-11.567f, -2.967f, -21.437f)
                }),
            [Arena13Name] = new MapDefinition(
                Arena13Name,
                new[]
                {
                    new Vector3(-20.923f, 1.2f, -24.92f),
                    new Vector3(-24.821f, 1.2f, -24.043f),
                    new Vector3(-24.161f, 1.2f, -16.815f),
                    new Vector3(-24.066f, 1.2f, -10.159f),
                    new Vector3(-24.738f, 1.2f, -4.266f),
                    new Vector3(-28.439f, 1.2f, -4.653f),
                    new Vector3(-28.748f, 7.504f, -8.51f),
                    new Vector3(-28.911f, 1.2f, 6.218f),
                    new Vector3(-28.232f, 1.2f, 13.37f),
                    new Vector3(-24.772f, 1.2f, 19.22f),
                    new Vector3(-23.746f, 1.2f, 25.962f),
                    new Vector3(-16.379f, 1.2f, 25.807f),
                    new Vector3(-10.511f, 1.2f, 25.523f),
                    new Vector3(-4.248f, 1.2f, 25.724f),
                    new Vector3(4.507f, 1.2f, 25.779f),
                    new Vector3(10.023f, 1.2f, 25.847f),
                    new Vector3(15.879f, 1.2f, 25.775f),
                    new Vector3(22.144f, 1.2f, 24.616f),
                    new Vector3(24.891f, 1.2f, 24.877f),
                    new Vector3(25.726f, 1.2f, 20.736f),
                    new Vector3(29.079f, 1.2f, 13.684f),
                    new Vector3(29.221f, 1.2f, 3.497f),
                    new Vector3(32.646f, 1.2f, 3.322f),
                    new Vector3(28.473f, 7.523f, 2.979f),
                    new Vector3(28.693f, 1.2f, -12.873f),
                    new Vector3(25.648f, 1.2f, -22.392f),
                    new Vector3(23.171f, 1.2f, -24.763f),
                    new Vector3(13.543f, 1.2f, -24.401f),
                    new Vector3(5.682f, 1.2f, -24.796f),
                    new Vector3(6.962f, 1.415f, -38.013f),
                    new Vector3(-6.888f, 1.406f, -38.124f)
                }),
            [Arena14Name] = new MapDefinition(
                Arena14Name,
                new[]
                {
                    new Vector3(7.919f, 1.403f, -38.137f),
                    new Vector3(-6.809f, 1.404f, -37.961f),
                    new Vector3(-5.596f, 1.569f, -24.269f),
                    new Vector3(-22.156f, 2.427f, -21.561f),
                    new Vector3(-24.473f, 1.659f, -12.342f),
                    new Vector3(-24.559f, 2.023f, -4.23f),
                    new Vector3(-28.542f, 7.504f, -8.415f),
                    new Vector3(-28.223f, 1.205f, -4.841f),
                    new Vector3(-28.544f, 1.163f, 0.962f),
                    new Vector3(-28.666f, 1.225f, 8.159f),
                    new Vector3(-27.809f, 1.246f, 12.836f),
                    new Vector3(-24.146f, 1.72f, 17.756f),
                    new Vector3(-22.95f, 1.803f, 25.527f),
                    new Vector3(-14.57f, 1.624f, 24.895f),
                    new Vector3(-10.546f, 1.755f, 24.769f),
                    new Vector3(-4.591f, 2.046f, 24.866f),
                    new Vector3(9.128f, 1.633f, 25.131f),
                    new Vector3(22.755f, 3.423f, 25.275f),
                    new Vector3(28.302f, 1.321f, 13.453f),
                    new Vector3(28.343f, 1.234f, 7.278f),
                    new Vector3(28.371f, 1.532f, 2.951f),
                    new Vector3(28.092f, 2.737f, -2.484f),
                    new Vector3(32.108f, 1.27f, 1.249f),
                    new Vector3(28.964f, 7.523f, 3.278f),
                    new Vector3(28.421f, 3.584f, -12.166f),
                    new Vector3(23.615f, 3.518f, -20.306f),
                    new Vector3(13.686f, 3.558f, -23.959f),
                    new Vector3(3.036f, 3.01f, -24.61f)
                }),
            [Adobe02Name] = CreateAdobe02Definition(Adobe02Name),
                [Adobe02AltName] = CreateAdobe02Definition(Adobe02AltName),
                [ArenaShadow08Name] = CreateArenaShadow08Definition(ArenaShadow08Name),
                [Garden01Name] = CreateGarden01Definition(Garden01Name),
                [Garden01AltName] = CreateGarden01Definition(Garden01AltName),
                [Chateaux01Name] = CreateChateaux01Definition(Chateaux01Name)
        };

    private static MapDefinition CreateBasketSwirlyDefinition(string name)
    {
        return new MapDefinition(
            name,
            new[]
            {
                new Vector3(-4.057f, -18.218f, -46.415f),
                new Vector3(-10.442f, -18.218f, -45.294f),
                new Vector3(-19.412f, -18.218f, -39.132f),
                new Vector3(-33.034f, -18.218f, -34.937f),
                new Vector3(-41.126f, -18.218f, -18.296f),
                new Vector3(-43.088f, -18.218f, -10.066f),
                new Vector3(-46.425f, -18.218f, 3.459f),
                new Vector3(-44.488f, -18.218f, 10.341f),
                new Vector3(-41.112f, -18.218f, 19.095f),
                new Vector3(-38.409f, -18.218f, 27.442f),
                new Vector3(-34.559f, -18.218f, 34.601f),
                new Vector3(-29.963f, -18.218f, 36.834f),
                new Vector3(-19.502f, -18.218f, 41.093f),
                new Vector3(-11.604f, -18.218f, 44.008f),
                new Vector3(-1.461f, -18.218f, 48.041f),
                new Vector3(5.614f, -18.218f, 46.477f),
                new Vector3(15.733f, -14.8f, 50.708f),
                new Vector3(20.543f, -18.218f, 39.414f),
                new Vector3(32.826f, -18.218f, 33.957f),
                new Vector3(40.686f, -18.218f, 16.67f),
                new Vector3(43.425f, -18.218f, 9.041f),
                new Vector3(45.667f, -18.218f, -3.406f),
                new Vector3(40.041f, -18.218f, -21.184f),
                new Vector3(30.083f, -18.218f, -35.127f),
                new Vector3(17.263f, -18.218f, -40.633f),
                new Vector3(10.474f, -18.218f, -43.378f),
                new Vector3(-3.75f, -18.218f, -23.653f),
                new Vector3(-4.469f, -18.218f, -14.891f),
                new Vector3(-22.605f, -18.218f, 3.494f),
                new Vector3(4.177f, -17.725f, 22.438f),
                new Vector3(4.639f, -18.218f, 15.942f)
            },
            new[]
            {
                new Vector3(39.575f, -18.218f, 14.225f),
                new Vector3(-9.267f, -14.218f, -19.768f)
            },
            new[]
            {
                new HardtatObjective(new Vector3(0.188f, -18.218f, -0.767f), 6f),
                new HardtatObjective(new Vector3(16.633f, -18.218f, 40.17f), 6f),
                new HardtatObjective(new Vector3(11.78f, -14.218f, -24.813f), 6f),
                new HardtatObjective(new Vector3(-41.731f, -18.218f, -3.857f), 6f),
                new HardtatObjective(new Vector3(-1.034f, -18.218f, 45.592f), 6f)
            },
            new[]
            {
                new Vector3(13.481f, -16.218f, -32.482f),
                new Vector3(-12.894f, -16.218f, 33.37f)
            },
            new[]
            {
                new Vector3(13.481f, -16.218f, -32.482f),
                new Vector3(-12.894f, -16.218f, 33.37f)
            });
    }

    private static MapDefinition CreateAdobe02Definition(string name)
    {
        return new MapDefinition(
            name,
            new[]
            {
                new Vector3(-50.165f, 35.126f, -7.951f),
                new Vector3(-50.449f, 36.143f, -4.314f),
                new Vector3(-49.122f, 35.868f, 4.176f),
                new Vector3(-49.146f, 37.613f, 9.37f),
                new Vector3(-43.084f, 38.131f, 10.872f),
                new Vector3(-44.902f, 38.414f, 24.423f),
                new Vector3(-24.69f, 35.415f, 41.533f),
                new Vector3(-17.47f, 35.829f, 40.744f),
                new Vector3(-9.376f, 37.032f, 36.097f),
                new Vector3(-4.83f, 39.886f, 36.188f),
                new Vector3(7.11f, 40.136f, 35.858f),
                new Vector3(14.748f, 36.898f, 18.653f),
                new Vector3(38.907f, 36.289f, 3.196f),
                new Vector3(45.196f, 36.433f, 2.664f),
                new Vector3(48.77f, 38.012f, -4.465f),
                new Vector3(47.979f, 36.322f, -8.692f),
                new Vector3(38.862f, 32.916f, -6.656f),
                new Vector3(32.116f, 35.452f, -28.66f),
                new Vector3(45.829f, 37.81f, -24.092f),
                new Vector3(29.536f, 35.038f, -28.671f),
                new Vector3(9.367f, 41.006f, -41.97f),
                new Vector3(0.841f, 37.061f, -35.693f),
                new Vector3(-20.427f, 34.684f, -32.642f),
                new Vector3(-28.027f, 31.71f, -26.043f)
            });
    }

    private static MapDefinition CreateArenaShadow08Definition(string name)
    {
        return new MapDefinition(
            name,
            new[]
            {
                new Vector3(24.87f, 0.648f, -23.41f),
                new Vector3(1.278f, 1.136f, -34.156f),
                new Vector3(-19.323f, 3.373f, -24.779f),
                new Vector3(-39.929f, 3.437f, -32.632f),
                new Vector3(-42.323f, 3.266f, -25.765f),
                new Vector3(-41.226f, 1.62f, -7.351f),
                new Vector3(-40.77f, 1.539f, 5.797f),
                new Vector3(-43.278f, 1.876f, 16.116f),
                new Vector3(-43.042f, 2.195f, 29.958f),
                new Vector3(-39.469f, 2.521f, 35.66f),
                new Vector3(-30.182f, 2.842f, 41.74f),
                new Vector3(-18.357f, 2.942f, 43.271f),
                new Vector3(-10.124f, 2.772f, 41.973f),
                new Vector3(-3.112f, 2.517f, 38.632f),
                new Vector3(7.944f, 2.097f, 41.199f),
                new Vector3(18.611f, 2.437f, 42.183f),
                new Vector3(27.126f, 2.457f, 42.975f),
                new Vector3(36.704f, 2.529f, 38.312f),
                new Vector3(42.564f, 2.582f, 32.261f),
                new Vector3(43.519f, 2.613f, 21.655f),
                new Vector3(43.173f, 1.755f, 9.98f),
                new Vector3(40.891f, 1.059f, 1.465f),
                new Vector3(40.973f, 0.62f, -9.237f),
                new Vector3(40.864f, 0.748f, -19.889f),
                new Vector3(39.358f, 1.775f, -32.111f),
                new Vector3(33.724f, 2.11f, -35.365f),
                new Vector3(24.572f, 2.088f, -39.51f),
                new Vector3(15.513f, 0.985f, -0.069f),
                new Vector3(12.571f, 1.3f, 15.612f),
                new Vector3(2.752f, 1.425f, 19.747f),
                new Vector3(2.751f, 1.425f, 19.747f),
                new Vector3(-13.481f, 2.192f, 9.291f),
                new Vector3(-12.172f, 0.944f, -14.889f)
            });
    }

    private static MapDefinition CreateGarden01Definition(string name)
    {
        return new MapDefinition(
            name,
            new[]
            {
                new Vector3(-20.71f, 0.488f, 7.948f),
                new Vector3(-24.785f, 0.292f, 15.327f),
                new Vector3(-31.594f, 0.2f, 24.307f),
                new Vector3(-36.133f, 0.314f, 28.013f),
                new Vector3(-44.682f, 0.397f, 32.132f),
                new Vector3(-42.468f, -0.44f, 34.581f),
                new Vector3(-54.805f, 0.382f, 33.175f),
                new Vector3(-65.823f, 0.374f, 31.119f),
                new Vector3(-76.392f, 0.206f, 29.318f),
                new Vector3(-94.591f, 0.269f, 31.626f),
                new Vector3(-96.847f, 0.427f, 22.421f),
                new Vector3(-93.458f, 0.327f, 8.373f),
                new Vector3(-91.547f, 0.304f, 5.014f),
                new Vector3(-89.068f, 0.2f, -6.765f),
                new Vector3(-89.289f, 0.202f, -16.383f),
                new Vector3(-85.4f, 0.201f, -25.633f),
                new Vector3(-76.097f, 0.2f, -27.235f),
                new Vector3(-72.389f, 0.2f, -31.273f),
                new Vector3(-64.289f, 0.2f, -34.866f),
                new Vector3(-52.626f, 0.683f, -49.234f),
                new Vector3(-41.42f, 0.221f, -32.238f),
                new Vector3(-33.403f, 0.2f, -28.839f),
                new Vector3(-25.474f, 0.2f, -28.435f),
                new Vector3(-14.306f, -0.536f, -29.704f),
                new Vector3(-7.787f, 0.895f, -18.616f),
                new Vector3(-9.135f, 0.211f, -9.921f),
                new Vector3(-14.686f, 0.286f, -1.779f),
                new Vector3(-16.411f, -0.4f, 5.151f),
                new Vector3(-20.921f, 0.813f, 11.457f),
                new Vector3(-43.968f, 0.4f, 1.308f),
                new Vector3(-50.78f, 0.3f, -11.776f),
                new Vector3(-73.696f, 0.2f, -3.119f),
                new Vector3(-73.5f, 0.2f, 0.996f),
                new Vector3(-31.211f, 0.2f, 0.94f),
                new Vector3(-78.415f, 0.222f, 18.351f),
                new Vector3(-87.029f, 0.288f, 22.615f)
            });
    }

    private static MapDefinition CreateChateaux01Definition(string name)
    {
        return new MapDefinition(
            name,
            new[]
            {
                new Vector3(3.098f, 19.685f, -16.908f),
                new Vector3(-2.399f, 18.597f, -36.276f),
                new Vector3(-8.21f, 18.332f, -41.06f),
                new Vector3(-14.354f, 17.913f, -46.482f),
                new Vector3(-26.338f, 14.294f, -45.775f),
                new Vector3(-26.334f, 12.378f, -40.184f),
                new Vector3(-32.668f, -1.73f, -50.247f),
                new Vector3(-44.1f, -2.128f, -53.52f),
                new Vector3(-53.676f, -2.356f, -54.11f),
                new Vector3(-65.322f, -4.198f, -54.953f),
                new Vector3(-74.816f, -3.527f, -55.537f),
                new Vector3(-64.295f, -3.691f, -41.502f),
                new Vector3(-63.797f, -3.55f, -29.911f),
                new Vector3(-74.076f, 17.262f, -44.329f),
                new Vector3(-74.362f, 16.844f, -36.917f),
                new Vector3(-74.784f, 16.36f, -26.027f),
                new Vector3(-69.609f, 16.628f, -17.326f),
                new Vector3(-62.637f, 15.449f, -8.265f),
                new Vector3(-57.339f, 15.214f, -3.605f),
                new Vector3(-51.074f, 12.055f, -9.964f),
                new Vector3(-48.349f, 10.203f, 3.026f),
                new Vector3(-38.792f, 9.625f, 15.52f),
                new Vector3(-28.524f, 11.965f, 5.642f),
                new Vector3(-27.923f, 2.408f, 12.919f),
                new Vector3(-22.46f, 0.414f, 8.452f),
                new Vector3(-13.062f, 2.416f, 27.36f),
                new Vector3(-9.961f, 1.97f, 27.503f),
                new Vector3(-2.117f, 0.418f, 25.437f),
                new Vector3(8.056f, -2.12f, 26.114f),
                new Vector3(20.816f, -2.672f, 28.98f),
                new Vector3(-30.603f, 5.483f, 0.166f),
                new Vector3(-37.923f, 5.78f, -9.5f),
                new Vector3(-33.516f, 10.235f, -20.004f),
                new Vector3(-30.474f, 5.445f, -19.832f),
                new Vector3(-33.204f, 5.633f, -16.552f),
                new Vector3(-36.609f, 6.243f, -19.361f)
            });
    }

    internal static bool TryGet(string mapName, out MapDefinition definition)
    {
        return Definitions.TryGetValue(mapName, out definition!);
    }

}