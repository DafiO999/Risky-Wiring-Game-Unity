using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class Highlight : MonoBehaviour
{
    private const string OutlineShaderResource = "RiskyWiringHighlightOutline";
    private const string OutlineObjectName = "__HighlightOutline";

    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    private static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
    private static readonly int ZTestId = Shader.PropertyToID("_ZTest");

    [Header("Appearance")]
    [SerializeField, ColorUsage(true, true)]
    private Color outlineColor = new(0f, 0.8f, 1f, 1f);

    [SerializeField, Min(0f)]
    [Tooltip("Outline thickness in world units.")]
    private float outlineWidth = 0.015f;

    [SerializeField, Min(0f)]
    [Tooltip("HDR brightness sent to Bloom. A value above 1 produces a glow when Bloom is enabled.")]
    private float glowIntensity = 3f;

    [Header("Pulse")]
    [SerializeField]
    private bool pulse;

    [SerializeField, Min(0f)]
    [Tooltip("Number of glow pulses per second.")]
    private float pulseSpeed = 1.5f;

    [SerializeField, Range(0f, 1f)]
    [Tooltip("Lowest brightness reached during a pulse, as a fraction of Glow Intensity.")]
    private float minimumPulseBrightness = 0.35f;

    [Header("Rendering")]
    [SerializeField]
    [Tooltip("Also outlines supported renderers below this GameObject.")]
    private bool includeChildren = true;

    [SerializeField]
    [Tooltip("Draws the outline even when the object is behind other geometry.")]
    private bool visibleThroughWalls;

    [SerializeField]
    [Tooltip("Controls the outline without disabling this component.")]
    private bool highlighted = true;

    private readonly List<OutlineRenderer> outlineRenderers = new();
    private Material outlineMaterial;

    public bool IsHighlighted => highlighted;
    public Color OutlineColor => outlineColor;

    private void OnEnable()
    {
        CreateOutline();
    }

    private void LateUpdate()
    {
        SynchronizeRenderers();

        if (pulse && outlineMaterial != null)
            outlineMaterial.SetFloat(GlowIntensityId, GetCurrentGlowIntensity());
    }

    private void OnDisable()
    {
        DestroyOutline();
    }

    private void OnDestroy()
    {
        DestroyOutline();
    }

    private void OnValidate()
    {
        outlineWidth = Mathf.Max(0f, outlineWidth);
        glowIntensity = Mathf.Max(0f, glowIntensity);
        pulseSpeed = Mathf.Max(0f, pulseSpeed);
        minimumPulseBrightness = Mathf.Clamp01(minimumPulseBrightness);

        if (outlineMaterial != null)
            ApplyMaterialSettings();
    }

    public void SetHighlighted(bool value)
    {
        highlighted = value;
        SynchronizeRenderers();
    }

    public void SetOutlineColor(Color color)
    {
        outlineColor = color;

        if (outlineMaterial != null)
            outlineMaterial.SetColor(OutlineColorId, outlineColor);
    }

    public void Show()
    {
        SetHighlighted(true);
    }

    public void Hide()
    {
        SetHighlighted(false);
    }

    /// <summary>
    /// Rebuilds the generated outline renderers. Call this after adding or removing
    /// renderers below this GameObject at runtime.
    /// </summary>
    public void Refresh()
    {
        DestroyOutline();

        if (isActiveAndEnabled)
            CreateOutline();
    }

    private void CreateOutline()
    {
        if (outlineMaterial != null || outlineRenderers.Count > 0)
            return;

        Shader outlineShader = Resources.Load<Shader>(OutlineShaderResource);
        if (outlineShader == null)
        {
            Debug.LogError(
                $"{nameof(Highlight)} could not load Resources/{OutlineShaderResource}.shader.",
                this);
            return;
        }

        outlineMaterial = new Material(outlineShader)
        {
            name = $"{name} - Highlight Outline (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        ApplyMaterialSettings();

        Renderer[] sources = includeChildren
            ? GetComponentsInChildren<Renderer>(true)
            : GetComponents<Renderer>();

        foreach (Renderer source in sources)
        {
            // Refresh() destroys runtime children at the end of the frame in play mode.
            // This filter keeps those pending objects from becoming outline sources.
            if ((source.gameObject.hideFlags & HideFlags.HideAndDontSave) != 0)
                continue;

            Renderer outline = CreateOutlineRenderer(source);
            if (outline != null)
                outlineRenderers.Add(new OutlineRenderer(source, outline));
        }

        SynchronizeRenderers();
    }

    private Renderer CreateOutlineRenderer(Renderer source)
    {
        Mesh mesh;

        if (source is MeshRenderer)
        {
            MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
            mesh = sourceFilter != null ? sourceFilter.sharedMesh : null;
            if (mesh == null)
                return null;

            GameObject outlineObject = CreateOutlineObject(source);
            MeshFilter outlineFilter = outlineObject.AddComponent<MeshFilter>();
            outlineFilter.sharedMesh = mesh;

            MeshRenderer outline = outlineObject.AddComponent<MeshRenderer>();
            ConfigureRenderer(source, outline, mesh.subMeshCount);
            return outline;
        }

        if (source is SkinnedMeshRenderer sourceSkinned)
        {
            mesh = sourceSkinned.sharedMesh;
            if (mesh == null)
                return null;

            GameObject outlineObject = CreateOutlineObject(source);
            SkinnedMeshRenderer outline = outlineObject.AddComponent<SkinnedMeshRenderer>();
            outline.sharedMesh = mesh;
            outline.rootBone = sourceSkinned.rootBone;
            outline.bones = sourceSkinned.bones;
            outline.localBounds = sourceSkinned.localBounds;
            outline.quality = sourceSkinned.quality;
            outline.updateWhenOffscreen = sourceSkinned.updateWhenOffscreen;
            ConfigureRenderer(source, outline, mesh.subMeshCount);
            return outline;
        }

        return null;
    }

    private static GameObject CreateOutlineObject(Renderer source)
    {
        GameObject outlineObject = new(OutlineObjectName)
        {
            hideFlags = HideFlags.HideAndDontSave,
            layer = source.gameObject.layer
        };

        Transform outlineTransform = outlineObject.transform;
        outlineTransform.SetParent(source.transform, false);
        outlineTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        outlineTransform.localScale = Vector3.one;
        return outlineObject;
    }

    private void ConfigureRenderer(Renderer source, Renderer outline, int subMeshCount)
    {
        int materialCount = Mathf.Max(1, subMeshCount);
        Material[] materials = new Material[materialCount];
        for (int i = 0; i < materials.Length; i++)
            materials[i] = outlineMaterial;

        outline.sharedMaterials = materials;
        outline.shadowCastingMode = ShadowCastingMode.Off;
        outline.receiveShadows = false;
        outline.lightProbeUsage = LightProbeUsage.Off;
        outline.reflectionProbeUsage = ReflectionProbeUsage.Off;
        outline.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        outline.allowOcclusionWhenDynamic = false;
        outline.sortingLayerID = source.sortingLayerID;
        outline.sortingOrder = source.sortingOrder;
        outline.renderingLayerMask = source.renderingLayerMask;
    }

    private void SynchronizeRenderers()
    {
        for (int i = outlineRenderers.Count - 1; i >= 0; i--)
        {
            OutlineRenderer pair = outlineRenderers[i];
            if (pair.Source == null || pair.Outline == null)
            {
                outlineRenderers.RemoveAt(i);
                continue;
            }

            pair.Outline.enabled = highlighted && pair.Source.enabled;

            if (pair.Source is SkinnedMeshRenderer sourceSkinned &&
                pair.Outline is SkinnedMeshRenderer outlineSkinned)
            {
                SynchronizeBlendShapes(sourceSkinned, outlineSkinned);
            }
        }
    }

    private static void SynchronizeBlendShapes(
        SkinnedMeshRenderer source,
        SkinnedMeshRenderer outline)
    {
        Mesh mesh = source.sharedMesh;
        if (mesh == null)
            return;

        for (int i = 0; i < mesh.blendShapeCount; i++)
            outline.SetBlendShapeWeight(i, source.GetBlendShapeWeight(i));
    }

    private void ApplyMaterialSettings()
    {
        outlineMaterial.SetColor(OutlineColorId, outlineColor);
        outlineMaterial.SetFloat(OutlineWidthId, outlineWidth);
        outlineMaterial.SetFloat(GlowIntensityId, GetCurrentGlowIntensity());
        outlineMaterial.SetInt(
            ZTestId,
            (int)(visibleThroughWalls ? CompareFunction.Always : CompareFunction.LessEqual));
    }

    private float GetCurrentGlowIntensity()
    {
        if (!pulse)
            return glowIntensity;

        float wave = (Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
        return glowIntensity * Mathf.Lerp(minimumPulseBrightness, 1f, wave);
    }

    private void DestroyOutline()
    {
        foreach (OutlineRenderer pair in outlineRenderers)
        {
            if (pair.Outline != null)
                DestroyRuntimeObject(pair.Outline.gameObject);
        }

        outlineRenderers.Clear();

        if (outlineMaterial != null)
            DestroyRuntimeObject(outlineMaterial);

        outlineMaterial = null;
    }

    private static void DestroyRuntimeObject(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    private sealed class OutlineRenderer
    {
        public readonly Renderer Source;
        public readonly Renderer Outline;

        public OutlineRenderer(Renderer source, Renderer outline)
        {
            Source = source;
            Outline = outline;
        }
    }
}
