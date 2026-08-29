using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public enum ChaosEventType
{
    WireFault,
    BatteryFluctuation,
    GeneratorOutputShift,
    ConsumerRequiredPowerShift,
    ConsumerIdealPowerShift = ConsumerRequiredPowerShift,
    ConsumerRemoval
}

public readonly struct ChaosEvent
{
    public ChaosEventType Type { get; }
    public float Strength { get; }
    public int AffectedCount { get; }

    public ChaosEvent(
        ChaosEventType type,
        float strength,
        int affectedCount)
    {
        Type = type;
        Strength = Mathf.Clamp01(strength);
        AffectedCount = Mathf.Max(0, affectedCount);
    }
}

/// <summary>
/// Owns all per-run randomness that can disturb an otherwise deterministic
/// circuit. CircuitSystem remains responsible only for electricity simulation.
/// </summary>
[DisallowMultipleComponent]
public sealed class ChaosController : MonoBehaviour
{
    private const int ChaosSeedSalt = unchecked((int)0x6D2B79F5);
    private const int MinimumGeneratorMaximumPower =
        GeneratorComponent.MinimumPowerPerActiveOutput + 1;
    private const int MinimumConsumerMaximumRequiredPower =
        ScoredPowerConsumerComponent.DefaultRequiredPower + 1;

    [Header("Target")]
    [SerializeField]
    [Tooltip("Board affected by chaos events. A board on this object is found automatically when empty.")]
    private GridBoard board;

    [SerializeField]
    [Tooltip("Owner used to completely unregister and destroy generated consumers.")]
    private RunGenerator runGenerator;

    [SerializeField]
    [Tooltip("Circuit rebuilt through its normal dirty-topology path after a consumer is removed.")]
    private CircuitSystem circuitSystem;

    [Header("Frequency")]
    [SerializeField, Min(0.1f)]
    [Tooltip("Average seconds between events at the lowest non-zero ChaosValue.")]
    private float lowChaosInterval = 18f;

    [SerializeField, Min(0.1f)]
    [Tooltip("Average seconds between events at the maximum ChaosValue.")]
    private float highChaosInterval = 4f;

    [SerializeField, Range(0f, 0.9f)]
    [Tooltip("Random variation applied around the interval selected by ChaosValue.")]
    private float intervalJitter = 0.25f;

    [Header("Strength")]
    [SerializeField, Min(1)]
    private int maximumWireFaultCells = 4;

    [SerializeField, Min(1)]
    private int maximumBatteryTargets = 3;

    [SerializeField, Range(0f, 1f)]
    private float minimumBatterySwing = 0.1f;

    [SerializeField, Range(0f, 1f)]
    private float maximumBatterySwing = 0.5f;

    [SerializeField, Min(1)]
    [Tooltip("Maximum number of generators changed by one full-strength chaos event.")]
    private int maximumGeneratorTargets = 3;

    [SerializeField, Min(MinimumGeneratorMaximumPower)]
    [Tooltip("Highest per-output generator power available at maximum ChaosValue.")]
    private int maximumGeneratorPowerAtFullChaos = 6;

    [SerializeField, Min(1)]
    [Tooltip("Maximum number of Lamp/Fan required power values changed by one full-strength event.")]
    private int maximumConsumerTargets = 3;

    [FormerlySerializedAs("maximumConsumerIdealPowerAtFullChaos")]
    [SerializeField, Min(MinimumConsumerMaximumRequiredPower)]
    [Tooltip("Highest Lamp/Fan required power available at maximum ChaosValue.")]
    private int maximumConsumerRequiredPowerAtFullChaos = 6;

    [SerializeField, Min(1)]
    [Tooltip("Maximum generated Lamps/Fans removed by one full-strength event.")]
    private int maximumConsumerRemovals = 2;

    private readonly List<Vector2Int> wireCandidates = new();
    private readonly List<BatteryComponent> batteryCandidates = new();
    private readonly List<GeneratorComponent> generatorCandidates = new();
    private readonly List<ScoredPowerConsumerComponent> consumerCandidates = new();
    private readonly List<ScoredPowerConsumerComponent> removableConsumerCandidates = new();
    private readonly List<ChaosEventType> availableEventTypes = new();

    private System.Random random;
    private bool runActive;
    private int chaosValue;
    private float timeUntilNextEvent = float.PositiveInfinity;
    private int eventCount;
    private ChaosEvent lastEvent;

