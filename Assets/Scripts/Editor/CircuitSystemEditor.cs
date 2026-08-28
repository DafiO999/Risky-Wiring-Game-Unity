using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CircuitSystem))]
public sealed class CircuitSystemEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CircuitSystem circuit = (CircuitSystem)target;
        if (!Application.isPlaying)
            circuit.RebuildIfDirty();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Topology", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Nets", circuit.Nets.Count.ToString());
        EditorGUILayout.LabelField("Rebuild Count", circuit.RebuildCount.ToString());
        EditorGUILayout.LabelField("Power Ticks", circuit.PowerTickCount.ToString());
        EditorGUILayout.LabelField(
            "Tick Accumulator",
            $"{circuit.TickAccumulator:0.000} / {circuit.TickInterval:0.000} s");

        if (GUILayout.Button("Rebuild Topology"))
        {
            circuit.RebuildTopology();
            SceneView.RepaintAll();
        }
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawNetIds(CircuitSystem circuit, GizmoType gizmoType)
    {
        if (circuit == null || !circuit.isActiveAndEnabled || !circuit.DrawNetIds)
            return;

        GridBoard board = circuit.Board;
        if (board == null)
            return;

        if (!Application.isPlaying)
            circuit.RebuildIfDirty();
        Vector3 labelOffset =
            board.transform.up * circuit.NetIdLabelHeight * board.CellSize;

        foreach (CircuitNet net in circuit.Nets)
        {
            Color netColor = Color.HSVToRGB(
                Mathf.Repeat(net.NetId * 0.237f, 1f),
                0.75f,
                1f);
            GUIStyle labelStyle = new(EditorStyles.boldLabel);
            labelStyle.normal.textColor = netColor;

            foreach (Vector2Int cell in net.WireCells)
            {
                Handles.Label(
                    board.CellToWorld(cell) + labelOffset,
                    $"Net {net.NetId}  P:{net.AvailablePower}",
                    labelStyle);
            }

        }

        Transform componentsRoot = board.ComponentsRoot;
        if (componentsRoot == null)
            return;

        foreach (ScoredPowerConsumerComponent consumer in
                 componentsRoot.GetComponentsInChildren<ScoredPowerConsumerComponent>(true))
        {
            if (consumer.Board != board)
                continue;

            GUIStyle powerStyle = new(EditorStyles.boldLabel);
            powerStyle.normal.textColor = consumer.ReceivedPower switch
            {
                2 => Color.green,
                >= 3 => Color.red,
                _ => Color.white
            };
            Handles.Label(
                consumer.transform.position + labelOffset,
                $"Received: {consumer.ReceivedPower}",
                powerStyle);
        }

        foreach (BatteryComponent battery in
                 componentsRoot.GetComponentsInChildren<BatteryComponent>(true))
        {
            if (battery.Board != board)
                continue;

            GUIStyle batteryStyle = new(EditorStyles.boldLabel);
            batteryStyle.normal.textColor = Color.yellow;
            Handles.Label(
                battery.transform.position + labelOffset,
                $"Charge: {battery.Charge:0.##}/{battery.Capacity:0.##}  " +
                $"In:{battery.ReceivedInputPower} Out:{battery.ProvidedOutputPower}",
                batteryStyle);
        }
    }
}
