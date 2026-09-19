using System.Runtime.CompilerServices;
using UnityEngine;

namespace Eights;

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
        public bool LastModeSpecificHealth;
        public int LastAppliedHealthCompensationVersion = -1;
        public int LastAppliedPlayerId = -1;
    }

    private static readonly ConditionalWeakTable<PlayerHealth, Memory> MemoryByInstance = new();

    internal static void CaptureBaseline(PlayerHealth controller)
    {
        if (controller == null)
        {
            return;
        }

        Memory memory = MemoryByInstance.GetOrCreateValue(controller);
        if (memory.BaselineFullHealth < 0f)
        {
            memory.BaselineFullHealth = controller.fullHealth;
        }
    }

    internal static void ApplyIfChanged(PlayerHealth controller, float healthMultiplier, int version)
    {
        CaptureBaseline(controller);
        if (GameModeManager.IsVanillaScene)
        {
            Memory vanillaMemory = MemoryByInstance.GetOrCreateValue(controller);
            controller.fullHealth = vanillaMemory.BaselineFullHealth;
            vanillaMemory.LastModeSpecificHealth = false;
            vanillaMemory.LastAppliedVersion = version;
            vanillaMemory.LastAppliedHealthCompensationVersion = TeamAssignment.HealthCompensationVersion;
            vanillaMemory.LastAppliedPlayerId = controller.playerValues?.playerClient?.PlayerId ?? -1;
            return;
        }

        if (RespawnProtection.IsProtected(controller))
        {
            RespawnProtection.ApplyHealth(controller);
            return;
        }

        Memory memory = MemoryByInstance.GetOrCreateValue(controller);
        if (GameModeManager.IsActive(GameMode.Infidel))
        {
            InfidelState.ApplyHealth(controller);
            memory.LastModeSpecificHealth = true;
            memory.LastAppliedVersion = version;
            return;
        }
        if (GameModeManager.IsActive(GameMode.OneInTheChamber))
        {
            OneInTheChamberState.ApplyHealth(controller);
            memory.LastModeSpecificHealth = true;
            memory.LastAppliedVersion = version;
            return;
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

        int playerId = controller.playerValues?.playerClient?.PlayerId ?? -1;
        int healthCompensationVersion = TeamAssignment.HealthCompensationVersion;
        float teamHealthMultiplier = TeamAssignment.GetHealthMultiplier(playerId);
        if (memory.LastAppliedVersion == version
            && memory.LastAppliedHealthCompensationVersion == healthCompensationVersion
            && memory.LastAppliedPlayerId == playerId)
        {
            return;
        }
        memory.LastAppliedVersion = version;
        memory.LastAppliedHealthCompensationVersion = healthCompensationVersion;
        memory.LastAppliedPlayerId = playerId;

        float scaledFullHealth = memory.BaselineFullHealth * healthMultiplier * teamHealthMultiplier;
        float previousFullHealth = controller.fullHealth;
        controller.fullHealth = scaledFullHealth;


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
            }
        }
    }

    internal static void RegenerateIfNeeded(PlayerHealth controller, Memory memory)
    {
        bool juggernautHealth = GameModeManager.IsActive(GameMode.Juggernaut) && JuggernautState.IsCurrentJuggernaut(controller);
        bool ratHealth = GameModeManager.IsActive(GameMode.KillTheRat) && KillTheRatState.IsRat(controller);
        if (juggernautHealth || ratHealth || GameModeManager.ShouldIgnoreGlobalHealthSettings || !controller.IsServer || !controller.gameObject.activeInHierarchy || controller.health <= 0f)
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

    internal static Memory GetMemory(PlayerHealth controller)
    {
        return MemoryByInstance.GetOrCreateValue(controller);
    }
}