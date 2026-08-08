using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Dungeon-specific Map controller: linear Level1/2/3 bookkeeping and legacy wave/mob machinery on
// top of MultiAreaMap's shared zone-activation/NavMesh-rebake/stuck-rescue/GoToExit machinery.
// Mobs and the advance-portal are now authored directly in each Level's hierarchy (drag-and-drop
// in the Editor) rather than spawned/positioned by code - the dungeon is freely explorable, not
// gated by waves, so there's no runtime reason to instantiate either. The old wave-clear/mob-
// spawn-by-code machinery is kept below, commented out rather than deleted, since that pattern
// (spawn on wave clear, gate a portal until N enemies die) could be useful again for something
// like a boss room.
public class DungeonMap : MultiAreaMap
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
    // Convention for any scene hosting more than one MapArea (this one included, if it ever stops
    // being strictly linear): place each instance at a distinct, well-separated world position -
    // not stacked at the origin - even though only one is ever SetActive at a time today.
    // NavMeshSurface registers its baked data globally while enabled - if two areas' colliders
    // ever end up active (and therefore baked) at the same time while occupying the same world
    // space, their meshes overlap and corrupt the bake (hit and fixed once already, see
    // project_navmesh_2d_gotchas memory). Spatial separation costs nothing now (SetActive-toggling
    // for perf/culling still works exactly as before) and is a prerequisite for any future
    // non-linear or concurrently-active scenario.
    public GameObject LevelMaps;
    // private GameObject portal; // dead: the advance portal used to be a single runtime-
                                   // instantiated instance reused across all levels at a hardcoded
                                   // position - now each Level has its own portal child instead,
                                   // authored in the Editor, active whenever that level is.

    // Where a fresh City -> Dungeon arrival lands, and the level that this SpawnPoint belongs to
    // determines which level gets activated. Dungeon has exactly one entrance, always Level1, so
    // it deliberately does NOT use MultiAreaMap.OnMapEntered's spawnPointId-driven multi-entrance
    // resolution below - this fixed reference replaces the old GetSpawnPoint("Entry") lookup for
    // this same purpose, which resolved to a single scene-wide SpawnPoint that, since nothing kept
    // its typed string ID and its actual position in sync, had drifted to sitting at (0,0,0),
    // unrelated to any level's real layout.
    [SerializeField] private SpawnPoint level1Entry;

    // Start is called before the first frame update
    void Start()
    {
        // LoadLevel(); // dead: populated Waves/mobs for wave-based spawning - mobs are now
                         // pre-placed per level in the scene instead.
        // portal = Instantiate((Resources.Load("Prefabs/GameObjects/Portal") as GameObject), new Vector3(0, 5, 1), Quaternion.identity);
        // portal.SetActive(true);

        ActivateLevel(Level);
        // NOT RebuildNavMesh() here - see OnMapEntered. This Start() runs as part of MapManager's
        // additive load of this scene, which happens BEFORE the scene being traveled FROM (e.g.
        // MainCity) is unloaded (TravelRoutine unloads it one frame later). navMeshSurface bakes
        // with collectObjects=All, which sweeps every currently loaded scene, not just this one -
        // baking here caught the previous scene's still-present geometry mid-transition and
        // produced a corrupted/disconnected Level1 mesh (some areas across a wall became
        // unreachable despite a real route existing). LoadNextLevel/GoToExit's own direct
        // RebuildNavMesh() calls are unaffected since those are same-scene transitions with no
        // other scene involved, which is why only the very first entry into the dungeon (not later
        // level-to-level transitions) ever showed this.

    }

    // Update is called once per frame
    protected override void Update()
    {
        base.Update(); // Unstuck() - see MultiAreaMap
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
    }

    // Single source of truth for "make level N the active one" (index-based, only meaningful for
    // this strictly linear Level1/2/3 dungeon). `level` is a 1-based dungeon level number;
    // LevelMaps' child index is level-1 since Level0 (the former hub, now its own MainCity scene)
    // no longer occupies child index 0 here. Each level's own mobs/advance-portal are authored as
    // children of that LevelN GameObject, so they activate/deactivate for free alongside it via
    // MultiAreaMap.ActivateArea, which also handles deactivating whatever was previously active
    // (via activeAreaObject) regardless of whether it got there via this index-based path or via
    // GoToExit's direct-reference path - the two can't leave two levels active at once.
    private void ActivateLevel(int level)
    {
        ActivateArea(LevelMaps.transform.GetChild(level - 1).gameObject);
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
        ActivateLevel(Level); // also rebakes, via MultiAreaMap.ActivateArea
        // Explicit reposition, not just relying on wherever the player physically stood when the
        // last AdvancePortal fired - that only ever looked reasonable by coincidence, since every
        // level currently sits stacked near the same world-space origin (see LevelMaps' own doc
        // comment on the spatial-separation convention) rather than because this was ever correct.
        Player.transform.position = level1Entry.transform.position;
        // LoadLevel(); // dead: wave/mob data no longer drives spawning
        // portal.SetActive(true); // dead: portals are per-level scene children now
    }


    public void InstantiateEnemy(string enemy, Vector3 pos) {Instantiate((Resources.Load(Path.Combine(enemyPath, enemy)) as GameObject), pos, Quaternion.identity);}


    // Legacy fallback for a portal that hasn't been given an explicit target (see OnPortalUsed) -
    // every currently-authored AdvancePortal HAS an explicit target now (see the fix note on
    // OnPortalUsed below), so in normal play this only ever fires for the Level3->clear wrap-
    // around. If a future level's AdvancePortal is ever left unconfigured, this still advances it
    // linearly, just without a precise landing spot - level1Entry is used as a not-wrong (if
    // imprecise) placeholder rather than the old GetSpawnPoint("Entry"), which resolved to a
    // scene-root SpawnPoint sitting at literal world (0,0,0), unrelated to any level's actual
    // layout - the same bug the entry flow already had fixed via level1Entry, just never applied
    // here too.
    public void LoadNextLevel()
    {
        // portal.SetActive(false); // dead: see DungeonClear() note above
        Level++;
        // Deactivation of the previous level now happens inside MultiAreaMap.ActivateArea (via
        // activeAreaObject), not here - needed once GoToExit could also have been the one that
        // last activated something, since a manual GetChild(Level-2)-style deactivate would assume
        // Level was always kept in lockstep with whatever's actually active, which GoToExit
        // (reference-based, no Level bookkeeping) doesn't guarantee.
        // CurrentWave = 0; // dead: wave state no longer used
        // cleared = false; // dead: wave state no longer used
        Player.transform.position = level1Entry.transform.position;

        if (Level <= MaxLevel) {
            ActivateLevel(Level); // also rebakes, via MultiAreaMap.ActivateArea
            // LoadLevel(); // dead: wave/mob data no longer drives spawning
        } else DungeonClear(); // also repositions correctly, overriding the line above


    }

    public void ReloadLevel() {
        Player.transform.position = level1Entry.transform.position;
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

    // Dungeon has exactly one entrance (always Level1), so it overrides MultiAreaMap's generic
    // spawnPointId-driven multi-entrance resolution and always lands at level1Entry instead -
    // deliberately ignores spawnPointId, unlike the base implementation.
    public override void OnMapEntered(string spawnPointId)
    {
        // Baking here instead of Start() - see the comment on Start()'s call site. MapManager only
        // invokes OnMapEntered after confirming the scene traveled FROM is fully unloaded, so this
        // is the first point at which collectObjects=All is guaranteed to see only this scene.
        RebuildNavMesh();

        if (level1Entry != null)
        {
            Player.transform.position = level1Entry.transform.position;
            return;
        }
        // Fallback only - level1Entry should always be assigned in the Editor.
        var spawn = GetSpawnPoint(spawnPointId) ?? GetSpawnPoint("Entry");
        if (spawn != null) Player.transform.position = spawn.transform.position;
    }

    public override void OnMapAboutToUnload()
    {
        // mobs.Clear(); // dead: mobs are pre-placed per level now, nothing accumulates in this list
    }

    // A portal with no explicit target falls back to the legacy linear "advance one level"
    // behavior; one WITH a target uses MultiAreaMap.OnPortalUsed's reference-based GoToExit instead.
    public override void OnPortalUsed(PortalBehaviour portalUsed)
    {
        if (portalUsed.Target != null)
            base.OnPortalUsed(portalUsed);
        else
            LoadNextLevel(); // legacy fallback for a portal that hasn't been given an explicit target yet
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
