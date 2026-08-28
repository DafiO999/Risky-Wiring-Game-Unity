using System.Globalization;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[ExecuteAlways]
[RequireComponent(typeof(TMP_Text))]
public sealed class ScoreDisplayView : MonoBehaviour
{
    [SerializeField]
    private ScoreSystem scoreSystem;

    [SerializeField]
    private TMP_Text scoreText;

    [SerializeField, Min(1)]
    private int minimumIntegerDigits = 9;

    [SerializeField, Range(0, 3)]
    private int decimalPlaces = 1;

    [SerializeField]
    private bool showScoreRate;

    private ScoreSystem subscribedScoreSystem;

    public ScoreSystem ScoreSystem => ResolveScoreSystem();
    public TMP_Text ScoreText => ResolveScoreText();
    public string DisplayText { get; private set; } = string.Empty;

    private void OnEnable()
    {
        BindScoreSystem();
        Refresh();
    }

    private void OnDisable()
    {
        UnbindScoreSystem();
    }

    private void OnValidate()
    {
        minimumIntegerDigits = Mathf.Max(1, minimumIntegerDigits);
        decimalPlaces = Mathf.Clamp(decimalPlaces, 0, 3);
        BindScoreSystem();
        Refresh();
    }

    private void Update()
    {
        // Rebind if scene enable order meant ScoreSystem.Instance was not ready
        // when this view first enabled.
        if (subscribedScoreSystem != ResolveScoreSystem())
            BindScoreSystem();
    }

    public void Refresh()
    {
        ScoreSystem targetScoreSystem = ResolveScoreSystem();
        TMP_Text targetText = ResolveScoreText();
        if (targetText == null)
            return;

        string format = new string('0', minimumIntegerDigits);
        if (decimalPlaces > 0)
            format += "." + new string('0', decimalPlaces);

        float score = targetScoreSystem != null
            ? targetScoreSystem.CurrentScore
            : 0f;
        DisplayText = score.ToString(format, CultureInfo.InvariantCulture);

        if (showScoreRate)
        {
            int rate = targetScoreSystem != null
                ? targetScoreSystem.CurrentScorePerSecond
                : 0;
            DisplayText += $"  ({rate:+#;-#;0}/s)";
        }

        targetText.text = DisplayText;
    }

    private void BindScoreSystem()
    {
        ScoreSystem target = ResolveScoreSystem();
        if (subscribedScoreSystem == target)
            return;

        UnbindScoreSystem();
        subscribedScoreSystem = target;
        if (subscribedScoreSystem == null)
            return;

        subscribedScoreSystem.ScoreChanged += HandleScoreChanged;
        subscribedScoreSystem.ScorePerSecondChanged += HandleScoreRateChanged;
        Refresh();
    }

    private void UnbindScoreSystem()
    {
        if (subscribedScoreSystem != null)
        {
            subscribedScoreSystem.ScoreChanged -= HandleScoreChanged;
            subscribedScoreSystem.ScorePerSecondChanged -= HandleScoreRateChanged;
        }

        subscribedScoreSystem = null;
    }

    private void HandleScoreChanged(float _)
    {
        Refresh();
    }

    private void HandleScoreRateChanged(int _)
    {
        Refresh();
    }

    private ScoreSystem ResolveScoreSystem()
    {
        if (scoreSystem == null)
            scoreSystem = global::ScoreSystem.Instance;

        if (scoreSystem == null)
            scoreSystem = FindFirstObjectByType<ScoreSystem>();

        return scoreSystem;
    }

    private TMP_Text ResolveScoreText()
    {
        if (scoreText == null)
            scoreText = GetComponent<TMP_Text>();

        return scoreText;
    }
}
