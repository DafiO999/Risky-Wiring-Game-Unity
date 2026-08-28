using System;
using UnityEngine;

[Serializable]
public sealed class RunSettings
{
    public const float MinimumDuration = 0.1f;

    [SerializeField, Range(
        DifficultyProfile.MinimumDifficulty,
        DifficultyProfile.MaximumDifficulty)]
    private int difficulty = DifficultyProfile.MinimumDifficulty;

    [SerializeField, Min(MinimumDuration)]
    [Tooltip("Run length in seconds.")]
    private float duration = 60f;

    [SerializeField]
    private int seed = 12345;

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

    internal void Validate()
    {
        Difficulty = difficulty;
        Duration = duration;
    }
}
