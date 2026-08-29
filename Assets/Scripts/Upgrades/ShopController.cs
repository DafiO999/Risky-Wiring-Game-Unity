using UnityEngine;
using System.Collections.Generic;
using System;

public class ShopController : MonoBehaviour
{
    [SerializeField]
    private ScoreSystem scoreSystem;

    [SerializeField]
    private List<UpgradeConfig> configs;

    private Dictionary<UpgradeId, int> currentLevels = new();

    [SerializeField]
    private SaveManager saveManager;

    private void Awake()
    {
        foreach (UpgradeConfig config in configs)
        {
            currentLevels[config.id] = 0;
        }

        LoadUpgrades();
    }

    public int GetLevel(UpgradeId id)
    {
        return currentLevels[id];
    }

    public int GetValue(UpgradeId id)
    {
        UpgradeConfig config = GetConfig(id);

        int level = GetLevel(id);

        return config.levels[level].value;
    }

    public bool IsMaxLevel(UpgradeId id)
    {
        UpgradeConfig config = GetConfig(id);

        return GetLevel(id) >= config.levels.Count - 1;
    }

    public int GetNextPrice(UpgradeId id)
    {
        if (IsMaxLevel(id)) return 0;

        UpgradeConfig config = GetConfig(id);

        return config.levels[GetLevel(id) + 1].price;
    }

    public bool TryUpgrade(UpgradeId id)
    {
        if (IsMaxLevel(id))
            return false;

        UpgradeConfig config = GetConfig(id);

        int nextLevel = GetLevel(id) + 1;
        int price = config.levels[nextLevel].price;

        if (!CanAfford(price))
            return false;

        SpendCurrency(price);

        currentLevels[id] = nextLevel;

        SaveUpgrades();

        return true;
    }

    public bool CanAfford(int price)
    {
        if (scoreSystem.CurrentScore < price) return false;

        return true;
    }

    public void SpendCurrency(int price)
    {
        scoreSystem.RemoveScore(price);
    }


    private UpgradeConfig GetConfig(UpgradeId id)
    {
        return configs.Find(config  => config.id == id);
    }

    private void SaveUpgrades()
    {
        PlayerSaveData saveData = new PlayerSaveData();
        saveData.score = (int)scoreSystem.CurrentScore;

        foreach (var kvp in currentLevels)
        {
            saveData.upgrades.Add(new UpgradeSaveData { id = kvp.Key, level = kvp.Value });
        }

        saveManager.Save(saveData);
    }

    private void LoadUpgrades()
    {
        PlayerSaveData saveData = saveManager.Load();
        scoreSystem.SetScore(saveData.score);

        foreach (UpgradeSaveData upgradeData in saveData.upgrades)
        {
            currentLevels[upgradeData.id] = upgradeData.level;
        }
    }
}
