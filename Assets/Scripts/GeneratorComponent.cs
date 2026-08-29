using System;
using UnityEngine;

public sealed class GeneratorComponent : BoardComponent, ICircuitPowerSource
{
    public const int MinimumPowerPerActiveOutput = 1;

    // Retained as the default value and for compatibility with existing code.
    public const int PowerPerConnectedOutput = MinimumPowerPerActiveOutput;

    [SerializeField, Min(MinimumPowerPerActiveOutput)]
    [Tooltip("Power produced by each output port that is attached to an electrical net.")]
    private int powerPerActiveOutput = PowerPerConnectedOutput;

    public int PowerPerActiveOutput => powerPerActiveOutput;

    public event Action<int> PowerPerActiveOutputChanged;

    protected override void Reset()
    {
        powerPerActiveOutput = PowerPerConnectedOutput;
        EnsureGeneratorPorts();
        base.Reset();
    }

    protected override void OnEnable()
    {
        powerPerActiveOutput = Mathf.Max(
            MinimumPowerPerActiveOutput,
            powerPerActiveOutput);
        EnsureGeneratorPorts();
        base.OnEnable();
    }

    protected override void OnValidate()
    {
        powerPerActiveOutput = Mathf.Max(
            MinimumPowerPerActiveOutput,
            powerPerActiveOutput);
        EnsureGeneratorPorts();
        base.OnValidate();
    }

    /// <summary>
    /// Changes generator state without calculating or distributing electricity.
    /// CircuitSystem reads this value during its next normal simulation tick.
    /// </summary>
    public void SetPowerPerActiveOutput(int value)
    {
        int nextValue = Mathf.Max(MinimumPowerPerActiveOutput, value);
        if (powerPerActiveOutput == nextValue)
            return;

        powerPerActiveOutput = nextValue;
        PowerPerActiveOutputChanged?.Invoke(powerPerActiveOutput);
    }

    public int GetPowerOutput(BoardPort port)
    {
        if (port == null || port.Type != BoardPortType.Output)
            return 0;

        foreach (BoardPort configuredPort in Ports)
        {
            if (ReferenceEquals(configuredPort, port))
                return powerPerActiveOutput;
        }

        return 0;
    }

    private void EnsureGeneratorPorts()
    {
        if (Ports.Count == 4 &&
            Matches(Ports[0], new Vector2Int(0, 1), BoardPortDirection.North) &&
            Matches(Ports[1], new Vector2Int(1, 1), BoardPortDirection.East) &&
            Matches(Ports[2], new Vector2Int(1, 0), BoardPortDirection.South) &&
            Matches(Ports[3], new Vector2Int(0, 0), BoardPortDirection.West))
        {
            return;
        }

        ReplacePorts(
            new BoardPort(new Vector2Int(0, 1), BoardPortDirection.North, BoardPortType.Output),
            new BoardPort(new Vector2Int(1, 1), BoardPortDirection.East, BoardPortType.Output),
            new BoardPort(new Vector2Int(1, 0), BoardPortDirection.South, BoardPortType.Output),
            new BoardPort(new Vector2Int(0, 0), BoardPortDirection.West, BoardPortType.Output));
    }

    private static bool Matches(
        BoardPort port,
        Vector2Int localCellOffset,
        BoardPortDirection direction)
    {
        return port != null &&
               port.LocalCellOffset == localCellOffset &&
               port.Direction == direction &&
               port.Type == BoardPortType.Output;
    }
}
