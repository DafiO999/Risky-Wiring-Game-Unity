using System;
using System.Collections.Generic;
using UnityEngine;

public interface ICircuitPowerSource
{
    int GetPowerOutput(BoardPort port);
}

public interface ICircuitPowerConsumer
{
    void SetReceivedPower(BoardPort port, int power);
}

/// <summary>
/// Optional consumer contract for sinks such as full batteries that should not
/// take a share of a net's power during the current simulation tick.
/// </summary>
public interface ICircuitPowerDemand
{
    bool CanReceivePower(BoardPort port);
}

public interface ICircuitPowerTick
{
    void BeginPowerTick(float deltaTime);
    void EndPowerTick(float deltaTime);
    void ResetPowerState();
}

public sealed class CircuitPortAttachment
{
    public BoardComponent Component { get; }
    public BoardPort Port { get; }
    public int PortIndex { get; }
    public Vector2Int ComponentCell { get; }
    public BoardPortDirection Direction { get; }
    public Vector2Int WireCell { get; }
    public int ReceivedPower { get; internal set; }

    internal CircuitPortAttachment(
        BoardComponent component,
        BoardPort port,
        int portIndex,
        Vector2Int componentCell,
        BoardPortDirection direction,
        Vector2Int wireCell)
    {
        Component = component;
        Port = port;
        PortIndex = portIndex;
        ComponentCell = componentCell;
        Direction = direction;
        WireCell = wireCell;
    }
}

public sealed class CircuitNet
{
    private readonly List<Vector2Int> wireCells = new();
    private readonly List<CircuitPortAttachment> ports = new();

    public int NetId { get; }
    public IReadOnlyList<Vector2Int> WireCells => wireCells;
    public IReadOnlyList<CircuitPortAttachment> Ports => ports;
    public int AvailablePower { get; private set; }
    public int ConsumerCount { get; private set; }
    public bool IsPowered => AvailablePower > 0;

    internal CircuitNet(int netId)
    {
        NetId = netId;
    }

    internal void AddWireCell(Vector2Int cell)
    {
        wireCells.Add(cell);
    }

    internal bool TryAddPort(CircuitPortAttachment attachment)
    {
        foreach (CircuitPortAttachment existing in ports)
        {
            if (ReferenceEquals(existing.Component, attachment.Component) &&
                ReferenceEquals(existing.Port, attachment.Port))
            {
                return false;
            }
        }

        ports.Add(attachment);
        return true;
    }

    internal void SetPowerSummary(int availablePower, int consumerCount)
    {
        AvailablePower = availablePower;
        ConsumerCount = consumerCount;
    }
}

[DisallowMultipleComponent]
[RequireComponent(typeof(GridBoard))]
[ExecuteAlways]
[DefaultExecutionOrder(100)]
public sealed class CircuitSystem : MonoBehaviour
{
    public const int NoNetId = -1;

    private static readonly WireConnection[] CardinalConnections =
    {
        WireConnection.North,
        WireConnection.East,
        WireConnection.South,
        WireConnection.West
    };

    [SerializeField]
    [Tooltip("Board whose wire and component topology is grouped into electrical nets.")]
    private GridBoard board;

    [Header("Simulation")]
    [SerializeField, Min(0.01f)]
    [Tooltip("Seconds of game time between fixed electricity simulation ticks.")]
    private float tickInterval = 0.1f;

    [Header("Net Debug")]
    [SerializeField]
    private bool drawNetIds = true;

    [SerializeField, Range(0f, 0.5f)]
    [Tooltip("Scene-view label height above the board as a fraction of cell size.")]
    private float netIdLabelHeight = 0.12f;

