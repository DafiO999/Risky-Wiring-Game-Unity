using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class WireView : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly WireConnection[] CardinalConnections =
    {
        WireConnection.North,
        WireConnection.East,
        WireConnection.South,
        WireConnection.West
    };

    [SerializeField]
    private GameObject center;

    [SerializeField]
    private GameObject northArm;

    [SerializeField]
    private GameObject eastArm;

    [SerializeField]
    private GameObject southArm;

    [SerializeField]
    private GameObject westArm;

    [Header("Power Visual")]
    [SerializeField]
    [Tooltip("Renderers tinted from the CircuitSystem power state. If empty, child renderers are found automatically.")]
    private Renderer[] targetRenderers;

    [SerializeField]
    private Color inactiveColor = new(0.16f, 0.18f, 0.2f, 1f);

    [SerializeField]
    private Color activeColor = new(0.15f, 1f, 0.75f, 1f);

    [SerializeField, HideInInspector]
    private Vector2Int gridCell;

    [SerializeField, HideInInspector]
    private bool hasCenter;

    [SerializeField, HideInInspector]
    private WireConnection connections;

    [SerializeField, HideInInspector]
    private int netId = CircuitSystem.NoNetId;

    [SerializeField, HideInInspector]
    private int availablePower;

    private MaterialPropertyBlock propertyBlock;
    private readonly Dictionary<Highlight, Color> normalOutlineColors = new();
    private readonly Dictionary<Material, Material> previewMaterials = new();
    private bool previewMode;
    private bool previewRenderingConfigured;
    private bool previewShowsCenter;
    private WireConnection previewArms;
    private Color previewColor;
    private bool interactionHighlightsCenter;
    private WireConnection interactionHighlightedArms;
    private Color interactionHighlightColor;

    public Vector2Int GridCell => gridCell;
    public WireConnection Connections => connections;
    public int NetId => netId;
    public int AvailablePower => availablePower;
    public bool IsPowered => availablePower > 0;

    public event Action<bool> PowerStateChanged;

    private void Awake()
    {
        EnsureRenderers();
        ApplyConnections();
        ApplyPowerVisual();
    }

    private void OnEnable()
    {
        EnsureRenderers();
        ApplyConnections();
        ApplyPowerVisual();
    }

    private void OnValidate()
    {
        EnsureRenderers();
        ApplyConnections();
        ApplyPowerVisual();
    }

    private void OnDestroy()
    {
        foreach (Material previewMaterial in previewMaterials.Values)
        {
            if (previewMaterial == null)
                continue;

            if (Application.isPlaying)
                Destroy(previewMaterial);
            else
                DestroyImmediate(previewMaterial);
        }

        previewMaterials.Clear();
    }

    public void SetState(
        Vector2Int cell,
        bool centerPlaced,
        WireConnection wireConnections)
    {
        previewMode = false;
        gridCell = cell;
        hasCenter = centerPlaced;
        connections = wireConnections;
        ApplyConnections();
        ApplyPowerVisual();
    }

    /// <summary>
    /// Configures this view as a non-interactive placement preview. Preview arms
    /// can be shown without a center so two views can form one complete edge.
    /// </summary>
    public void SetPreviewState(
        bool showCenter,
        WireConnection visibleArms,
        Color color)
    {
        previewMode = true;
        previewShowsCenter = showCenter;
        previewArms = visibleArms;
        previewColor = color;
        interactionHighlightsCenter = false;
        interactionHighlightedArms = WireConnection.None;

        ConfigurePreviewRendering();
        SetPointerInteractionEnabled(false);
        foreach (Collider targetCollider in GetComponentsInChildren<Collider>(true))
            targetCollider.enabled = false;

        ApplyConnections();
        ApplyPowerVisual();
        ApplyOutlineVisual();
    }

    /// <summary>
    /// Highlights only the live center and arms affected by the current wire
    /// edit target. The logical wire and its power state are left unchanged.
    /// </summary>
    public void SetInteractionHighlight(
        bool highlightCenter,
        WireConnection highlightedArms,
        Color color)
    {
        if (previewMode)
            return;

        interactionHighlightsCenter = highlightCenter;
        interactionHighlightedArms = highlightedArms;
        interactionHighlightColor = color;
        ApplyPowerVisual();
        ApplyOutlineVisual();
    }

    public void ClearInteractionHighlight()
    {
        if (previewMode)
            return;

        interactionHighlightsCenter = false;
        interactionHighlightedArms = WireConnection.None;
        ApplyPowerVisual();
        ApplyOutlineVisual();
    }

    /// <summary>
    /// Lets GridBoard own logical center/edge hover instead of each half-arm
    /// responding independently to its collider.
    /// </summary>
    public void SetPointerInteractionEnabled(bool enabled)
    {
        foreach (InteractionHighlight interaction in
                 GetComponentsInChildren<InteractionHighlight>(true))
        {
            interaction.enabled = enabled;
        }
    }

    /// <summary>
    /// Applies power already calculated for this cell's electrical net. WireView
    /// never derives or propagates electricity itself.
    /// </summary>
    public void SetPowerState(int circuitNetId, int netAvailablePower)
    {
        bool wasPowered = IsPowered;
        netId = circuitNetId;
        availablePower = Mathf.Max(0, netAvailablePower);

        bool isPowered = IsPowered;
        if (isPowered == wasPowered)
            return;

        ApplyPowerVisual();
        PowerStateChanged?.Invoke(isPowered);
    }

    public bool IsArmActive(WireConnection direction)
    {
        GameObject arm = GetArm(direction);
        return arm != null && arm.activeSelf;
    }

    private void ApplyConnections()
    {
        WireConnection visibleConnections = previewMode ? previewArms : connections;
        SetPartActive(
            center,
            previewMode ? previewShowsCenter : hasCenter);
        SetPartActive(
            northArm,
            HasConnection(visibleConnections, WireConnection.North));
        SetPartActive(
            eastArm,
            HasConnection(visibleConnections, WireConnection.East));
        SetPartActive(
            southArm,
            HasConnection(visibleConnections, WireConnection.South));
        SetPartActive(
            westArm,
            HasConnection(visibleConnections, WireConnection.West));
    }

    private bool HasConnection(WireConnection connection)
    {
        return HasConnection(connections, connection);
    }

    private static bool HasConnection(
        WireConnection availableConnections,
        WireConnection connection)
    {
        return (availableConnections & connection) != 0;
    }

    private void EnsureRenderers()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void ConfigurePreviewRendering()
    {
        if (previewRenderingConfigured)
            return;

        EnsureRenderers();
        foreach (Renderer targetRenderer in targetRenderers)
        {
            if (targetRenderer == null)
                continue;

            Material[] materials = targetRenderer.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
            {
                Material source = materials[index];
                if (source == null)
                    continue;

                if (!previewMaterials.TryGetValue(source, out Material previewMaterial))
                {
                    previewMaterial = new Material(source)
                    {
                        name = $"{source.name} - Wire Ghost (Runtime)",
                        hideFlags = HideFlags.HideAndDontSave,
                        renderQueue = (int)RenderQueue.Transparent
                    };
                    ConfigureTransparentMaterial(previewMaterial);
                    previewMaterials.Add(source, previewMaterial);
                }

                materials[index] = previewMaterial;
            }

            targetRenderer.sharedMaterials = materials;
            targetRenderer.shadowCastingMode = ShadowCastingMode.Off;
            targetRenderer.receiveShadows = false;
            targetRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
        }

        previewRenderingConfigured = true;
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");
        SetMaterialFloat(material, "_Surface", 1f);
        SetMaterialFloat(material, "_Blend", 0f);
        SetMaterialFloat(material, "_AlphaClip", 0f);
        SetMaterialFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
        SetMaterialFloat(
            material,
            "_DstBlend",
            (float)BlendMode.OneMinusSrcAlpha);
        SetMaterialFloat(material, "_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
    }

    private static void SetMaterialFloat(
        Material material,
        string propertyName,
        float value)
    {
        if (material.HasProperty(propertyName))
            material.SetFloat(propertyName, value);
    }

    private void ApplyPowerVisual()
    {
        EnsureRenderers();
        propertyBlock ??= new MaterialPropertyBlock();
        Color color = previewMode
            ? previewColor
            : IsPowered ? activeColor : inactiveColor;

        foreach (Renderer targetRenderer in targetRenderers)
            ApplyRendererColor(targetRenderer, color);

        if (previewMode)
            return;

        if (interactionHighlightsCenter)
            ApplyPartColor(center, interactionHighlightColor);

        foreach (WireConnection connection in CardinalConnections)
        {
            if (HasConnection(interactionHighlightedArms, connection))
                ApplyPartColor(GetArm(connection), interactionHighlightColor);
        }
    }

    private void ApplyPartColor(GameObject part, Color color)
    {
        if (part == null)
            return;

        foreach (Renderer partRenderer in part.GetComponentsInChildren<Renderer>(true))
            ApplyRendererColor(partRenderer, color);
    }

    private void ApplyRendererColor(Renderer targetRenderer, Color color)
    {
        if (targetRenderer == null)
            return;

        targetRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, color);
        propertyBlock.SetColor(ColorId, color);
        targetRenderer.SetPropertyBlock(propertyBlock);
    }

    private void ApplyOutlineVisual()
    {
        CacheNormalOutlineColors();
        ApplyPartOutline(
            center,
            previewMode
                ? previewShowsCenter
                : interactionHighlightsCenter,
            previewMode ? previewColor : interactionHighlightColor);

        foreach (WireConnection connection in CardinalConnections)
        {
            bool highlighted = previewMode
                ? HasConnection(previewArms, connection)
                : HasConnection(interactionHighlightedArms, connection);
            ApplyPartOutline(
                GetArm(connection),
                highlighted,
                previewMode ? previewColor : interactionHighlightColor);
        }
    }

    private void CacheNormalOutlineColors()
    {
        CacheNormalOutlineColor(center);
        CacheNormalOutlineColor(northArm);
        CacheNormalOutlineColor(eastArm);
        CacheNormalOutlineColor(southArm);
        CacheNormalOutlineColor(westArm);
    }

    private void CacheNormalOutlineColor(GameObject part)
    {
        Highlight highlight = part != null ? part.GetComponent<Highlight>() : null;
        if (highlight != null && !normalOutlineColors.ContainsKey(highlight))
            normalOutlineColors.Add(highlight, highlight.OutlineColor);
    }

    private void ApplyPartOutline(GameObject part, bool highlighted, Color color)
    {
        Highlight highlight = part != null ? part.GetComponent<Highlight>() : null;
        if (highlight == null)
            return;

        Color targetColor = highlighted
            ? color
            : normalOutlineColors[highlight];
        highlight.SetOutlineColor(targetColor);
    }

    private GameObject GetArm(WireConnection direction)
    {
        return direction switch
        {
            WireConnection.North => northArm,
            WireConnection.East => eastArm,
            WireConnection.South => southArm,
            WireConnection.West => westArm,
            _ => null
        };
    }

    private static void SetPartActive(GameObject part, bool active)
    {
        if (part != null && part.activeSelf != active)
            part.SetActive(active);
    }
}
