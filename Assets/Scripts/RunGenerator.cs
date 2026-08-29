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
        lastGeneratedCounts = default;

        ClearBoard();
        BuildComponentKindList(
            requestedCounts,
            (targetBoard.Width / BoardComponent.FootprintWidth) *
            (targetBoard.Height / BoardComponent.FootprintHeight));

        Transform stagingRoot = CreateStagingRoot();
        try
        {
            return GenerateLayout(
                targetBoard,
                requestedCounts,
                random,
                stagingRoot);
        }
        finally
        {
            DestroySafely(stagingRoot.gameObject);
        }
    }

    private bool GenerateLayout(
        GridBoard targetBoard,
        RunComponentCounts requestedCounts,
        System.Random random,
        Transform stagingRoot)
    {

        for (int boardAttempt = 0;
             boardAttempt < boardRegenerationAttempts;
             boardAttempt++)
        {
            DestroyGeneratedComponents();
            reservedPortCells.Clear();

            for (int componentIndex = 0;
                 componentIndex < componentKinds.Count;
                 componentIndex++)
            {
                ComponentKind kind = componentKinds[componentIndex];
                BoardComponent component = CreateComponent(
                    kind,
                    componentIndex,
                    stagingRoot);
                if (component == null)
                {
                    DestroyGeneratedComponents();
                    reservedPortCells.Clear();
                    return Fail(
                        string.IsNullOrEmpty(lastFailureReason)
                            ? $"Could not create {kind}."
                            : lastFailureReason,
                        true);
                }

                if (!TryPlaceComponent(component, targetBoard, random))
                {
                    DestroyComponentObject(component);
                    continue;
                }

                generatedComponents.Add(component);
            }

            if (ValidateConsumerReachability(
                    targetBoard,
                    out string reachabilityFailureReason))
            {
                return CompleteGeneration(requestedCounts);
            }

            lastFailureReason =
                $"{reachabilityFailureReason} " +
                $"(layout attempt {boardAttempt + 1}/" +
                $"{boardRegenerationAttempts}).";

            if (boardAttempt + 1 < boardRegenerationAttempts)
                continue;

            RemoveUnreachableConsumers(targetBoard);
            RebuildReservedPortCells(targetBoard);
            return CompleteGeneration(requestedCounts);
        }

        return CompleteGeneration(requestedCounts);
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
        lastGeneratedCounts = default;

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
        HashSet<Vector2Int> reachableCells =
            BuildGeneratorReachableCells(targetBoard);

        foreach (BoardComponent component in generatedComponents)
        {
            if (component is not ScoredPowerConsumerComponent ||
                !component.IsPlaced ||
                AreAllConsumerInputsReachable(
                    component,
                    targetBoard,
                    reachableCells))
            {
                continue;
            }

            failureReason =
                $"{component.name} input port has no empty-cell route " +
                "to a generator output";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    private HashSet<Vector2Int> BuildGeneratorReachableCells(
        GridBoard targetBoard)
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

        return reachableCells;
    }

    private static bool AreAllConsumerInputsReachable(
        BoardComponent component,
        GridBoard targetBoard,
        HashSet<Vector2Int> reachableCells)
    {
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
                return false;
            }
        }

        return true;
    }

    private void RemoveUnreachableConsumers(GridBoard targetBoard)
    {
        HashSet<Vector2Int> reachableCells =
            BuildGeneratorReachableCells(targetBoard);

        for (int componentIndex = generatedComponents.Count - 1;
             componentIndex >= 0;
             componentIndex--)
        {
            BoardComponent component = generatedComponents[componentIndex];
            if (component is not ScoredPowerConsumerComponent ||
                AreAllConsumerInputsReachable(
                    component,
                    targetBoard,
                    reachableCells))
            {
                continue;
            }

            generatedComponents.RemoveAt(componentIndex);
            DestroyComponentObject(component);
        }
    }

    private void RebuildReservedPortCells(GridBoard targetBoard)
    {
        reservedPortCells.Clear();

        foreach (BoardComponent component in generatedComponents)
        {
            if (component == null || !component.IsPlaced)
                continue;

            for (int portIndex = 0; portIndex < component.Ports.Count; portIndex++)
            {
                if (TryGetEmptyCellOutsidePort(
                        component,
                        portIndex,
                        targetBoard,
                        out Vector2Int outsideCell))
                {
                    reservedPortCells.Add(outsideCell);
                }
            }
        }
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

    private void BuildComponentKindList(
        RunComponentCounts counts,
        int maximumComponentCount)
    {
        componentKinds.Clear();
        AddKinds(ComponentKind.Generator, counts.generators, maximumComponentCount);
        AddKinds(ComponentKind.Battery, counts.batteries, maximumComponentCount);
        AddKinds(ComponentKind.Lamp, counts.lamps, maximumComponentCount);
        AddKinds(ComponentKind.Fan, counts.fans, maximumComponentCount);
    }

    private void AddKinds(
        ComponentKind kind,
        int count,
        int maximumComponentCount)
    {
        int remainingCapacity = Mathf.Max(
            0,
            maximumComponentCount - componentKinds.Count);
        int amountToAdd = Mathf.Min(count, remainingCapacity);
        for (int index = 0; index < amountToAdd; index++)
            componentKinds.Add(kind);
    }

    private bool CompleteGeneration(RunComponentCounts requestedCounts)
    {
        lastGeneratedCounts = CountGeneratedComponents();
        int skippedCount = Mathf.Max(
            0,
            requestedCounts.Total - lastGeneratedCounts.Total);

        lastFailureReason = string.Empty;
        if (skippedCount > 0)
        {
            Debug.LogWarning(
                $"Generated difficulty {difficulty} with " +
                $"{lastGeneratedCounts.Total}/{requestedCounts.Total} components. " +
                $"Skipped {skippedCount} component(s) that could not be placed safely.",
                this);
        }

        return true;
    }

    private RunComponentCounts CountGeneratedComponents()
    {
        int generators = 0;
        int batteries = 0;
        int lamps = 0;
        int fans = 0;

        foreach (BoardComponent component in generatedComponents)
        {
            switch (component)
            {
                case GeneratorComponent:
                    generators++;
                    break;
                case BatteryComponent:
                    batteries++;
                    break;
                case LampComponent:
                    lamps++;
                    break;
                case FanComponent:
                    fans++;
                    break;
            }
        }

        return new RunComponentCounts(generators, batteries, lamps, fans);
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

    private Transform CreateStagingRoot()
    {
        GameObject stagingObject = new("Run Generation Staging");
        stagingObject.hideFlags = HideFlags.HideInHierarchy |
                                  HideFlags.DontSave;
        stagingObject.transform.SetParent(transform, false);
        stagingObject.SetActive(false);
        return stagingObject.transform;
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

        if (Application.isPlaying)
        {
            componentObject.transform.SetParent(null, false);
            componentObject.hideFlags |= HideFlags.HideInHierarchy;
        }

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
