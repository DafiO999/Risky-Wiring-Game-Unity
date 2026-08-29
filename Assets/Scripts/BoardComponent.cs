using System.Collections.Generic;
using UnityEngine;

public enum BoardRotation
{
    Degrees0 = 0,
    Degrees90 = 1,
    Degrees180 = 2,
    Degrees270 = 3
}

[DisallowMultipleComponent]
[ExecuteAlways]
public class BoardComponent : MonoBehaviour
{
    public const int FootprintWidth = 2;
    public const int FootprintHeight = 2;

    public static readonly Vector2Int FootprintSize = new(FootprintWidth, FootprintHeight);

    [SerializeField, HideInInspector]
    private GridBoard board;

    [SerializeField, HideInInspector]
    private Vector2Int gridPosition;

    [SerializeField, HideInInspector]
    private BoardRotation rotation;

    [SerializeField]
    private List<BoardPort> ports = new();

    [SerializeField]
    private bool drawPortGizmos = true;

    [SerializeField, Range(0.02f, 0.4f)]
    [Tooltip("Port marker radius as a fraction of the board's cell size.")]
    private float portGizmoSize = 0.12f;

    [SerializeField]
    private Color inputPortColor = new(0.1f, 0.85f, 1f, 1f);

    [SerializeField]
    private Color outputPortColor = new(1f, 0.45f, 0.05f, 1f);

    private bool isPlaced;
    private bool isApplyingPlacement;

    public GridBoard Board => board;
    public Vector2Int GridPosition => gridPosition;
    public BoardRotation Rotation => rotation;
    public int RotationSteps => (int)rotation;
    public int RotationDegrees => RotationSteps * 90;
    public bool IsPlaced => isPlaced;
    public IReadOnlyList<BoardPort> Ports => ports;

    public IEnumerable<Vector2Int> OccupiedCells
    {
        get
        {
            for (int y = 0; y < FootprintHeight; y++)
            {
                for (int x = 0; x < FootprintWidth; x++)
                    yield return gridPosition + new Vector2Int(x, y);
            }
        }
    }

    protected virtual void Reset()
    {
        board = FindBoardForTransform();
        rotation = BoardRotation.Degrees0;

        if (board == null)
            return;

        gridPosition = board.WorldToCell(transform.position);
        board.TryPlaceComponent(this, gridPosition, RotationSteps);
    }

    protected virtual void OnEnable()
    {
        if (isApplyingPlacement)
            return;

        if (!IsBoardInSameScene(board))
        {
            board = null;
            isPlaced = false;
            board = FindBoardForTransform();
        }

        if (board != null)
            board.TryPlaceComponent(this, gridPosition, RotationSteps);
    }

    protected virtual void OnValidate()
    {
        rotation = (BoardRotation)GridBoard.NormalizeRotationSteps((int)rotation);
        portGizmoSize = Mathf.Clamp(portGizmoSize, 0.02f, 0.4f);
        ValidatePortConfiguration();

        if (board != null && isPlaced)
        {
            RefreshPortViews();
            board.RefreshWireConnections();
        }
    }

    protected virtual void OnTransformParentChanged()
    {
        if (!isApplyingPlacement && isActiveAndEnabled && board != null)
            board.TryPlaceComponent(this, gridPosition, RotationSteps);
    }

    protected virtual void OnDestroy()
    {
        if (board != null)
            board.RemoveComponent(this);
    }

    public bool TrySetPlacement(
        GridBoard targetBoard,
        Vector2Int targetGridPosition,
        BoardRotation targetRotation)
    {
        return TrySetPlacement(
            targetBoard,
            targetGridPosition,
            (int)targetRotation,
            out _);
    }

    public bool TrySetPlacement(
        GridBoard targetBoard,
        Vector2Int targetGridPosition,
        int targetRotationSteps,
        out string failureReason)
    {
        if (targetBoard == null)
        {
            failureReason = "A GridBoard reference is required.";
            return false;
        }

        return targetBoard.TryPlaceComponent(
            this,
            targetGridPosition,
            targetRotationSteps,
            out failureReason);
    }

