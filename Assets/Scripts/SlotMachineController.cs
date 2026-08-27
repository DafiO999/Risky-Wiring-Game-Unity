using System;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public sealed class SlotResultEvent : UnityEvent<string, int, float>
{
}

[DisallowMultipleComponent]
public sealed class SlotMachineController : MonoBehaviour
{
    private const int DisplayCount = 4;
    private const float DegreesPerDisplay = 90f;
    private const float DegreesPerRound = 360f;

    [Header("Trigger")]
    [SerializeField]
    [Tooltip("The lever that starts a spin. A lever below this object is found automatically if empty.")]
    private LeverPull lever;

    [Header("Cylinders")]
    [SerializeField]
    [Tooltip("Cylinder whose four displays choose the level type.")]
    private Transform firstCylinder;

    [SerializeField]
    [Tooltip("Cylinder whose four displays choose the difficulty.")]
    private Transform secondCylinder;

    [SerializeField]
    [Tooltip("Cylinder whose four displays choose the time.")]
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

    [Header("Display Values (0, -90, -180, -270 degrees)")]
    [SerializeField]
    private string[] levelTypes = { "Level Type 1", "Level Type 2", "Level Type 3", "Level Type 4" };

    [SerializeField]
    private int[] difficulties = { 1, 2, 3, 4 };

    [SerializeField]
    private float[] times = { 30f, 60f, 90f, 120f };

    [Header("Result Event")]
    [SerializeField]
    [Tooltip("Invoked after every cylinder has stopped: level type, difficulty, time.")]
    private SlotResultEvent onSpinCompleted = new();

    private readonly Transform[] cylinders = new Transform[3];
    private readonly Quaternion[] zeroRotations = new Quaternion[3];
    private readonly float[] currentAngles = new float[3];
    private readonly float[] targetAngles = new float[3];
    private readonly int[] currentDisplayIndices = new int[3];
    private readonly int[] targetDisplayIndices = new int[3];
    private readonly bool[] cylinderStopped = new bool[3];

    private bool isSpinning;
    private bool hasResult;

    public bool IsSpinning => isSpinning;
    public bool HasResult => hasResult;
    public string LevelType { get; private set; }
    public int Difficulty { get; private set; }
    public float Time { get; private set; }
    public SlotResultEvent OnSpinCompleted => onSpinCompleted;

    /// <summary>
    /// C# result callback with the selected level type, difficulty, and time.
    /// </summary>
    public event Action<string, int, float> SpinCompleted;

    private void Awake()
    {
        EnsureValueArraySizes();

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
        {
            zeroRotations[i] = cylinders[i].localRotation;
            currentDisplayIndices[i] = 0;
            currentAngles[i] = 0f;
        }
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

        EnsureValueArraySizes();
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

        hasResult = false;
        isSpinning = true;

        for (int i = 0; i < cylinders.Length; i++)
        {
            int targetIndex = UnityEngine.Random.Range(0, DisplayCount);
            int rounds = UnityEngine.Random.Range(minimumRounds, maximumRounds + 1);
            int stepsToTarget = (targetIndex - currentDisplayIndices[i] + DisplayCount) % DisplayCount;

            targetDisplayIndices[i] = targetIndex;
            targetAngles[i] = currentAngles[i]
                              - rounds * DegreesPerRound
                              - stepsToTarget * DegreesPerDisplay;
            cylinderStopped[i] = false;
        }

        return true;
    }

    /// <summary>
    /// Returns the latest completed result. False means the first spin is still
    /// pending or a new spin is currently running.
    /// </summary>
    public bool TryGetResult(out string levelType, out int difficulty, out float time)
    {
        levelType = LevelType;
        difficulty = Difficulty;
        time = Time;
        return hasResult;
    }

    private void HandleLeverPulled()
    {
        TrySpin();
    }

    private void FinishCylinder(int cylinderIndex)
    {
        int displayIndex = targetDisplayIndices[cylinderIndex];
        currentDisplayIndices[cylinderIndex] = displayIndex;

        // Keep the angle small after every spin, and land exactly on a display.
        currentAngles[cylinderIndex] = -displayIndex * DegreesPerDisplay;
        SetCylinderAngle(cylinderIndex, currentAngles[cylinderIndex]);
        cylinderStopped[cylinderIndex] = true;
    }

    private void CompleteSpin()
    {
        isSpinning = false;
        LevelType = levelTypes[currentDisplayIndices[0]];
        Difficulty = difficulties[currentDisplayIndices[1]];
        Time = times[currentDisplayIndices[2]];
        hasResult = true;

        SpinCompleted?.Invoke(LevelType, Difficulty, Time);
        onSpinCompleted.Invoke(LevelType, Difficulty, Time);
    }

    private void SetCylinderAngle(int cylinderIndex, float angle)
    {
        cylinders[cylinderIndex].localRotation =
            zeroRotations[cylinderIndex] * Quaternion.AngleAxis(angle, localRotationAxis);
    }

    private bool HasAllReferences()
    {
        return lever != null &&
               firstCylinder != null &&
               secondCylinder != null &&
               thirdCylinder != null;
    }

    private void EnsureValueArraySizes()
    {
        Array.Resize(ref levelTypes, DisplayCount);
        Array.Resize(ref difficulties, DisplayCount);
        Array.Resize(ref times, DisplayCount);
    }
}
