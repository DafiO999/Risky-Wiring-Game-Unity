using TMPro;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(ChaosController))]
[DefaultExecutionOrder(-200)]
public sealed class RunManager : MonoBehaviour
{
    [Header("Run")]
    [SerializeField]
    private RunSettings settings = new();

    [SerializeField]
    [Tooltip("Start an inspector-configured run when play mode begins. Slot-driven games should leave this off.")]
    private bool startAutomatically;

    [Header("Systems")]
    [SerializeField]
    [Tooltip("Slot machine whose grid size, difficulty, time, and chaos start each game.")]
    private SlotMachineController slotMachine;

    [SerializeField]
    private RunGenerator runGenerator;

    [SerializeField]
    private ScoreSystem scoreSystem;

    [SerializeField]
    [Tooltip("Dedicated owner of random per-run gameplay events.")]
    private ChaosController chaosController;

    [SerializeField]
    [Tooltip("Electricity simulation reset and paused when a run ends.")]
    private CircuitSystem circuitSystem;

    [Header("UI")]
    [SerializeField]
    [Tooltip("Optional text that displays the authoritative run time remaining as MM:SS.")]
    private TMP_Text timerText;

    [Header("Events")]
    [SerializeField]
    [Tooltip("Invoked after a completed run has stopped and the Display board and circuit have been cleared.")]
    private UnityEvent onRunEnd = new();

    private SlotMachineController subscribedSlotMachine;
    private bool isRunning;
    private float remainingTime;
    private int displayedWholeSeconds = int.MinValue;
    private string lastStartFailureReason;

    public RunSettings Settings => ResolveSettings();
    public RunGenerator Generator => ResolveRunGenerator();
    public ScoreSystem ScoreSystem => ResolveScoreSystem();
    public ChaosController ChaosController => ResolveChaosController();
    public CircuitSystem CircuitSystem => ResolveCircuitSystem();
    public TMP_Text TimerText => timerText;
    public UnityEvent OnRunEnd => onRunEnd;
    public bool IsRunning => isRunning;
    public float RemainingTime => remainingTime;
    public float ElapsedTime => Mathf.Max(0f, Settings.Duration - remainingTime);
    public float NormalizedTimeRemaining => Settings.Duration > 0f
        ? Mathf.Clamp01(remainingTime / Settings.Duration)
        : 0f;
    public string LastStartFailureReason => lastStartFailureReason;

    private void Reset()
    {
        settings = new RunSettings();
        startAutomatically = false;
        slotMachine = FindFirstObjectByType<SlotMachineController>();
        runGenerator = GetComponent<RunGenerator>();
        scoreSystem = GetComponent<ScoreSystem>();
        chaosController = GetComponent<ChaosController>();
        circuitSystem = GetComponent<CircuitSystem>();
    }

    private void OnEnable()
    {
        BindSlotMachine();
        if (!isRunning)
            StopGameplayUpdates(clearBoard: false);
        RefreshTimerText(force: true);
    }

    private void OnValidate()
    {
        ResolveSettings().Validate();
        remainingTime = Mathf.Max(0f, remainingTime);
        RefreshTimerText(force: true);
    }

    private void Start()
    {
        BindSlotMachine();

        if (startAutomatically)
            StartRun();
    }

    private void Update()
    {
        AdvanceTime(Time.deltaTime);
    }

    private void OnDisable()
    {
        UnbindSlotMachine();

        if (isRunning)
            EndRun();
        else
            StopGameplayUpdates(clearBoard: false);
    }

    /// <summary>
    /// Starts a run from the inspector settings with a newly generated seed.
    /// </summary>
    public bool StartRun()
    {
        RunSettings currentSettings = ResolveSettings();
        return StartRun(
            currentSettings.GridSize,
            currentSettings.Difficulty,
            currentSettings.Duration,
            currentSettings.ChaosValue);
    }

