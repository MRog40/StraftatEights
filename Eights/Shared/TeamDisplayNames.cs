namespace Eights;

internal static class TeamDisplayNames
{
    private static readonly string[] Names =
    {
        "Shadow Force",
        "Aboubi",
        "Mercs"
    };

    internal static string Get(int teamId)
    {
        return teamId >= 0 && teamId < Names.Length
            ? Names[teamId]
            : "Unknown Team";
    }
}