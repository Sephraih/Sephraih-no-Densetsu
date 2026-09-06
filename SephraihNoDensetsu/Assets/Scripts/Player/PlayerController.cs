using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using UnityEngine;
using UnityEngine.Tilemaps;

// the character controller of character one (and three, which is an identical copy)
public class PlayerController : UnitController
{


    // called once
    public void Start()
    {
        attackingDirection.transform.localPosition = new Vector2(0, -0.5f); // set an attacking direction before the player moves for the first time
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

    // Actual physics-affecting movement happens here, on the fixed physics timestep, not in Update()
    // (which runs at render framerate). Setting rb.linearVelocity once per Update() call meant it was
    // being reasserted at a rate independent of - and out of sync with - Unity's own physics solver,
    // which only resolves collisions once per FixedUpdate. Holding into a static wall produced a
    // visible wobble: the solver would zero/redirect the velocity component pushing into the wall and
    // nudge the body back out each physics step, but the next Update() (or several, at a higher
    // framerate than the physics step) would blindly reassert full speed into the wall again before
    // the correction ever got a chance to stick, undoing it. Setting velocity exactly once per physics
    // step, right before it's consumed, removes that mismatch.
    public void FixedUpdate()
    {
        if (!GetComponent<MovementController>().stunned)
        {
            GetComponent<MovementController>().Move(movementDirection, msi);
        }
    }

    // reads movement input - the actual velocity assignment happens in FixedUpdate() above, not here,
    // see its comment for why
    public override void Move()
    {
        movementDirection = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        movementDirection.Normalize();
        msi = Mathf.Clamp(movementDirection.magnitude, 0.0f, 1.0f);
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