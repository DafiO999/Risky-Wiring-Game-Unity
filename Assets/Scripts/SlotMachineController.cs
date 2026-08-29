using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public sealed class SlotResultEvent : UnityEvent<Vector2Int, int, float>
{
}

[Serializable]
public sealed class SlotChaosResultEvent : UnityEvent<Vector2Int, int, float, int>
{
}

[DisallowMultipleComponent]
public sealed class SlotMachineController : MonoBehaviour
{

    [SerializeField]
    private ShopController shopController;
    private const float DegreesPerRound = 360f;

    [Header("Trigger")]
    [SerializeField]
    [Tooltip("The lever that starts a spin. A lever below this object is found automatically if empty.")]
    private LeverPull lever;

    [Header("Cylinders")]
    [SerializeField]
    [Tooltip("Cylinder that chooses the grid size.")]
    private Transform firstCylinder;

    [SerializeField]
    [Tooltip("Cylinder that chooses the difficulty.")]
    private Transform secondCylinder;

    [SerializeField]
    [Tooltip("Cylinder that chooses the run time.")]
    private Transform thirdCylinder;

    [SerializeField]
    [Tooltip("Rotation axis in each cylinder's local space.")]
    private Vector3 localRotationAxis = Vector3.up;

    [Header("Spin")]
    [SerializeField, Min(0.01f)]
    [Tooltip("Cylinder rotation speed in degrees per second.")]
    private float rotationSpeed = 720f;

    [SerializeField, Min(0)]
    [Tooltip("Minimum number of complete rounds made by each cylinder (inclusive).")]
    private int minimumRounds = 2;

    [SerializeField, Min(0)]
    [Tooltip("Maximum number of complete rounds made by each cylinder (inclusive).")]
    private int maximumRounds = 5;

    [Header("Values")]
    [SerializeField]
    private Vector2Int[] gridSizes =
    {
        new(7, 5),
        new(8, 6),
        new(9, 7),
        new(10, 8)
    };

    [SerializeField]
    private int[] difficulties = { 1, 2, 3, 4 };

    [SerializeField]
    private float[] times = { 30f, 60f, 90f, 120f };

    [SerializeField]
    [Tooltip("Chaos values rolled independently from grid size, difficulty, and time.")]
    private int[] chaosValues = { 0, 3, 6, 10 };

    [Header("Result Text")]
    [SerializeField]
    [Tooltip("Text that displays the selected grid size after a spin.")]
    private TMP_Text gridSizeResultText;

    [SerializeField]
    [Tooltip("Text that displays the selected difficulty after a spin.")]
    private TMP_Text difficultyResultText;

    [SerializeField]
    [Tooltip("Text that displays the selected run time after a spin.")]
    private TMP_Text timeResultText;

    [SerializeField]
    [Tooltip("Optional text that displays the independently selected ChaosValue.")]
    private TMP_Text chaosResultText;

    [Header("Result Event")]
    [SerializeField]
    [Tooltip("Legacy callback invoked with grid size, difficulty, and time.")]
    private SlotResultEvent onSpinCompleted = new();

    [SerializeField]
    [Tooltip("Invoked with grid size, difficulty, time, and ChaosValue.")]
    private SlotChaosResultEvent onSpinCompletedWithChaos = new();

    private readonly Transform[] cylinders = new Transform[3];
    private readonly Quaternion[] originalLocalRotations = new Quaternion[3];
    private readonly float[] currentAngles = new float[3];
    private readonly float[] targetAngles = new float[3];
    private readonly bool[] cylinderStopped = new bool[3];

    private bool isSpinning;
    private bool hasResult;
    private Vector2Int selectedGridSize;
    private int selectedDifficulty;
    private float selectedTime;
    private int selectedChaosValue;

    public bool IsSpinning => isSpinning;
    public bool HasResult => hasResult;
    public Vector2Int GridSize { get; private set; }
    public int Difficulty { get; private set; }
    public float Time { get; private set; }
    public int ChaosValue { get; private set; }
    public SlotResultEvent OnSpinCompleted => onSpinCompleted;
    public SlotChaosResultEvent OnSpinCompletedWithChaos =>
        onSpinCompletedWithChaos;

    /// <summary>
    /// C# result callback with the independently selected grid size, difficulty,
    /// and run time. Use SpinCompletedWithChaos for the complete run result.
    /// </summary>
    public event Action<Vector2Int, int, float> SpinCompleted;

    /// <summary>
    /// Complete result callback including the independent ChaosValue.
    /// </summary>
    public event Action<Vector2Int, int, float, int> SpinCompletedWithChaos;

    private void Awake()
    {
        if (lever == null)
            lever = GetComponentInChildren<LeverPull>(true);

        cylinders[0] = firstCylinder;
        cylinders[1] = secondCylinder;
        cylinders[2] = thirdCylinder;

        if (!HasAllReferences())
        {
            Debug.LogError(
                $"{nameof(SlotMachineController)} requires a lever and all three cylinder transforms.",
                this);
            enabled = false;
            return;
        }

        localRotationAxis.Normalize();

        for (int i = 0; i < cylinders.Length; i++)
            originalLocalRotations[i] = cylinders[i].localRotation;
    }

    private void OnEnable()
    {
        if (lever != null)
            lever.Pulled += HandleLeverPulled;
    }

    private void OnDisable()
    {
        if (lever != null)
            lever.Pulled -= HandleLeverPulled;

        if (isSpinning)
        {
            isSpinning = false;
            RestoreOriginalRotations();
        }
    }