    /// <summary>
    /// Starts from three legacy slot results while preserving the configured
    /// ChaosValue.
    /// </summary>
    public bool StartRun(Vector2Int gridSize, int difficulty, float duration)
    {
        return StartRun(
            gridSize,
            difficulty,
            duration,
            ResolveSettings().ChaosValue);
    }

    /// <summary>
    /// Applies all four independent run parameters and starts with a new seed.
    /// </summary>
    public bool StartRun(
        Vector2Int gridSize,
        int difficulty,
        float duration,
        int chaosValue)
    {
        return StartRunInternal(
            gridSize,
            difficulty,
            duration,
            chaosValue,
            GenerateRandomSeed());
    }

    private bool StartRunInternal(
        Vector2Int gridSize,
        int difficulty,
        float duration,
        int chaosValue,
        int seed)
    {
        RunSettings currentSettings = ResolveSettings();
        currentSettings.GridSize = gridSize;
        currentSettings.Difficulty = difficulty;
        currentSettings.Duration = duration;
        currentSettings.ChaosValue = chaosValue;
        currentSettings.Seed = seed;

        RunGenerator targetGenerator = ResolveRunGenerator();
        ScoreSystem targetScoreSystem = ResolveScoreSystem();
        ChaosController targetChaosController = ResolveChaosController();
        CircuitSystem targetCircuitSystem = ResolveCircuitSystem();

        isRunning = false;
        remainingTime = 0f;
        lastStartFailureReason = string.Empty;
        targetScoreSystem?.SetAccumulationEnabled(false);
        targetChaosController?.StopRun();
        targetCircuitSystem?.SetSimulationEnabled(false);

        if (targetGenerator == null)
            return FailStart("RunManager requires a RunGenerator.");

        if (targetScoreSystem == null)
            return FailStart("RunManager requires a ScoreSystem.");

        if (targetChaosController == null)
            return FailStart("RunManager requires a ChaosController.");

        if (targetCircuitSystem == null)
            return FailStart("RunManager requires a CircuitSystem.");

        GridBoard targetBoard = targetGenerator.Board;
        if (targetBoard == null)
            return FailStart("RunManager requires a GridBoard through its RunGenerator.");

        targetBoard.SetGameplayInputEnabled(false);
        targetGenerator.ClearBoard();
        targetCircuitSystem.ResetCircuitState();
        targetBoard.SetSize(
            currentSettings.GridSize.x,
            currentSettings.GridSize.y);

        if (!targetGenerator.Generate(
                currentSettings.Difficulty,
                currentSettings.Seed))
        {
            return FailStart(
                string.IsNullOrEmpty(targetGenerator.LastFailureReason)
                    ? "Run generation failed."
                    : targetGenerator.LastFailureReason);
        }

        remainingTime = currentSettings.Duration;
        isRunning = true;
        RefreshTimerText(force: true);
        targetBoard.SetGameplayInputEnabled(true);
        targetCircuitSystem.MarkTopologyDirty();
        targetCircuitSystem.SetSimulationEnabled(true);
        targetChaosController.StartRun(
            targetBoard,
            targetGenerator,
            currentSettings.ChaosValue,
            currentSettings.Seed);
        targetScoreSystem.SetAccumulationEnabled(true);
        return true;
    }

    [ContextMenu("Start Run")]
    private void StartRunFromContextMenu()
    {
        StartRun();
    }

    public void AdvanceTime(float deltaTime)
    {
        if (!isRunning || deltaTime <= 0f)
            return;

        remainingTime = Mathf.Max(0f, remainingTime - deltaTime);
        if (remainingTime <= 0f)
        {
            EndRun();
            return;
        }

        RefreshTimerText(force: false);
    }

    [ContextMenu("End Run")]
    public void EndRun()
    {
        bool invokeRunEnded = isRunning;
        isRunning = false;
        remainingTime = 0f;
        RefreshTimerText(force: true);
        StopGameplayUpdates(clearBoard: true);

        if (invokeRunEnded)
            onRunEnd?.Invoke();
    }

    private void HandleSpinCompleted(
        Vector2Int gridSize,
        int difficulty,
        float duration,
        int chaosValue)
    {
        StartRun(gridSize, difficulty, duration, chaosValue);
    }

