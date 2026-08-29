using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class GeneratorPowerDisplayView : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Generator whose runtime output value is displayed. If empty, a parent GeneratorComponent is used.")]
    private GeneratorComponent generator;

    [SerializeField]
    [Tooltip("World-space UI text updated from GeneratorComponent state.")]
    private TMP_Text valueText;

    [SerializeField]
    private string prefix = "Power: ";

    private GeneratorComponent subscribedGenerator;

    public GeneratorComponent Generator => ResolveGenerator();
    public TMP_Text ValueText => ResolveValueText();
    public int DisplayedPower { get; private set; }
    public string DisplayText { get; private set; } = string.Empty;

    private void OnEnable()
    {
        BindGenerator();
        Refresh();
    }

    private void OnDisable()
    {
        UnbindGenerator();
    }

    private void OnValidate()
    {
        BindGenerator();
        Refresh();
    }

    public void Refresh()
    {
        GeneratorComponent target = ResolveGenerator();
        TMP_Text targetText = ResolveValueText();
        DisplayedPower = target != null
            ? target.PowerPerActiveOutput
            : 0;
        DisplayText = target != null
            ? $"{prefix}{DisplayedPower}"
            : $"{prefix}--";

        if (targetText != null)
            targetText.text = DisplayText;
    }

    private void BindGenerator()
    {
        GeneratorComponent target = ResolveGenerator();
        if (subscribedGenerator == target)
            return;

        UnbindGenerator();
        subscribedGenerator = target;
        if (subscribedGenerator != null)
            subscribedGenerator.PowerPerActiveOutputChanged += HandlePowerChanged;
    }

    private void UnbindGenerator()
    {
        if (subscribedGenerator != null)
        {
            subscribedGenerator.PowerPerActiveOutputChanged -=
                HandlePowerChanged;
        }

        subscribedGenerator = null;
    }

    private void HandlePowerChanged(int _)
    {
        Refresh();
    }

    private GeneratorComponent ResolveGenerator()
    {
        if (generator == null)
            generator = GetComponentInParent<GeneratorComponent>();

        return generator;
    }

    private TMP_Text ResolveValueText()
    {
        if (valueText == null)
            valueText = GetComponentInChildren<TMP_Text>(true);

        return valueText;
    }
}
