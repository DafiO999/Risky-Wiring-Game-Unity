using System;
using UnityEngine;

public enum BatteryPowerState
{
    Empty,
    Idle,
    Charging,
    Discharging,
    ChargingAndDischarging,
    Full
}

public sealed class BatteryComponent :
    BoardComponent,
    ICircuitPowerSource,
    ICircuitPowerConsumer,
    ICircuitPowerDemand,
    ICircuitPowerTick
{
    private const int InputPortIndex = 0;
    private const int OutputPortIndex = 1;

    [SerializeField, Min(0f)]
    private float capacity = 10f;

    [SerializeField, Min(0f)]
    private float charge;

    [SerializeField, Min(0)]
    [Tooltip("Integer power supplied per second while this battery starts a simulation tick with charge.")]
    private int outputPower = 1;

    [SerializeField, HideInInspector]
    private int receivedInputPower;

    [SerializeField, HideInInspector]
    private int providedOutputPower;

    private float tickStartCharge;
    private float tickDuration;
    private bool powerTickActive;
    private bool outputWasConnectedThisTick;
    private int receivedInputPowerBeforeTick;
    private int providedOutputPowerBeforeTick;

    public float Charge => charge;
    public float Capacity => capacity;
    public int OutputPower => outputPower;
    public int ReceivedInputPower => receivedInputPower;
    public int ProvidedOutputPower => providedOutputPower;
    public float ChargeNormalized => capacity > 0f
        ? Mathf.Clamp01(charge / capacity)
        : 0f;
    public bool IsEmpty => charge <= Mathf.Epsilon;
    public bool IsFull => capacity <= 0f || charge >= capacity - Mathf.Epsilon;
    public bool IsCharging => receivedInputPower > 0;
    public bool IsDischarging => providedOutputPower > 0;
    public bool CanAcceptInputPower =>
        capacity > 0f && GetProjectedChargeBeforeInput() < capacity - Mathf.Epsilon;

    public BatteryPowerState PowerState
    {
        get
        {
            if (IsCharging && IsDischarging)
                return BatteryPowerState.ChargingAndDischarging;

            if (IsCharging)
                return BatteryPowerState.Charging;

            if (IsDischarging)
                return BatteryPowerState.Discharging;

            if (IsEmpty)
                return BatteryPowerState.Empty;

            return IsFull ? BatteryPowerState.Full : BatteryPowerState.Idle;
        }
    }

    public event Action<float> ChargeChanged;
    public event Action StateChanged;

    protected override void Reset()
    {
        EnsureBatteryPorts();
        base.Reset();
    }

    protected override void OnEnable()
    {
        EnsureBatteryPorts();
        ResetPowerStateInternal(false);
        base.OnEnable();
        StateChanged?.Invoke();
    }

    protected override void OnValidate()
    {
        capacity = Mathf.Max(0f, capacity);
        charge = Mathf.Clamp(charge, 0f, capacity);
        outputPower = Mathf.Max(0, outputPower);
        EnsureBatteryPorts();
        base.OnValidate();
    }

    public void SetCapacity(float value)
    {
        float nextCapacity = Mathf.Max(0f, value);
        bool capacityChanged = !Mathf.Approximately(capacity, nextCapacity);
        capacity = nextCapacity;
        bool chargeChanged = SetChargeInternal(charge);

        if (capacityChanged || chargeChanged)
            StateChanged?.Invoke();
    }

    public void SetCharge(float value)
    {
        if (SetChargeInternal(value))
            StateChanged?.Invoke();
    }

    public void SetOutputPower(int value)
    {
        int nextOutputPower = Mathf.Max(0, value);
        if (outputPower == nextOutputPower)
            return;

        outputPower = nextOutputPower;
        StateChanged?.Invoke();
    }

    public int GetPowerOutput(BoardPort port)
    {
        if (!IsConfiguredPort(port, OutputPortIndex, BoardPortType.Output))
            return 0;

        float availableCharge = powerTickActive ? tickStartCharge : charge;
        int availableOutput = availableCharge > 0f ? outputPower : 0;

        if (powerTickActive)
        {
            outputWasConnectedThisTick = availableOutput > 0;
            providedOutputPower = availableOutput;
        }

        return availableOutput;
    }

    public void SetReceivedPower(BoardPort port, int power)
    {
        if (!IsConfiguredPort(port, InputPortIndex, BoardPortType.Input))
            return;

        receivedInputPower = CanAcceptInputPower
            ? Mathf.Max(0, power)
            : 0;
    }

    public bool CanReceivePower(BoardPort port)
    {
        return IsConfiguredPort(port, InputPortIndex, BoardPortType.Input) &&
               CanAcceptInputPower;
    }

    public void BeginPowerTick(float deltaTime)
    {
        receivedInputPowerBeforeTick = receivedInputPower;
        providedOutputPowerBeforeTick = providedOutputPower;
        ResetPowerStateInternal(false);
        powerTickActive = true;
        tickDuration = Mathf.Max(0f, deltaTime);
    }

    public void EndPowerTick(float deltaTime)
    {
        float chargeAfterOutput = tickStartCharge;
        if (outputWasConnectedThisTick)
        {
            chargeAfterOutput = Mathf.Max(
                0f,
                tickStartCharge - providedOutputPower * deltaTime);
        }

        float chargeAfterInput = chargeAfterOutput + receivedInputPower * deltaTime;
        powerTickActive = false;
        tickDuration = 0f;
        bool chargeChanged = SetChargeInternal(chargeAfterInput);
        bool powerChanged =
            receivedInputPowerBeforeTick != receivedInputPower ||
            providedOutputPowerBeforeTick != providedOutputPower;

        if (chargeChanged || powerChanged)
            StateChanged?.Invoke();
    }

    public void ResetPowerState()
    {
        ResetPowerStateInternal(true);
    }

    private void ResetPowerStateInternal(bool notifyStateChanged)
    {
        bool powerChanged = receivedInputPower != 0 || providedOutputPower != 0;
        tickStartCharge = charge;
        tickDuration = 0f;
        receivedInputPower = 0;
        providedOutputPower = 0;
        outputWasConnectedThisTick = false;
        powerTickActive = false;

        if (notifyStateChanged && powerChanged)
            StateChanged?.Invoke();
    }

    private bool SetChargeInternal(float value)
    {
        float clampedCharge = Mathf.Clamp(value, 0f, capacity);
        if (Mathf.Approximately(charge, clampedCharge))
            return false;

        charge = clampedCharge;
        ChargeChanged?.Invoke(charge);
        return true;
    }

    private float GetProjectedChargeBeforeInput()
    {
        if (!powerTickActive || !outputWasConnectedThisTick)
            return charge;

        return Mathf.Max(
            0f,
            tickStartCharge - providedOutputPower * tickDuration);
    }

    private void EnsureBatteryPorts()
    {
        if (Ports.Count == 2 &&
            Matches(
                Ports[InputPortIndex],
                new Vector2Int(0, 0),
                BoardPortDirection.West,
                BoardPortType.Input) &&
            Matches(
                Ports[OutputPortIndex],
                new Vector2Int(1, 0),
                BoardPortDirection.East,
                BoardPortType.Output))
        {
            return;
        }

        ReplacePorts(
            new BoardPort(
                new Vector2Int(0, 0),
                BoardPortDirection.West,
                BoardPortType.Input),
            new BoardPort(
                new Vector2Int(1, 0),
                BoardPortDirection.East,
                BoardPortType.Output));
    }

    private bool IsConfiguredPort(
        BoardPort port,
        int portIndex,
        BoardPortType expectedType)
    {
        return port != null &&
               Ports.Count > portIndex &&
               ReferenceEquals(Ports[portIndex], port) &&
               port.Type == expectedType;
    }

    private static bool Matches(
        BoardPort port,
        Vector2Int localCellOffset,
        BoardPortDirection direction,
        BoardPortType type)
    {
        return port != null &&
               port.LocalCellOffset == localCellOffset &&
               port.Direction == direction &&
               port.Type == type;
    }
}
