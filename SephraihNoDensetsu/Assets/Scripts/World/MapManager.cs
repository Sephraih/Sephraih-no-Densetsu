using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Lives in Bootstrap.unity (never unloaded). Owns which Map scene is currently loaded and drives
// additive load/unload on cross-scene Portal use. Same-scene sub-area transitions (dungeon level
// advance, etc.) don't go through here - PortalBehaviour calls CurrentMap.OnPortalUsed directly.
public class MapManager : MonoBehaviour
{
    public static MapManager Instance { get; private set; }

    [SerializeField] private GameObject player;
    public GameObject Player => player;

    [SerializeField] private string startingScene = "MainCity";
    [SerializeField] private string startingSpawnId = "Start";

    public IMap CurrentMap { get; private set; }
    public string CurrentMapScene { get; private set; }

    // Guards against a source (e.g. a Portal re-triggering every FixedUpdate tick while the
    // player's collider still overlaps it) firing TravelTo again before the current transition
    // finishes - without this, overlapping TravelRoutine coroutines each additively load their
    // own copy of the target scene, stacking up duplicates.
    bool isTraveling;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        Initialize(startingScene, startingSpawnId);
    }

    public void Initialize(string startingScene, string startingSpawnId) => StartCoroutine(TravelRoutine(startingScene, startingSpawnId));

    public void TravelTo(string sceneName, string spawnPointId)
    {
        if (isTraveling || sceneName == CurrentMapScene) return;
        StartCoroutine(TravelRoutine(sceneName, spawnPointId));
    }

    IEnumerator TravelRoutine(string sceneName, string spawnPointId)
    {
        Debug.Assert(sceneName != "Bootstrap", "MapManager should never target the Bootstrap scene.");

        isTraveling = true;
        string previousScene = CurrentMapScene;
        CurrentMap?.OnMapAboutToUnload();

        yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        Scene newScene = SceneManager.GetSceneByName(sceneName);
        SceneManager.SetActiveScene(newScene);

        // Let the newly loaded scene's own Start() (LoadLevel, RebuildNavMesh, runtime portal
        // spawn, etc.) run before we hand off control to it.
        yield return null;

        // Unload the previous scene BEFORE trusting NavMesh queries against the new one.
        // NavMeshSurface adds its baked data in OnEnable and only removes it in OnDisable, so
        // until this unload completes both scenes' NavMesh data are simultaneously registered in
        // Unity's global (not per-scene) navigation system - a Z-offset probe during that window
        // can land on the wrong map's mesh. Only once the old scene is gone is it safe to
        // invalidate/re-probe and let gameplay (enemy pathing) go live in the new one.
        if (!string.IsNullOrEmpty(previousScene))
            yield return SceneManager.UnloadSceneAsync(previousScene);

        NavMesh2DUtility.InvalidateCache();
        CurrentMap = FindMap(newScene);
        CurrentMapScene = sceneName;
        // One shared Camera lives in Bootstrap (never destroyed across map transitions) - so a
        // per-map backdrop has to be applied here on entry rather than being a per-scene Inspector
        // value, which is what MainCamera.backgroundColor would otherwise look like it already is.
        if (CurrentMap != null && Camera.main != null)
            Camera.main.backgroundColor = CurrentMap.BackgroundColor;
        CurrentMap?.OnMapEntered(spawnPointId);

        isTraveling = false;
    }

    static IMap FindMap(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var map = root.GetComponentInChildren<IMap>(true);
            if (map != null) return map;
        }
        Debug.LogError($"[MapManager] No IMap found in scene '{scene.name}'.");
        return null;
    }
}