    private readonly List<CircuitNet> nets = new();
    private readonly Dictionary<Vector2Int, CircuitNet> wireNets = new();
    private readonly Dictionary<BoardPort, CircuitNet> portNets = new();
    private readonly Queue<Vector2Int> floodFillQueue = new();
    private readonly List<CircuitPortAttachment> consumerBuffer = new();
    private readonly List<ICircuitPowerTick> powerTickBuffer = new();
    private readonly HashSet<ICircuitPowerTick> powerTickSet = new();
    private readonly Dictionary<CircuitNet, int> availablePowerBuffer = new();
    private readonly List<ScoredPowerConsumerComponent> scoreRateBuffer = new();
    private readonly HashSet<ScoredPowerConsumerComponent> scoreRateSet = new();

    private GridBoard subscribedBoard;
    private bool topologyDirty = true;
    private int lastBuiltTopologyRevision = -1;
    private int rebuildCount;
    private int powerTickCount;
    private double tickAccumulator;

    public GridBoard Board => ResolveBoard();
    public bool IsTopologyDirty
    {
        get
        {
            DetectTopologyRevisionChange();
            return topologyDirty;
        }
    }
    public bool DrawNetIds => drawNetIds;
    public float NetIdLabelHeight => netIdLabelHeight;
    public float TickInterval => tickInterval;
    public float TickAccumulator => (float)tickAccumulator;
    public int RebuildCount => rebuildCount;
    public int PowerTickCount => powerTickCount;
    public int LastBuiltTopologyRevision => lastBuiltTopologyRevision;

    public IReadOnlyList<CircuitNet> Nets
    {
        get
        {
            RebuildIfDirty();

            return nets;
        }
    }

    public event Action TopologyRebuilt;
    public event Action PowerTickCompleted;

    private void Reset()
    {
        board = GetComponent<GridBoard>();
        tickInterval = 0.1f;
        MarkTopologyDirty();
    }

    private void Awake()
    {
        BindBoard();
        MarkTopologyDirty();
    }

    private void OnEnable()
    {
        tickAccumulator = 0d;
        BindBoard();
        MarkTopologyDirty();
    }

    private void OnDisable()
    {
        ResetPreviousPowerParticipants();
        ResetPreviousConsumers();
        ResetWireVisualStates();
        tickAccumulator = 0d;
        UnbindBoard();
    }

    private void OnValidate()
    {
        tickInterval = Mathf.Max(0.01f, tickInterval);
        netIdLabelHeight = Mathf.Clamp(netIdLabelHeight, 0f, 0.5f);
        BindBoard();
        MarkTopologyDirty();
    }

    private void Update()
    {
        if (Application.isPlaying)
            AdvanceSimulation(Time.deltaTime);
        else
            RebuildIfDirty();
    }

    public void MarkTopologyDirty()
    {
        topologyDirty = true;
    }

    public void RebuildIfDirty()
    {
        BindBoard();
        DetectTopologyRevisionChange();
        if (topologyDirty)
            RebuildTopology();
    }

    public void SetTickInterval(float seconds)
    {
        tickInterval = Mathf.Max(0.01f, seconds);
    }

    /// <summary>
    /// Adds elapsed game time and runs only complete fixed electricity ticks.
    /// Returns the number of ticks completed during this call.
    /// </summary>
    public int AdvanceSimulation(float deltaTime)
    {
        if (deltaTime <= 0f)
            return 0;

        tickAccumulator += deltaTime;
        int completedTicks = 0;
        double fixedInterval = tickInterval;

        // Time.deltaTime is a float, so nominally exact frame sums can land a
        // few nanoseconds below the interval when promoted to double.
        while (tickAccumulator + 0.000001d >= fixedInterval)
        {
            SimulatePower(tickInterval);
            tickAccumulator = Math.Max(0d, tickAccumulator - fixedInterval);
            completedTicks++;
        }

        return completedTicks;
    }

