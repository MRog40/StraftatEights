using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Eights;

[HarmonyPatch]
internal static class BloodCleanup_Instantiate_Patch
{
    private static readonly (Type Type, string MethodName)[] ProducerMethods =
    {
        (typeof(PhysicsProp), "OnControllerColliderHit"),
        (typeof(MeleeChildCollision), "OnCollisionEnter"),
        (typeof(Weapon), "TriggerEnvironment"),
        (typeof(MeleeWeapon), "HitServer"),
        (typeof(PredictedProjectile), "OnHit"),
        (typeof(PhysicsGrenade), "Update"),
        (typeof(ShrapnelBallistic), "Action"),
        (typeof(BeamGun), "ObserversFX"),
        (typeof(ChargeGun), "ObserversFX"),
        (typeof(Gun), "ObserversFX"),
        (typeof(LargeRaycastGun), "ObserversFX"),
        (typeof(Minigun), "ObserversFX"),
        (typeof(Shotgun), "ObserversFX")
    };

    private static readonly HashSet<string> BloodFieldNames = new(StringComparer.Ordinal)
    {
        "bloodFX",
        "bloodImpact",
        "bloodSplatter",
        "bloodVfx",
        "headBloodVfx"
    };

    private static IEnumerable<MethodBase> TargetMethods()
    {
        HashSet<MethodBase> addedMethods = new();
        foreach ((Type type, string methodName) in ProducerMethods)
        {
            MethodInfo? method = ResolveProducerMethod(type, methodName);
            if (method != null && addedMethods.Add(method))
            {
                yield return method;
            }
            else
            {
                Plugin.Logger.LogWarning($"[BloodCleanup] Could not find {type.Name}.{methodName}.");
            }
        }

        MethodInfo? deathMethod = FishNetCompatibility.FindGeneratedMethod(typeof(PlayerHealth),
            "RpcLogic___ExplodeForAll_", method => method.ReturnType == typeof(void)
                && method.GetParameters().Length == 6);
        if (deathMethod != null && addedMethods.Add(deathMethod))
        {
            yield return deathMethod;
        }
        else
        {
            Plugin.Logger.LogWarning("[BloodCleanup] Could not find PlayerHealth.ExplodeForAll logic.");
        }
    }

    private static MethodInfo? ResolveProducerMethod(Type type, string methodName)
    {
        if (methodName == "ObserversFX")
        {
            return FishNetCompatibility.FindGeneratedMethod(type, "RpcLogic___ObserversFX_",
                method => method.ReturnType == typeof(void)
                    && method.GetParameters().Length == 2);
        }

        return AccessTools.Method(type, methodName);
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codes = new(instructions);
        MethodInfo? registerMethod = AccessTools.Method(typeof(BloodCleanupState),
            nameof(BloodCleanupState.Register));
        if (registerMethod == null)
        {
            throw new InvalidOperationException("Blood cleanup register method was not found.");
        }

        bool bloodFieldLoaded = false;
        for (int index = 0; index < codes.Count; index++)
        {
            CodeInstruction instruction = codes[index];
            if (instruction.opcode == OpCodes.Ldfld
                && instruction.operand is FieldInfo field
                && BloodFieldNames.Contains(field.Name))
            {
                bloodFieldLoaded = true;
            }

            if (IsBranch(instruction))
            {
                bloodFieldLoaded = false;
            }

            if (bloodFieldLoaded && IsInstantiateCall(instruction))
            {
                if (index + 1 < codes.Count && codes[index + 1].opcode == OpCodes.Pop)
                {
                    yield return instruction;
                    yield return ReplacePop(codes[index + 1], registerMethod);
                    index++;
                    bloodFieldLoaded = false;
                    continue;
                }

                Plugin.Logger.LogWarning("[BloodCleanup] Blood instantiate result was not discarded; call was not tracked.");
                bloodFieldLoaded = false;
            }

            yield return instruction;
        }
    }

    private static CodeInstruction ReplacePop(CodeInstruction original, MethodInfo registerMethod)
    {
        CodeInstruction replacement = new(OpCodes.Call, registerMethod);
        replacement.labels.AddRange(original.labels);
        replacement.blocks.AddRange(original.blocks);
        return replacement;
    }

    private static bool IsInstantiateCall(CodeInstruction instruction)
    {
        if ((instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
            || instruction.operand is not MethodInfo method)
        {
            return false;
        }

        return method.Name == "Instantiate"
            && method.DeclaringType == typeof(UnityEngine.Object)
            && method.ReturnType == typeof(GameObject);
    }

    private static bool IsBranch(CodeInstruction instruction)
    {
        return instruction.opcode.FlowControl == FlowControl.Branch
            || instruction.opcode.FlowControl == FlowControl.Cond_Branch;
    }
}