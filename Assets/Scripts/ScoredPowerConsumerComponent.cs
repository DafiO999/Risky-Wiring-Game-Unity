using System;
using UnityEngine;

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
    [SerializeField]
    [Tooltip("Score system that receives this consumer's score rate. If empty, ScoreSystem.Instance is used.")]
    private ScoreSystem scoreSystem;

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

    public ConsumerPowerState PowerState => receivedPower switch
    {
        1 => ConsumerPowerState.Underpowered,
        2 => ConsumerPowerState.CorrectlyPowered,
        >= 3 => ConsumerPowerState.Overloaded,
        _ => ConsumerPowerState.Off
    };

    public int ScorePerSecond => receivedPower switch
    {
        2 => 1,
        >= 3 => -1,
        _ => 0
    };

    public event Action<int> ReceivedPowerChanged;
    public event Action<ConsumerPowerState> PowerStateChanged;

    protected override void Reset()
    {
        EnsureInputPort();
        base.Reset();
    }

    protected override void OnEnable()
    {
        EnsureInputPort();
        SetReceivedPower(null, 0);
        base.OnEnable();
        RefreshScoreRateReport();
    }

    protected override void OnValidate()
    {
        EnsureInputPort();
        base.OnValidate();
        RefreshScoreRateReport();
    }

    protected virtual void OnDisable()
    {
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