    /// <summary>
    /// Evaluates power using the cached topology, then lets stateful components
    /// apply the result. This does not rebuild electrical nets unless topology
    /// was marked dirty.
    /// </summary>
    public void SimulatePower(float deltaTime)
    {
        RebuildIfDirty();
        if (deltaTime <= 0f)
            return;

        ClearPower();
        GatherPowerTickParticipants();

        foreach (ICircuitPowerTick participant in powerTickBuffer)
            participant.BeginPowerTick(deltaTime);

        CollectGeneratorOutputs();
        CollectBatteryOutputs();
        DistributePower();
        ApplyBatteryInputPower();

        foreach (ICircuitPowerTick participant in powerTickBuffer)
            participant.EndPowerTick(deltaTime);

        ApplyConsumerReceivedPower();
        CalculateScoreRates();
        UpdateWireVisualStates();
        powerTickCount++;
        PowerTickCompleted?.Invoke();
    }

    /// <summary>
    /// Rebuilds all nets immediately. Normal runtime use relies on board change
    /// events and RebuildIfDirty so repeated frames do not rebuild unchanged data.
    /// </summary>
    public void RebuildTopology()
    {
        BindBoard();
        GridBoard targetBoard = Board;
        topologyDirty = false;
        lastBuiltTopologyRevision = targetBoard != null
            ? targetBoard.TopologyRevision
            : -1;
        rebuildCount++;
        ResetPreviousPowerParticipants();
        ResetPreviousConsumers();
        nets.Clear();
        wireNets.Clear();
        portNets.Clear();
        floodFillQueue.Clear();

        if (targetBoard == null)
        {
            TopologyRebuilt?.Invoke();
            return;
        }

        HashSet<Vector2Int> visited = new();
        foreach (Vector2Int startCell in targetBoard.WireCells)
        {
            if (!visited.Add(startCell))
                continue;

            CircuitNet net = new(nets.Count);
            nets.Add(net);
            floodFillQueue.Enqueue(startCell);

            while (floodFillQueue.Count > 0)
            {
                Vector2Int cell = floodFillQueue.Dequeue();
                net.AddWireCell(cell);
                wireNets[cell] = net;

                WireCell wire = targetBoard.GetWire(cell);
                if (wire == null)
                    continue;

                foreach (WireConnection connection in CardinalConnections)
                {
                    if (!wire.HasConnection(connection))
                        continue;

                    Vector2Int neighbor =
                        cell + WireConnectionUtility.ToCellOffset(connection);
                    WireCell neighborWire = targetBoard.GetWire(neighbor);
                    if (neighborWire == null ||
                        !neighborWire.HasConnection(
                            WireConnectionUtility.GetOpposite(connection)) ||
                        !visited.Add(neighbor))
                    {
                        continue;
                    }

                    floodFillQueue.Enqueue(neighbor);
                }
            }
        }

        AttachPorts(targetBoard);
        ClearPower();
        UpdateWireVisualStates();
        TopologyRebuilt?.Invoke();
    }

    public int GetNetId(Vector2Int wireCell)
    {
        return TryGetNet(wireCell, out CircuitNet net)
            ? net.NetId
            : NoNetId;
    }

    public bool TryGetNet(Vector2Int wireCell, out CircuitNet net)
    {
        RebuildIfDirty();

        return wireNets.TryGetValue(wireCell, out net);
    }

    public bool TryGetPortNet(
        BoardComponent component,
        int portIndex,
        out CircuitNet net)
    {
        RebuildIfDirty();

        if (component == null ||
            portIndex < 0 ||
            portIndex >= component.Ports.Count ||
            component.Ports[portIndex] == null)
        {
            net = null;
            return false;
        }

        return portNets.TryGetValue(component.Ports[portIndex], out net);
    }

    public bool TryGetPortNet(BoardPort port, out CircuitNet net)
    {
        RebuildIfDirty();

        if (port == null)
        {
            net = null;
            return false;
        }

        return portNets.TryGetValue(port, out net);
    }

