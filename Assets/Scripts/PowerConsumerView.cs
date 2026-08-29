using UnityEngine;

[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class PowerConsumerView : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [SerializeField]
    private ScoredPowerConsumerComponent consumer;

    [SerializeField]
    [Tooltip("Renderers tinted from the consumer state. If empty, child renderers are found automatically.")]
    private Renderer[] targetRenderers;

    [Header("Power State Colors")]
    [SerializeField]
    private Color offColor = new(0.16f, 0.18f, 0.2f, 1f);

    [SerializeField]
    private Color underpoweredColor = new(1f, 0.62f, 0.08f, 1f);

    [SerializeField]
    private Color correctlyPoweredColor = new(0.2f, 1f, 0.35f, 1f);

    [SerializeField]
    private Color overloadedColor = new(1f, 0.12f, 0.08f, 1f);

    private MaterialPropertyBlock propertyBlock;
    private bool hasAppliedState;

    public ScoredPowerConsumerComponent Consumer => ResolveConsumer();
    public ConsumerPowerState DisplayedState { get; private set; }

    private void OnEnable()
    {
        hasAppliedState = false;
        ScoredPowerConsumerComponent target = ResolveConsumer();
        if (target != null)
        {
            target.PowerStateChanged += HandlePowerStateChanged;
            target.RequiredPowerChanged += HandleRequiredPowerChanged;
        }

        EnsureRenderers();
        ApplyState(target != null ? target.PowerState : ConsumerPowerState.Off);
    }

    private void OnDisable()
    {
        if (consumer != null)
        {
            consumer.PowerStateChanged -= HandlePowerStateChanged;
            consumer.RequiredPowerChanged -= HandleRequiredPowerChanged;
        }
    }

    private void OnValidate()
    {
        hasAppliedState = false;
        ResolveConsumer();
        EnsureRenderers();
        ApplyState(consumer != null ? consumer.PowerState : ConsumerPowerState.Off);
    }

    public void ApplyState(ConsumerPowerState state)
    {
        if (hasAppliedState && DisplayedState == state)
            return;

        DisplayedState = state;
        hasAppliedState = true;
        Color color = state switch
        {
            ConsumerPowerState.Underpowered => underpoweredColor,
            ConsumerPowerState.CorrectlyPowered => correctlyPoweredColor,
            ConsumerPowerState.Overloaded => overloadedColor,
            _ => offColor
        };

        propertyBlock ??= new MaterialPropertyBlock();
        foreach (Renderer targetRenderer in targetRenderers)
        {
            if (targetRenderer == null)
                continue;

            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    private void HandlePowerStateChanged(ConsumerPowerState state)
    {
        ApplyState(state);
    }

    private void HandleRequiredPowerChanged(int _)
    {
        ApplyState(consumer != null
            ? consumer.PowerState
            : ConsumerPowerState.Off);
    }

    private ScoredPowerConsumerComponent ResolveConsumer()
    {
        if (consumer == null)
            consumer = GetComponent<ScoredPowerConsumerComponent>();

        return consumer;
    }

    private void EnsureRenderers()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<Renderer>(true);
    }
}
