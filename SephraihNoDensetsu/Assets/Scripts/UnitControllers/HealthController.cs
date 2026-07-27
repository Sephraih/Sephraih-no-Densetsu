using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HealthController : MonoBehaviour
{
    //the script is attached to all character objects.

    public int maxHealth = 2000; // maximal health the number here is default, overwritten in inspector
    public int health = 2000; // current health - default set to avoid errors
    private GameObject dmgText; // a damage text prefab to be displayed when the character takes damage
    private GameObject healText; // a text prefab to display the amount of health recovered as a number
    private GameObject bloodEffect; //a blood effect spawned by the character when damage is taken
    private GameObject healedEffect; // a recovery effect
    private float htSpawnCount = 0; // a counter to influence the spawn position relative to the character of any health change numbers

    public GameObject ht; // health text variable, this can be a heal or dmg text

    // Fires exactly once when health crosses from >0 to <=0 - subscribers (e.g. EnemyController)
    // react to this instead of polling health every frame. dmger is passed through so a death
    // handler can know who landed the killing blow.
    public event System.Action<Transform> OnDeath;
    public bool isDead { get; private set; }

    void Start()
    {
        maxHealth = 1000 *(GetComponent<StatusController>().lvl + transform.GetComponent<StatusController>().Vit);
        Max(); // initialize character to start at max health

        //loading prefabs to be instantiated later
        bloodEffect = Resources.Load("Prefabs/Effects/BloodEffectPrefab") as GameObject;
        healedEffect = Resources.Load("Prefabs/Effects/HealedEffectPrefab") as GameObject;

        dmgText = Resources.Load("Prefabs/TextEffects/DmgTextPrefab") as GameObject;
        healText = Resources.Load("Prefabs/TextEffects/HealTextPrefab") as GameObject;

    }
    private void Update()
    {
        maxHealth = 1000 * (GetComponent<StatusController>().lvl + transform.GetComponent<StatusController>().Vit);
    }

    // take damage, display number and blood effect
    public void TakeDamage(int damage, Transform dmger)
    {
        if (isDead) return; // already dead - ignore further hits (e.g. two hits landing the same frame)

        GameObject blood = Instantiate(bloodEffect, transform.position, Quaternion.identity); // at character's position without any rotation
        blood.transform.parent = transform; // make the effect child of the character to let the effect follow it
        Destroy(blood, 0.7f);

        ShowDamageText(damage);
        bool wasAlive = health > 0;
        health -= damage;
        if (health < 0) { health = 0; }

        //Debug.Log("took dmg" + damage);

        // Detected BEFORE the clamp above so an overkill hit (damage far exceeding remaining
        // health) still fires this exactly once, not skipped because health "landed" past 0.
        if (wasAlive && health <= 0)
        {
            isDead = true;
            OnDeath?.Invoke(dmger);
        }
    }

    // recover damage, display number and recovery effect
    public void Heal(int heal, Transform healer)
    {

       
        GameObject hef = Instantiate(healedEffect, transform.position, Quaternion.identity);
        hef.transform.parent = transform;
        Destroy(hef, 1.0f);

        if (health < maxHealth) { health += heal; }
        if (health > maxHealth) { health = maxHealth; }
        if (health > 0) isDead = false;
        ShowHealText(heal);
    }

    public void ShowDamageText(int damage)
    {
        if (htSpawnCount > 0.6) htSpawnCount = 0;
        if (dmgText)
        {
            ht = Instantiate(dmgText, transform.position + new Vector3(0, 1, 0), Quaternion.identity);
            ht.GetComponent<TextMesh>().text = damage.ToString();
            ht.transform.localPosition += new Vector3(htSpawnCount, htSpawnCount, 0);
            htSpawnCount += 0.3f;
            Destroy(ht, 2.0f);
        }

    }

    public void ShowHealText(int heal)
    {

        if (htSpawnCount > 0.6) htSpawnCount = 0;
        if (healText)
        {
            ht = Instantiate(healText, transform.position + new Vector3(0, 1, 0), Quaternion.identity);
            ht.GetComponent<TextMesh>().text = heal.ToString();
            ht.transform.localPosition += new Vector3(-htSpawnCount, htSpawnCount, 0);
            htSpawnCount += 0.3f;
            Destroy(ht, 2.0f);
        }

    }

    public void Max()
    {
        health = maxHealth;
        isDead = false;
        ShowHealText(maxHealth);
    }

}