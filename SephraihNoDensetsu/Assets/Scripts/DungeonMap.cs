using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;
using Unity.AI.Navigation;

// Generic dungeon Map controller: level-toggle machinery, reusable across any dungeon. Mobs and
// the advance-portal are now authored directly in each Level's hierarchy (drag-and-drop in the
// Editor) rather than spawned/positioned by code - the dungeon is freely explorable, not gated by
// waves, so there's no runtime reason to instantiate either. The old wave-clear/mob-spawn-by-code
// machinery is kept below, commented out rather than deleted, since that pattern (spawn on wave
// clear, gate a portal until N enemies die) could be useful again for something like a boss room.
public class DungeonMap : MapBehaviour
{
    private string enemyPath = "Prefabs/Enemies/";
    private int Waves;
    private int CurrentWave;
    private bool cleared = false;
    private int Level = 1;
    private int MaxLevel = 3;
    private List<LevelMob> mobs = new List<LevelMob>();
    // Still holds each level's authored mob/wave data (migrated from the old hardcoded LoadLevel()
    // branches) even though nothing spawns from it anymore - kept as a ready-made reference/backup
    // for a future wave-gated encounter rather than deleted.
    [SerializeField] private List<LevelConfig> levels = new List<LevelConfig>(); // index 0 = Level1, index 1 = Level2, ...
    public GameObject LevelMaps;
    // private GameObject portal; // dead: the advance portal used to be a single runtime-
                                   // instantiated instance reused across all levels at a hardcoded
                                   // position - now each Level has its own portal child instead,
                                   // authored in the Editor, active whenever that level is.

    [SerializeField] private NavMeshSurface navMeshSurface;

    private Vector2 worldPoint;
    [SerializeField] private Tilemap obstacleMap;
    [SerializeField] private Tilemap boundaryMap;

    GameObject Player => MapManager.Instance.Player;

    // Start is called before the first frame update
    void Start()
    {
        // LoadLevel(); // dead: populated Waves/mobs for wave-based spawning - mobs are now
                         // pre-placed per level in the scene instead.
        // portal = Instantiate((Resources.Load("Prefabs/GameObjects/Portal") as GameObject), new Vector3(0, 5, 1), Quaternion.identity);
        // portal.SetActive(true);

        ActivateLevel(Level);
        RebuildNavMesh();

    }

    // Update is called once per frame
    void Update()
    {
        // Wave-clear gating removed - the dungeon is freely explorable now and mobs are pre-placed
        // in the scene instead of spawned by code. Commented out rather than deleted: "N enemies
        // must die before the exit unlocks" could be exactly what a boss room wants later.
        /*
        if (Camera.main.GetComponent<GameBehaviour>().characterList.Count == 1 && cleared == false) // 1 enemy or player remaining
        {
            if (CurrentWave == Waves) { StageClear(); return; }
            LoadEnemies();
        }
        */
        Unstuck();


    }

    // Rebuilds the NavMesh for whichever level is currently active. Only one Dungeon level is
    // ever active at a time (the rest sit disabled), so a single shared NavMeshSurface is baked
    // fresh on every level transition rather than maintaining one pre-baked NavMeshData per level.
    private void RebuildNavMesh()
    {
        if (navMeshSurface != null) navMeshSurface.BuildNavMesh();
        // The baked mesh's real height (navmesh Z) can shift between bakes - drop the cached
        // value so the next NavMesh2DUtility query re-discovers it instead of using a stale one.
        NavMesh2DUtility.InvalidateCache();
    }

    // Single source of truth for "make level N the active one" - always resolves obstacle/boundary
    // tilemaps via each level's LevelBehaviour component rather than hardcoded sibling indices,
    // since level sub-hierarchies aren't uniformly structured. `level` is a 1-based dungeon level
    // number (Level1 = 1); LevelMaps' child index is level-1 since Level0 (the former hub, now its
    // own MainCity scene) no longer occupies child index 0 here. Each level's own mobs/advance-
    // portal are authored as children of that LevelN GameObject, so they activate/deactivate for
    // free alongside it here - no separate bookkeeping needed.
    private void ActivateLevel(int level)
    {
        LevelMaps.transform.GetChild(level - 1).gameObject.SetActive(true);
        var lb = LevelMaps.transform.GetChild(level - 1).GetComponent<LevelBehaviour>();
        obstacleMap = lb.obstacleMap;
        boundaryMap = lb.boundaryMap;
    }

