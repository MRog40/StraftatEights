using System.Collections.Generic;
using UnityEngine;

namespace Eights;

internal static class BloodCleanupState
{
    private const int DefaultMaximumBloodEffects = 50;
    private static readonly Queue<GameObject> BloodHistory = new();

    internal static void Register(GameObject bloodEffect)
    {
        if (bloodEffect == null || !bloodEffect)
        {
            return;
        }

        RemoveDestroyedEffects();
        BloodHistory.Enqueue(bloodEffect);
        TrimToLimit();
    }

    internal static void Update()
    {
        RemoveDestroyedEffects();
    }

    internal static void TrimToLimit()
    {
        RemoveDestroyedEffects();

        int maximumBloodEffects = Plugin.MaximumBloodEffects != null
            ? Plugin.MaximumBloodEffects.Value
            : DefaultMaximumBloodEffects;
        while (BloodHistory.Count > maximumBloodEffects)
        {
            GameObject oldestEffect = BloodHistory.Dequeue();
            if (oldestEffect != null && oldestEffect)
            {
                Object.Destroy(oldestEffect);
            }
        }
    }

    private static void RemoveDestroyedEffects()
    {
        int historyCount = BloodHistory.Count;
        for (int index = 0; index < historyCount; index++)
        {
            GameObject effect = BloodHistory.Dequeue();
            if (effect != null && effect)
            {
                BloodHistory.Enqueue(effect);
            }
        }
    }
}