    private void AttachPorts(GridBoard targetBoard)
    {
        foreach (CircuitNet net in nets)
        {
            foreach (Vector2Int wireCell in net.WireCells)
            {
                WireCell wire = targetBoard.GetWire(wireCell);
                if (wire == null)
                    continue;

                foreach (WireConnection connection in CardinalConnections)
                {
                    if (!wire.HasConnection(connection) ||
                        !targetBoard.TryGetConnectedPort(
                            wireCell,
                            connection,
                            out BoardComponent component,
                            out BoardPort port))
                    {
                        continue;
                    }

                    int portIndex = FindPortIndex(component, port);
                    if (portIndex < 0 ||
                        !component.TryGetPortGridPose(
                            portIndex,
                            out Vector2Int componentCell,
                            out BoardPortDirection direction))
                    {
                        continue;
                    }

                    CircuitPortAttachment attachment = new(
                        component,
                        port,
                        portIndex,
                        componentCell,
                        direction,
                        wireCell);
                    if (net.TryAddPort(attachment))
                        portNets[port] = net;
                }
            }
        }
    }

    private void ClearPower()
    {
        availablePowerBuffer.Clear();

        foreach (CircuitNet net in nets)
        {
            availablePowerBuffer[net] = 0;
            net.SetPowerSummary(0, 0);

            foreach (CircuitPortAttachment attachment in net.Ports)
                attachment.ReceivedPower = 0;
        }
    }

    private void CollectGeneratorOutputs()
    {
        CollectSourceOutputs(includeBatteries: false);
    }

    private void CollectBatteryOutputs()
    {
        CollectSourceOutputs(includeBatteries: true);
    }

    private void CollectSourceOutputs(bool includeBatteries)
    {
        foreach (CircuitNet net in nets)
        {
            foreach (CircuitPortAttachment attachment in net.Ports)
            {
                if (attachment.Port.Type != BoardPortType.Output ||
                    attachment.Component is not ICircuitPowerSource source ||
                    (attachment.Component is BatteryComponent) != includeBatteries)
                {
                    continue;
                }

                availablePowerBuffer[net] += Mathf.Max(
                    0,
                    source.GetPowerOutput(attachment.Port));
            }
        }
    }

    private void DistributePower()
    {
        foreach (CircuitNet net in nets)
        {
            consumerBuffer.Clear();

            foreach (CircuitPortAttachment attachment in net.Ports)
            {
                if (attachment.Port.Type == BoardPortType.Input &&
                    attachment.Component is ICircuitPowerConsumer consumer &&
                    (consumer is not ICircuitPowerDemand demand ||
                     demand.CanReceivePower(attachment.Port)))
                {
                    consumerBuffer.Add(attachment);
                }
            }

            int availablePower = availablePowerBuffer[net];
            net.SetPowerSummary(availablePower, consumerBuffer.Count);
            if (consumerBuffer.Count == 0)
                continue;

            int powerPerConsumer = availablePower / consumerBuffer.Count;
            int remainder = availablePower % consumerBuffer.Count;

            for (int consumerIndex = 0;
                 consumerIndex < consumerBuffer.Count;
                 consumerIndex++)
            {
                CircuitPortAttachment attachment = consumerBuffer[consumerIndex];
                int receivedPower = powerPerConsumer +
                                    (consumerIndex < remainder ? 1 : 0);
                attachment.ReceivedPower = receivedPower;
            }
        }
    }

    private void ApplyBatteryInputPower()
    {
        foreach (CircuitNet net in nets)
        {
            foreach (CircuitPortAttachment attachment in net.Ports)
            {
                if (attachment.Port.Type == BoardPortType.Input &&
                    attachment.Component is BatteryComponent battery)
                {
                    battery.SetReceivedPower(
                        attachment.Port,
                        attachment.ReceivedPower);
                }
            }
        }
    }

    private void ApplyConsumerReceivedPower()
    {
        foreach (CircuitNet net in nets)
        {
            foreach (CircuitPortAttachment attachment in net.Ports)
            {
                if (attachment.Port.Type == BoardPortType.Input &&
                    attachment.Component is ICircuitPowerConsumer consumer &&
                    attachment.Component is not BatteryComponent)
                {
                    consumer.SetReceivedPower(
                        attachment.Port,
                        attachment.ReceivedPower);
                }
            }
        }
    }

