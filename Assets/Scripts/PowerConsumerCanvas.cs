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

    private ScoredPowerConsumerComponent subscribedConsumer;

    public ScoredPowerConsumerComponent Consumer => ResolveConsumer();
    public TMP_Text ValueText => ResolveValueText();
    public int DisplayedIdealPower { get; private set; }
    public int DisplayedCurrentPower { get; private set; }
    public string DisplayText { get; private set; } = string.Empty;

    private void OnEnable()
    {
        BindConsumer();
        Refresh();
    }

    private void OnDisable()
    {
        UnbindConsumer();
    }

    private void OnValidate()
    {
        BindConsumer();
        Refresh();
    }

    public void Refresh()
    {
        ScoredPowerConsumerComponent target = ResolveConsumer();
        TMP_Text targetText = ResolveValueText();
        DisplayedIdealPower = target != null
            ? target.IdealPower
            : 0;
        DisplayedCurrentPower = target != null
            ? target.ReceivedPower
            : 0;
        DisplayText = target != null
            ? $"{prefix}{DisplayedCurrentPower}/{DisplayedIdealPower}"
            : $"{prefix}--";

        if (targetText != null)
            targetText.text = DisplayText;
    }

    private void BindConsumer()
    {
        ScoredPowerConsumerComponent target = ResolveConsumer();
        if (subscribedConsumer == target)
            return;

        UnbindConsumer();
        subscribedConsumer = target;
        if (subscribedConsumer != null)
            subscribedConsumer.PowerStateChanged += HandlePowerChanged;
    }

    private void UnbindConsumer()
    {
        if (subscribedConsumer != null)
        {
            subscribedConsumer.PowerStateChanged -=
                HandlePowerChanged;
        }

        subscribedConsumer = null;
    }

    private void HandlePowerChanged(ConsumerPowerState state)
    {
        Refresh();
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