    public GridBoard Board => ResolveBoard();
    public RunGenerator RunGenerator => ResolveRunGenerator();
    public CircuitSystem CircuitSystem => ResolveCircuitSystem();
    public bool IsRunning => runActive;
    public bool IsChaosActive => runActive && chaosValue > RunSettings.MinimumChaosValue;
    public int ChaosValue => chaosValue;
    public float NormalizedChaos => Mathf.InverseLerp(
        RunSettings.MinimumChaosValue,
        RunSettings.MaximumChaosValue,
        chaosValue);
    public float TimeUntilNextEvent => timeUntilNextEvent;
    public int EventCount => eventCount;
    public ChaosEvent LastEvent => lastEvent;

    public event Action<ChaosEvent> EventTriggered;

    private void Reset()
    {
        board = GetComponent<GridBoard>();
        runGenerator = GetComponent<RunGenerator>();
        circuitSystem = GetComponent<CircuitSystem>();
    }

    private void OnValidate()
    {
        lowChaosInterval = Mathf.Max(0.1f, lowChaosInterval);
        highChaosInterval = Mathf.Clamp(
            highChaosInterval,
            0.1f,
            lowChaosInterval);
        intervalJitter = Mathf.Clamp(intervalJitter, 0f, 0.9f);
        maximumWireFaultCells = Mathf.Max(1, maximumWireFaultCells);
        maximumBatteryTargets = Mathf.Max(1, maximumBatteryTargets);
        minimumBatterySwing = Mathf.Clamp01(minimumBatterySwing);
        maximumBatterySwing = Mathf.Clamp(
            maximumBatterySwing,
            minimumBatterySwing,
            1f);
        maximumGeneratorTargets = Mathf.Max(1, maximumGeneratorTargets);
        maximumGeneratorPowerAtFullChaos = Mathf.Max(
            MinimumGeneratorMaximumPower,
            maximumGeneratorPowerAtFullChaos);
        maximumConsumerTargets = Mathf.Max(1, maximumConsumerTargets);
        maximumConsumerRequiredPowerAtFullChaos = Mathf.Max(
            MinimumConsumerMaximumRequiredPower,
            maximumConsumerRequiredPowerAtFullChaos);
        maximumConsumerRemovals = Mathf.Max(1, maximumConsumerRemovals);
    }

    private void Update()
    {
        AdvanceChaos(Time.deltaTime);
    }

    private void OnDisable()
    {
        StopRun();
    }

    /// <summary>
    /// Starts a deterministic chaos stream dedicated to this run. ChaosValue is
    /// independent from difficulty, board dimensions, and duration.
    /// </summary>
    public void StartRun(GridBoard targetBoard, int targetChaosValue, int runSeed)
    {
        StartRun(
            targetBoard,
            ResolveRunGenerator(),
            targetChaosValue,
            runSeed);
    }

    public void StartRun(
        GridBoard targetBoard,
        RunGenerator targetRunGenerator,
        int targetChaosValue,
        int runSeed)
    {
        board = targetBoard != null ? targetBoard : ResolveBoard();
        runGenerator = targetRunGenerator != null
            ? targetRunGenerator
            : ResolveRunGenerator();
        chaosValue = Mathf.Clamp(
            targetChaosValue,
            RunSettings.MinimumChaosValue,
            RunSettings.MaximumChaosValue);
        random = new System.Random(unchecked(runSeed ^ ChaosSeedSalt));
        runActive = true;
        eventCount = 0;
        lastEvent = default;
        timeUntilNextEvent = IsChaosActive
            ? RollNextInterval()
            : float.PositiveInfinity;
    }

    public void StopRun()
    {
        runActive = false;
        timeUntilNextEvent = float.PositiveInfinity;
        random = null;
        wireCandidates.Clear();
        batteryCandidates.Clear();
        generatorCandidates.Clear();
        consumerCandidates.Clear();
        removableConsumerCandidates.Clear();
        availableEventTypes.Clear();
    }

    /// <summary>
    /// Advances only the chaos schedule and returns the number of gameplay
    /// events that actually affected the board.
    /// </summary>
    public int AdvanceChaos(float deltaTime)
    {
        if (!IsChaosActive || deltaTime <= 0f)
            return 0;

        timeUntilNextEvent -= deltaTime;
        int triggeredCount = 0;
        int safetyCounter = 0;

        while (timeUntilNextEvent <= 0f && safetyCounter++ < 100)
        {
            if (TriggerNextEvent())
                triggeredCount++;

            timeUntilNextEvent += RollNextInterval();
        }

        return triggeredCount;
    }

