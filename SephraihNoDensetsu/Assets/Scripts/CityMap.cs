using UnityEngine;

// Generic "hub" Map controller for city/overworld-field-type regions - no internal sub-areas of
// its own, just exposes its boundary/spawn points to MapManager for cross-scene portal arrival,
// per the Map/Boundary/Portal system. A specific place (e.g. MainCity) is just a scene/GameObject
// carrying this component plus its own MapBoundary/SpawnPoints - no subclass needed per instance.
public class CityMap : MapBehaviour
{
    public override void OnMapEntered(string spawnPointId)
    {
        var spawn = GetSpawnPoint(spawnPointId);
        if (spawn == null) return;
        MapManager.Instance.Player.transform.position = spawn.transform.position;
    }

    public override void OnMapAboutToUnload()
    {
    }
}
