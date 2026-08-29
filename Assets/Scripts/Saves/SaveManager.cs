using System;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    private string SavePath => Application.persistentDataPath + "/save.json";

    public void Save(PlayerSaveData data)
    {
        string json = JsonUtility.ToJson(data);
        System.IO.File.WriteAllText(SavePath, json);
    }

    public PlayerSaveData Load()
    {
        if (!System.IO.File.Exists(SavePath))
        {
            return new PlayerSaveData();
        }
        string json = System.IO.File.ReadAllText(SavePath);
        return JsonUtility.FromJson<PlayerSaveData>(json);
    }

    public void DeleteSave()
    {
        if (System.IO.File.Exists(SavePath))
        {
            System.IO.File.Delete(SavePath);
        }
    }

    public void SaveScore(float currentScore)
    {
        PlayerSaveData saveData = Load();
        saveData.score = (int)currentScore;
        Save(saveData);
    }
}