    /// <summary>
    /// Immediately attempts one random event. Useful for debug controls and
    /// deterministic gameplay tests without involving CircuitSystem.
    /// </summary>
    public bool TriggerNextEvent()
    {
        if (!IsChaosActive || random == null)
            return false;

        CollectCandidates();
        BuildAvailableEventTypes();
        if (availableEventTypes.Count == 0)
            return false;

        ChaosEventType selectedType = availableEventTypes[
            random.Next(availableEventTypes.Count)];
        ChaosEvent chaosEvent = selectedType switch
        {
            ChaosEventType.WireFault => ApplyWireFault(),
            ChaosEventType.BatteryFluctuation => ApplyBatteryFluctuation(),
            ChaosEventType.GeneratorOutputShift => ApplyGeneratorOutputShift(),
            ChaosEventType.ConsumerRequiredPowerShift => ApplyConsumerRequiredPowerShift(),
            ChaosEventType.ConsumerRemoval => ApplyConsumerRemoval(),
            _ => default
        };

        if (chaosEvent.AffectedCount <= 0)
            return false;

        eventCount++;
        lastEvent = chaosEvent;
        EventTriggered?.Invoke(chaosEvent);
        return true;
    }

    private void BuildAvailableEventTypes()
    {
        availableEventTypes.Clear();

        if (wireCandidates.Count > 0)
            availableEventTypes.Add(ChaosEventType.WireFault);

        if (batteryCandidates.Count > 0)
            availableEventTypes.Add(ChaosEventType.BatteryFluctuation);

        if (generatorCandidates.Count > 0)
            availableEventTypes.Add(ChaosEventType.GeneratorOutputShift);

        if (consumerCandidates.Count > 0)
            availableEventTypes.Add(ChaosEventType.ConsumerRequiredPowerShift);

        if (removableConsumerCandidates.Count > 0)
            availableEventTypes.Add(ChaosEventType.ConsumerRemoval);
    }

    private ChaosEvent ApplyWireFault()
    {
        float strength = NormalizedChaos;
        int targetCount = GetScaledTargetCount(maximumWireFaultCells, strength);
        int affectedCount = 0;

        for (int index = 0;
             index < targetCount && index < wireCandidates.Count;
             index++)
        {
            int selectionIndex = random.Next(index, wireCandidates.Count);
            (wireCandidates[index], wireCandidates[selectionIndex]) =
                (wireCandidates[selectionIndex], wireCandidates[index]);

            if (board.ClearWire(wireCandidates[index]))
                affectedCount++;
        }

        return new ChaosEvent(
            ChaosEventType.WireFault,
            strength,
            affectedCount);
    }

    private ChaosEvent ApplyBatteryFluctuation()
    {
        float strength = NormalizedChaos;
        float chargeFraction = Mathf.Lerp(
            minimumBatterySwing,
            maximumBatterySwing,
            strength);
        int targetCount = GetScaledTargetCount(maximumBatteryTargets, strength);
        int affectedCount = 0;

        for (int index = 0;
             index < targetCount && index < batteryCandidates.Count;
             index++)
        {
            int selectionIndex = random.Next(index, batteryCandidates.Count);
            (batteryCandidates[index], batteryCandidates[selectionIndex]) =
                (batteryCandidates[selectionIndex], batteryCandidates[index]);

            BatteryComponent battery = batteryCandidates[index];
            float direction;
            if (battery.IsEmpty)
                direction = 1f;
            else if (battery.IsFull)
                direction = -1f;
            else
                direction = random.Next(2) == 0 ? -1f : 1f;

            float previousCharge = battery.Charge;
            battery.SetCharge(
                previousCharge + battery.Capacity * chargeFraction * direction);
            if (!Mathf.Approximately(previousCharge, battery.Charge))
                affectedCount++;
        }

        return new ChaosEvent(
            ChaosEventType.BatteryFluctuation,
            strength,
            affectedCount);
    }

