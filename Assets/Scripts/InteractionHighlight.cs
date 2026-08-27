using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(Highlight))]
public sealed class InteractionHighlight : MonoBehaviour
{
    [Header("Hover")]
    [SerializeField, ColorUsage(true, true)]
    private Color hoverColor = new(1f, 0.45f, 0f, 1f);

    [Header("Raycast")]
    [SerializeField]
    [Tooltip("Camera used to cast from the cursor. If empty, the Main Camera is used.")]
    private Camera raycastCamera;

    [SerializeField]
    [Tooltip("Only colliders on these layers can be detected by the cursor raycast.")]
    private LayerMask hoverLayers = Physics.DefaultRaycastLayers;

    [SerializeField, Min(0f)]
    private float maximumDistance = 1000f;

    [SerializeField]
    private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal;

    [SerializeField]
    [Tooltip("Treat colliders on child objects as part of this highlighted object.")]
    private bool includeChildColliders = true;

    private Highlight highlight;
    private Color normalColor;
    private bool isHovered;

    private void Awake()
    {
        highlight = GetComponent<Highlight>();
    }

    private void OnEnable()
    {
        if (highlight == null)
            highlight = GetComponent<Highlight>();

        normalColor = highlight.OutlineColor;
        isHovered = false;
    }

    private void Update()
    {
        SetHovered(IsCursorOverObject());
    }

    private void OnDisable()
    {
        SetHovered(false);
    }

    private void OnValidate()
    {
        maximumDistance = Mathf.Max(0f, maximumDistance);
    }

    private bool IsCursorOverObject()
    {
        Mouse mouse = Mouse.current;
        Camera cameraToUse = raycastCamera != null ? raycastCamera : Camera.main;
        if (mouse == null || cameraToUse == null)
            return false;

        Ray ray = cameraToUse.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                maximumDistance,
                hoverLayers,
                triggerInteraction))
        {
            return false;
        }

        Transform hitTransform = hit.collider.transform;
        return hitTransform == transform ||
               (includeChildColliders && hitTransform.IsChildOf(transform));
    }

    private void SetHovered(bool value)
    {
        if (isHovered == value || highlight == null)
            return;

        isHovered = value;
        highlight.SetOutlineColor(isHovered ? hoverColor : normalColor);
    }
}
