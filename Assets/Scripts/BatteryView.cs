using UnityEngine;

[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class BatteryView : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [SerializeField]
    private BatteryComponent battery;

    [SerializeField]
    [Tooltip("Renderers tinted from battery charge and power flow. If empty, child renderers are found automatically.")]
    private Renderer[] targetRenderers;

    [SerializeField]
    [Tooltip("Optional world-space text used to show charge, input, and output at runtime.")]
    private TextMesh statusText;

    [Header("Battery Colors")]
    [SerializeField]
    private Color emptyColor = new(0.16f, 0.18f, 0.2f, 1f);

    [SerializeField]
    private Color chargedColor = new(0.2f, 0.85f, 1f, 1f);

    [SerializeField]
    private Color chargingColor = new(0.2f, 1f, 0.35f, 1f);

    [SerializeField]
    private Color dischargingColor = new(1f, 0.62f, 0.08f, 1f);

    private MaterialPropertyBlock propertyBlock;

    public BatteryComponent Battery => ResolveBattery();
    public BatteryPowerState DisplayedState { get; private set; }
    public float DisplayedChargeNormalized { get; private set; }
    public string RuntimeStatus { get; private set; } = string.Empty;

    private void OnEnable()
    {
        BatteryComponent target = ResolveBattery();
        if (target != null)
            target.StateChanged += Refresh;

        EnsureRenderers();
        Refresh();
    }

    private void OnDisable()
    {
        if (battery != null)
            battery.StateChanged -= Refresh;
    }

    private void OnValidate()
    {
        ResolveBattery();
        EnsureRenderers();
        Refresh();
    }

    public void Refresh()
    {
        BatteryComponent target = ResolveBattery();
        DisplayedState = target != null
            ? target.PowerState
            : BatteryPowerState.Empty;
        DisplayedChargeNormalized = target != null
            ? target.ChargeNormalized
            : 0f;
        RuntimeStatus = target != null
            ? $"{target.Charge:0.##}/{target.Capacity:0.##}  In {target.ReceivedInputPower}  Out {target.ProvidedOutputPower}"
            : "--/--  In --  Out --";

        if (statusText != null)
            statusText.text = RuntimeStatus;

        Color color = Color.Lerp(
            emptyColor,
            chargedColor,
            DisplayedChargeNormalized);
        color = DisplayedState switch
        {
            BatteryPowerState.Charging => Color.Lerp(color, chargingColor, 0.7f),
            BatteryPowerState.Discharging => Color.Lerp(color, dischargingColor, 0.7f),
            BatteryPowerState.ChargingAndDischarging => Color.Lerp(chargingColor, dischargingColor, 0.5f),
            BatteryPowerState.Empty => emptyColor,
            _ => color
        };

        propertyBlock ??= new MaterialPropertyBlock();
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

    private BatteryComponent ResolveBattery()
    {
        if (battery == null)
            battery = GetComponent<BatteryComponent>();

        return battery;
    }

    private void EnsureRenderers()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<Renderer>(true);
    }
}