    private void BindSlotMachine()
    {
        SlotMachineController targetSlotMachine = ResolveSlotMachine();
        if (subscribedSlotMachine == targetSlotMachine)
            return;

        UnbindSlotMachine();
        subscribedSlotMachine = targetSlotMachine;

        if (subscribedSlotMachine != null)
            subscribedSlotMachine.SpinCompletedWithChaos += HandleSpinCompleted;
    }

    private void UnbindSlotMachine()
    {
        if (subscribedSlotMachine != null)
            subscribedSlotMachine.SpinCompletedWithChaos -= HandleSpinCompleted;

        subscribedSlotMachine = null;
    }

    private RunSettings ResolveSettings()
    {
        settings ??= new RunSettings();
        return settings;
    }

    private SlotMachineController ResolveSlotMachine()
    {
        if (slotMachine == null)
            slotMachine = FindFirstObjectByType<SlotMachineController>();

        return slotMachine;
    }

    private RunGenerator ResolveRunGenerator()
    {
        if (runGenerator == null)
            runGenerator = GetComponent<RunGenerator>();

        if (runGenerator == null)
            runGenerator = FindFirstObjectByType<RunGenerator>();

        return runGenerator;
    }

    private ScoreSystem ResolveScoreSystem()
    {
        if (scoreSystem == null)
            scoreSystem = GetComponent<ScoreSystem>();

        if (scoreSystem == null)
            scoreSystem = global::ScoreSystem.Instance;

        if (scoreSystem == null)
            scoreSystem = FindFirstObjectByType<ScoreSystem>();

        return scoreSystem;
    }

    private ChaosController ResolveChaosController()
    {
        if (chaosController == null)
            chaosController = GetComponent<ChaosController>();

        return chaosController;
    }

    private CircuitSystem ResolveCircuitSystem()
    {
        if (circuitSystem == null)
            circuitSystem = GetComponent<CircuitSystem>();

        if (circuitSystem == null)
        {
            GridBoard targetBoard = ResolveRunGenerator()?.Board;
            if (targetBoard != null)
                circuitSystem = targetBoard.GetComponent<CircuitSystem>();
        }

        return circuitSystem;
    }

    private void StopGameplayUpdates(bool clearBoard)
    {
        ResolveChaosController()?.StopRun();
        ResolveScoreSystem()?.SetAccumulationEnabled(false);

        RunGenerator targetGenerator = ResolveRunGenerator();
        GridBoard targetBoard = targetGenerator != null
            ? targetGenerator.Board
            : null;
        targetBoard?.SetGameplayInputEnabled(false);

        CircuitSystem targetCircuitSystem = ResolveCircuitSystem();
        targetCircuitSystem?.SetSimulationEnabled(false);

        if (!clearBoard)
            return;

        targetGenerator?.ClearBoard();
        targetCircuitSystem?.ResetCircuitState();
    }

    private int GenerateRandomSeed()
    {
        int previousSeed = ResolveSettings().Seed;
        int nextSeed;

        do
        {
            nextSeed = Random.Range(1, int.MaxValue);
        }
        while (nextSeed == previousSeed);

        return nextSeed;
    }

    private void RefreshTimerText(bool force)
    {
        int wholeSeconds = Mathf.CeilToInt(Mathf.Max(0f, remainingTime));
        if (!force && displayedWholeSeconds == wholeSeconds)
            return;

        displayedWholeSeconds = wholeSeconds;
        if (timerText == null)
            return;

        int minutes = wholeSeconds / 60;
        int seconds = wholeSeconds % 60;
        timerText.text = $"{minutes:00}:{seconds:00}";
    }

    private bool FailStart(string reason)
    {
        isRunning = false;
        remainingTime = 0f;
        RefreshTimerText(force: true);
        lastStartFailureReason = reason;
        StopGameplayUpdates(clearBoard: true);
        Debug.LogError(reason, this);
        return false;
    }
}
