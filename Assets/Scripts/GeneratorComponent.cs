using UnityEngine;

public sealed class GeneratorComponent : BoardComponent, ICircuitPowerSource
{
    public const int PowerPerConnectedOutput = 1;

    protected override void Reset()
    {
        EnsureGeneratorPorts();
        base.Reset();
    }

    protected override void OnEnable()
    {
        EnsureGeneratorPorts();
        base.OnEnable();
    }

    protected override void OnValidate()
    {
        EnsureGeneratorPorts();
        base.OnValidate();
    }

    public int GetPowerOutput(BoardPort port)
    {
        if (port == null || port.Type != BoardPortType.Output)
            return 0;

        foreach (BoardPort configuredPort in Ports)
        {
            if (ReferenceEquals(configuredPort, port))
                return PowerPerConnectedOutput;
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
