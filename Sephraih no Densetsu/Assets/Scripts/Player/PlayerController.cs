using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using UnityEngine;
using UnityEngine.Tilemaps;

// the character controller of character one (and three, which is an identical copy)
public class PlayerController : UnitController
{


    protected Transform enemy;
     

    // called once
    public void Start()
    {
        attackingDirection.transform.localPosition = new Vector2(0, -0.5f); // set an attacking direction before the player moves for the first time
        enemy = Camera.main.GetComponent<GameBehaviour>().ClosestEnemy(transform); //get closest enemy inside arena
        Camera.main.GetComponent<GameBehaviour>().Register(transform);
        GetComponent<StatusController>().teamID = teamID;
        saveSpot = Vector3.zero;
    }

    // called each frame
    public void Update()
    {
        if (!GetComponent<MovementController>().stunned)
        {
            Move();
            Skills();
            Aim();
            Attack();
        }
        SaveLoad();
        Reset();
    }

    //process movement input
    public override void Move()
    {
        // movement based on input
        movementDirection = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        movementDirection.Normalize();
        msi = Mathf.Clamp(movementDirection.magnitude, 0.0f, 1.0f);
        GetComponent<MovementController>().Move(movementDirection, msi);
    }
       

    void Reset()
    {
        if (GetComponent<HealthController>().health <= 0)
        {
            GetComponent<HealthController>().Max();
        }
    }


    public void SaveLoad()
    {
        if (Input.GetButtonUp("Load"))
        {

            Debug.Log("loading");
            Load();
        }

        if (Input.GetButtonUp("Save"))
        {

            Debug.Log("saving");
            Save();
        }

    }
    public void Save()
    {
        SaveSystem.SavePlayer(transform);
        Debug.Log("saved");
    }

    public void Load()
    {
        PlayerData data = SaveSystem.LoadPlayer("Link");

        StatusController stats = GetComponent<StatusController>();
        stats.lvl = data.lvl;
        stats.Str = data.Str;
        stats.Int = data.Int;
        stats.Agi = data.Agi;
        stats.Vit = data.Vit;

        //transform.position = new Vector3(data.pos[0], data.pos[1], 0);
        
        Debug.Log("loaded");
    }

  
  


}