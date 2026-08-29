using System;
using UnityEngine;
using UnityEngine.Serialization;

public enum ConsumerPowerState
{
    Off,
    Underpowered,
    CorrectlyPowered,
    Overloaded
}

public abstract class ScoredPowerConsumerComponent :
    BoardComponent,
    ICircuitPowerConsumer,
    IScoreRateSource
{
    public const int MinimumRequiredPower = 1;
    public const int DefaultRequiredPower = 2;

    // Compatibility names retained for code written against the earlier API.
    public const int MinimumIdealPower = MinimumRequiredPower;
    public const int DefaultIdealPower = DefaultRequiredPower;

    [SerializeField]
    [Tooltip("Score system that receives this consumer's score rate. If empty, ScoreSystem.Instance is used.")]
    private ScoreSystem scoreSystem;

    [FormerlySerializedAs("idealPower")]
    [SerializeField, Min(MinimumRequiredPower)]
    [Tooltip("Received power that gives this Lamp or Fan its positive score rate.")]
    private int requiredPower = DefaultRequiredPower;

    [SerializeField, HideInInspector]
    private int receivedPower;

    private ScoreSystem reportedScoreSystem;

    public ScoreSystem ScoreSystem
    {
        get => scoreSystem;
        set
        {
            if (scoreSystem == value)
                return;

            RemoveScoreRateReport();
            scoreSystem = value;
            RefreshScoreRateReport();
        }
    }

    public int ReceivedPower => receivedPower;
    public int RequiredPower => requiredPower;
    public int IdealPower => RequiredPower;

    public ConsumerPowerState PowerState
    {
        get
        {
            if (receivedPower <= 0)
                return ConsumerPowerState.Off;

            if (receivedPower < requiredPower)
                return ConsumerPowerState.Underpowered;

            return receivedPower == requiredPower
                ? ConsumerPowerState.CorrectlyPowered
                : ConsumerPowerState.Overloaded;
        }
    }

    public int ScorePerSecond => receivedPower == requiredPower
        ? 1
        : receivedPower > requiredPower
            ? -1
            : 0;

    public event Action<int> ReceivedPowerChanged;
    public event Action<int> RequiredPowerChanged;
    public event Action<int> IdealPowerChanged;
    public event Action<ConsumerPowerState> PowerStateChanged;

    protected override void Reset()
    {
        requiredPower = DefaultRequiredPower;
        EnsureInputPort();
        base.Reset();
    }

    protected override void OnEnable()
    {
        requiredPower = Mathf.Max(MinimumRequiredPower, requiredPower);
        EnsureInputPort();
        SetReceivedPower(null, 0);
        base.OnEnable();
        RefreshScoreRateReport();
    }

    protected override void OnValidate()
    {
        requiredPower = Mathf.Max(MinimumRequiredPower, requiredPower);
        EnsureInputPort();
        base.OnValidate();
        RefreshScoreRateReport();
    }

    protected virtual void OnDisable()
    {
        SetReceivedPower(null, 0);
        RemoveScoreRateReport();
    }

    protected override void OnDestroy()
    {
        RemoveScoreRateReport();
        base.OnDestroy();
    }

    public void SetReceivedPower(BoardPort port, int power)
    {
        int nextPower = Mathf.Max(0, power);
        if (receivedPower == nextPower)
            return;

        ConsumerPowerState previousState = PowerState;
        receivedPower = nextPower;
        ReceivedPowerChanged?.Invoke(receivedPower);

        if (PowerState != previousState)
            PowerStateChanged?.Invoke(PowerState);
    }

    public void SetRequiredPower(int value)
    {
        int nextRequiredPower = Mathf.Max(MinimumRequiredPower, value);
        if (requiredPower == nextRequiredPower)
            return;

        ConsumerPowerState previousState = PowerState;
        int previousScoreRate = ScorePerSecond;
        requiredPower = nextRequiredPower;
        RequiredPowerChanged?.Invoke(requiredPower);
        IdealPowerChanged?.Invoke(requiredPower);

        if (PowerState != previousState)
            PowerStateChanged?.Invoke(PowerState);

        if (ScorePerSecond != previousScoreRate)
            RefreshScoreRateReport();
    }

    public void SetIdealPower(int value)
    {
        SetRequiredPower(value);
    }

    public void RefreshScoreRateReport()
    {
        ScoreSystem targetScoreSystem = scoreSystem != null
            ? scoreSystem
            : global::ScoreSystem.Instance;

        if (reportedScoreSystem != targetScoreSystem)
        {
            RemoveScoreRateReport();
            reportedScoreSystem = targetScoreSystem;
        }

        if (reportedScoreSystem != null && isActiveAndEnabled)
            reportedScoreSystem.ReportScoreRate(this, ScorePerSecond);
    }

    private void RemoveScoreRateReport()
    {
        if (reportedScoreSystem != null)
            reportedScoreSystem.RemoveScoreRate(this);

        reportedScoreSystem = null;
    }

    private void EnsureInputPort()
    {
        if (Ports.Count == 1)
        {
            BoardPort port = Ports[0];
            if (port != null &&
                port.LocalCellOffset == Vector2Int.zero &&
                port.Direction == BoardPortDirection.West &&
                port.Type == BoardPortType.Input)
            {
                return;
            }
        }

        ReplacePorts(
            new BoardPort(
                Vector2Int.zero,
                BoardPortDirection.West,
                BoardPortType.Input));
    }
}
