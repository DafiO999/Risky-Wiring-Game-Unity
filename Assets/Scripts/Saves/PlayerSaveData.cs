using System;
using System.Collections.Generic;

[Serializable]
public class PlayerSaveData
{
    public int score;
    public List<UpgradeSaveData> upgrades = new();

   
}

[Serializable]
public class UpgradeSaveData
{
    public UpgradeId id;
    public int level;
}