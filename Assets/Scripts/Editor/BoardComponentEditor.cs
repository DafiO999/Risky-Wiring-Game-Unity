using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BoardComponent), true)]
public sealed class BoardComponentEditor : Editor
{
    private string lastFailureReason;
    private SerializedProperty portsProperty;
    private SerializedProperty drawPortGizmosProperty;
    private SerializedProperty portGizmoSizeProperty;
    private SerializedProperty inputPortColorProperty;
    private SerializedProperty outputPortColorProperty;

    private void OnEnable()
    {
        portsProperty = serializedObject.FindProperty("ports");
        drawPortGizmosProperty = serializedObject.FindProperty("drawPortGizmos");
        portGizmoSizeProperty = serializedObject.FindProperty("portGizmoSize");
        inputPortColorProperty = serializedObject.FindProperty("inputPortColor");
        outputPortColorProperty = serializedObject.FindProperty("outputPortColor");
    }

    public override void OnInspectorGUI()
    {
        BoardComponent component = (BoardComponent)target;
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                "Script",
                MonoScript.FromMonoBehaviour(component),
                typeof(MonoScript),
                false);
        }

        GridBoard targetBoard = (GridBoard)EditorGUILayout.ObjectField(
            "Grid Board",
            component.Board,
            typeof(GridBoard),
            true);
        Vector2Int targetPosition = EditorGUILayout.Vector2IntField(
            "Grid Position",
            component.GridPosition);
        BoardRotation targetRotation = (BoardRotation)EditorGUILayout.EnumPopup(
            "Rotation",
            component.Rotation);

        bool placementChanged = targetBoard != component.Board ||
                                targetPosition != component.GridPosition ||
                                targetRotation != component.Rotation;

        if (placementChanged)
            ApplyPlacement(component, targetBoard, targetPosition, targetRotation);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(targetBoard == null))
        {
            if (GUILayout.Button("Apply Placement"))
                ApplyPlacement(component, targetBoard, targetPosition, targetRotation);
        }

        DrawPlacementStatus(component);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Ports", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(component.GetType() != typeof(BoardComponent)))
            EditorGUILayout.PropertyField(portsProperty, true);

        if (component is ScoredPowerConsumerComponent consumer)
        {
            SerializedProperty scoreSystemProperty =
                serializedObject.FindProperty("scoreSystem");
            if (scoreSystemProperty != null)
                EditorGUILayout.PropertyField(scoreSystemProperty);

            SerializedProperty requiredPowerProperty =
                serializedObject.FindProperty("requiredPower");
            if (requiredPowerProperty != null)
                EditorGUILayout.PropertyField(requiredPowerProperty);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Power Debug", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Required Power",
                consumer.RequiredPower.ToString());
            EditorGUILayout.LabelField("Received Power", consumer.ReceivedPower.ToString());
            EditorGUILayout.LabelField("Score / Second", consumer.ScorePerSecond.ToString());
        }

        if (component is BatteryComponent battery)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Battery", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("capacity"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("charge"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("outputPower"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Power Debug", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Received Input Power",
                battery.ReceivedInputPower.ToString());
            EditorGUILayout.LabelField(
                "Provided Output Power",
                battery.ProvidedOutputPower.ToString());
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Port Debug", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(drawPortGizmosProperty);
        EditorGUILayout.PropertyField(portGizmoSizeProperty);
        EditorGUILayout.PropertyField(inputPortColorProperty);
        EditorGUILayout.PropertyField(outputPortColorProperty);

        if (serializedObject.ApplyModifiedProperties())
        {
            component.ValidatePortConfiguration();
            EditorUtility.SetDirty(component);
            SceneView.RepaintAll();
        }
    }

    private void ApplyPlacement(
        BoardComponent component,
        GridBoard targetBoard,
        Vector2Int targetPosition,
        BoardRotation targetRotation)
    {
        if (targetBoard == null)
        {
            Undo.RecordObject(component, "Clear Board Component Placement");
            component.ClearPlacement();
            EditorUtility.SetDirty(component);
            lastFailureReason = string.Empty;
            return;
        }

        if (!targetBoard.CanPlaceComponent(
                component,
                targetPosition,
                (int)targetRotation,
                out string failureReason))
        {
            lastFailureReason = failureReason;
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(component.gameObject, "Place Board Component");

        Transform previousRoot = targetBoard.ComponentsRoot;
        Transform targetRoot = targetBoard.EnsureComponentsRoot();
        if (previousRoot == null && targetRoot != null)
            Undo.RegisterCreatedObjectUndo(targetRoot.gameObject, "Create Components Container");

        if (component.transform.parent != targetRoot)
            Undo.SetTransformParent(component.transform, targetRoot, "Parent Board Component");

        if (component.TrySetPlacement(
                targetBoard,
                targetPosition,
                targetRotation))
        {
            EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(targetBoard);
            lastFailureReason = string.Empty;
            SceneView.RepaintAll();
        }
    }

    private void DrawPlacementStatus(BoardComponent component)
    {
        if (!string.IsNullOrEmpty(lastFailureReason))
        {
            EditorGUILayout.HelpBox(lastFailureReason, MessageType.Error);
            return;
        }

        if (component.IsPlaced)
        {
            EditorGUILayout.HelpBox(
                $"Occupies a 2x2 footprint from {component.GridPosition}. " +
                $"Rotation: {component.RotationDegrees} degrees.",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "This component is not currently placed on a board.",
                MessageType.Warning);
        }
    }
}
