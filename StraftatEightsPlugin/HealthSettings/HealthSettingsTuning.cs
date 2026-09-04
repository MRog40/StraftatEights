using System.Runtime.CompilerServices;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class HealthSettingsTuning
{
    internal sealed class Memory
    {
        public float BaselineFullHealth = -1f;
        public int LastAppliedVersion = -1;
        public float LastObservedHealth = -1f;
        public float LastDamageTime;
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

        Plugin.Logger.LogInfo($"[HealthSettings] Health apply: owner={controller.IsOwner} server={controller.IsServer} baseline={memory.BaselineFullHealth:0.##} fullHealth={previousFullHealth:0.##}->{scaledFullHealth:0.##} healthBefore={controller.health:0.##} multiplier={healthMultiplier:0.##} version={version}");

        if (controller.IsServer)
        {
            float bonus = scaledFullHealth - previousFullHealth;
            if (!Mathf.Approximately(bonus, 0f))
            {
                float health = controller.sync___get_value_health();
                controller.sync___set_value_health(health + bonus, true);
                Plugin.Logger.LogInfo($"[HealthSettings] Health server write: owner={controller.IsOwner} healthAfter={controller.health:0.##} bonus={bonus:0.##}");
            }
        }
    }

    internal static void RegenerateIfNeeded(PlayerHealth controller, Memory memory)
    {
        if (!controller.IsServer || !controller.gameObject.activeInHierarchy || controller.health <= 0f)
        {
            return;
        }

        float health = controller.sync___get_value_health();
        if (memory.LastObservedHealth < 0f)
        {
            memory.LastObservedHealth = health;
            memory.LastDamageTime = Time.unscaledTime;
            return;
        }

        if (health < memory.LastObservedHealth - 0.001f)
        {
            memory.LastDamageTime = Time.unscaledTime;
        }
        memory.LastObservedHealth = health;

        if (Time.unscaledTime - memory.LastDamageTime < HealthSettingsState.RegenDelaySeconds || health >= controller.fullHealth)
        {
            return;
        }

        float nextHealth = Mathf.Min(controller.fullHealth, health + HealthSettingsState.RegenRate * Time.deltaTime);
        if (nextHealth <= health)
        {
            return;
        }

        controller.sync___set_value_health(nextHealth, true);
        memory.LastObservedHealth = nextHealth;
    }

    internal static Memory GetMemory(PlayerHealth controller)
    {
        return MemoryByInstance.GetOrCreateValue(controller);
    }
}