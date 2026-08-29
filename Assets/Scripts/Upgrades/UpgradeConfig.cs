using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Upgrades/Upgrade Config")]
public class UpgradeConfig : ScriptableObject
{
    public UpgradeId id;

    public string UpgradeName;

    public List<UpgradeLevel> levels;
}

[Serializable]
public class UpgradeLevel
{
    public int price;

    public int value;
}
