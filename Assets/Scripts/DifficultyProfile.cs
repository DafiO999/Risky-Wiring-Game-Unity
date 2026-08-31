using System;
using UnityEngine;

[Serializable]
public struct DifficultyAmountRange
{
    [SerializeField, Min(0)]
    private int minimum;

    [SerializeField, Min(0)]
    private int maximum;

    public int Minimum => minimum;
    public int Maximum => maximum;

    public DifficultyAmountRange(int minimum, int maximum)
    {
        this.minimum = Mathf.Max(0, minimum);
        this.maximum = Mathf.Max(this.minimum, maximum);
    }

    public int Roll(System.Random random)
    {
        if (random == null)
            throw new ArgumentNullException(nameof(random));

        long inclusiveRange = (long)maximum - minimum + 1L;
        if (inclusiveRange <= 1L)
            return minimum;

        long offset = (long)Math.Floor(random.NextDouble() * inclusiveRange);
        return (int)Math.Min(int.MaxValue, minimum + offset);
    }

    internal DifficultyAmountRange Clamped()
    {
        return new DifficultyAmountRange(minimum, maximum);
    }
}

[Serializable]
public struct DifficultyLevel
{
    [SerializeField]
    private DifficultyAmountRange generators;

    [SerializeField]
    private DifficultyAmountRange batteries;

    [SerializeField]
    private DifficultyAmountRange consumers;

    public DifficultyAmountRange Generators => generators;
    public DifficultyAmountRange Batteries => batteries;
    public DifficultyAmountRange Consumers => consumers;

    public DifficultyLevel(
        DifficultyAmountRange generators,
        DifficultyAmountRange batteries,
        DifficultyAmountRange consumers)
    {
        this.generators = generators.Clamped();
        this.batteries = batteries.Clamped();
        this.consumers = consumers.Clamped();
    }

    internal DifficultyLevel Clamped()
    {
        return new DifficultyLevel(generators, batteries, consumers);
    }
}

public readonly struct DifficultyAmounts
{
    public int Generators { get; }
    public int Batteries { get; }
    public int Consumers { get; }
    public int Total
    {
        get
        {
            long total = (long)Generators + Batteries + Consumers;
            return (int)Math.Min(int.MaxValue, total);
        }
    }

    public DifficultyAmounts(int generators, int batteries, int consumers)
    {
        Generators = Mathf.Max(0, generators);
        Batteries = Mathf.Max(0, batteries);
        Consumers = Mathf.Max(0, consumers);
    }
}

[Serializable]
public sealed class DifficultyProfile
{
    public const int MinimumDifficulty = 1;
    public const int MaximumDifficulty = 5;

    [SerializeField]
    private DifficultyLevel[] levels = CreateDefaultLevels();

    public DifficultyLevel GetLevel(int difficulty)
    {
        EnsureLevels();
        int index = Mathf.Clamp(
            difficulty,
            MinimumDifficulty,
            MaximumDifficulty) - 1;
        return levels[index];
    }

    public void SetLevel(int difficulty, DifficultyLevel level)
    {
        EnsureLevels();
        int index = Mathf.Clamp(
            difficulty,
            MinimumDifficulty,
            MaximumDifficulty) - 1;
        levels[index] = level.Clamped();
    }

    public DifficultyAmounts Roll(int difficulty, System.Random random)
    {
        DifficultyLevel level = GetLevel(difficulty);
        return new DifficultyAmounts(
            level.Generators.Roll(random),
            level.Batteries.Roll(random),
            level.Consumers.Roll(random));
    }

    internal void Validate()
    {
        EnsureLevels();
        for (int index = 0; index < levels.Length; index++)
            levels[index] = levels[index].Clamped();
    }

    private void EnsureLevels()
    {
        if (levels != null && levels.Length == MaximumDifficulty)
            return;

        DifficultyLevel[] defaults = CreateDefaultLevels();
        if (levels != null)
        {
            int copyCount = Mathf.Min(levels.Length, defaults.Length);
            Array.Copy(levels, defaults, copyCount);
        }

        levels = defaults;
    }

    private static DifficultyLevel[] CreateDefaultLevels()
    {
        return new[]
        {
            Level(1, 1, 1, 1, 1, 2),
            Level(1, 1, 1, 1, 2, 2),
            Level(1, 1, 1, 1, 2, 3),
            Level(1, 1, 1, 1, 3, 3),
            Level(1, 2, 1, 1, 2, 3)
        };
    }

    private static DifficultyLevel Level(
        int minimumGenerators,
        int maximumGenerators,
        int minimumBatteries,
        int maximumBatteries,
        int minimumConsumers,
        int maximumConsumers)
    {
        return new DifficultyLevel(
            new DifficultyAmountRange(minimumGenerators, maximumGenerators),
            new DifficultyAmountRange(minimumBatteries, maximumBatteries),
            new DifficultyAmountRange(minimumConsumers, maximumConsumers));
    }
}
