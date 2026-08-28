using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RunGenerator))]
public sealed class RunGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        RunGenerator generator = (RunGenerator)target;
        DifficultyLevel level = generator.Profile.GetLevel(generator.Difficulty);
        RunComponentCounts preview =
            generator.PreviewCounts(generator.Difficulty, generator.Seed);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Difficulty Preview", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Generators", FormatRange(level.Generators));
        EditorGUILayout.LabelField("Batteries", FormatRange(level.Batteries));
        EditorGUILayout.LabelField("Consumers", FormatRange(level.Consumers));
        EditorGUILayout.LabelField(
            "Seed Roll",
            $"{preview.generators} generators, {preview.batteries} batteries, " +
            $"{preview.lamps + preview.fans} consumers");

        if (!string.IsNullOrEmpty(generator.LastFailureReason))
            EditorGUILayout.HelpBox(generator.LastFailureReason, MessageType.Error);

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate Run"))
        {
            generator.Generate();
            EditorUtility.SetDirty(generator);
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("Clear Board"))
        {
            generator.ClearBoard();
            EditorUtility.SetDirty(generator);
            SceneView.RepaintAll();
        }
    }


    private static string FormatRange(DifficultyAmountRange range)
    {
        return range.Minimum == range.Maximum
            ? range.Minimum.ToString()
            : $"{range.Minimum}-{range.Maximum}";
    }
}
