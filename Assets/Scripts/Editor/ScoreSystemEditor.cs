using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ScoreSystem), true)]
public sealed class ScoreSystemEditor : Editor
{
    private SerializedProperty currentScoreProperty;

    private void OnEnable()
    {
        currentScoreProperty = serializedObject.FindProperty("currentScore");
    }

    public override void OnInspectorGUI()
    {
        ScoreSystem scoreSystem = (ScoreSystem)target;
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                "Script",
                MonoScript.FromMonoBehaviour(scoreSystem),
                typeof(MonoScript),
                false);
        }

        EditorGUILayout.PropertyField(currentScoreProperty, new GUIContent("Current Score"));

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField(
                "Current Score / Second",
                scoreSystem.CurrentScorePerSecond);
        }

        serializedObject.ApplyModifiedProperties();

        if (GUILayout.Button("Reset Score"))
        {
            Undo.RecordObject(scoreSystem, "Reset Score");
            scoreSystem.ResetScore();
            EditorUtility.SetDirty(scoreSystem);
        }
    }
}
