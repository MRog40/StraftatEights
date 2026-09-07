using System.Runtime.CompilerServices;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class HealthSettingsTuning
{
    internal static bool ApplyingPassiveHealth;

    internal sealed class Memory
    {
        public float BaselineFullHealth = -1f;
        public int LastAppliedVersion = -1;
        public float LastObservedHealth = -1f;
        public float LastDamageTime;
        public float RegenAccumulator;
        public float LastRegenWriteTime;
        public float LastLoggedHealth = -1f;
        public bool LastModeSpecificHealth;
    }

    private static readonly ConditionalWeakTable<PlayerHealth, Memory> MemoryByInstance = new();

    internal static void ApplyIfChanged(PlayerHealth controller, float healthMultiplier, int version)
    {
        Memory memory = MemoryByInstance.GetOrCreateValue(controller);
        if (memory.BaselineFullHealth < 0f)
        {
            memory.BaselineFullHealth = controller.fullHealth;
        }
        if (GameModeManager.IsActive(GameMode.Juggernaut) && JuggernautState.IsCurrentJuggernaut(controller))
        {
            JuggernautState.ApplyHealth(controller);
            memory.LastModeSpecificHealth = true;
            memory.LastAppliedVersion = version;
            return;
        }
        if (GameModeManager.IsActive(GameMode.SniperBattle))
        {
            SniperBattleState.ApplyHealth(controller);
            memory.LastModeSpecificHealth = true;
            memory.LastAppliedVersion = version;
            return;
        }
        if (GameModeManager.IsActive(GameMode.Default))
        {
            controller.fullHealth = memory.BaselineFullHealth;
            memory.LastModeSpecificHealth = true;
            memory.LastAppliedVersion = version;
            return;
        }
        if (memory.LastModeSpecificHealth)
        {
            memory.LastModeSpecificHealth = false;
            memory.LastAppliedVersion = -1;
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
                ApplyingPassiveHealth = true;
                try
                {
                    FishNetCompatibility.TryRemoveHealth(controller, -bonus);
                }
                finally
                {
                    ApplyingPassiveHealth = false;
                }
                Plugin.Logger.LogInfo($"[HealthSettings] Health server write: owner={controller.IsOwner} healthAfter={controller.health:0.##} bonus={bonus:0.##}");
            }
        }
    }

    internal static void RegenerateIfNeeded(PlayerHealth controller, Memory memory)
    {
        bool juggernautHealth = GameModeManager.IsActive(GameMode.Juggernaut) && JuggernautState.IsCurrentJuggernaut(controller);
        if (juggernautHealth || GameModeManager.ShouldIgnoreGlobalHealthSettings || !controller.IsServer || !controller.gameObject.activeInHierarchy || controller.health <= 0f)
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
            memory.RegenAccumulator = 0f;
        }
        memory.LastObservedHealth = health;

        if (!HealthSettingsState.RegenEnabled || Time.unscaledTime - memory.LastDamageTime < HealthSettingsState.RegenDelaySeconds || health >= controller.fullHealth)
        {
            if (!HealthSettingsState.RegenEnabled)
            {
                memory.RegenAccumulator = 0f;
            }
            return;
        }

        const float DisplayedHealthPerGameUnit = 25f;
        float gameHealthPerSecond = HealthSettingsState.RegenRate / DisplayedHealthPerGameUnit;
        memory.RegenAccumulator += Time.unscaledDeltaTime * gameHealthPerSecond;
        if (Time.unscaledTime - memory.LastRegenWriteTime < 0.1f || memory.RegenAccumulator <= 0f)
        {
            return;
        }

        float healthToAdd = Mathf.Min(memory.RegenAccumulator, controller.fullHealth - health);
        memory.RegenAccumulator -= healthToAdd;
        memory.LastRegenWriteTime = Time.unscaledTime;
        ApplyingPassiveHealth = true;
        try
        {
            FishNetCompatibility.TryRemoveHealth(controller, -healthToAdd);
        }
        finally
        {
            ApplyingPassiveHealth = false;
        }
        memory.LastObservedHealth = controller.sync___get_value_health();
    }

    internal static void ObserveHealth(PlayerHealth controller)
    {
        Memory memory = MemoryByInstance.GetOrCreateValue(controller);
        float health = controller.sync___get_value_health();
        if (Mathf.Approximately(memory.LastLoggedHealth, health))
        {
            return;
        }

        Plugin.Logger.LogInfo($"[HealthSettings] Health observed: owner={controller.IsOwner} server={controller.IsServer} health={health:0.###} fullHealth={controller.fullHealth:0.###}");
        memory.LastLoggedHealth = health;
    }

    internal static Memory GetMemory(PlayerHealth controller)
    {
        return MemoryByInstance.GetOrCreateValue(controller);
    }
}