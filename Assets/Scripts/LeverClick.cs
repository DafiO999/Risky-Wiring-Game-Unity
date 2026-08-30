using UnityEngine;
using UnityEngine.InputSystem;

public class LeverClick : MonoBehaviour
{
    [SerializeField]
    private RectTransform shopUI;

    [SerializeField]
    private CameraSwitch cameraSwitch;

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

    private void Update()
    {
        CheckForClick();
    }

    private void CheckForClick()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            return;

        Camera cameraToUse = raycastCamera != null ? raycastCamera : Camera.main;
        if (cameraToUse == null)
            return;

        Ray ray = cameraToUse.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                maximumDistance,
                clickableLayers,
                triggerInteraction))
        {
            return;
        }

        Transform hitTransform = hit.collider.transform;
        bool clickedThisObject = hitTransform == transform ||
                                (includeChildColliders && hitTransform.IsChildOf(transform));

        if (clickedThisObject)
        {
            cameraSwitch.SwitchToGamba(0);
            shopUI.gameObject.SetActive(false);
        }
    }
}
