using System.Collections.Generic;
using UnityEngine;

// Raycast-and-filter helpers answering "is the line between two points clear of X". Each does one
// combined-layer raycast, walks hits in distance order, and defers to the hit Obstacle's flag -
// or treats the hit as blocking if it has no registered Obstacle yet (fail-safe during rollout).
public static class ObstacleQuery
{
    static int obstacleLayerMask = -1;
    public static int ObstacleLayerMask
    {
        get
        {
            if (obstacleLayerMask == -1)
                obstacleLayerMask = LayerMask.GetMask("Boundaries", "Obstacles");
            return obstacleLayerMask;
        }
    }

    static readonly RaycastHit2D[] buf = new RaycastHit2D[16];
    static readonly IComparer<RaycastHit2D> byDistance =
        Comparer<RaycastHit2D>.Create((x, y) => x.distance.CompareTo(y.distance));

    static bool IsBlocked(Vector2 a, Vector2 b, System.Func<Obstacle, bool> selector)
    {
        Vector2 delta = b - a;
        float dist = delta.magnitude;
        if (dist < 0.0001f) return false;

        int n = Physics2D.RaycastNonAlloc(a, delta / dist, buf, dist, ObstacleLayerMask);
        System.Array.Sort(buf, 0, n, byDistance);

        for (int i = 0; i < n; i++)
        {
            if (ObstacleRegistry.TryGet(buf[i].collider, out var obs))
            {
                if (selector(obs)) return true;
                continue; // this obstacle instance doesn't block this query type - see past it
            }
            return true; // solid collider on an obstacle layer with no Obstacle component yet: fail safe
        }
        return false;
    }

    public static bool BlocksSight(Vector2 a, Vector2 b) => IsBlocked(a, b, o => o.BlocksSight);
    public static bool BlocksProjectile(Vector2 a, Vector2 b) => IsBlocked(a, b, o => o.BlocksProjectiles);
    public static bool BlocksTeleport(Vector2 a, Vector2 b) => IsBlocked(a, b, o => o.BlocksMovement);
}
