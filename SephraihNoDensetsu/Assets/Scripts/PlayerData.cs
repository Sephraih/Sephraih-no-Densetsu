using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class PlayerData
{
    public int lvl;
    public int Str;
    public int Int;
    public int Agi;
    public int Vit;
    public float[] pos;

    public PlayerData(Transform player) {

        StatusController stats = player.GetComponent<StatusController>();
        lvl = stats.lvl;
        Str = stats.Str;
        Int = stats.Int;
        Agi = stats.Agi;
        Vit = stats.Vit;

        pos = new float[2];
        pos[0] = player.position.x;
        pos[1] = player.position.y;
    }
}
