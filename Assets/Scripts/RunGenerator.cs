using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct RunComponentCounts
{
    [Min(0)] public int generators;
    [Min(0)] public int batteries;
    [Min(0)] public int lamps;
    [Min(0)] public int fans;

    public int Total
    {
        get
        {
            long total = (long)generators + batteries + lamps + fans;
            return (int)Math.Min(int.MaxValue, total);
        }
    }

    public RunComponentCounts(int generators, int batteries, int lamps, int fans)
    {
        this.generators = generators;
        this.batteries = batteries;
        this.lamps = lamps;
        this.fans = fans;
    }

    internal RunComponentCounts Clamped()
    {
        return new RunComponentCounts(
            Mathf.Max(0, generators),
            Mathf.Max(0, batteries),
            Mathf.Max(0, lamps),
            Mathf.Max(0, fans));
    }

}

[DisallowMultipleComponent]
[RequireComponent(typeof(GridBoard))]
public sealed class RunGenerator : MonoBehaviour
{
    private static readonly Vector2Int[] CardinalCellOffsets =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private enum ComponentKind
    {
        Generator,
        Battery,
        Lamp,
        Fan
    }

    private readonly struct PlacementCandidate
    {
        public Vector2Int Position { get; }
        public int RotationSteps { get; }

        public PlacementCandidate(Vector2Int position, int rotationSteps)
        {
            Position = position;
            RotationSteps = rotationSteps;
        }
    }

    [SerializeField]
    [Tooltip("Display grid that receives the generated run. If empty, the GridBoard on this object is used.")]
    private GridBoard board;

    [Header("Run")]
    [SerializeField, Range(
        DifficultyProfile.MinimumDifficulty,
        DifficultyProfile.MaximumDifficulty)]
    private int difficulty = 1;

    [SerializeField]
    private int seed = 12345;

    [Header("Difficulty")]
    [SerializeField]
    private DifficultyProfile difficultyProfile = new();

    [Header("Optional Visual Prefabs")]
    [SerializeField]
    private GameObject generatorPrefab;

    [SerializeField]
    private GameObject batteryPrefab;

    [SerializeField]
    private GameObject lampPrefab;

    [SerializeField]
    private GameObject fanPrefab;

    [Header("Placement Retry Limits")]
    [SerializeField, Min(1)]
    private int placementAttemptsPerComponent = 96;

    [SerializeField, Min(1)]
    private int boardRegenerationAttempts = 12;

    private readonly List<BoardComponent> generatedComponents = new();
    private readonly List<ComponentKind> componentKinds = new();
    private readonly List<PlacementCandidate> placementCandidates = new();
    private readonly HashSet<Vector2Int> reservedPortCells = new();

    private string lastFailureReason;
    private RunComponentCounts lastGeneratedCounts;

    public GridBoard Board => ResolveBoard();
    public int Difficulty => difficulty;
    public int Seed => seed;
    public DifficultyProfile Profile => ResolveProfile();
    public RunComponentCounts LastGeneratedCounts => lastGeneratedCounts;
    public IReadOnlyList<BoardComponent> GeneratedComponents => generatedComponents;
    public IReadOnlyCollection<Vector2Int> ReservedPortCells => reservedPortCells;
    public string LastFailureReason => lastFailureReason;

    private void Reset()
    {
        board = GetComponent<GridBoard>();
        difficulty = 1;
        seed = 12345;
        difficultyProfile = new DifficultyProfile();
        placementAttemptsPerComponent = 96;
        boardRegenerationAttempts = 12;
    }

    private void OnValidate()
    {
        difficulty = Mathf.Clamp(
            difficulty,
            DifficultyProfile.MinimumDifficulty,
            DifficultyProfile.MaximumDifficulty);
        ResolveProfile().Validate();
        placementAttemptsPerComponent = Mathf.Max(1, placementAttemptsPerComponent);
        boardRegenerationAttempts = Mathf.Max(1, boardRegenerationAttempts);
    }

    public bool Generate()
    {
        return Generate(difficulty, seed);
    }

