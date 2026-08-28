using System;

/// <summary>
/// Compatibility component for scenes and scripts created before ScoreSystem
/// became the authoritative score accumulator.
/// </summary>
[Obsolete("Use ScoreSystem instead. ScoreManager remains for compatibility.")]
public sealed class ScoreManager : ScoreSystem
{
    public new static ScoreManager Instance => ScoreSystem.Instance as ScoreManager;
}
