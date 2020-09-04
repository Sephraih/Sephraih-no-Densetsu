
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// a script attached to every character object, defining it's status and status conditions such as slow or burn
public class StatusController : MonoBehaviour
{
    public int teamID =0;
    //Stats, to be defined in the editor for each character or character prefab
    public int atk; //melee attack stat
    public int matk; //magical attack stat
    public int aspd; // attack speed stat


    public float mvspd; // current movement speed
    public float dmvspd; // default movement speed for an object, set in inspector

    public int crit; //critical strike chance, currently unused

    private int slows = 0; // counter to reset movement speed only when no slows are applied
    private int mvspdBoni = 0; // opposite of slows

    //Status Effects
    public GameObject burnEffect; // prefab of a burning effect, displayed when suffering from the burning status condition

    public bool AIControlled = false;

    public void Start()
    {
        mvspd = dmvspd; // setting the current speed to the default
    }
    public void Update()
    {

    }

    // burn the character for a total amount of damage over a certain time, slowing it for the duration
    public void Burn(float totalDmg, float time, Transform dmger)
    {
        StartCoroutine(DoTCoroutine(totalDmg, time,dmger));
    }

    // slow the character's speed to a percentual amount of its default speed for given time
    public void Slow(float slowAmt, float time) {
        StartCoroutine(SlowCoroutine(slowAmt, time));
    }
    
    public void IncreaseMovementSpeed(float mvspdUpAmt, float time) {
        StartCoroutine(MvspdUpCoroutine(mvspdUpAmt, time));
    }


    // coroutines make it possible to have events last for a period of time opposed to finishing with a method that must terminate in a single and each frame
    IEnumerator DoTCoroutine(float dmg, float time, Transform dmger)
    {
        float amountDamaged = 0;
        GameObject burn = Instantiate(burnEffect, transform.position, Quaternion.Euler(-90f, 0f,0f));
        burn.transform.parent = transform;

        Destroy(burn, time);
        while (amountDamaged < dmg)
        {
            // apply dmg dmg/time
            this.GetComponent<HealthController>().TakeDamage((int)(dmg / time),dmger);
            amountDamaged += dmg / time;
            yield return new WaitForSeconds(1f);
        }
    }

   
    IEnumerator SlowCoroutine(float slow, float time)
    {
        slows++; // counter to be aware of multiple slows and know when to revert to default movement speed
        float count = 0.0f;
        while (count < time)
        {
            mvspd = slow * dmvspd;
            count += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
        slows --;
        if(slows==0 && mvspdBoni == 0) mvspd = dmvspd; // reset to default movement speed if not slowed / buffed


    }
    // inverted equal to slow coroutine, having duplicate code here improves simplicity
    IEnumerator MvspdUpCoroutine(float mvspdUp, float time)
    {
        mvspdBoni++;
        float count = 0.0f;
        while (count < time)
        {
            mvspd = mvspdUp * dmvspd;
            count += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
        mvspdBoni--;
        if (mvspdBoni == 0 && slows == 0) mvspd = dmvspd; 


    }





}