    public bool Generate(int targetDifficulty, int runSeed)
    {
        GridBoard targetBoard = ResolveBoard();
        if (targetBoard == null)
            return Fail("RunGenerator requires a GridBoard.", true);

        difficulty = Mathf.Clamp(
            targetDifficulty,
            DifficultyProfile.MinimumDifficulty,
            DifficultyProfile.MaximumDifficulty);
        seed = runSeed;
        lastFailureReason = string.Empty;

        System.Random random = new(runSeed);
        RunComponentCounts requestedCounts = RollComponentCounts(difficulty, random);
        lastGeneratedCounts = requestedCounts;
        if (requestedCounts.Total > targetBoard.CellCount / 4)
        {
            ClearBoard();
            return Fail(
                $"Difficulty {difficulty} requests {requestedCounts.Total} components, " +
                $"but the {targetBoard.Width}x{targetBoard.Height} board can fit at most " +
                $"{targetBoard.CellCount / 4} non-overlapping 2x2 footprints.",
                true);
        }

        ClearBoard();
        BuildComponentKindList(requestedCounts);

        for (int boardAttempt = 0;
             boardAttempt < boardRegenerationAttempts;
             boardAttempt++)
        {
            DestroyGeneratedComponents();
            reservedPortCells.Clear();

            bool placedEveryComponent = true;
            for (int componentIndex = 0;
                 componentIndex < componentKinds.Count;
                 componentIndex++)
            {
                ComponentKind kind = componentKinds[componentIndex];
                BoardComponent component = CreateComponent(
                    kind,
                    componentIndex,
                    targetBoard.EnsureComponentsRoot());
                if (component == null)
                {
                    placedEveryComponent = false;
                    break;
                }

                generatedComponents.Add(component);
                if (!TryPlaceComponent(component, targetBoard, random))
                {
                    lastFailureReason =
                        $"Could not place {kind} after checking up to " +
                        $"{placementAttemptsPerComponent} candidates " +
                        $"(layout attempt {boardAttempt + 1}/{boardRegenerationAttempts}).";
                    placedEveryComponent = false;
                    break;
                }
            }

            if (placedEveryComponent)
            {
                if (!ValidateConsumerReachability(
                        targetBoard,
                        out string reachabilityFailureReason))
                {
                    lastFailureReason =
                        $"{reachabilityFailureReason} " +
                        $"(layout attempt {boardAttempt + 1}/" +
                        $"{boardRegenerationAttempts}).";
                    continue;
                }

                lastFailureReason = string.Empty;
                return true;
            }
        }

        DestroyGeneratedComponents();
        reservedPortCells.Clear();
        return Fail(
            string.IsNullOrEmpty(lastFailureReason)
                ? $"Failed to generate difficulty {difficulty} after " +
                  $"{boardRegenerationAttempts} complete layout attempts."
                : lastFailureReason,
            true);
    }

    public RunComponentCounts PreviewCounts(int targetDifficulty, int runSeed)
    {
        return RollComponentCounts(
            targetDifficulty,
            new System.Random(runSeed));
    }

    public RunComponentCounts GetCountsForDifficulty(int targetDifficulty)
    {
        return PreviewCounts(targetDifficulty, seed);
    }

    public void ClearBoard()
    {
        GridBoard targetBoard = ResolveBoard();
        generatedComponents.Clear();
        reservedPortCells.Clear();
        lastFailureReason = string.Empty;

        if (targetBoard == null)
            return;

        targetBoard.ClearAllWires();
        Transform componentsRoot = targetBoard.ComponentsRoot;
        if (componentsRoot == null)
            return;

        BoardComponent[] components =
            componentsRoot.GetComponentsInChildren<BoardComponent>(true);
        foreach (BoardComponent component in components)
        {
            if (component != null)
                DestroyComponentObject(component);
        }
    }

    private bool TryPlaceComponent(
        BoardComponent component,
        GridBoard targetBoard,
        System.Random random)
    {
        BuildPlacementCandidates(targetBoard);
        Shuffle(placementCandidates, random);

        int candidateCount = Mathf.Min(
            placementAttemptsPerComponent,
            placementCandidates.Count);
        for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
        {
            PlacementCandidate candidate = placementCandidates[candidateIndex];
            if (!CanPlaceWithPortClearance(
                    component,
                    targetBoard,
                    candidate.Position,
                    candidate.RotationSteps,
                    out List<Vector2Int> portCells))
            {
                continue;
            }

            if (!targetBoard.TryPlaceComponent(
                    component,
                    candidate.Position,
                    candidate.RotationSteps,
                    out _))
            {
                continue;
            }

            foreach (Vector2Int portCell in portCells)
                reservedPortCells.Add(portCell);

            component.gameObject.SetActive(true);
            return true;
        }

        return false;
    }

