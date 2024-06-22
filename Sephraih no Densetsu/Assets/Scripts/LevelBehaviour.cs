using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using static UnityEditor.PlayerSettings;

public class LevelBehaviour : MonoBehaviour
{
    private string enemyPath = "Prefabs/Enemies/";
    private int Waves;
    private int CurrentWave;
    private bool cleared = false;

    // Start is called before the first frame update
    void Start()
    {
        Waves = 3; // [0, n-1]
        CurrentWave = 0;
        cleared = false;
        LoadEnemies();

    }

    // Update is called once per frame
    void Update()
    {
        if (Camera.main.GetComponent<GameBehaviour>().characterList.Count == 1 && cleared == false) // 1 enemy or player remaining
        {
            if (CurrentWave == Waves) { StageClear(); return; }
            LoadEnemies();
        }

    //  Debug.Log(Camera.main.GetComponent<GameBehaviour>().characterList.Count);


    }

    public void LoadEnemies() {

        if (CurrentWave == 0)
        {
            InstantiateEnemy("mob", new Vector3(10, 10, 1));
        }

        if (CurrentWave == 1)
        {
            InstantiateEnemy("mob", new Vector3(10, 10, 1));
            InstantiateEnemy("mob", new Vector3(-10, 10, 1));
        }

        if (CurrentWave == 2)
        {
            InstantiateEnemy("mob", new Vector3(10, 10, 1));
            InstantiateEnemy("mob", new Vector3(-10, 10, 1));
            InstantiateEnemy("mob", new Vector3(0, -10, 1));
        }

        CurrentWave++;
    }
    public void StageClear() {

        GameObject a = Instantiate((Resources.Load("Prefabs/GameObjects/Portal") as GameObject), new Vector3(0, 0, 1), Quaternion.identity);
        cleared = true;

    }


    public void InstantiateEnemy(string enemy, Vector3 pos)
    {
        Instantiate((Resources.Load(Path.Combine(enemyPath, enemy)) as GameObject), pos, Quaternion.identity);


    }


    public void LoadNext()
    {
        SceneManager.LoadScene("Main");

    }

}
