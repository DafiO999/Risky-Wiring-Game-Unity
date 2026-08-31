using System.Collections;
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

    [Header("Value Change Animation")]
    [SerializeField, Min(0f)]
    [Tooltip("How long the display rapidly cycles through visual-only values after generator power changes.")]
    private float rollDuration = 0.45f;

    [SerializeField, Min(0.01f)]
    [Tooltip("Seconds between visual value changes. The animation does no work after it settles.")]
    private float rollInterval = 0.04f;

    [SerializeField, Min(2)]
    [Tooltip("Highest placeholder value used by the roll, unless the real power is higher.")]
    private int rollValueCeiling = 6;

    private GeneratorComponent subscribedGenerator;
    private Coroutine rollCoroutine;
    private uint visualRandomState;

    public GeneratorComponent Generator => ResolveGenerator();
    public TMP_Text ValueText => ResolveValueText();
    public int DisplayedPower { get; private set; }
    public string DisplayText { get; private set; } = string.Empty;

    private void OnEnable()
    {
        InitializeVisualRandomState();
        BindGenerator();
        Refresh();
    }

    private void OnDisable()
    {
        StopRoll();
        UnbindGenerator();
    }

    private void OnValidate()
    {
        rollDuration = Mathf.Max(0f, rollDuration);
        rollInterval = Mathf.Max(0.01f, rollInterval);
        rollValueCeiling = Mathf.Max(2, rollValueCeiling);
        StopRoll();
        BindGenerator();
        Refresh();
    }

    public void Refresh()
    {
        StopRoll();
        GeneratorComponent target = ResolveGenerator();
        DisplayedPower = target != null
            ? target.PowerPerActiveOutput
            : 0;

        if (target != null)
            SetDisplayText(DisplayedPower);
        else
            SetUnavailableText();
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

    private void HandlePowerChanged(int power)
    {
        DisplayedPower = power;
        StartRoll(power);
    }

    private void StartRoll(int finalPower)
    {
        StopRoll();

        if (!Application.isPlaying ||
            !isActiveAndEnabled ||
            rollDuration <= 0f ||
            ResolveValueText() == null)
        {
            SetDisplayText(finalPower);
            return;
        }

        rollCoroutine = StartCoroutine(RollToValue(finalPower));
    }

    private IEnumerator RollToValue(int finalPower)
    {
        float elapsed = 0f;
        float nextSwitchTime = 0f;

        while (elapsed < rollDuration)
        {
            if (elapsed >= nextSwitchTime)
            {
                SetDisplayText(NextVisualValue(finalPower));
                nextSwitchTime += rollInterval;
            }

            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }

        SetDisplayText(finalPower);
        rollCoroutine = null;
    }

    private void StopRoll()
    {
        if (rollCoroutine == null)
            return;

        StopCoroutine(rollCoroutine);
        rollCoroutine = null;
    }

    private void SetDisplayText(int shownPower)
    {
        DisplayText = $"{prefix}{shownPower}";
        TMP_Text targetText = ResolveValueText();
        if (targetText != null)
            targetText.text = DisplayText;
    }

    private void SetUnavailableText()
    {
        DisplayText = $"{prefix}--";
        TMP_Text targetText = ResolveValueText();
        if (targetText != null)
            targetText.text = DisplayText;
    }

    private void InitializeVisualRandomState()
    {
        visualRandomState = unchecked((uint)GetInstanceID() * 747796405u) ^
                            0x9E3779B9u;
        if (visualRandomState == 0u)
            visualRandomState = 0xA341316Cu;
    }

    private int NextVisualValue(int finalPower)
    {
        // A private xorshift state keeps this cosmetic effect from touching
        // Unity's or the run's gameplay random streams.
        visualRandomState ^= visualRandomState << 13;
        visualRandomState ^= visualRandomState >> 17;
        visualRandomState ^= visualRandomState << 5;

        int minimum = GeneratorComponent.MinimumPowerPerActiveOutput;
        int maximum = Mathf.Max(rollValueCeiling, finalPower);
        int value = minimum +
                    (int)(visualRandomState %
                          (uint)(maximum - minimum + 1));

        if (value == finalPower && maximum > minimum)
            value = value == maximum ? minimum : value + 1;

        return value;
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