    private void CalculateScoreRates()
    {
        scoreRateBuffer.Clear();
        scoreRateSet.Clear();

        foreach (CircuitNet net in nets)
        {
            foreach (CircuitPortAttachment attachment in net.Ports)
            {
                if (attachment.Component is ScoredPowerConsumerComponent consumer &&
                    scoreRateSet.Add(consumer))
                {
                    scoreRateBuffer.Add(consumer);
                }
            }
        }

        foreach (ScoredPowerConsumerComponent consumer in scoreRateBuffer)
            consumer.RefreshScoreRateReport();
    }

    private void UpdateWireVisualStates()
    {
        GridBoard targetBoard = Board;
        if (targetBoard == null)
            return;

        foreach (CircuitNet net in nets)
        {
            foreach (Vector2Int wireCell in net.WireCells)
            {
                WireView view = targetBoard.GetWireView(wireCell);
                if (view != null)
                {
                    view.SetPowerState(
                        net.NetId,
                        net.AvailablePower);
                }
            }
        }
    }

    private void ResetWireVisualStates()
    {
        GridBoard targetBoard = ResolveBoard();
        if (targetBoard == null)
            return;

        foreach (Vector2Int wireCell in targetBoard.WireCells)
        {
            WireView view = targetBoard.GetWireView(wireCell);
            if (view != null)
                view.SetPowerState(NoNetId, 0);
        }
    }

    private void GatherPowerTickParticipants()
    {
        powerTickBuffer.Clear();
        powerTickSet.Clear();

        foreach (CircuitNet net in nets)
        {
            foreach (CircuitPortAttachment attachment in net.Ports)
            {
                if (attachment.Component is ICircuitPowerTick participant &&
                    attachment.Component != null &&
                    powerTickSet.Add(participant))
                {
                    powerTickBuffer.Add(participant);
                }
            }
        }
    }

    private void ResetPreviousPowerParticipants()
    {
        GatherPowerTickParticipants();

        foreach (ICircuitPowerTick participant in powerTickBuffer)
            participant.ResetPowerState();
    }

    private void ResetPreviousConsumers()
    {
        scoreRateBuffer.Clear();
        scoreRateSet.Clear();

        foreach (CircuitNet net in nets)
        {
            foreach (CircuitPortAttachment attachment in net.Ports)
            {
                if (attachment.Component != null &&
                    attachment.Component is ICircuitPowerConsumer consumer)
                {
                    consumer.SetReceivedPower(attachment.Port, 0);

                    if (attachment.Component is ScoredPowerConsumerComponent scoredConsumer &&
                        scoreRateSet.Add(scoredConsumer))
                    {
                        scoreRateBuffer.Add(scoredConsumer);
                    }
                }
            }
        }

        foreach (ScoredPowerConsumerComponent consumer in scoreRateBuffer)
            consumer.RefreshScoreRateReport();
    }

    private static int FindPortIndex(BoardComponent component, BoardPort port)
    {
        for (int portIndex = 0; portIndex < component.Ports.Count; portIndex++)
        {
            if (ReferenceEquals(component.Ports[portIndex], port))
                return portIndex;
        }

        return -1;
    }

    private GridBoard ResolveBoard()
    {
        if (board == null)
            board = GetComponent<GridBoard>();

        return board;
    }

    private void DetectTopologyRevisionChange()
    {
        GridBoard targetBoard = ResolveBoard();
        if (targetBoard != null &&
            targetBoard.TopologyRevision != lastBuiltTopologyRevision)
        {
            topologyDirty = true;
        }
    }

    private void BindBoard()
    {
        GridBoard targetBoard = ResolveBoard();
        if (subscribedBoard == targetBoard)
            return;

        UnbindBoard();
        subscribedBoard = targetBoard;
        if (isActiveAndEnabled && subscribedBoard != null)
            subscribedBoard.TopologyChanged += MarkTopologyDirty;

        topologyDirty = true;
    }

    private void UnbindBoard()
    {
        if (subscribedBoard != null)
            subscribedBoard.TopologyChanged -= MarkTopologyDirty;

        subscribedBoard = null;
    }
}
