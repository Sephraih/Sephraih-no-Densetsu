using System.Collections.Generic;
using UnityEngine;

// Base for a scene's top-level Map controller (one per Map scene - an overworld region or a whole
// dungeon). Sub-area transitions within one Map (dungeon level 1->2, etc.) stay handled by the
// map's own subclass logic via OnPortalUsed; MapManager only calls in for cross-scene transitions.
public abstract class MapBehaviour : MonoBehaviour, IMap
{
    [SerializeField] protected MapBoundary boundary;
    [SerializeField] protected List<SpawnPoint> spawnPoints = new();
    // Defaults to this project's existing single shared background color, so any map that doesn't
    // explicitly set its own keeps looking exactly like it did before this field existed.
    [SerializeField] protected Color backgroundColor = new Color(0.3294118f, 0.3294118f, 0.3294118f, 1f);

    public MapBoundary Boundary => boundary;
    public Color BackgroundColor => backgroundColor;
    public SpawnPoint GetSpawnPoint(string id) => spawnPoints.Find(s => s.Id == id);

    public virtual void OnMapEntered(string spawnPointId) { }
    public virtual void OnMapAboutToUnload() { }
    public virtual void OnPortalUsed(PortalBehaviour portal) { }
}