    private bool CanPlaceWithPortClearance(
        BoardComponent component,
        GridBoard targetBoard,
        Vector2Int position,
        int rotationSteps,
        out List<Vector2Int> portCells)
    {
        portCells = new List<Vector2Int>(component.Ports.Count);

        if (!targetBoard.CanPlaceComponent(
                component,
                position,
                rotationSteps,
                out _))
        {
            return false;
        }

        for (int y = 0; y < BoardComponent.FootprintHeight; y++)
        {
            for (int x = 0; x < BoardComponent.FootprintWidth; x++)
            {
                Vector2Int footprintCell = position + new Vector2Int(x, y);
                if (reservedPortCells.Contains(footprintCell))
                    return false;
            }
        }

        foreach (BoardPort port in component.Ports)
        {
            if (port == null)
                return false;

            Vector2Int componentCell = position +
                                       port.GetRotatedCellOffset(
                                           rotationSteps,
                                           BoardComponent.FootprintSize);
            BoardPortDirection direction = port.GetRotatedDirection(rotationSteps);
            Vector2Int outsideCell = componentCell +
                                     BoardPort.DirectionToCellOffset(direction);

            if (!targetBoard.IsInside(outsideCell) ||
                IsInsideFootprint(outsideCell, position) ||
                targetBoard.GetOccupant(outsideCell) != null)
            {
                return false;
            }

            portCells.Add(outsideCell);
        }

        return true;
    }

    private bool ValidateConsumerReachability(
        GridBoard targetBoard,
        out string failureReason)
    {
        HashSet<Vector2Int> reachableCells = new();
        Queue<Vector2Int> frontier = new();

        foreach (BoardComponent component in generatedComponents)
        {
            if (component is not GeneratorComponent || !component.IsPlaced)
                continue;

            for (int portIndex = 0; portIndex < component.Ports.Count; portIndex++)
            {
                BoardPort port = component.Ports[portIndex];
                if (port == null ||
                    port.Type != BoardPortType.Output ||
                    !TryGetEmptyCellOutsidePort(
                        component,
                        portIndex,
                        targetBoard,
                        out Vector2Int outsideCell) ||
                    !reachableCells.Add(outsideCell))
                {
                    continue;
                }

                frontier.Enqueue(outsideCell);
            }
        }

        while (frontier.Count > 0)
        {
            Vector2Int cell = frontier.Dequeue();
            foreach (Vector2Int offset in CardinalCellOffsets)
            {
                Vector2Int neighbor = cell + offset;
                if (!targetBoard.IsInside(neighbor) ||
                    targetBoard.GetOccupant(neighbor) != null ||
                    !reachableCells.Add(neighbor))
                {
                    continue;
                }

                frontier.Enqueue(neighbor);
            }
        }

        foreach (BoardComponent component in generatedComponents)
        {
            if (component is not ScoredPowerConsumerComponent ||
                !component.IsPlaced)
            {
                continue;
            }

            for (int portIndex = 0; portIndex < component.Ports.Count; portIndex++)
            {
                BoardPort port = component.Ports[portIndex];
                if (port == null || port.Type != BoardPortType.Input)
                    continue;

                if (!TryGetEmptyCellOutsidePort(
                        component,
                        portIndex,
                        targetBoard,
                        out Vector2Int outsideCell) ||
                    !reachableCells.Contains(outsideCell))
                {
                    failureReason =
                        $"{component.name} input port has no empty-cell route " +
                        "to a generator output";
                    return false;
                }
            }
        }

        failureReason = string.Empty;
        return true;
    }

    private static bool TryGetEmptyCellOutsidePort(
        BoardComponent component,
        int portIndex,
        GridBoard targetBoard,
        out Vector2Int outsideCell)
    {
        if (component.TryGetPortGridPose(
                portIndex,
                out Vector2Int componentCell,
                out BoardPortDirection direction))
        {
            outsideCell = componentCell +
                          BoardPort.DirectionToCellOffset(direction);
            return targetBoard.IsInside(outsideCell) &&
                   targetBoard.GetOccupant(outsideCell) == null;
        }

        outsideCell = default;
        return false;
    }

    private void BuildPlacementCandidates(GridBoard targetBoard)
    {
        placementCandidates.Clear();
        int maximumX = targetBoard.Width - BoardComponent.FootprintWidth;
        int maximumY = targetBoard.Height - BoardComponent.FootprintHeight;

        for (int y = 0; y <= maximumY; y++)
        {
            for (int x = 0; x <= maximumX; x++)
            {
                for (int rotationSteps = 0; rotationSteps < 4; rotationSteps++)
                {
                    placementCandidates.Add(
                        new PlacementCandidate(
                            new Vector2Int(x, y),
                            rotationSteps));
                }
            }
        }
    }

