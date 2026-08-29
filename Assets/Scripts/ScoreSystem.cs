using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public interface IScoreRateSource
{
    int ScorePerSecond { get; }
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public class ScoreSystem : MonoBehaviour
{
    

    [FormerlySerializedAs("score")]
    [SerializeField, Min(0f)]
    private float currentScore;

    [SerializeField, HideInInspector]
    private int currentScorePerSecond;

    [SerializeField]
    private SaveManager saveManager;

    private bool accumulationEnabled = true;

    private readonly Dictionary<UnityEngine.Object, int> reportedRates = new();
    private readonly List<UnityEngine.Object> staleSources = new();

    public static ScoreSystem Instance { get; private set; }
    public float CurrentScore => currentScore;
    public int CurrentScorePerSecond => currentScorePerSecond;
    public bool AccumulationEnabled => accumulationEnabled;

    // Kept as a convenient read-only alias for code written against task 8.
    public float Score => currentScore;

    public event Action<float> ScoreChanged;
    public event Action<int> ScorePerSecondChanged;

    protected virtual void OnEnable()
    {
        if (Instance == null || Instance == this)
            Instance = this;

        RefreshScoreRateSources();
    }

    protected virtual void OnDisable()
    {
        if (Instance == this)
            Instance = null;

        reportedRates.Clear();
        SetCurrentScorePerSecond(0);
    }

    protected virtual void OnValidate()
    {
        currentScore = Mathf.Max(0f, currentScore);
    }

    protected virtual void Update()
    {
        Tick(Time.deltaTime);
    }

    public void Tick(float deltaTime)
    {
        if (!accumulationEnabled ||
            deltaTime <= 0f ||
            currentScorePerSecond == 0)
            return;

        SetScore(currentScore + currentScorePerSecond * deltaTime);
    }

    public void ReportScoreRate(UnityEngine.Object source, int scorePerSecond)
    {
        if (source == null)
            return;

        if (scorePerSecond == 0)
            reportedRates.Remove(source);
        else
            reportedRates[source] = scorePerSecond;

        RecalculateScorePerSecond();
    }

    public void RemoveScoreRate(UnityEngine.Object source)
    {
        if (ReferenceEquals(source, null))
            return;

        if (reportedRates.Remove(source))
            RecalculateScorePerSecond();
    }

    public void AddScore(float amount)
    {
        SetScore(currentScore + amount);
    }
    public void RemoveScore(float amount)
    {
        SetScore(currentScore - amount);
    }

    public void SetScore(float value)
    {
        float clampedValue = Mathf.Max(0f, value);
        if (Mathf.Approximately(currentScore, clampedValue))
            return;

        currentScore = clampedValue;
        saveManager.SaveScore(currentScore);
        ScoreChanged?.Invoke(currentScore);
    }

    public void ResetScore()
    {
        SetScore(0f);
    }

    public void SetAccumulationEnabled(bool enabled)
    {
        accumulationEnabled = enabled;
    }

    private void RefreshScoreRateSources()
    {
        BoardComponent[] components = FindObjectsByType<BoardComponent>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (BoardComponent component in components)
        {
            if (component is ScoredPowerConsumerComponent consumer)
                consumer.RefreshScoreRateReport();
        }
    }

    private void RecalculateScorePerSecond()
    {
        staleSources.Clear();
        int totalRate = 0;

        foreach (KeyValuePair<UnityEngine.Object, int> report in reportedRates)
        {
            if (report.Key == null)
                staleSources.Add(report.Key);
            else
                totalRate += report.Value;
        }

        foreach (UnityEngine.Object staleSource in staleSources)
            reportedRates.Remove(staleSource);

        SetCurrentScorePerSecond(totalRate);
    }

    private void SetCurrentScorePerSecond(int value)
    {
        if (currentScorePerSecond == value)
            return;

        currentScorePerSecond = value;
        ScorePerSecondChanged?.Invoke(currentScorePerSecond);
    }
}
