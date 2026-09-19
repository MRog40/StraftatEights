using System;
using System.Collections.Generic;

namespace Eights;

internal sealed class TeamWeaponSequence
{
    private readonly List<string> _allowed;
    private readonly Random _random;
    private readonly List<string> _sequence = new();

    internal TeamWeaponSequence(IEnumerable<string> allowed, int seed)
    {
        _allowed = new List<string>(allowed);
        if (_allowed.Count == 0)
        {
            throw new ArgumentException("At least one allowed weapon is required.", nameof(allowed));
        }

        _random = new Random(seed);
    }

    internal int CycleLength => _allowed.Count;

    internal string GetAt(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        EnsureLength(index + 1);
        return _sequence[index];
    }

    private void EnsureLength(int length)
    {
        while (_sequence.Count < length)
        {
            List<string> cycle = new(_allowed);
            for (int index = cycle.Count - 1; index > 0; index--)
            {
                int swapIndex = _random.Next(index + 1);
                (cycle[index], cycle[swapIndex]) = (cycle[swapIndex], cycle[index]);
            }

            _sequence.AddRange(cycle);
        }
    }
}