    private void BuildComponentKindList(RunComponentCounts counts)
    {
        componentKinds.Clear();
        AddKinds(ComponentKind.Generator, counts.generators);
        AddKinds(ComponentKind.Battery, counts.batteries);
        AddKinds(ComponentKind.Lamp, counts.lamps);
        AddKinds(ComponentKind.Fan, counts.fans);
    }

    private void AddKinds(ComponentKind kind, int count)
    {
        for (int index = 0; index < count; index++)
            componentKinds.Add(kind);
    }

    private RunComponentCounts RollComponentCounts(
        int targetDifficulty,
        System.Random random)
    {
        DifficultyAmounts amounts = ResolveProfile().Roll(
            Mathf.Clamp(
                targetDifficulty,
                DifficultyProfile.MinimumDifficulty,
                DifficultyProfile.MaximumDifficulty),
            random);

        int lamps = 0;
        int fans = 0;
        for (int consumerIndex = 0;
             consumerIndex < amounts.Consumers;
             consumerIndex++)
        {
            if (random.Next(2) == 0)
                lamps++;
            else
                fans++;
        }

        return new RunComponentCounts(
            amounts.Generators,
            amounts.Batteries,
            lamps,
            fans);
    }

    private BoardComponent CreateComponent(
        ComponentKind kind,
        int componentIndex,
        Transform parent)
    {
        GameObject prefab = GetPrefab(kind);
        GameObject instance;

        if (prefab != null)
        {
            instance = Instantiate(prefab, parent);
            instance.SetActive(false);
        }
        else
        {
            instance = new GameObject();
            instance.SetActive(false);
            instance.transform.SetParent(parent, false);
        }

        instance.name = $"{kind} {componentIndex + 1:00}";
        Type requiredType = GetComponentType(kind);
        BoardComponent component = instance.GetComponent(requiredType) as BoardComponent;
        BoardComponent existingComponent = instance.GetComponent<BoardComponent>();

        if (component == null && existingComponent != null)
        {
            existingComponent.ClearPlacement();
            DestroySafely(instance);
            Fail(
                $"Prefab for {kind} contains {existingComponent.GetType().Name} " +
                $"instead of {requiredType.Name}.",
                false);
            return null;
        }

        if (component == null)
            component = instance.AddComponent(requiredType) as BoardComponent;

        if (component == null)
        {
            DestroySafely(instance);
            Fail($"Could not create {requiredType.Name}.", false);
            return null;
        }

        component.ClearPlacement();
        return component;
    }

    private GameObject GetPrefab(ComponentKind kind)
    {
        return kind switch
        {
            ComponentKind.Generator => generatorPrefab,
            ComponentKind.Battery => batteryPrefab,
            ComponentKind.Lamp => lampPrefab,
            ComponentKind.Fan => fanPrefab,
            _ => null
        };
    }

    private static Type GetComponentType(ComponentKind kind)
    {
        return kind switch
        {
            ComponentKind.Generator => typeof(GeneratorComponent),
            ComponentKind.Battery => typeof(BatteryComponent),
            ComponentKind.Lamp => typeof(LampComponent),
            ComponentKind.Fan => typeof(FanComponent),
            _ => typeof(BoardComponent)
        };
    }

    private void DestroyGeneratedComponents()
    {
        foreach (BoardComponent component in generatedComponents)
        {
            if (component != null)
                DestroyComponentObject(component);
        }

        generatedComponents.Clear();
    }

    private static void DestroyComponentObject(BoardComponent component)
    {
        if (component == null)
            return;

        component.ClearPlacement();
        GameObject componentObject = component.gameObject;
        componentObject.SetActive(false);
        DestroySafely(componentObject);
    }

    private static void DestroySafely(UnityEngine.Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    private static bool IsInsideFootprint(Vector2Int cell, Vector2Int position)
    {
        return cell.x >= position.x &&
               cell.x < position.x + BoardComponent.FootprintWidth &&
               cell.y >= position.y &&
               cell.y < position.y + BoardComponent.FootprintHeight;
    }

    private static void Shuffle<T>(IList<T> values, System.Random random)
    {
        for (int index = values.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }

    private GridBoard ResolveBoard()
    {
        if (board == null)
            board = GetComponent<GridBoard>();

        return board;
    }

    private DifficultyProfile ResolveProfile()
    {
        difficultyProfile ??= new DifficultyProfile();
        return difficultyProfile;
    }

    private bool Fail(string reason, bool logError)
    {
        lastFailureReason = reason;
        if (logError)
            Debug.LogError(reason, this);

        return false;
    }
}
