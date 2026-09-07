using System;
using System.Linq;
using System.Reflection;
using FishNet.Managing;
using MyceliumNetworking;

namespace StraftatEightsPlugin;

internal static class FishNetCompatibility
{
    private const BindingFlags MethodFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static MethodInfo? cmdRespawnLogic;
    private static bool cmdRespawnResolved;
    private static bool respawnUnavailableLogged;
    private static MethodInfo? removeHealthLogic;
    private static bool removeHealthResolved;
    private static bool removeHealthUnavailableLogged;

    internal static MethodInfo? FindGeneratedMethod(Type type, string namePrefix, Func<MethodInfo, bool> signature)
    {
        return type.GetMethods(MethodFlags)
            .Where(method => method.Name.StartsWith(namePrefix, StringComparison.Ordinal))
            .Where(signature)
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    internal static bool CanRespawn => ResolveCmdRespawnLogic() != null;

    internal static void LogPreflight()
    {
        LogAssembly("Game", typeof(GameManager).Assembly);
        LogAssembly("FishNet", typeof(NetworkManager).Assembly);
        LogAssembly("Mycelium", typeof(MyceliumNetwork).Assembly);
        LogAssembly("Computerys", typeof(ComputerysModdingUtilities.StraftatModAttribute).Assembly);

        MethodInfo? respawn = ResolveCmdRespawnLogic();
        MethodInfo? removeHealth = ResolveRemoveHealthLogic();
        Plugin.Logger.LogInfo($"[Compatibility] Respawn={(respawn == null ? "missing" : respawn.Name)}, "
            + $"RemoveHealth={(removeHealth == null ? "missing" : removeHealth.Name)}");
    }

    private static void LogAssembly(string label, Assembly assembly)
    {
        Plugin.Logger.LogInfo($"[Compatibility] {label} assembly: {assembly.GetName().FullName}");
    }

    internal static bool TryInvokeRespawn(PlayerManager manager)
    {
        MethodInfo? method = ResolveCmdRespawnLogic();
        if (method == null || manager == null)
        {
            return false;
        }

        try
        {
            method.Invoke(manager, null);
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Logger.LogWarning($"[Compatibility] FishNet respawn invocation failed: {exception.GetBaseException().Message}");
            return false;
        }
    }

    internal static bool TryRemoveHealth(PlayerHealth health, float damage)
    {
        MethodInfo? method = ResolveRemoveHealthLogic();
        if (method == null || health == null)
        {
            return false;
        }

        try
        {
            method.Invoke(health, new object[] { damage });
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Logger.LogWarning($"[Compatibility] FishNet health invocation failed: {exception.GetBaseException().Message}");
            return false;
        }
    }

    private static MethodInfo? ResolveCmdRespawnLogic()
    {
        if (cmdRespawnResolved)
        {
            return cmdRespawnLogic;
        }

        cmdRespawnResolved = true;
        cmdRespawnLogic = FindGeneratedMethod(typeof(PlayerManager), "RpcLogic___CmdRespawn_",
            method => method.ReturnType == typeof(void) && method.GetParameters().Length == 0);

        if (cmdRespawnLogic == null && !respawnUnavailableLogged)
        {
            respawnUnavailableLogged = true;
            Plugin.Logger.LogError("[Compatibility] FishNet respawn method was not found. Custom respawn is disabled.");
        }

        return cmdRespawnLogic;
    }

    private static MethodInfo? ResolveRemoveHealthLogic()
    {
        if (removeHealthResolved)
        {
            return removeHealthLogic;
        }

        removeHealthResolved = true;
        removeHealthLogic = FindGeneratedMethod(typeof(PlayerHealth), "RpcLogic___RemoveHealth_",
            method => method.ReturnType == typeof(void)
                && method.GetParameters() is { Length: 1 } parameters
                && parameters[0].ParameterType == typeof(float));

        if (removeHealthLogic == null && !removeHealthUnavailableLogged)
        {
            removeHealthUnavailableLogged = true;
            Plugin.Logger.LogError("[Compatibility] FishNet health method was not found. Health writes are disabled.");
        }

        return removeHealthLogic;
    }
}