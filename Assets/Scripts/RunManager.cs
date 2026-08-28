using UnityEngine;

[DisallowMultipleComponent]
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
    [Tooltip("Slot machine whose grid size, difficulty, and time start each game.")]
    private SlotMachineController slotMachine;

    [SerializeField]
    private RunGenerator runGenerator;

    [SerializeField]
    private ScoreSystem scoreSystem;

    private SlotMachineController subscribedSlotMachine;
    private bool isRunning;
    private float remainingTime;
    private string lastStartFailureReason;

    public RunSettings Settings => ResolveSettings();
    public RunGenerator Generator => ResolveRunGenerator();
    public ScoreSystem ScoreSystem => ResolveScoreSystem();
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
    }

    private void OnEnable()
    {
        BindSlotMachine();
    }

    private void OnValidate()
    {
        ResolveSettings().Validate();
        remainingTime = Mathf.Max(0f, remainingTime);
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
            currentSettings.Duration);
    }

    /// <summary>
    /// Applies the three slot results and starts a run with a newly generated seed.
    /// </summary>
    public bool StartRun(int gridSize, int difficulty, float duration)
    {
        return StartRunInternal(
            gridSize,
            difficulty,
            duration,
            GenerateRandomSeed());
    }

    private bool StartRunInternal(
        int gridSize,
        int difficulty,
        float duration,
        int seed)
    {
        RunSettings currentSettings = ResolveSettings();
        currentSettings.GridSize = gridSize;
        currentSettings.Difficulty = difficulty;
        currentSettings.Duration = duration;
        currentSettings.Seed = seed;

        RunGenerator targetGenerator = ResolveRunGenerator();
        ScoreSystem targetScoreSystem = ResolveScoreSystem();

        isRunning = false;
        remainingTime = 0f;
        lastStartFailureReason = string.Empty;
        targetScoreSystem?.SetAccumulationEnabled(false);

        if (targetGenerator == null)
            return FailStart("RunManager requires a RunGenerator.");

        if (targetScoreSystem == null)
            return FailStart("RunManager requires a ScoreSystem.");

        GridBoard targetBoard = targetGenerator.Board;
        if (targetBoard == null)
            return FailStart("RunManager requires a GridBoard through its RunGenerator.");

        targetScoreSystem.ResetScore();
        targetGenerator.ClearBoard();
        targetBoard.SetSize(currentSettings.GridSize);

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
            EndRun();
    }

    [ContextMenu("End Run")]
    public void EndRun()
    {
        isRunning = false;
        remainingTime = 0f;
        ResolveScoreSystem()?.SetAccumulationEnabled(false);
    }

    private void HandleSpinCompleted(int gridSize, int difficulty, float duration)
    {
        StartRun(gridSize, difficulty, duration);
    }

    private void BindSlotMachine()
    {
        SlotMachineController targetSlotMachine = ResolveSlotMachine();
        if (subscribedSlotMachine == targetSlotMachine)
            return;

        UnbindSlotMachine();
        subscribedSlotMachine = targetSlotMachine;

        if (subscribedSlotMachine != null)
            subscribedSlotMachine.SpinCompleted += HandleSpinCompleted;
    }

    private void UnbindSlotMachine()
    {
        if (subscribedSlotMachine != null)
            subscribedSlotMachine.SpinCompleted -= HandleSpinCompleted;

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

    private bool FailStart(string reason)
    {
        isRunning = false;
        remainingTime = 0f;
        lastStartFailureReason = reason;
        Debug.LogError(reason, this);
        return false;
    }
}