    public void ClearPlacement()
    {
        GridBoard previousBoard = board;
        if (previousBoard != null)
            previousBoard.RemoveComponent(this);

        board = null;
        isPlaced = false;
    }

    /// <summary>
    /// Returns a port's rotated board cell and cardinal direction.
    /// </summary>
    public bool TryGetPortGridPose(
        int portIndex,
        out Vector2Int cell,
        out BoardPortDirection direction)
    {
        if (ports == null || portIndex < 0 || portIndex >= ports.Count || ports[portIndex] == null)
        {
            cell = default;
            direction = default;
            return false;
        }

        BoardPort port = ports[portIndex];
        Vector2Int rotatedOffset = port.GetRotatedCellOffset(RotationSteps, FootprintSize);
        cell = gridPosition + rotatedOffset;
        direction = port.GetRotatedDirection(RotationSteps);
        return true;
    }

    /// <summary>
    /// Finds the port configured on one outward-facing edge of the placed footprint.
    /// The lookup uses only rotated logical cell coordinates and cardinal direction.
    /// </summary>
    public bool TryGetPortAtGridEdge(
        Vector2Int cell,
        BoardPortDirection direction,
        out BoardPort port)
    {
        if (ports != null)
        {
            for (int portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                BoardPort candidate = ports[portIndex];
                if (candidate != null &&
                    TryGetPortGridPose(
                        portIndex,
                        out Vector2Int candidateCell,
                        out BoardPortDirection candidateDirection) &&
                    candidateCell == cell &&
                    candidateDirection == direction)
                {
                    port = candidate;
                    return true;
                }
            }
        }

        port = null;
        return false;
    }

    /// <summary>
    /// Returns the world position on the owning cell edge and the outward world direction.
    /// </summary>
    public bool TryGetPortWorldPose(
        int portIndex,
        out Vector3 worldPosition,
        out Vector3 worldDirection)
    {
        if (board == null ||
            !isPlaced ||
            !TryGetPortGridPose(portIndex, out Vector2Int cell, out BoardPortDirection direction))
        {
            worldPosition = default;
            worldDirection = default;
            return false;
        }

        Vector2Int directionOffset = BoardPort.DirectionToCellOffset(direction);
        Vector3 localDirection = new(directionOffset.x, 0f, directionOffset.y);
        Vector3 localEdgeOffset = localDirection * (board.CellSize * 0.5f);

        worldPosition = board.CellToWorld(cell) +
                        board.transform.TransformVector(localEdgeOffset);
        worldDirection = board.transform.TransformDirection(localDirection).normalized;
        return true;
    }

    /// <summary>
    /// Clamps every local port offset to this component's 2x2 footprint.
    /// </summary>
    public void ValidatePortConfiguration()
    {
        ports ??= new List<BoardPort>();

        foreach (BoardPort port in ports)
            port?.Validate(FootprintSize);
    }

    protected void ReplacePorts(params BoardPort[] configuredPorts)
    {
        ports = configuredPorts != null
            ? new List<BoardPort>(configuredPorts)
            : new List<BoardPort>();
        ValidatePortConfiguration();
    }

    internal void ApplyPlacementFromBoard(
        GridBoard owner,
        Vector2Int targetGridPosition,
        int targetRotationSteps,
        Transform targetParent)
    {
        isApplyingPlacement = true;

        board = owner;
        gridPosition = targetGridPosition;
        rotation = (BoardRotation)GridBoard.NormalizeRotationSteps(targetRotationSteps);

        if (targetParent != null && transform.parent != targetParent)
            transform.SetParent(targetParent, true);

        Vector3 worldPosition = owner.GetFootprintCenterWorld(gridPosition, FootprintSize);
        Quaternion worldRotation = owner.transform.rotation *
                                   Quaternion.Euler(0f, RotationDegrees, 0f);
        transform.SetPositionAndRotation(worldPosition, worldRotation);

        isPlaced = true;
        RefreshPortViews();
        isApplyingPlacement = false;
    }

