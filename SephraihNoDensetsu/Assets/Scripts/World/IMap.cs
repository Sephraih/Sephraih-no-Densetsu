// Contract a scene's top-level Map controller implements so MapManager and Portal can talk to
// whichever Map is currently loaded without knowing its concrete type (overworld region, dungeon).
public interface IMap
{
    MapBoundary Boundary { get; }
    // Applied to Camera.main by MapManager.TravelRoutine whenever this map becomes the current one -
    // lets each map/scene show its own backdrop color instead of one fixed color for every map.
    UnityEngine.Color BackgroundColor { get; }
    SpawnPoint GetSpawnPoint(string id);

    // Called by MapManager once this map's scene has finished its own Start()-time setup.
    void OnMapEntered(string spawnPointId);

    // Called by MapManager just before this map's scene unloads.
    void OnMapAboutToUnload();

    // Called by a same-scene Portal (sub-area transition, e.g. dungeon level advance).
    void OnPortalUsed(PortalBehaviour portal);
}
