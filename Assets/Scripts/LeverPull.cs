using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class LeverPull : MonoBehaviour
{
    private static WaitForSeconds _waitForSeconds5 = new WaitForSeconds(5);
    [SerializeField]
    private CinemachineImpulseSource impulse;

    [Header("Movement")]
    [SerializeField]
    [Tooltip("Rotation applied around the object's local X axis. Use a negative value to reverse direction.")]
    private float pullAngle = -45f;

    [SerializeField, Min(0.01f)]
    [Tooltip("Rotation speed in degrees per second, used in both directions.")]
    private float rotationSpeed = 120f;

    [Header("Click Raycast")]
    [SerializeField]
    [Tooltip("Camera used to cast from the cursor. If empty, the Main Camera is used.")]
    private Camera raycastCamera;

    [SerializeField]
    [Tooltip("Only colliders on these layers can receive the click.")]
    private LayerMask clickableLayers = Physics.DefaultRaycastLayers;

    [SerializeField, Min(0f)]
    private float maximumDistance = 1000f;

    [SerializeField]
    private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal;

    [SerializeField]
    [Tooltip("Treat colliders on child objects as part of this lever.")]
    private bool includeChildColliders = true;

    private Quaternion restingLocalRotation;
    private float currentAngle;
    private LeverState state;

    public bool IsMoving => state != LeverState.Idle;

    /// <summary>
    /// Raised once when a new pull starts. Systems such as the slot cylinders can
    /// subscribe without being coupled to this lever's click handling.
    /// </summary>
    public event Action Pulled;

    private void Awake()
    {
        restingLocalRotation = transform.localRotation;
    }

    private void Update()
    {
        CheckForClick();
        AnimateLever();
    }

    private void OnDisable()
    {
        currentAngle = 0f;
        state = LeverState.Idle;
        transform.localRotation = restingLocalRotation;
    }

    private void OnValidate()
    {
        rotationSpeed = Mathf.Max(0.01f, rotationSpeed);
        maximumDistance = Mathf.Max(0f, maximumDistance);
    }

    /// <summary>
    /// Starts one pull-and-return cycle. Calls made while the lever is moving are ignored.
    /// </summary>
    public void Pull()
    {
        if (state != LeverState.Idle)
            return;

        impulse.GenerateImpulse();
        state = LeverState.Pulling;
        Pulled?.Invoke();
    }

    private void CheckForClick()
    {
        Mouse mouse = Mouse.current;
        if (state != LeverState.Idle || mouse == null || !mouse.leftButton.wasPressedThisFrame)
            return;

        Camera cameraToUse = raycastCamera != null ? raycastCamera : Camera.main;
        if (cameraToUse == null)
            return;

        Ray ray = cameraToUse.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                maximumDistance,
                clickableLayers,
                triggerInteraction))
        {
            return;
        }

        Transform hitTransform = hit.collider.transform;
        bool clickedThisLever = hitTransform == transform ||
                                (includeChildColliders && hitTransform.IsChildOf(transform));

        if (clickedThisLever)
            Pull();
    }

    private void AnimateLever()
    {
        if (state == LeverState.Idle)
            return;

        float targetAngle = state == LeverState.Pulling ? pullAngle : 0f;
        currentAngle = Mathf.MoveTowards(
            currentAngle,
            targetAngle,
            rotationSpeed * Time.deltaTime);

        transform.localRotation = restingLocalRotation * Quaternion.AngleAxis(currentAngle, Vector3.forward);

        if (!Mathf.Approximately(currentAngle, targetAngle))
            return;

        if (state == LeverState.Pulling)
        {
            state = LeverState.Returning;
        }
        else
        {
            currentAngle = 0f;
            StartCoroutine(WaitBeforeAllowPull());
            transform.localRotation = restingLocalRotation;
        }
    }

    private IEnumerator WaitBeforeAllowPull()
    {
        yield return _waitForSeconds5;
        state = LeverState.Idle;
    }

    private enum LeverState
    {
        Idle,
        Pulling,
        Returning
    }
}
