using System;
using UnityEngine;

[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class WireView : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

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
    private WireConnection connections;

    [SerializeField, HideInInspector]
    private int netId = CircuitSystem.NoNetId;

    [SerializeField, HideInInspector]
    private int availablePower;

    private MaterialPropertyBlock propertyBlock;

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

    public void SetState(Vector2Int cell, WireConnection wireConnections)
    {
        gridCell = cell;
        connections = wireConnections;
        ApplyConnections();
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
        ApplyPowerVisual();

        if (IsPowered != wasPowered)
            PowerStateChanged?.Invoke(IsPowered);
    }

    public bool IsArmActive(WireConnection direction)
    {
        GameObject arm = GetArm(direction);
        return arm != null && arm.activeSelf;
    }

    private void ApplyConnections()
    {
        SetPartActive(center, connections != WireConnection.None);
        SetPartActive(northArm, HasConnection(WireConnection.North));
        SetPartActive(eastArm, HasConnection(WireConnection.East));
        SetPartActive(southArm, HasConnection(WireConnection.South));
        SetPartActive(westArm, HasConnection(WireConnection.West));
    }

    private bool HasConnection(WireConnection connection)
    {
        return (connections & connection) != 0;
    }

    private void EnsureRenderers()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void ApplyPowerVisual()
    {
        EnsureRenderers();
        propertyBlock ??= new MaterialPropertyBlock();
        Color color = IsPowered ? activeColor : inactiveColor;

        foreach (Renderer targetRenderer in targetRenderers)
        {
            if (targetRenderer == null)
                continue;

            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }
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
