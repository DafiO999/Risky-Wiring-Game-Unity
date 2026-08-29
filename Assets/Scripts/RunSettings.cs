using System;
using UnityEngine;

[Serializable]
public sealed class RunSettings
{
    public const int MinimumGridSize = 1;
    public const float MinimumDuration = 0.1f;
    public const int MinimumChaosValue = 0;
    public const int MaximumChaosValue = 10;

    [SerializeField]
    [Tooltip("Grid dimensions in cells: width (X) and height (Y).")]
    private Vector2Int gridSize = new(8, 8);

    [SerializeField, Range(
        DifficultyProfile.MinimumDifficulty,
        DifficultyProfile.MaximumDifficulty)]
    private int difficulty = DifficultyProfile.MinimumDifficulty;

    [SerializeField, Min(MinimumDuration)]
    [Tooltip("Run length in seconds.")]
    private float duration = 60f;

    [SerializeField, Range(MinimumChaosValue, MaximumChaosValue)]
    [Tooltip("Controls only the frequency and strength of random chaos events. Zero disables chaos.")]
    private int chaosValue;

    [SerializeField]
    private int seed = 12345;

    public Vector2Int GridSize
    {
        get => gridSize;
        set => gridSize = new Vector2Int(
            Mathf.Max(MinimumGridSize, value.x),
            Mathf.Max(MinimumGridSize, value.y));
    }

    public int Difficulty
    {
        get => difficulty;
        set => difficulty = Mathf.Clamp(
            value,
            DifficultyProfile.MinimumDifficulty,
            DifficultyProfile.MaximumDifficulty);
    }

    public float Duration
    {
        get => duration;
        set => duration = Mathf.Max(MinimumDuration, value);
    }

    public int ChaosValue
    {
        get => chaosValue;
        set => chaosValue = Mathf.Clamp(
            value,
            MinimumChaosValue,
            MaximumChaosValue);
    }

    public int Seed
    {
        get => seed;
        set => seed = value;
    }

    public RunSettings()
    {
    }

    public RunSettings(int difficulty, float duration, int seed)
    {
        Difficulty = difficulty;
        Duration = duration;
        Seed = seed;
    }

    public RunSettings(int gridSize, int difficulty, float duration, int seed)
        : this(new Vector2Int(gridSize, gridSize), difficulty, duration, seed)
    {
    }

    public RunSettings(
        Vector2Int gridSize,
        int difficulty,
        float duration,
        int seed)
        : this(gridSize, difficulty, duration, MinimumChaosValue, seed)
    {
    }

    public RunSettings(
        Vector2Int gridSize,
        int difficulty,
        float duration,
        int chaosValue,
        int seed)
    {
        GridSize = gridSize;
        Difficulty = difficulty;
        Duration = duration;
        ChaosValue = chaosValue;
        Seed = seed;
    }

    public RunSettings(
        int gridSize,
        int difficulty,
        float duration,
        int chaosValue,
        int seed)
        : this(
            new Vector2Int(gridSize, gridSize),
            difficulty,
            duration,
            chaosValue,
            seed)
    {
    }

    internal void Validate()
    {
        GridSize = gridSize;
        Difficulty = difficulty;
        Duration = duration;
        ChaosValue = chaosValue;
    }
}
