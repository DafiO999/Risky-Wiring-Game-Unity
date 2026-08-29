using System.Globalization;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class BatteryChargeDisplayView : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Battery whose runtime charge and capacity are displayed. If empty, a parent BatteryComponent is used.")]
    private BatteryComponent battery;

    [SerializeField]
    [Tooltip("World-space UI text updated from BatteryComponent state.")]
    private TMP_Text valueText;

    [SerializeField]
    private string prefix = "Charge: ";

    [SerializeField]
    private string separator = " / ";

    [SerializeField, Range(0, 3)]
    private int decimalPlaces = 1;

    private BatteryComponent subscribedBattery;

    public BatteryComponent Battery => ResolveBattery();
    public TMP_Text ValueText => ResolveValueText();
    public float DisplayedCharge { get; private set; }
    public float DisplayedCapacity { get; private set; }
    public string DisplayText { get; private set; } = string.Empty;

    private void OnEnable()
    {
        BindBattery();
        Refresh();
    }

    private void OnDisable()
    {
        UnbindBattery();
    }

    private void OnValidate()
    {
        decimalPlaces = Mathf.Clamp(decimalPlaces, 0, 3);
        BindBattery();
        Refresh();
    }

    public void Refresh()
    {
        BatteryComponent target = ResolveBattery();
        TMP_Text targetText = ResolveValueText();
        DisplayedCharge = target != null ? target.Charge : 0f;
        DisplayedCapacity = target != null ? target.Capacity : 0f;

        if (target == null)
        {
            DisplayText = $"{prefix}--{separator}--";
        }
        else
        {
            string format = decimalPlaces > 0
                ? "0." + new string('0', decimalPlaces)
                : "0";
            DisplayText =
                prefix +
                DisplayedCharge.ToString(format, CultureInfo.InvariantCulture) +
                separator +
                DisplayedCapacity.ToString(format, CultureInfo.InvariantCulture);
        }

        if (targetText != null)
            targetText.text = DisplayText;
    }

    private void BindBattery()
    {
        BatteryComponent target = ResolveBattery();
        if (subscribedBattery == target)
            return;

        UnbindBattery();
        subscribedBattery = target;
        if (subscribedBattery != null)
            subscribedBattery.StateChanged += Refresh;
    }

    private void UnbindBattery()
    {
        if (subscribedBattery != null)
            subscribedBattery.StateChanged -= Refresh;

        subscribedBattery = null;
    }

    private BatteryComponent ResolveBattery()
    {
        if (battery == null)
            battery = GetComponentInParent<BatteryComponent>();

        return battery;
    }

    private TMP_Text ResolveValueText()
    {
        if (valueText == null)
            valueText = GetComponentInChildren<TMP_Text>(true);

        return valueText;
    }
}
