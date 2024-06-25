using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using static UnityEditor.PlayerSettings;

public class DungeonBehaviour : MonoBehaviour
{
    private string enemyPath = "Prefabs/Enemies/";
    private int Waves;
    private int CurrentWave;
    private bool cleared = false;
    private int Level = 0;
    private int MaxLevel = 3;
    private List<LevelMob> mobs = new List<LevelMob>();
    public GameObject Player;
    public GameObject LevelMaps;
    private GameObject portal;

    private Vector2 worldPoint;
    [SerializeField] private Tilemap obstacleMap;
    [SerializeField] private Tilemap boundaryMap;

    // Start is called before the first frame update
    void Start()
    {
        LoadLevel();
        portal = Instantiate((Resources.Load("Prefabs/GameObjects/Portal") as GameObject), new Vector3(0, 5, 1), Quaternion.identity);
        portal.SetActive(true);

        //obstacleMap = LevelMaps.transform.GetChild(Level).GetChild(1).GetChild(0).GetComponent<Tilemap>();
        //boundaryMap = LevelMaps.transform.GetChild(Level).GetChild(2).GetChild(0).GetComponent<Tilemap>();

        obstacleMap = LevelMaps.transform.GetChild(Level).GetComponent<LevelBehaviour>().obstacleMap;
        boundaryMap = LevelMaps.transform.GetChild(Level).GetComponent<LevelBehaviour>().boundaryMap;

    }

    // Update is called once per frame
    void Update()
    {
        if (Camera.main.GetComponent<GameBehaviour>().characterList.Count == 1 && cleared == false) // 1 enemy or player remaining
        {
            if (CurrentWave == Waves) { StageClear(); return; }
            LoadEnemies();
        }
        Unstuck();
    //  Debug.Log(Camera.main.GetComponent<GameBehaviour>().characterList.Count);


    }

    public void LoadEnemies() {

        foreach (LevelMob mob in mobs)
        {
            if (mob.level == Level && mob.wave == CurrentWave) { InstantiateEnemy(mob.mobtype, mob.location); }

        }
        CurrentWave++;

    }
    public void StageClear() {

        Player.transform.position = new Vector3(0, 0, 0);
        portal.SetActive(true);
        cleared = true;
       
    }

    public void DungeonClear() {
        LevelMaps.transform.GetChild(0).gameObject.SetActive(true);
        obstacleMap = LevelMaps.transform.GetChild(0).GetChild(1).GetChild(0).GetComponent<Tilemap>();
        boundaryMap = LevelMaps.transform.GetChild(0).GetChild(2).GetChild(0).GetComponent<Tilemap>();
        // instantiate start Portal
        Level = 0;
        portal.SetActive(true);
    }


    public void InstantiateEnemy(string enemy, Vector3 pos) {Instantiate((Resources.Load(Path.Combine(enemyPath, enemy)) as GameObject), pos, Quaternion.identity);}


    public void LoadNextLevel()
    {
        //SceneManager.LoadScene("Main");
        portal.SetActive(false);
        Level++;
        LevelMaps.transform.GetChild(Level - 1).gameObject.SetActive(false);
        CurrentWave = 0;
        cleared = false;
        Player.transform.position = new Vector3(0, 0, 0);

        if (Level <= MaxLevel) {
            LevelMaps.transform.GetChild(Level).gameObject.SetActive(true); 
            obstacleMap = LevelMaps.transform.GetChild(Level).GetChild(1).GetChild(0).GetComponent<Tilemap>();
            boundaryMap = LevelMaps.transform.GetChild(Level).GetChild(2).GetChild(0).GetComponent<Tilemap>();
            LoadLevel();
        } else DungeonClear();
        
        
    }

    public void ReloadLevel() {
        Player.transform.position = new Vector3(0, 0, 0);
        CurrentWave = 0;
        Camera.main.GetComponent<GameBehaviour>().ClearEnemies(Player.transform);
        mobs.Clear();
        LoadLevel() ;

    }

