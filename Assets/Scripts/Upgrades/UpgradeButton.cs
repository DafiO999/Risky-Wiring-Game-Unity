using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


public class UpgradeButton : MonoBehaviour, IPointerEnterHandler
{
    [SerializeField]
    private UpgradeId upgradeId;

    [SerializeField]
    private ShopController shopController;

    [SerializeField]
    private TextMeshProUGUI priceText;

    [SerializeField]
    private TextMeshProUGUI levelText;

    [SerializeField]
    private Button upgradeButton;

    [SerializeField]
    private TextMeshProUGUI descText;

    [SerializeField]
    private string description;

    private void Start()
    {
        Refresh();
    }

    public void Buy()
    {
        if (shopController.TryUpgrade(upgradeId))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        int level = shopController.GetLevel(upgradeId);
        levelText.text = $"Level: {level}";
        if (shopController.IsMaxLevel(upgradeId))
        {
            priceText.text = "Max Level";
            upgradeButton.interactable = false;
        }
        else
        {
            int nextPrice = shopController.GetNextPrice(upgradeId);
            priceText.text = $"Price: {nextPrice}";
            upgradeButton.interactable = true;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        descText.text = description;
        priceText.text = shopController.IsMaxLevel(upgradeId) ? "Max Level" : $"Price: {shopController.GetNextPrice(upgradeId)}";
    }
}
