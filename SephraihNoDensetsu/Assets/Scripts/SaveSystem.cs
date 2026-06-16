using System.IO;
using UnityEngine;

public static class SaveSystem
{
    public static void SavePlayer(Transform player)
    {
        string name = "Link";
        string path = Application.persistentDataPath + "/" + name + "/player.json";
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        PlayerData data = new PlayerData(player);
        string json = JsonUtility.ToJson(data, prettyPrint: true);
        File.WriteAllText(path, json);
    }

    public static PlayerData LoadPlayer(string name)
    {
        string path = Application.persistentDataPath + "/" + name + "/player.json";
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<PlayerData>(json);
        }
        else
        {
            Debug.LogError("Save file not found: " + path);
            return null;
        }
    }
}