    public void LoadLevel() {
        
        

        if (Level == 1)
        {
            LevelMaps.transform.GetChild(Level).gameObject.SetActive(true); 
            obstacleMap = LevelMaps.transform.GetChild(Level).GetChild(1).GetChild(0).GetComponent<Tilemap>();
            boundaryMap = LevelMaps.transform.GetChild(Level).GetChild(2).GetChild(0).GetComponent<Tilemap>();
            Waves = 3; // [0, n-1]
                    

            mobs.Add(new LevelMob(1, 0, "mob", new Vector3(10, 10, 0)));
            mobs.Add(new LevelMob(1, 1, "mob", new Vector3(10, 10, 0)));
            mobs.Add(new LevelMob(1, 1, "mob", new Vector3(-10, 10, 0)));
            mobs.Add(new LevelMob(1, 2, "mob", new Vector3(10, 10, 0)));
            mobs.Add(new LevelMob(1, 2, "mob", new Vector3(-10, 10, 0)));
            mobs.Add(new LevelMob(1, 2, "mob", new Vector3(0, -10, 0)));
        }

        if (Level == 2)
        {
            Waves = 3;
            mobs.Add(new LevelMob(2, 0, "Guard", new Vector3(-2, 10, 0)));
            mobs.Add(new LevelMob(2, 0, "Guard", new Vector3(1, 10, 0)));
            mobs.Add(new LevelMob(2, 0, "mob", new Vector3(1, 30, 0)));
            mobs.Add(new LevelMob(2, 0, "mob", new Vector3(2, 30, 0)));

            mobs.Add(new LevelMob(2, 1, "Guard", new Vector3(4, 20, 0)));
            mobs.Add(new LevelMob(2, 1, "Guard", new Vector3(-9, 23, 0))); 
            mobs.Add(new LevelMob(2, 1, "Guard", new Vector3(-2, 10, 0)));
            mobs.Add(new LevelMob(2, 1, "Guard", new Vector3(-8, 33, 0)));

            mobs.Add(new LevelMob(2, 2, "Guard", new Vector3(-4, 37, 0)));
            mobs.Add(new LevelMob(2, 2, "Guard", new Vector3(-4, 33, 0))); 
            mobs.Add(new LevelMob(2, 2, "Guard", new Vector3(-19, 28, 0)));
            mobs.Add(new LevelMob(2, 2, "Guard", new Vector3(-30, 24, 0)));
            mobs.Add(new LevelMob(2, 2, "Guard", new Vector3(-22, 22, 0)));
            mobs.Add(new LevelMob(2, 2, "mob", new Vector3(-27, 18, 0)));

        }

        if (Level == 3)
        {
            Waves = 1;
            mobs.Add(new LevelMob(3, 0, "Guard", new Vector3(0, 10, 0)));
            mobs.Add(new LevelMob(3, 0, "Guard", new Vector3(-12, 7, 0)));
            mobs.Add(new LevelMob(3, 0, "Guard", new Vector3(12, 7, 0)));
            mobs.Add(new LevelMob(3, 0, "Guard", new Vector3(10, 8, 0)));
            mobs.Add(new LevelMob(3, 0, "Guard", new Vector3(-10, 8, 0)));
            mobs.Add(new LevelMob(3, 0, "Guard", new Vector3(7.5f, 16, 0)));
            mobs.Add(new LevelMob(3, 0, "Guard", new Vector3(-7.5f, 16, 0)));
            mobs.Add(new LevelMob(3, 0, "Guard", new Vector3(0, 17, 0)));
            mobs.Add(new LevelMob(3, 0, "Wizard", new Vector3(-13, 21, 0)));
            mobs.Add(new LevelMob(3, 0, "Wizard", new Vector3(13, 21, 0)));
            mobs.Add(new LevelMob(3, 0, "Wizard", new Vector3(-1, 25, 0)));
            mobs.Add(new LevelMob(3, 0, "Wizard", new Vector3(-3, 25, 0)));
            mobs.Add(new LevelMob(3, 0, "Wizard", new Vector3(3, 25, 0)));
            mobs.Add(new LevelMob(3, 0, "Wizard", new Vector3(1, 25, 0)));


        }

    }


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

    struct LevelMob{
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



}
