using UnityEngine;

[DisallowMultipleComponent]
public sealed class RunManager : MonoBehaviour
{
    [Header("Run")]
    [SerializeField]
    private RunSettings settings = new();

    [SerializeField]
    [Tooltip("Start the inspector-configured run when play mode begins.")]
    private bool startAutomatically = true;

    [Header("Systems")]
    [SerializeField]
    private RunGenerator runGenerator;

    [SerializeField]
    private ScoreSystem scoreSystem;

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
        startAutomatically = true;
        runGenerator = GetComponent<RunGenerator>();
        scoreSystem = GetComponent<ScoreSystem>();
    }

    private void OnValidate()
    {
        ResolveSettings().Validate();
        remainingTime = Mathf.Max(0f, remainingTime);
    }

    private void Start()
    {
        if (startAutomatically)
            StartRun();
    }

    private void Update()
    {
        AdvanceTime(Time.deltaTime);
    }

    private void OnDisable()
    {
        if (isRunning)
            EndRun();
    }

    public bool StartRun()
    {
        RunSettings currentSettings = ResolveSettings();
        return StartRun(
            currentSettings.Difficulty,
            currentSettings.Duration,
            currentSettings.Seed);
    }

    public bool StartRun(int difficulty, float duration, int seed)
    {
        RunSettings currentSettings = ResolveSettings();
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

        targetScoreSystem.ResetScore();

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

    private RunSettings ResolveSettings()
    {
        settings ??= new RunSettings();
        return settings;
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

    private bool FailStart(string reason)
    {
        isRunning = false;
        remainingTime = 0f;
        lastStartFailureReason = reason;
        Debug.LogError(reason, this);
        return false;
    }
}
