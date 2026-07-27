using System.Collections.Generic;
using UnityEngine;

// Maps every Collider2D belonging to an Obstacle back to that Obstacle, so raycast/overlap
// results (which only ever hand back a Collider2D) can be resolved to blocking-flag data.
// Self-maintaining via each Obstacle's OnEnable/OnDisable - no manual bookkeeping required.
public static class ObstacleRegistry
{
    static readonly Dictionary<Collider2D, Obstacle> byCollider = new Dictionary<Collider2D, Obstacle>();

    public static void Register(Obstacle o)
    {
        foreach (var c in o.GetComponentsInChildren<Collider2D>())
            byCollider[c] = o;
    }

    public static void Unregister(Obstacle o)
    {
        foreach (var c in o.GetComponentsInChildren<Collider2D>())
            if (byCollider.TryGetValue(c, out var reg) && reg == o)
                byCollider.Remove(c);
    }

    public static bool TryGet(Collider2D c, out Obstacle o) => byCollider.TryGetValue(c, out o);
}
