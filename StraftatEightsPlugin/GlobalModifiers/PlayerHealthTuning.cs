using System.Runtime.CompilerServices;
using UnityEngine;

namespace StraftatEightsPlugin;

// Scales PlayerHealth.fullHealth (host-authoritative), used by GlobalModifiersPatches
internal static class PlayerHealthTuning
{
    private sealed class Memory
    {
        public float BaselineFullHealth = -1f;
        public int LastAppliedVersion = -1;
    }

    private static readonly ConditionalWeakTable<PlayerHealth, Memory> MemoryByInstance = new();

    internal static void ApplyIfChanged(PlayerHealth controller, float healthMultiplier, int version)
    {
        Memory memory = MemoryByInstance.GetOrCreateValue(controller);
        if (memory.BaselineFullHealth < 0f)
        {
            memory.BaselineFullHealth = controller.fullHealth;
        }
        if (memory.LastAppliedVersion == version)
        {
            return;
        }
        memory.LastAppliedVersion = version;

        float scaledFullHealth = memory.BaselineFullHealth * healthMultiplier;
        float previousFullHealth = controller.fullHealth;
        controller.fullHealth = scaledFullHealth;

        Plugin.Logger.LogInfo($"[GlobalModifiers] Health apply: owner={controller.IsOwner} server={controller.IsServer} baseline={memory.BaselineFullHealth:0.##} fullHealth={previousFullHealth:0.##}->{scaledFullHealth:0.##} healthBefore={controller.health:0.##} multiplier={healthMultiplier:0.##} version={version}");

        if (controller.IsServer)
        {
            float bonus = scaledFullHealth - previousFullHealth;
            if (!Mathf.Approximately(bonus, 0f))
            {
                float health = controller.sync___get_value_health();
                controller.sync___set_value_health(health + bonus, true);
                Plugin.Logger.LogInfo($"[GlobalModifiers] Health server write: owner={controller.IsOwner} healthAfter={controller.health:0.##} bonus={bonus:0.##}");
            }
        }
    }
}
