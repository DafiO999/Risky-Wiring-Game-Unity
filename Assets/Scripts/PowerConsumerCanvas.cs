using System.Collections;
using TMPro;
using UnityEngine;

public class PowerConsumerCanvas : MonoBehaviour
{
    [SerializeField]
    private ScoredPowerConsumerComponent consumer;

    [SerializeField]
    private TMP_Text valueText;

    [SerializeField]
    private string prefix = "";

    [Header("Value Change Animation")]
    [SerializeField, Min(0f)]
    [Tooltip("How long the required-power number rapidly cycles through visual-only values after it changes.")]
    private float rollDuration = 0.45f;

    [SerializeField, Min(0.01f)]
    [Tooltip("Seconds between visual value changes. The animation does no work after it settles.")]
    private float rollInterval = 0.04f;

    [SerializeField, Min(2)]
    [Tooltip("Highest placeholder value used by the roll, unless the real required power is higher.")]
    private int rollValueCeiling = 6;

    private ScoredPowerConsumerComponent subscribedConsumer;
    private Coroutine rollCoroutine;
    private uint visualRandomState;
    private int shownIdealPower;
    private bool isRolling;

    public ScoredPowerConsumerComponent Consumer => ResolveConsumer();
    public TMP_Text ValueText => ResolveValueText();
    public int DisplayedIdealPower { get; private set; }
    public int DisplayedCurrentPower { get; private set; }
    public string DisplayText { get; private set; } = string.Empty;

    private void OnEnable()
    {
        InitializeVisualRandomState();
        BindConsumer();
        Refresh();
    }

    private void OnDisable()
    {
        StopRoll();
        UnbindConsumer();
    }

    private void OnValidate()
    {
        rollDuration = Mathf.Max(0f, rollDuration);
        rollInterval = Mathf.Max(0.01f, rollInterval);
        rollValueCeiling = Mathf.Max(2, rollValueCeiling);
        StopRoll();
        BindConsumer();
        Refresh();
    }

    public void Refresh()
    {
        StopRoll();
        ScoredPowerConsumerComponent target = ResolveConsumer();
        DisplayedIdealPower = target != null
            ? target.IdealPower
            : 0;
        DisplayedCurrentPower = target != null
            ? target.ReceivedPower
            : 0;
        shownIdealPower = DisplayedIdealPower;
        if (target != null)
            SetDisplayText(shownIdealPower);
        else
            SetUnavailableText();
    }

    private void BindConsumer()
    {
        ScoredPowerConsumerComponent target = ResolveConsumer();
        if (subscribedConsumer == target)
            return;

        UnbindConsumer();
        subscribedConsumer = target;
        if (subscribedConsumer != null)
        {
            subscribedConsumer.ReceivedPowerChanged +=
                HandleReceivedPowerChanged;
            subscribedConsumer.RequiredPowerChanged +=
                HandleRequiredPowerChanged;
        }
    }

    private void UnbindConsumer()
    {
        if (subscribedConsumer != null)
        {
            subscribedConsumer.ReceivedPowerChanged -=
                HandleReceivedPowerChanged;
            subscribedConsumer.RequiredPowerChanged -=
                HandleRequiredPowerChanged;
        }

        subscribedConsumer = null;
    }

    private void HandleReceivedPowerChanged(int power)
    {
        DisplayedCurrentPower = power;
        SetDisplayText(isRolling ? shownIdealPower : DisplayedIdealPower);
    }

    private void HandleRequiredPowerChanged(int power)
    {
        DisplayedIdealPower = power;
        ScoredPowerConsumerComponent target = ResolveConsumer();
        DisplayedCurrentPower = target != null ? target.ReceivedPower : 0;
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
            shownIdealPower = finalPower;
            SetDisplayText(finalPower);
            return;
        }

        isRolling = true;
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
                shownIdealPower = NextVisualValue(finalPower);
                SetDisplayText(shownIdealPower);
                nextSwitchTime += rollInterval;
            }

            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }

        shownIdealPower = finalPower;
        isRolling = false;
        SetDisplayText(finalPower);
        rollCoroutine = null;
    }

    private void StopRoll()
    {
        if (rollCoroutine != null)
        {
            StopCoroutine(rollCoroutine);
            rollCoroutine = null;
        }

        isRolling = false;
    }

    private void SetDisplayText(int displayedIdealPower)
    {
        DisplayText = $"{prefix}{DisplayedCurrentPower}/{displayedIdealPower}";
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
                            0x85EBCA6Bu;
        if (visualRandomState == 0u)
            visualRandomState = 0xC8013EA4u;
    }

    private int NextVisualValue(int finalPower)
    {
        visualRandomState ^= visualRandomState << 13;
        visualRandomState ^= visualRandomState >> 17;
        visualRandomState ^= visualRandomState << 5;

        int minimum = ScoredPowerConsumerComponent.MinimumRequiredPower;
        int maximum = Mathf.Max(rollValueCeiling, finalPower);
        int value = minimum +
                    (int)(visualRandomState %
                          (uint)(maximum - minimum + 1));

        if (value == finalPower && maximum > minimum)
            value = value == maximum ? minimum : value + 1;

        return value;
    }

    private ScoredPowerConsumerComponent ResolveConsumer()
    {
        if (consumer == null)
            consumer = GetComponentInParent<ScoredPowerConsumerComponent>();

        return consumer;
    }

    private TMP_Text ResolveValueText()
    {
        if (valueText == null)
            valueText = GetComponentInChildren<TMP_Text>(true);

        return valueText;
    }
}
