using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class CircuitDebugView : MonoBehaviour
{
    [Header("Debug Toggle")]
    [SerializeField]
    private bool showDebug = true;

    [SerializeField]
    private bool enableToggleHotkey = true;

    [SerializeField]
    private Key toggleKey = Key.F3;

    [Header("References")]
    [SerializeField]
    private GridBoard board;

    [SerializeField]
    private CircuitSystem circuitSystem;

    [SerializeField]
    private ScoreSystem scoreSystem;

    [SerializeField]
    [Tooltip("Camera used to project the pointer onto the grid. If empty, Camera.main is used.")]
    private Camera pointerCamera;

    [Header("Overlay")]
    [SerializeField]
    private Vector2 screenOffset = new(12f, 12f);

    [SerializeField, Min(220f)]
    private float panelWidth = 420f;

    [SerializeField, Range(10, 24)]
    private int fontSize = 14;

    private readonly StringBuilder textBuilder = new();
    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;

    public bool Visible
    {
        get => showDebug;
        set => showDebug = value;
    }

    public GridBoard Board => ResolveBoard();
    public CircuitSystem Circuit => ResolveCircuitSystem();
    public ScoreSystem ScoreSystem => ResolveScoreSystem();

    private void Reset()
    {
        showDebug = true;
        enableToggleHotkey = true;
        toggleKey = Key.F3;
        board = GetComponent<GridBoard>();
        circuitSystem = GetComponent<CircuitSystem>();
        scoreSystem = GetComponent<ScoreSystem>();
        screenOffset = new Vector2(12f, 12f);
        panelWidth = 420f;
        fontSize = 14;
    }

    private void OnValidate()
    {
        screenOffset = new Vector2(
            Mathf.Max(0f, screenOffset.x),
            Mathf.Max(0f, screenOffset.y));
        panelWidth = Mathf.Max(220f, panelWidth);
        fontSize = Mathf.Clamp(fontSize, 10, 24);
        titleStyle = null;
        bodyStyle = null;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (enableToggleHotkey &&
            toggleKey != Key.None &&
            keyboard != null &&
            keyboard[toggleKey].wasPressedThisFrame)
        {
            Toggle();
        }
    }

    private void OnGUI()
    {
        if (!showDebug)
            return;

        EnsureStyles();
        string debugText = TryGetHoveredCell(out Vector2Int hoveredCell)
            ? BuildDebugText(hoveredCell)
            : BuildNoHoverDebugText();

        int lineCount = 1;
        for (int index = 0; index < debugText.Length; index++)
        {
            if (debugText[index] == '\n')
                lineCount++;
        }

        float maximumPanelHeight = Mathf.Max(
            80f,
            Screen.height - screenOffset.y * 2f);
        float panelHeight = Mathf.Min(
            maximumPanelHeight,
            40f + lineCount * (fontSize + 4f));
        Rect panelRect = new(
            screenOffset.x,
            screenOffset.y,
            Mathf.Max(
                100f,
                Mathf.Min(panelWidth, Screen.width - screenOffset.x * 2f)),
            panelHeight);

        GUI.depth = -1000;
        GUILayout.BeginArea(panelRect, GUI.skin.box);
        string title = enableToggleHotkey && toggleKey != Key.None
            ? $"Circuit Debug  [{toggleKey}]"
            : "Circuit Debug";
        GUILayout.Label(title, titleStyle);
        GUILayout.Label(debugText, bodyStyle);
        GUILayout.EndArea();
    }

    [ContextMenu("Toggle Debug Overlay")]
    public void Toggle()
    {
        showDebug = !showDebug;
    }

    public string BuildDebugText(Vector2Int cell)
    {
        BeginDebugText();

        GridBoard targetBoard = ResolveBoard();
        if (targetBoard == null)
        {
            textBuilder.AppendLine("Grid: unavailable");
            return textBuilder.ToString();
        }

        if (!targetBoard.IsInside(cell))
        {
            textBuilder.Append("Hovered Cell: ").Append(cell).AppendLine(" (outside grid)");
            return textBuilder.ToString();
        }

        CircuitSystem targetCircuit = ResolveCircuitSystem();
        targetCircuit?.RebuildIfDirty();

        textBuilder.Append("Hovered Cell: ").AppendLine(FormatCell(cell));

        WireConnection connections = targetBoard.GetWireConnections(cell);
        textBuilder.Append("Wire: ").AppendLine(connections.ToString());
        CircuitNet wireNet = null;
        bool hasWireNet = connections != WireConnection.None &&
                          targetCircuit != null &&
                          targetCircuit.TryGetNet(cell, out wireNet);
        textBuilder.Append("NetId: ").AppendLine(
            hasWireNet ? FormatNetId(wireNet.NetId) : "--");
        textBuilder.Append("Wire Power: ").AppendLine(
            hasWireNet ? wireNet.AvailablePower.ToString() : "--");
        textBuilder.Append("Wire Visual: ").AppendLine(
            hasWireNet && wireNet.IsPowered ? "Active" : "Inactive");

        BoardComponent component = targetBoard.GetOccupant(cell);
        if (component == null)
        {
            textBuilder.AppendLine("Component: None");
            return textBuilder.ToString();
        }

        textBuilder
            .Append("Component: ")
            .Append(component.name)
            .Append(" (")
            .Append(component.GetType().Name)
            .AppendLine(")");
        textBuilder
            .Append("Grid Position: ")
            .Append(FormatCell(component.GridPosition))
            .Append("  Rotation: ")
            .Append(component.RotationDegrees)
            .AppendLine("°");

        AppendPortDetails(component, targetCircuit);
        AppendPowerDetails(component, targetCircuit);
        return textBuilder.ToString();
    }

    public string BuildNoHoverDebugText()
    {
        BeginDebugText();
        textBuilder.AppendLine("Hovered Cell: --");
        return textBuilder.ToString();
    }

    private void BeginDebugText()
    {
        textBuilder.Clear();
        ScoreSystem targetScore = ResolveScoreSystem();
        CircuitSystem targetCircuit = ResolveCircuitSystem();
        textBuilder
            .Append("Score: ")
            .AppendLine(targetScore != null
                ? targetScore.CurrentScore.ToString(
                    "0.0",
                    CultureInfo.InvariantCulture)
                : "--");
        textBuilder
            .Append("Score / Second: ")
            .AppendLine(targetScore != null
                ? targetScore.CurrentScorePerSecond.ToString()
                : "--");
        textBuilder
            .Append("Simulation Ticks: ")
            .AppendLine(targetCircuit != null
                ? targetCircuit.PowerTickCount.ToString()
                : "--");
        textBuilder
            .Append("Topology Rebuilds: ")
            .AppendLine(targetCircuit != null
                ? targetCircuit.RebuildCount.ToString()
                : "--");
        textBuilder
            .Append("Topology Dirty: ")
            .AppendLine(targetCircuit != null
                ? targetCircuit.IsTopologyDirty.ToString()
                : "--");
        textBuilder
            .Append("Topology Revision: ")
            .Append(targetCircuit?.Board != null
                ? targetCircuit.Board.TopologyRevision.ToString()
                : "--")
            .Append("  Built: ")
            .AppendLine(targetCircuit != null
                ? targetCircuit.LastBuiltTopologyRevision.ToString()
                : "--");
        textBuilder
            .Append("Electrical Nets: ")
            .AppendLine(targetCircuit != null
                ? targetCircuit.Nets.Count.ToString()
                : "--");
    }

    private void AppendPortDetails(
        BoardComponent component,
        CircuitSystem targetCircuit)
    {
        textBuilder.AppendLine("Ports:");
        if (component.Ports.Count == 0)
        {
            textBuilder.AppendLine("  None");
            return;
        }

        for (int portIndex = 0; portIndex < component.Ports.Count; portIndex++)
        {
            BoardPort port = component.Ports[portIndex];
            textBuilder.Append("  [").Append(portIndex).Append("] ");
            if (port == null ||
                !component.TryGetPortGridPose(
                    portIndex,
                    out Vector2Int portCell,
                    out BoardPortDirection direction))
            {
                textBuilder.AppendLine("Invalid");
                continue;
            }

            int netId = targetCircuit != null &&
                        targetCircuit.TryGetPortNet(component, portIndex, out CircuitNet net)
                ? net.NetId
                : CircuitSystem.NoNetId;

            textBuilder
                .Append(port.Type)
                .Append(' ')
                .Append(direction)
                .Append("  Cell ")
                .Append(FormatCell(portCell))
                .Append("  NetId ")
                .AppendLine(FormatNetId(netId));
        }
    }

    private void AppendPowerDetails(
        BoardComponent component,
        CircuitSystem targetCircuit)
    {
        if (component is ScoredPowerConsumerComponent consumer)
        {
            textBuilder
                .Append("Received Power: ")
                .AppendLine(consumer.ReceivedPower.ToString());
            textBuilder
                .Append("Power State: ")
                .AppendLine(consumer.PowerState.ToString());
        }

        if (component is GeneratorComponent generator)
        {
            int connectedOutput = 0;
            for (int portIndex = 0; portIndex < generator.Ports.Count; portIndex++)
            {
                BoardPort port = generator.Ports[portIndex];
                if (port != null &&
                    port.Type == BoardPortType.Output &&
                    targetCircuit != null &&
                    targetCircuit.TryGetPortNet(generator, portIndex, out _))
                {
                    connectedOutput += generator.GetPowerOutput(port);
                }
            }

            textBuilder
                .Append("Generator Output: ")
                .Append(connectedOutput)
                .Append("  (")
                .Append(GeneratorComponent.PowerPerConnectedOutput)
                .AppendLine(" per connected port)");
        }

        if (component is BatteryComponent battery)
        {
            textBuilder
                .Append("Battery Charge: ")
                .Append(battery.Charge.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture))
                .Append(" / ")
                .AppendLine(battery.Capacity.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture));
            textBuilder
                .Append("Battery Input: ")
                .AppendLine(battery.ReceivedInputPower.ToString());
            textBuilder
                .Append("Battery Output: ")
                .Append(battery.ProvidedOutputPower)
                .Append("  (configured ")
                .Append(battery.OutputPower)
                .AppendLine(")");
            textBuilder
                .Append("Battery State: ")
                .AppendLine(battery.PowerState.ToString());
            textBuilder
                .Append("Accepting Input: ")
                .AppendLine(battery.CanAcceptInputPower ? "Yes" : "No");
        }
    }

    private bool TryGetHoveredCell(out Vector2Int cell)
    {
        GridBoard targetBoard = ResolveBoard();
        Camera cameraToUse = pointerCamera != null
            ? pointerCamera
            : Camera.main;
        Mouse mouse = Mouse.current;

        if (targetBoard == null || cameraToUse == null || mouse == null)
        {
            cell = default;
            return false;
        }

        Ray pointerRay = cameraToUse.ScreenPointToRay(mouse.position.ReadValue());
        Plane boardPlane = new(targetBoard.transform.up, targetBoard.transform.position);
        if (!boardPlane.Raycast(pointerRay, out float distance))
        {
            cell = default;
            return false;
        }

        return targetBoard.TryWorldToCell(pointerRay.GetPoint(distance), out cell);
    }

    private GridBoard ResolveBoard()
    {
        if (board == null)
            board = GetComponent<GridBoard>();

        if (board == null && circuitSystem != null)
            board = circuitSystem.Board;

        if (board == null)
            board = FindFirstObjectByType<GridBoard>();

        return board;
    }

    private CircuitSystem ResolveCircuitSystem()
    {
        if (circuitSystem == null)
            circuitSystem = GetComponent<CircuitSystem>();

        GridBoard targetBoard = ResolveBoard();
        if (circuitSystem == null && targetBoard != null)
            circuitSystem = targetBoard.GetComponent<CircuitSystem>();

        if (circuitSystem == null)
            circuitSystem = FindFirstObjectByType<CircuitSystem>();

        return circuitSystem;
    }

    private ScoreSystem ResolveScoreSystem()
    {
        if (scoreSystem == null)
            scoreSystem = GetComponent<ScoreSystem>();

        if (scoreSystem == null)
            scoreSystem = global::ScoreSystem.Instance;

        if (scoreSystem == null)
            scoreSystem = FindFirstObjectByType<ScoreSystem>();

        return scoreSystem;
    }

    private void EnsureStyles()
    {
        titleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 2,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };
        bodyStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            wordWrap = true
        };
    }

    private static string FormatCell(Vector2Int cell)
    {
        return $"({cell.x}, {cell.y})";
    }

    private static string FormatNetId(int netId)
    {
        return netId == CircuitSystem.NoNetId
            ? "--"
            : netId.ToString();
    }
}