    public void RefreshPortViews()
    {
        BoardPortView[] portViews =
            GetComponentsInChildren<BoardPortView>(true);
        foreach (BoardPortView portView in portViews)
        {
            if (portView != null)
                portView.RefreshPose(this);
        }
    }

    internal void SetPlacementValidity(bool value)
    {
        isPlaced = value;
    }

    protected virtual void OnDrawGizmos()
    {
        if (!drawPortGizmos || ports == null || board == null || !isPlaced)
            return;

        float rightCellSize = board.transform
            .TransformVector(Vector3.right * board.CellSize)
            .magnitude;
        float forwardCellSize = board.transform
            .TransformVector(Vector3.forward * board.CellSize)
            .magnitude;
        float worldCellSize = Mathf.Min(rightCellSize, forwardCellSize);
        float markerRadius = Mathf.Max(0.01f, worldCellSize * portGizmoSize);
        Vector3 boardNormal = board.transform.up.normalized;
        Color previousColor = Gizmos.color;

        for (int portIndex = 0; portIndex < ports.Count; portIndex++)
        {
            if (!TryGetPortWorldPose(
                    portIndex,
                    out Vector3 portPosition,
                    out Vector3 portDirection))
            {
                continue;
            }

            BoardPort port = ports[portIndex];
            Vector3 markerPosition = portPosition + boardNormal * markerRadius * 0.2f;
            Gizmos.color = port.Type == BoardPortType.Input
                ? inputPortColor
                : outputPortColor;

            Gizmos.DrawSphere(markerPosition, markerRadius);
            DrawDirectionArrow(markerPosition, portDirection, boardNormal, markerRadius);
        }

        Gizmos.color = previousColor;
    }

    private static void DrawDirectionArrow(
        Vector3 start,
        Vector3 direction,
        Vector3 boardNormal,
        float markerRadius)
    {
        float arrowLength = markerRadius * 3f;
        Vector3 tip = start + direction * arrowLength;
        Vector3 side = Vector3.Cross(boardNormal, direction).normalized;
        Vector3 arrowBase = tip - direction * markerRadius;

        Gizmos.DrawLine(start, tip);
        Gizmos.DrawLine(tip, arrowBase + side * markerRadius * 0.65f);
        Gizmos.DrawLine(tip, arrowBase - side * markerRadius * 0.65f);
    }

    private GridBoard FindBoardForTransform()
    {
        GridBoard parentBoard = GetComponentInParent<GridBoard>();
        if (IsBoardInSameScene(parentBoard))
            return parentBoard;

        GridBoard[] boards = FindObjectsByType<GridBoard>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        GridBoard onlyBoardInSameScene = null;
        int boardsInSameScene = 0;

        foreach (GridBoard candidate in boards)
        {
            // Prefab Mode uses a separate preview scene. Never associate a
            // prefab-stage component with a GridBoard from the main scene:
            // TryPlaceComponent reparents objects, which would corrupt both
            // stages and make the scene hierarchy disappear until a reload.
            if (!IsBoardInSameScene(candidate))
                continue;

            onlyBoardInSameScene = candidate;
            boardsInSameScene++;

            Transform candidateComponents = candidate.ComponentsRoot;
            if (candidateComponents != null && transform.IsChildOf(candidateComponents))
                return candidate;

            Transform candidateDisplay = candidate.DisplayRoot;
            if (candidateDisplay != null && transform.IsChildOf(candidateDisplay))
                return candidate;
        }

        return boardsInSameScene == 1 ? onlyBoardInSameScene : null;
    }

    private bool IsBoardInSameScene(GridBoard candidate)
    {
        return candidate != null &&
               candidate.gameObject.scene == gameObject.scene;
    }
}
