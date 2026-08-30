using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;


public class UpgradeButton : MonoBehaviour
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
    private TextMeshProUGUI descText;

    [SerializeField]
    private string description;

    [SerializeField]
    private Highlight highlight;

    [SerializeField]
    private InteractionHighlight interactionHighlight;

    [SerializeField]
    private Camera raycastCamera;

    [SerializeField]
    private LayerMask clickableLayers = Physics.DefaultRaycastLayers;

    [SerializeField, Min(0f)]
    private float maximumDistance = 1000f;

    [SerializeField]
    private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal;

    [SerializeField]
    private bool includeChildColliders = true;

    private bool interactable;

    private void Start()
    {
        Refresh();
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        bool isIt = IsCursorOverObject(mouse);

        OnHover(isIt);

        if (!interactable || mouse == null || !mouse.leftButton.wasPressedThisFrame)
            return;

        CheckForClick(isIt);
    }

    public void Buy()
    {
        if (shopController.TryUpgrade(upgradeId))
        {
            Refresh();
        }
    }

    private void CheckForClick(bool isOverObject)
    {
        if (isOverObject)
            Buy();
    }

    private void OnHover(bool isHovered)
    {
        if (!isHovered) return;
        descText.text = description;
        priceText.text = shopController.IsMaxLevel(upgradeId) ? "Max Level" : $"Price: {shopController.GetNextPrice(upgradeId)}";
    }

    private bool IsCursorOverObject(Mouse mouse)
    {
        Camera cameraToUse = raycastCamera != null ? raycastCamera : Camera.main;
        if (mouse == null || cameraToUse == null)
            return false;

        Ray ray = cameraToUse.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                maximumDistance,
                clickableLayers,
                triggerInteraction))
        {
            return false;
        }

        Transform hitTransform = hit.collider.transform;
        return hitTransform == transform ||
               (includeChildColliders && hitTransform.IsChildOf(transform));
    }



    private void Refresh()
    {
        int level = shopController.GetLevel(upgradeId);
        levelText.text = $"Level: {level}";
        if (shopController.IsMaxLevel(upgradeId))
        {
            priceText.text = "Max Level";
            interactable = false;   
            highlight.enabled = false;
            interactionHighlight.enabled = false;
        }
        else
        {
            int nextPrice = shopController.GetNextPrice(upgradeId);
            priceText.text = $"Price: {nextPrice}";
            interactable = true;
            highlight.enabled = true;
            interactionHighlight.enabled = true;
        }
    }



    
}