    private ChaosEvent ApplyGeneratorOutputShift()
    {
        float strength = NormalizedChaos;
        int targetCount = GetScaledTargetCount(
            maximumGeneratorTargets,
            strength);
        int eventMaximumPower = Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(
                MinimumGeneratorMaximumPower,
                maximumGeneratorPowerAtFullChaos,
                strength)),
            MinimumGeneratorMaximumPower,
            maximumGeneratorPowerAtFullChaos);
        int maximumStep = Mathf.Max(
            1,
            Mathf.CeilToInt(
                strength *
                (eventMaximumPower -
                 GeneratorComponent.MinimumPowerPerActiveOutput)));
        int affectedCount = 0;

        for (int index = 0;
             index < targetCount && index < generatorCandidates.Count;
             index++)
        {
            int selectionIndex = random.Next(index, generatorCandidates.Count);
            (generatorCandidates[index], generatorCandidates[selectionIndex]) =
                (generatorCandidates[selectionIndex], generatorCandidates[index]);

            GeneratorComponent generator = generatorCandidates[index];
            int currentPower = generator.PowerPerActiveOutput;
            bool canIncrease = currentPower < eventMaximumPower;
            bool canDecrease =
                currentPower > GeneratorComponent.MinimumPowerPerActiveOutput;
            bool increase = canIncrease &&
                            (!canDecrease || random.Next(2) == 0);
            int step = random.Next(1, maximumStep + 1);
            int nextPower = increase
                ? Mathf.Min(eventMaximumPower, currentPower + step)
                : Mathf.Max(
                    GeneratorComponent.MinimumPowerPerActiveOutput,
                    currentPower - step);

            generator.SetPowerPerActiveOutput(nextPower);
            if (generator.PowerPerActiveOutput != currentPower)
                affectedCount++;
        }

        return new ChaosEvent(
            ChaosEventType.GeneratorOutputShift,
            strength,
            affectedCount);
    }

    private ChaosEvent ApplyConsumerRequiredPowerShift()
    {
        float strength = NormalizedChaos;
        int targetCount = GetScaledTargetCount(
            maximumConsumerTargets,
            strength);
        int eventMaximumRequiredPower = Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(
                MinimumConsumerMaximumRequiredPower,
                maximumConsumerRequiredPowerAtFullChaos,
                strength)),
            MinimumConsumerMaximumRequiredPower,
            maximumConsumerRequiredPowerAtFullChaos);
        int maximumStep = Mathf.Max(
            1,
            Mathf.CeilToInt(
                strength *
                (eventMaximumRequiredPower -
                 ScoredPowerConsumerComponent.MinimumRequiredPower)));
        int affectedCount = 0;

        for (int index = 0;
             index < targetCount && index < consumerCandidates.Count;
             index++)
        {
            int selectionIndex = random.Next(index, consumerCandidates.Count);
            (consumerCandidates[index], consumerCandidates[selectionIndex]) =
                (consumerCandidates[selectionIndex], consumerCandidates[index]);

            ScoredPowerConsumerComponent consumer = consumerCandidates[index];
            int currentRequiredPower = consumer.RequiredPower;
            bool canIncrease =
                currentRequiredPower < eventMaximumRequiredPower;
            bool canDecrease =
                currentRequiredPower >
                ScoredPowerConsumerComponent.MinimumRequiredPower;
            bool increase = canIncrease &&
                            (!canDecrease || random.Next(2) == 0);
            int step = random.Next(1, maximumStep + 1);
            int nextRequiredPower = increase
                ? Mathf.Min(
                    eventMaximumRequiredPower,
                    currentRequiredPower + step)
                : Mathf.Max(
                    ScoredPowerConsumerComponent.MinimumRequiredPower,
                    currentRequiredPower - step);

            consumer.SetRequiredPower(nextRequiredPower);
            if (consumer.RequiredPower != currentRequiredPower)
                affectedCount++;
        }

        return new ChaosEvent(
            ChaosEventType.ConsumerRequiredPowerShift,
            strength,
            affectedCount);
    }

    private ChaosEvent ApplyConsumerRemoval()
    {
        float strength = NormalizedChaos;
        int targetCount = GetScaledTargetCount(
            maximumConsumerRemovals,
            strength);
        int affectedCount = 0;
        RunGenerator targetRunGenerator = ResolveRunGenerator();
        if (targetRunGenerator == null)
        {
            return new ChaosEvent(
                ChaosEventType.ConsumerRemoval,
                strength,
                0);
        }

        for (int index = 0;
             index < targetCount && index < removableConsumerCandidates.Count;
             index++)
        {
            int selectionIndex = random.Next(
                index,
                removableConsumerCandidates.Count);
            (removableConsumerCandidates[index],
             removableConsumerCandidates[selectionIndex]) =
                (removableConsumerCandidates[selectionIndex],
                 removableConsumerCandidates[index]);

            if (targetRunGenerator.RemoveGeneratedComponent(
                    removableConsumerCandidates[index]))
            {
                affectedCount++;
            }
        }

        if (affectedCount > 0)
            ResolveCircuitSystem()?.RebuildIfDirty();

        // Removed Unity objects are destroyed at end of frame. Do not retain
        // them in chaos scratch buffers during that deferred-destruction window.
        consumerCandidates.Clear();
        removableConsumerCandidates.Clear();

        return new ChaosEvent(
            ChaosEventType.ConsumerRemoval,
            strength,
            affectedCount);
    }

    private void CollectCandidates()
    {
        wireCandidates.Clear();
        batteryCandidates.Clear();
        generatorCandidates.Clear();
        consumerCandidates.Clear();
        removableConsumerCandidates.Clear();

        GridBoard targetBoard = ResolveBoard();
        if (targetBoard == null)
            return;

        foreach (Vector2Int cell in targetBoard.WireCells)
            wireCandidates.Add(cell);

        Transform componentsRoot = targetBoard.ComponentsRoot;
        if (componentsRoot == null)
            return;

        BatteryComponent[] batteries =
            componentsRoot.GetComponentsInChildren<BatteryComponent>(true);
        foreach (BatteryComponent battery in batteries)
        {
            if (battery != null && battery.Capacity > 0f)
                batteryCandidates.Add(battery);
        }

        GeneratorComponent[] generators =
            componentsRoot.GetComponentsInChildren<GeneratorComponent>(true);
        foreach (GeneratorComponent generator in generators)
        {
            if (generator != null)
                generatorCandidates.Add(generator);
        }

        ScoredPowerConsumerComponent[] consumers =
            componentsRoot.GetComponentsInChildren<
                ScoredPowerConsumerComponent>(true);
        foreach (ScoredPowerConsumerComponent consumer in consumers)
        {
            if (consumer != null &&
                consumer.IsPlaced &&
                consumer.isActiveAndEnabled)
            {
                consumerCandidates.Add(consumer);
            }
        }

        RunGenerator targetRunGenerator = ResolveRunGenerator();
        if (targetRunGenerator == null)
            return;

        foreach (BoardComponent generatedComponent in
                 targetRunGenerator.GeneratedComponents)
        {
            if (generatedComponent is ScoredPowerConsumerComponent consumer &&
                consumer.IsPlaced &&
                consumer.isActiveAndEnabled)
            {
                removableConsumerCandidates.Add(consumer);
            }
        }
    }

    private float RollNextInterval()
    {
        float frequencyScale = Mathf.InverseLerp(
            RunSettings.MinimumChaosValue + 1,
            RunSettings.MaximumChaosValue,
            chaosValue);
        float averageInterval = Mathf.Lerp(
            lowChaosInterval,
            highChaosInterval,
            frequencyScale);
        float jitterMultiplier = Mathf.Lerp(
            1f - intervalJitter,
            1f + intervalJitter,
            (float)random.NextDouble());
        return Mathf.Max(0.1f, averageInterval * jitterMultiplier);
    }

    private static int GetScaledTargetCount(int maximumCount, float strength)
    {
        return Mathf.Clamp(
            1 + Mathf.FloorToInt(strength * (maximumCount - 1)),
            1,
            maximumCount);
    }

    private GridBoard ResolveBoard()
    {
        if (board == null)
            board = GetComponent<GridBoard>();

        return board;
    }

    private RunGenerator ResolveRunGenerator()
    {
        if (runGenerator == null)
            runGenerator = GetComponent<RunGenerator>();

        return runGenerator;
    }

    private CircuitSystem ResolveCircuitSystem()
    {
        if (circuitSystem == null)
            circuitSystem = GetComponent<CircuitSystem>();

        if (circuitSystem != null)
            return circuitSystem;

        GridBoard targetBoard = ResolveBoard();
        CircuitSystem[] circuitSystems = FindObjectsByType<CircuitSystem>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (CircuitSystem candidate in circuitSystems)
        {
            if (candidate != null && ReferenceEquals(candidate.Board, targetBoard))
            {
                circuitSystem = candidate;
                break;
            }
        }

        return circuitSystem;
    }
}
