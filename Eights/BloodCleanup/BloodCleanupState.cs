using System.Collections.Generic;
using UnityEngine;

namespace Eights;

internal static class BloodCleanupState
{
    private const int DefaultMaximumBloodEffects = 25;
    private static readonly Queue<GameObject> BloodHistory = new();
    private static int _maximumBloodEffects = DefaultMaximumBloodEffects;

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

    internal static void ApplyMaximumBloodEffects(int maximumBloodEffects)
    {
        _maximumBloodEffects = Mathf.Clamp(maximumBloodEffects, 0, 50);
        TrimToLimit();
    }

    internal static void TrimToLimit()
    {
        RemoveDestroyedEffects();

        while (BloodHistory.Count > _maximumBloodEffects)
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