    private void Update()
    {
        if (!isSpinning)
            return;

        bool allStopped = true;
        float maximumStep = rotationSpeed * UnityEngine.Time.deltaTime;

        for (int i = 0; i < cylinders.Length; i++)
        {
            if (cylinderStopped[i])
                continue;

            currentAngles[i] = Mathf.MoveTowards(
                currentAngles[i],
                targetAngles[i],
                maximumStep);

            SetCylinderAngle(i, currentAngles[i]);

            if (Mathf.Approximately(currentAngles[i], targetAngles[i]))
                FinishCylinder(i);
            else
                allStopped = false;
        }

        if (allStopped)
            CompleteSpin();
    }

    private void OnValidate()
    {
        rotationSpeed = Mathf.Max(0.01f, rotationSpeed);
        minimumRounds = Mathf.Max(0, minimumRounds);
        maximumRounds = Mathf.Max(minimumRounds, maximumRounds);

        if (localRotationAxis.sqrMagnitude < 0.0001f)
            localRotationAxis = Vector3.up;
    }

    /// <summary>
    /// Starts a spin when the machine is ready. Useful for UnityEvents.
    /// </summary>
    public void Spin()
    {
        TrySpin();
    }

    /// <summary>
    /// Starts a spin and reports whether it was accepted.
    /// </summary>
    public bool TrySpin()
    {
        if (isSpinning || !isActiveAndEnabled || !HasAllReferences())
            return false;

        if (!HasConfiguredValues())
        {
            Debug.LogError(
                $"{nameof(SlotMachineController)} requires at least one configured " +
                "grid size, difficulty, time, and chaos value.",
                this);
            return false;
        }



        selectedGridSize = gridSizes[UnityEngine.Random.Range(0, shopController.GetValue(UpgradeId.GridSize))];
        selectedDifficulty = difficulties[UnityEngine.Random.Range(0, shopController.GetValue(UpgradeId.Difficulty))];
        selectedTime = times[UnityEngine.Random.Range(0, shopController.GetValue(UpgradeId.Time))];
        selectedChaosValue = chaosValues[UnityEngine.Random.Range(0, shopController.GetValue(UpgradeId.Chaos))];

        hasResult = false;
        isSpinning = true;

        for (int i = 0; i < cylinders.Length; i++)
        {
            int rounds = UnityEngine.Random.Range(minimumRounds, maximumRounds + 1);
            currentAngles[i] = 0f;
            targetAngles[i] = -rounds * DegreesPerRound;
            cylinderStopped[i] = false;
            cylinders[i].localRotation = originalLocalRotations[i];
        }

        return true;
    }

    /// <summary>
    /// Returns the latest completed result. False means the first spin is still
    /// pending or a new spin is currently running.
    /// </summary>
    public bool TryGetResult(out Vector2Int gridSize, out int difficulty, out float time)
    {
        gridSize = GridSize;
        difficulty = Difficulty;
        time = Time;
        return hasResult;
    }

    public bool TryGetResult(
        out Vector2Int gridSize,
        out int difficulty,
        out float time,
        out int chaosValue)
    {
        bool resultAvailable = TryGetResult(
            out gridSize,
            out difficulty,
            out time);
        chaosValue = ChaosValue;
        return resultAvailable;
    }

    private void HandleLeverPulled()
    {
        TrySpin();
    }

    private void FinishCylinder(int cylinderIndex)
    {
        currentAngles[cylinderIndex] = 0f;
        cylinders[cylinderIndex].localRotation = originalLocalRotations[cylinderIndex];
        cylinderStopped[cylinderIndex] = true;
    }

    private void CompleteSpin()
    {
        isSpinning = false;
        GridSize = selectedGridSize;
        Difficulty = selectedDifficulty;
        Time = selectedTime;
        ChaosValue = Mathf.Clamp(
            selectedChaosValue,
            RunSettings.MinimumChaosValue,
            RunSettings.MaximumChaosValue);
        hasResult = true;

        UpdateResultTexts();
        SpinCompleted?.Invoke(GridSize, Difficulty, Time);
        SpinCompletedWithChaos?.Invoke(GridSize, Difficulty, Time, ChaosValue);
        onSpinCompleted.Invoke(GridSize, Difficulty, Time);
        onSpinCompletedWithChaos.Invoke(GridSize, Difficulty, Time, ChaosValue);
    }

    private void UpdateResultTexts()
    {
        if (gridSizeResultText != null)
            gridSizeResultText.text = $"{GridSize.x}x{GridSize.y}";

        if (difficultyResultText != null)
            difficultyResultText.text = Difficulty.ToString();

        if (timeResultText != null)
            timeResultText.text = Time.ToString("0.##");

        if (chaosResultText != null)
            chaosResultText.text = ChaosValue.ToString();
    }

    private void SetCylinderAngle(int cylinderIndex, float angle)
    {
        cylinders[cylinderIndex].localRotation =
            originalLocalRotations[cylinderIndex] *
            Quaternion.AngleAxis(angle, localRotationAxis);
    }

    private void RestoreOriginalRotations()
    {
        for (int i = 0; i < cylinders.Length; i++)
        {
            if (cylinders[i] == null)
                continue;

            currentAngles[i] = 0f;
            cylinders[i].localRotation = originalLocalRotations[i];
            cylinderStopped[i] = true;
        }
    }

    private bool HasAllReferences()
    {
        return lever != null &&
               firstCylinder != null &&
               secondCylinder != null &&
               thirdCylinder != null;
    }

    private bool HasConfiguredValues()
    {
        return gridSizes != null && gridSizes.Length > 0 &&
               difficulties != null && difficulties.Length > 0 &&
               times != null && times.Length > 0 &&
               chaosValues != null && chaosValues.Length > 0;
    }
}
