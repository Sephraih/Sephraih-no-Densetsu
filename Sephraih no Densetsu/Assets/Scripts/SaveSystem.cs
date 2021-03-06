﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

public static class SaveSystem
{

    public static void SavePlayer(Transform player)
    {

        BinaryFormatter formatter = new BinaryFormatter();
        string name = "Link";
        string path = Application.persistentDataPath + "/" + name + "/player.stats";
        FileStream stream = new FileStream(path, FileMode.Create);

        PlayerData data = new PlayerData(player);
        
        formatter.Serialize(stream, data);
        stream.Close();
    }

    public static PlayerData LoadPlayer(string name)
    {

        string path = Application.persistentDataPath + "/" + name + "/player.stats";
        if (File.Exists(path))
        {
            BinaryFormatter formatter = new BinaryFormatter();
            FileStream stream = new FileStream(path, FileMode.Open);

            PlayerData data = formatter.Deserialize(stream) as PlayerData;
            stream.Close();
            return data;
        }
        else
        {
            Debug.LogError("Save File not found" + path);
            return null;
        }


    }

}