    /*
    public void LoadEnemies() {

        foreach (LevelMob mob in mobs)
        {
            if (mob.level == Level && mob.wave == CurrentWave) { InstantiateEnemy(mob.mobtype, mob.location); }

        }
        CurrentWave++;

    }
    public void StageClear() {

        Player.transform.position = GetSpawnPoint("Entry").transform.position;
        portal.SetActive(true);
        cleared = true;

    }
    */

    // Resets the dungeon back to Level1, relying on the always-on entrance portal (now pointing
    // back at MainCity) as the single way out - correct for "one path" dungeons with no branching,
    // since resetting to Level1 puts the player back where that permanent exit portal already sits.
    public void DungeonClear() {
        Level = 1;
        // mobs.Clear(); // dead: mobs are pre-placed per level now, not spawned into a shared list
        ActivateLevel(Level);
        RebuildNavMesh();
        // LoadLevel(); // dead: wave/mob data no longer drives spawning
        // portal.SetActive(true); // dead: portals are per-level scene children now
    }


    public void InstantiateEnemy(string enemy, Vector3 pos) {Instantiate((Resources.Load(Path.Combine(enemyPath, enemy)) as GameObject), pos, Quaternion.identity);}


    public void LoadNextLevel()
    {
        // portal.SetActive(false); // dead: see DungeonClear() note above
        Level++;
        LevelMaps.transform.GetChild(Level - 2).gameObject.SetActive(false);
        // CurrentWave = 0; // dead: wave state no longer used
        // cleared = false; // dead: wave state no longer used
        Player.transform.position = GetSpawnPoint("Entry").transform.position;

        if (Level <= MaxLevel) {
            ActivateLevel(Level);
            RebuildNavMesh();
            // LoadLevel(); // dead: wave/mob data no longer drives spawning
        } else DungeonClear();


    }

    public void ReloadLevel() {
        Player.transform.position = GetSpawnPoint("Entry").transform.position;
        // CurrentWave = 0; // dead: wave state no longer used
        // mobs.Clear(); // dead: mobs are pre-placed per level now
        // LoadLevel(); // dead: wave/mob data no longer drives spawning

    }

    /*
    // Loads the active level's wave count + mob spawn list from this instance's `levels` config
    // (index Level-1) - data-driven per-dungeon-instance configuration instead of hardcoded
    // per-level code branches, so a different dungeon scene only needs a differently-configured
    // `levels` list, not a new subclass.
    public void LoadLevel() {
        if (Level < 1 || Level > levels.Count)
        {
            Debug.LogError($"[DungeonMap] No LevelConfig for Level {Level} (levels.Count={levels.Count}).");
            return;
        }

        var config = levels[Level - 1];
        Waves = config.waves;
        mobs.AddRange(config.mobs);
    }
    */


    public void Unstuck()
    {
        // change to a foreach loop to loop over all active units

        // Try to get a tile from cell position matching player position
        var obstacle = obstacleMap.GetTile(obstacleMap.WorldToCell(Player.transform.position));
        var boundary = boundaryMap.GetTile(boundaryMap.WorldToCell(Player.transform.position));


        if (obstacle || boundary) // if a tile (obstacle or boundary) was found -> move player
        {
            Player.transform.position = Player.GetComponent<UnitController>().saveSpot; // unstuck to last savespot triggered by critical abilities.
        }


    }

    public override void OnMapEntered(string spawnPointId)
    {
        var spawn = GetSpawnPoint(spawnPointId) ?? GetSpawnPoint("Entry");
        if (spawn != null) Player.transform.position = spawn.transform.position;
    }

    public override void OnMapAboutToUnload()
    {
        // mobs.Clear(); // dead: mobs are pre-placed per level now, nothing accumulates in this list
    }

    public override void OnPortalUsed(PortalBehaviour portalUsed)
    {
        LoadNextLevel();
    }

    [Serializable]
    public struct LevelMob{
        //Variable declaration
        //Note: I'm explicitly declaring them as public, but they are public by default. You can use private if you choose.
        public int level;
        public int wave;
        public String mobtype;
        public Vector3 location;

        //Constructor (not necessary, but helpful)
        public LevelMob(int level, int wave, String mobtype, Vector3 location )
        {
            this.level = level;
            this.wave = wave;
            this.mobtype = mobtype;
            this.location = location;
        }
    };

    // Per-level data for one DungeonMap instance: how many waves, and which mob spawns at which
    // wave. Not read by anything active right now (see LoadLevel() above) - kept as a ready-made
    // reference/backup for a future wave-gated encounter (e.g. a boss room) instead of deleted.
    [Serializable]
    public class LevelConfig
    {
        public int waves;
        public List<LevelMob> mobs = new List<LevelMob>();
    }

}
